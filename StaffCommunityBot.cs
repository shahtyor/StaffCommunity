using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.Caching;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace StaffCommunity
{
    public partial class StaffCommunityBot : ServiceBase
    {
        //static ITelegramBotClient bot = new TelegramBotClient("6636694790:AAGzuH6T3wmsD27KU0cU5BoAuesvsOX7hqA");
        static ITelegramBotClient bot = new TelegramBotClient(Properties.Settings.Default.BotToken);
        static ITelegramBotClient botSearch = new TelegramBotClient(Properties.Settings.Default.BotSearchToken);
        //TestStaffCommunityBot 6457417713:AAFrqt3BSYdQy3-w73SAXKvrMXGy8btoJ0E
        //TestStaffSearchBot 6906986784:AAGWHhFXFQ3YVyu_c0fdJ1v13Pwsn5nbmBg
        static ObjectCache cache = MemoryCache.Default;
        static CacheItemPolicy policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMonths(6) };

        public enum ServiceState
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceStatus
        {
            public int dwServiceType;
            public ServiceState dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        };

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(System.IntPtr handle, ref ServiceStatus serviceStatus);

        public StaffCommunityBot()
        {
            InitializeComponent();

            eventLogBot = new EventLog();
            if (!EventLog.SourceExists("StaffCommunity"))
            {
                EventLog.CreateEventSource(
                    "StaffCommunity", "StaffCommunityLog");
            }
            eventLogBot.Source = "StaffCommunity";
            eventLogBot.Log = "StaffCommunityLog";

            Methods.conn.Open();
            Methods.connProc.Open();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                // Update the service state to Start Pending.
                ServiceStatus serviceStatus = new ServiceStatus();
                serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
                serviceStatus.dwWaitHint = 100000;
                SetServiceStatus(this.ServiceHandle, ref serviceStatus);

                eventLogBot.WriteEntry("Staff Community Bot --- OnStart");

                // Update the service state to Running.
                serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
                SetServiceStatus(this.ServiceHandle, ref serviceStatus);

                var task = Task.Run(async () => await bot.GetMeAsync());
                if (task.IsCompleted)
                {
                    var sfname = task.Result.FirstName;
                    eventLogBot.WriteEntry("Запущен бот " + sfname);
                }

                System.Timers.Timer aTimer = new System.Timers.Timer();

                aTimer.Elapsed += new ElapsedEventHandler(this.OnTimedEvent);

                aTimer.Interval = 60000;
                aTimer.Enabled = true;
                aTimer.Start();

                var cts = new CancellationTokenSource();
                var cancellationToken = cts.Token;
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>(), // receive all update types
                };
                bot.StartReceiving(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    receiverOptions,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
            }
        }

        public void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            //eventLogBot.WriteEntry("Таймер " + DateTime.Now.ToString());
            try
            {
                var task = Task.Run(async () => await Processing());
            }
            catch (Exception ex) 
            {
                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
            }
        }

        public static async Task Processing()
        {
            eventLogBot.WriteEntry("Processing: " + DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"));

            //Message msg = await bot.SendTextMessageAsync(new ChatId(5231676978), "test");

            try
            {
                HideRequests(CancelType.Ready);
                HideRequests(CancelType.Take);
                HideRequests(CancelType.Void);

                var requests = Methods.SearchRequests();

                foreach (var req in requests)
                {
                    Methods.SetRequestStatus(1, req);

                    var ikm = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Take request", "/take " + req.Id),
                        },
                    });

                    eventLogBot.WriteEntry("Show request id=" + req.Id);

                    var reporters = Methods.GetReporters(req.Operating);

                    eventLogBot.WriteEntry("reporters=" + Newtonsoft.Json.JsonConvert.SerializeObject(reporters));

                    var repgroup = Methods.GetReporterGroup(reporters);

                    if (!Properties.Settings.Default.AgentControl)
                    {
                        repgroup = new ReporterGroup() { Main = reporters, Control = new List<long>() };
                    }

                    eventLogBot.WriteEntry("repgroup=" + Newtonsoft.Json.JsonConvert.SerializeObject(repgroup));

                    if (req.Version_request == 0)
                    {
                        foreach (var rep in repgroup.Main)
                        {
                            Message tm = await bot.SendTextMessageAsync(new ChatId(rep), req.Desc_fligth, null, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, req.Id, 0);

                            eventLogBot.WriteEntry("Save telegram_history. Chat.Id=" + tm.Chat.Id + ", MessageId=" + tm.MessageId + ", req.Id=" + req.Id + ", type=0");
                        }
                    }
                    else
                    {
                        foreach (var rep in repgroup.Control)
                        {
                            Message tm = await bot.SendTextMessageAsync(new ChatId(rep), req.Desc_fligth, null, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, req.Id, 0);
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                eventLogBot.WriteEntry("Processing error: " + ex.Message + "..." + ex.StackTrace);
            }
        }

        public static async void HideRequests(CancelType type)
        {
            // type ready=0/cancel=1/take=2
            try
            {

                var forcancel = Methods.SearchRequestsForCancel(type);

                foreach (var req in forcancel)
                {
                    // Сообщение репортеру
                    telegram_user urep = new telegram_user();
                    if (!string.IsNullOrEmpty(req.Id_reporter))
                    {
                        urep = Methods.GetUser(req.Id_reporter);
                    }

                    List<long> replist = null;

                    short mhtype = 1;
                    short newstat = 3;
                    if (type == CancelType.Void)
                    {
                        mhtype = 0;
                        newstat = 6;
                        replist = Methods.GetReporters(req.Operating);
                    }

                    // Убираем сообщение с ready/cancel/take
                    var mespar = Methods.GetMessageParameters(req.Id, mhtype);
                    foreach (var tm in mespar)
                    {
                        try
                        {
                            await bot.DeleteMessageAsync(new ChatId(tm.ChatId), tm.MessageId);
                        }
                        catch { }
                    }
                    Methods.DelMessageParameters(req.Id, mhtype);

                    Methods.SetRequestStatus(newstat, req);

                    TokenCollection rt = new TokenCollection();
                    if (type == CancelType.Void)
                    {
                        rt = await Methods.ReturnToken(req);
                        eventLogBot.WriteEntry("auto cancel2 request id=" + req.Id + " Return token. " + Newtonsoft.Json.JsonConvert.SerializeObject(rt));
                    }
                    else
                    {
                        eventLogBot.WriteEntry("auto cancel1 request id=" + req.Id);
                        if (urep.id != null)
                        {
                            await bot.SendTextMessageAsync(new ChatId(urep.id.Value), "You didn't respond to the request in time!");
                        }
                    }

                    // Сообщение реквестору
                    string mestext1 = "";
                    if (type == CancelType.Void)
                    {
                        mestext1 = "Request processing timeout! Your balance: " + (rt.SubscribeTokens + rt.NonSubscribeTokens) + " token(s)";
                    }
                    else
                    {
                        mestext1 = "The agent " + urep.Nickname + " didn't reply to your request " + req.Number_flight + " " + req.Origin + "-" + req.Destination + " at " + req.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + " in time!";
                    }

                    if (req.Source == 0)
                    {
                        var u = Methods.GetUser(req.Id_requestor);
                        await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), mestext1);
                    }
                    else
                    {
                        var res = Methods.PushStatusRequest(req, mestext1);
                        eventLogBot.WriteEntry("Timeout. " + res);
                    }
                }
            }
            catch (Exception ex) 
            {
                eventLogBot.WriteEntry("HideRequests Error. " + ex.Message + "..." + ex.StackTrace);
            }
        }

        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            eventLogBot.WriteEntry(Newtonsoft.Json.JsonConvert.SerializeObject(update));

            try
            {
                if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message)
                {
                    var message = update.Message;
                    long? userid = null;
                    telegram_user user = null;
                    string keyuser = null;

                    //eventLogBot.WriteEntry("Message0: " + message?.Text);

                    if (message != null)
                    {
                        userid = message.Chat.Id;
                        keyuser = "teluser:" + userid;
                        var userexist = cache.Contains(keyuser);
                        if (userexist) user = (telegram_user)cache.Get(keyuser);
                        else
                        {
                            user = Methods.GetUser(userid.Value);
                            cache.Add(keyuser, user, policyuser);
                        }
                    }

                    var ruleText = "Welcome to the Staff Airlines, agents!" + Environment.NewLine +
                        "This bot is special tool for agents. Agents — airline employees who can provide accurate flight load data to help their colleagues use SA benefits." + Environment.NewLine +
                        "To get started, you need to link your Telegram account with your profile in the Staff Airlines app. After this, you will receive requests from users for your airline's flights. " +
                        "For each answer you will receive a reward - 1 token. " +
                        "You can use the received tokens for your requests in the Staff Airlines app or in Staff Airlines Search telegram bot (@StaffAirlinesSearchBot). Or you can purchase a premium subscription Staff Airlines with the <b>/sub</b> command." + Environment.NewLine +
                        "Premium subscription cost:" + Environment.NewLine +
                        "20 tokens for 1 month." + Environment.NewLine +
                        "10 tokens for 1 week." + Environment.NewLine +
                        "5 tokens for 3 days." + Environment.NewLine + Environment.NewLine +
                        "Main commands:" + Environment.NewLine +
                        "<b>/sub</b> purchase Premium subscription" + Environment.NewLine +
                        "<b>/profile</b> link your telegram account with your Staff Airlines profile " + Environment.NewLine +
                        "<b>/nick</b> change your callsigh" + Environment.NewLine +
                        "<b>/preset</b> change your airline" + Environment.NewLine +
                        "<b>/help</b> description of all commands";

                    try
                    {
                        if (message?.Text?.ToLower() == "\U0001F7e1" || message?.Text?.ToLower() == "U+1F534" || message?.Text?.ToLower() == "/stop")
                        {
                            return;
                        }

                        if (message?.Text?.ToLower() == "/start")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, ruleText, null, parseMode: ParseMode.Html);

                            if (user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Enter the UID." + Environment.NewLine + "Generate and copy the UID from the Profile section of Staff Airlines app, after logging in:" + Environment.NewLine);

                                UpdateCommandInCache(userid.Value, "entertoken");
                            }
                            else if (string.IsNullOrEmpty(user.Nickname))
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Specify your callsign." + Environment.NewLine + "It can be your real name or just a nickname:");

                                UpdateCommandInCache(userid.Value, "enternick");
                            }
                            else if (user.own_ac == "??")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                UpdateCommandInCache(userid.Value, "preset");
                            }
                            else
                            {
                                string nameac = Methods.TestAC(user.own_ac);
                                await botClient.SendTextMessageAsync(message.Chat, "Airline: " + nameac + " (" + user.own_ac + ")" + Environment.NewLine + "Agent callsign: " + user.Nickname + Environment.NewLine + "Agent is online. Waiting for requests...");                                
                            }

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/help")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, ruleText, parseMode: ParseMode.Html);
                            return;
                        }
                        else if (message?.Text?.ToLower() == "/profile")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Enter the UID." + Environment.NewLine + "Generate and copy the UID from the Profile section of Staff Airlines app, after logging in:" + Environment.NewLine);

                            UpdateCommandInCache(userid.Value, "entertoken");

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/preset")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Specify your airline. Enter your airline's code (for example: AA):");

                            UpdateCommandInCache(userid.Value, "preset");

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/nick")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Specify your callsign." + Environment.NewLine + "It can be your real name or just a nickname:");

                            UpdateCommandInCache(userid.Value, "enternick");

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/sub")
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "To purchase the subscription, you have to log in (/profile)");
                            }
                            else
                            {
                                var ProfTok = Methods.GetProfile(user.Token.type + "_" + user.Token.id_user).Result;
                                if (ProfTok.Premium)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "You already have an active subscription!");
                                }
                                else
                                {
                                    user.TokenSet = new TokenCollection() { SubscribeTokens = ProfTok.SubscribeTokens, NonSubscribeTokens = ProfTok.NonSubscribeTokens };
                                    UpdateUserInCache(user);

                                    var replyKeyboardMarkup = new InlineKeyboardMarkup(new[]
                                    {
                                   new[]
                                   {
                                       InlineKeyboardButton.WithCallbackData("1 month", "/sub1month"),
                                       InlineKeyboardButton.WithCallbackData("1 week", "/sub1week"),
                                       InlineKeyboardButton.WithCallbackData("3 days", "/sub3day")
                                   },
                            });

                                    await botClient.SendTextMessageAsync(message.Chat, "Select the premium subscription option:" + Environment.NewLine + 
                                        "   - 1 month for " + Properties.Settings.Default.TokensFor_1_month_sub + " tokens" + Environment.NewLine +
                                        "   - 1 week for " + Properties.Settings.Default.TokensFor_1_week_sub + " tokens" + Environment.NewLine + 
                                        "   - 3 days for " + Properties.Settings.Default.TokensFor_3_day_sub + " tokens", null, null, replyMarkup: replyKeyboardMarkup);
                                }
                            }
                        }


                        string comm = "";
                        var commexist = cache.Contains("User" + userid.Value);
                        if (commexist) comm = (string)cache.Get("User" + userid.Value);

                        eventLogBot.WriteEntry("comm: " + comm);

                        if (comm == "entertoken" && !string.IsNullOrEmpty(message.Text))
                        {
                            Guid gu;
                            bool isGuid0 = Guid.TryParse(message.Text, out gu);

                            if (isGuid0)
                            {
                                string alert = null;
                                user = Methods.ProfileCommand(userid.Value, message.Text, eventLogBot, out alert);

                                if (string.IsNullOrEmpty(alert))
                                {
                                    UpdateUserInCache(user);

                                    if (!string.IsNullOrEmpty(user.own_ac))
                                    {
                                        string nameac = Methods.TestAC(user.own_ac);
                                        await botClient.SendTextMessageAsync(message.Chat, "Airline: " + nameac + " (" + user.own_ac + ")");
                                    }

                                    cache.Remove("User" + message.Chat.Id);

                                    if (string.IsNullOrEmpty(user.Nickname))
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Specify your callsign." + Environment.NewLine + "It can be your real name or just a nickname:");

                                        UpdateCommandInCache(userid.Value, "enternick");
                                    }
                                    else if (user.own_ac == "??")
                                    {
                                        //await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline:", replyMarkup: GetIkmSetAir(user.own_ac));
                                        await botClient.SendTextMessageAsync(message.Chat, "Airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                        UpdateCommandInCache(userid.Value, "preset");
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, alert);
                                    if (user is null || user.id == 0 || user.Token is null)
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Enter the UID (generate it in the Staff Airlines app in the Profile section, after logging in):");
                                    }
                                    else
                                    {
                                        cache.Remove("User" + message.Chat.Id);
                                    }
                                }
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "The UID must contain 32 digits/letters and 4 hyphens!");
                                if (user is null || user.Token is null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the UID (generate it in the Staff Airlines app in the Profile section, after logging in):");
                                }
                                else
                                {
                                    cache.Remove("User" + message.Chat.Id);
                                }
                            }

                            return;
                        }

                        if (comm == "enternick" && user.Token != null && !string.IsNullOrEmpty(message.Text))
                        {
                            var NickAvail = Methods.NickAvailable(message.Text, user.Token.type + "_" + user.Token.id_user);

                            if (NickAvail)
                            {
                                var alertnick = Methods.SetNickname(message.Text, user.Token.type + "_" + user.Token.id_user);

                                if (string.IsNullOrEmpty(alertnick))
                                {
                                    user.Nickname = message.Text;
                                    UpdateUserInCache(user);
                                    cache.Remove("User" + message.Chat.Id);
                                    await botClient.SendTextMessageAsync(message.Chat, "Agent callsign: " + user.Nickname + "!");

                                    if (user.own_ac == "??")
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                        UpdateCommandInCache(userid.Value, "preset");
                                    }
                                    else
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Agent is online. Waiting for requests...");
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, alertnick);
                                }
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "This callsign is already in use by another agent. Please choose another one:");
                            }
                        }                        

                        if (comm == "preset")
                        {
                            //eventLogBot.WriteEntry("command preset");

                            var ac = message.Text;
                            var nameac = Methods.TestAC(ac.ToUpper());
                            if (!string.IsNullOrEmpty(nameac))
                            {
                                try
                                {
                                    eventLogBot.WriteEntry("UpdateUserAC. " + ac.ToUpper() + "/" + user.own_ac.ToUpper() + "/" + message.Chat.Id);
                                    Methods.UpdateUserAC(message.Chat.Id, ac.ToUpper(), user.own_ac.ToUpper());
                                }
                                catch (Exception ex)
                                {
                                    eventLogBot.WriteEntry("UpdateUserAC. " + ac.ToUpper() + "/" + user.own_ac.ToUpper() + ". " + ex.Message + "..." + ex.StackTrace);
                                }
                                //var permitted = GetPermittedAC(ac.ToUpper());
                                //var sperm = string.Join('-', permitted.Select(p => p.Permit));
                                user.own_ac = ac;
                                //user.permitted_ac = sperm;
                                UpdateUserInCache(user);

                                cache.Remove("User" + message.Chat.Id);

                                await botClient.SendTextMessageAsync(message.Chat, "Airline: " + nameac + " (" + ac.ToUpper() + ")" + Environment.NewLine + "Status: Online. Waiting for requests...");
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Airline code " + ac.ToUpper() + " not found! Enter your airline's code:");
                            }
                            return;
                        }

                        //eventLogBot.WriteEntry("command=" + comm + ", chat=" + message.Chat.Id);

                        if (!string.IsNullOrEmpty(comm))
                        {
                            if (comm.Substring(0, 6) == "ready1")
                            {
                                var commwithpar = comm.Split('/');

                                eventLogBot.WriteEntry("command ready1. id: " + commwithpar[1]);

                                short n;
                                bool isNumeric = short.TryParse(message?.Text, out n);
                                if (isNumeric)
                                {
                                    try
                                    {
                                        Methods.SetCount(long.Parse(commwithpar[1]), PlaceType.Economy, n);
                                    }
                                    catch (Exception ex) 
                                    {
                                        eventLogBot.WriteEntry("SetCount: " + ex.Message + "..." + ex.StackTrace);
                                    }

                                    UpdateCommandInCache(message.Chat.Id, "ready2/" + commwithpar[1]);
                                    await botClient.SendTextMessageAsync(message.Chat, "Number of available seats in business class:");
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Please enter a positive or negative number!");
                                }
                                return;
                            }

                            if (comm.Substring(0, 6) == "ready2")
                            {
                                //eventLogBot.WriteEntry("command ready2");

                                var commwithpar = comm.Split('/');
                                short n;
                                bool isNumeric = short.TryParse(message?.Text, out n);
                                if (isNumeric)
                                {
                                    Methods.SetCount(long.Parse(commwithpar[1]), PlaceType.Business, n);

                                    UpdateCommandInCache(message.Chat.Id, "ready3/" + commwithpar[1]);

                                    await botClient.SendTextMessageAsync(message.Chat, "Number of standby (SA) passengers:");
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Please enter a positive or negative number!");
                                }
                                return;
                            }

                            if (comm.Substring(0, 6) == "ready3")
                            {
                                //eventLogBot.WriteEntry("command ready3");

                                var commwithpar = comm.Split('/');
                                short n;
                                bool isNumeric = short.TryParse(message?.Text, out n);
                                if (isNumeric)
                                {
                                    var id_req = long.Parse(commwithpar[1]);
                                    cache.Remove("User" + message.Chat.Id);
                                    Methods.SetCount(id_req, PlaceType.SA, n);
                                    Request req = Methods.GetRequestStatus(id_req);
                                    Methods.SetRequestStatus(5, req);

                                    eventLogBot.WriteEntry("finished request id=" + id_req);
                                    Methods.DelMessageParameters(id_req);

                                    await botClient.SendTextMessageAsync(message.Chat, req.Desc_fligth + Environment.NewLine + "Classes available: Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value, null, ParseMode.Html);

                                    if (req.Source == 1)
                                    {
                                        var res = Methods.PushStatusRequest(req, "Processing is finished");
                                        eventLogBot.WriteEntry("Push. " + res);
                                    }
                                    else
                                    {
                                        var u = Methods.GetUser(req.Id_requestor);
                                        await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), req.Desc_fligth + Environment.NewLine + "Classes available: Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value + " (agent just reported this)", null, ParseMode.Html);
                                    }

                                    var Coll = await Methods.CredToken(CombineUserId(user));

                                    await botClient.SendTextMessageAsync(message.Chat, "Tokens accrued: " + Coll.DebtNonSubscribeTokens + Environment.NewLine + "Your balance: " + (Coll.SubscribeTokens + Coll.NonSubscribeTokens) + " token(s)");
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Please enter a positive or negative number!");
                                }
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                    }

                    /*var command = message?.Text;
                    if (command != null && command[0] == '/')
                    {
                        command = command.Remove(0, 1);
                    }*/
                    return;
                }
                else if (update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQuery)
                {
                    var callbackquery = update.CallbackQuery;
                    var message = callbackquery.Data;

                    //eventLogBot.WriteEntry("Message2: " + message);

                    if (message != null)
                    {
                        //var message = update.Message;
                        long? userid = null;
                        telegram_user user = null;

                        if (callbackquery.From != null)
                        {
                            userid = callbackquery.From.Id;
                            string keyuser = "teluser:" + userid;
                            var userexist = cache.Contains(keyuser);
                            if (userexist) user = (telegram_user)cache.Get(keyuser);
                            else
                            {
                                user = Methods.GetUser(userid.Value);
                                cache.Add(keyuser, user, policyuser);
                            }
                        }

                        eventLogBot.WriteEntry(message + "..." + message.Substring(0, 5));

                        if (message == "/set_air")
                        {
                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Specify your airline. Enter your airline's code (for example: AA):");

                            UpdateCommandInCache(callbackquery.From.Id, "preset");
                        }
                        else if (message == "/sub1month")
                        {
                            if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_1_month_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(user.Token.type + "_" + user.Token.id_user, 30);
                                if (!string.IsNullOrEmpty(TC.Error))
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, TC.Error);
                                }
                                else
                                {
                                    user.TokenSet = TC;
                                    UpdateUserInCache(user);
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "The subscription is activated! Your balance: " + (TC.SubscribeTokens + TC.NonSubscribeTokens) + " token(s)");
                                }
                            }
                        }
                        else if (message == "/sub1week")
                        {
                            if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_1_week_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(user.Token.type + "_" + user.Token.id_user, 7);
                                if (!string.IsNullOrEmpty(TC.Error))
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, TC.Error);
                                }
                                else
                                {
                                    user.TokenSet = TC;
                                    UpdateUserInCache(user);
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "The subscription is activated! Your balance: " + (TC.SubscribeTokens + TC.NonSubscribeTokens) + " token(s)");
                                }
                            }
                        }
                        else if (message == "/sub3day")
                        {
                            if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_3_day_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(user.Token.type + "_" + user.Token.id_user, 3);
                                if (!string.IsNullOrEmpty(TC.Error))
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, TC.Error);
                                }
                                else
                                {
                                    user.TokenSet = TC;
                                    UpdateUserInCache(user);
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "The subscription is activated! Your balance: " + (TC.SubscribeTokens + TC.NonSubscribeTokens) + " token(s)");
                                }
                            }
                        }
                        else if (message.Substring(0, 5) == "/take")
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "To continue, please log in (/profile)");
                            }
                            else
                            {
                                //eventLogBot.WriteEntry("take request id=");

                                bool ta = true;
                                try
                                {
                                    //eventLogBot.WriteEntry("ta by " + callbackquery.From.Id);

                                    ta = Methods.TakeAvailable(CombineUserId(user));
                                }
                                catch (Exception ex)
                                {
                                    eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                                }

                                //eventLogBot.WriteEntry("TakeAvailable=" + ta.ToString());

                                if (!ta)
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have a request in progress, complete it first!");
                                }
                                else
                                {
                                    var msg = message.Split(' ');
                                    var id = long.Parse(msg[1]);
                                    var request = Methods.GetRequestStatus(id);
                                    var status = request.Request_status;

                                    //eventLogBot.WriteEntry("id=" + id + ", status=" + status);

                                    if (status == 2 || status == 4 || status == 5)
                                    {
                                        string txt = "";
                                        if (status == 2 || status == 4)
                                        {
                                            txt = "Sorry, but the request has just been taken by another agent";
                                        }
                                        else
                                        {
                                            txt = "Sorry, but the request has just been taken by another agent";
                                        }
                                        await botClient.SendTextMessageAsync(callbackquery.Message.Chat, txt);
                                    }
                                    else
                                    {
                                        Methods.SetRequestStatus(2, id, CombineUserId(user));
                                        var mespar = Methods.GetMessageParameters(id, 0);
                                        foreach (TelMessage tm in mespar)
                                        {
                                            await botClient.DeleteMessageAsync(new ChatId(tm.ChatId), tm.MessageId);
                                            eventLogBot.WriteEntry("DeleteMessege. ChatId=" + tm.ChatId + ", MessageId=" + tm.MessageId);
                                        }
                                        Methods.DelMessageParameters(id, 0);
                                        eventLogBot.WriteEntry("DeleteMessege. ChatId=" + id + ", type=0");

                                        eventLogBot.WriteEntry("take request id=" + id);

                                        var replyKeyboardMarkup = new InlineKeyboardMarkup(new[]
                                        {
                                            new[]
                                            {
                                                InlineKeyboardButton.WithCallbackData("Ready", "/ready " + id),
                                                InlineKeyboardButton.WithCallbackData("Cancel", "/cancel " + id)
                                            },
                                        });

                                        var tmes = await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You have " + Properties.Settings.Default.TimeoutVoid + " minutes to respond:" + Environment.NewLine + Environment.NewLine + request.Desc_fligth, null, ParseMode.Html, replyMarkup: replyKeyboardMarkup);
                                        Methods.SaveMessageParameters(tmes.Chat.Id, tmes.MessageId, id, 1);
                                        eventLogBot.WriteEntry("Save telegram_history. Chat.Id=" + tmes.Chat.Id + ", MessageId=" + tmes.MessageId + ", req.Id=" + id + ", type=1");

                                        if (request.Source == 1)
                                        {
                                            var res = Methods.PushStatusRequest(request, "Taken to work");
                                            eventLogBot.WriteEntry("Push. " + res);
                                        }
                                        else
                                        {
                                            var u = Methods.GetUser(request.Id_requestor);
                                            eventLogBot.WriteEntry("GetUser. Id_requestor=" + request.Id_requestor + ", User=" + Newtonsoft.Json.JsonConvert.SerializeObject(u));
                                            await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "The request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + " has been taken by " + user.Nickname);
                                        }
                                    }
                                }
                            }
                        }
                        else if (message.Substring(0, 7) == "/cancel")
                        {
                            var msg = message.Split(' ');
                            var id_request = long.Parse(msg[1]);
                            var request = Methods.GetRequestStatus(id_request);
                            Methods.SetRequestStatus(3, request);

                            var mespar = Methods.GetMessageParameters(id_request, 1);
                            foreach (TelMessage tm in mespar)
                            {
                                await botClient.DeleteMessageAsync(new ChatId(tm.ChatId), tm.MessageId);
                                eventLogBot.WriteEntry("DeleteMessege. ChatId=" + tm.ChatId + ", MessageId=" + tm.MessageId);
                            }
                            Methods.DelMessageParameters(id_request, 1);
                            eventLogBot.WriteEntry("DeleteMessege. ChatId=" + id_request + ", type=1");

                            eventLogBot.WriteEntry("cancel request id=" + id_request);

                            cache.Remove("User" + userid.Value);
                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Request has been canceled!");

                            if (request.Source == 1)
                            {
                                var res = Methods.PushStatusRequest(request, "Request has been canceled!");
                                eventLogBot.WriteEntry("Push. " + res);
                            }
                            else
                            {
                                var u = Methods.GetUser(request.Id_requestor);
                                await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "The agent " + user.Nickname + " refused to take your request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + "!");
                            }
                        }
                        else if (message.Substring(0, 6) == "/ready")
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "To continue, please log in (/profile)");
                            }
                            else
                            {
                                var msg = message.Split(' ');
                                var id_request = long.Parse(msg[1]);
                                //var request = Methods.GetRequestStatus(id_request);
                                Methods.SetRequestStatus(4, id_request, CombineUserId(user));

                                var mespar = Methods.GetMessageParameters(id_request, 1);
                                foreach (TelMessage tm in mespar)
                                {
                                    await botClient.EditMessageReplyMarkupAsync(new ChatId(tm.ChatId), tm.MessageId, replyMarkup: null);
                                }

                                eventLogBot.WriteEntry("ready request id=" + id_request + ", User=" + userid.Value);

                                UpdateCommandInCache(userid.Value, "ready1/" + id_request);
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Number of available seats in economy class:");
                            }
                        }
                    }

                    return;
                }
                else if (update.Type == UpdateType.MyChatMember)
                {
                    var myChatMember = update.MyChatMember;

                    //var message = update.Message;
                    long? userid = null;
                    telegram_user user = null;

                    if (myChatMember.From != null)
                    {
                        userid = myChatMember.From.Id;
                        string keyuser = "teluser:" + userid;
                        var userexist = cache.Contains(keyuser);
                        if (userexist) user = (telegram_user)cache.Get(keyuser);
                        else
                        {
                            user = Methods.GetUser(userid.Value);
                            cache.Add(keyuser, user, policyuser);
                        }
                    }

                    var CM = myChatMember.NewChatMember;
                    if (CM.Status == ChatMemberStatus.Kicked) // Заблокировал чат
                    {
                        Methods.UserBlockChat(userid.Value, AirlineAction.Delete);
                        if (user.own_ac != "??")
                        {
                            Methods.UpdateAirlinesReporter(user.own_ac, AirlineAction.Delete);
                        }
                    }
                    else if (CM.Status == ChatMemberStatus.Member) // Разблокировал чат
                    {
                        Methods.UserBlockChat(userid.Value, AirlineAction.Add);
                        if (user.own_ac != "??")
                        {
                            Methods.UpdateAirlinesReporter(user.own_ac, AirlineAction.Add);
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                eventLogBot.WriteEntry("Exception: " + ex.Message + "..." + ex.StackTrace);
            }
        }

        public static string CombineUserId(telegram_user user)
        {
            return user.Token.type + "_" + user.Token.id_user;
        }

        private static void UpdateUserInCache(telegram_user user)
        {
            string keyuser = "teluser:" + user.id;
            var userexist = cache.Contains(keyuser);
            if (userexist)
            {
                cache.Set(keyuser, user, policyuser);
            }
            else
            {
                cache.Add(keyuser, user, policyuser);
            }
            try
            {
                eventLogBot.WriteEntry("UpdateUserInCache. keyuser=" + keyuser + ". " + Newtonsoft.Json.JsonConvert.SerializeObject(user));
            }
            catch { }
        }

        private static void UpdateCommandInCache(long user, string command)
        {
            string keycommand = "User" + user;

            var commexist = cache.Contains(keycommand);
            if (commexist)
            {
                cache.Set(keycommand, command, policyuser);
            }
            else
            {
                cache.Add(keycommand, command, policyuser);
            }
            try
            {
                eventLogBot.WriteEntry("UpdateCommandInCache. keycommand=" + keycommand + ", command=" + command);
            }
            catch { }
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // Некоторые действия
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
        }

        protected override void OnStop()
        {
            //Methods.conn.Close();
            eventLogBot.WriteEntry("Staff Community Bot --- OnStop");
        }

        private static InlineKeyboardMarkup GetIkmSetAir(string own_ac)
        {
            InlineKeyboardButton ikb = null;
            if (own_ac == "??")
            {
                ikb = InlineKeyboardButton.WithCallbackData("Set air", "/set_air");
            }
            else
            {
                ikb = InlineKeyboardButton.WithCallbackData("Change air", "/set_air");
            }

            var ikm = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                   ikb,
                },
            });

            return ikm;
        }
    }
}
