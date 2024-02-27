using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.Caching;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
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
        static CacheItemPolicy policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10) };

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
                    AllowedUpdates = { }, // receive all update types
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

            try
            {
                HideRequests(CancelType.Ready);
                HideRequests(CancelType.Take);
                HideRequests(CancelType.Void);

                var requests = Methods.SearchRequests();

                foreach (var req in requests)
                {
                    Methods.SetRequestStatus(1, req.Id);

                    var ikm = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Take request", "/take " + req.Id),
                        },
                    });

                    eventLogBot.WriteEntry("Show request id=" + req.Id);

                    var reporters = Methods.GetReporters(req.Operating);
                    var repgroup = Methods.GetReporterGroup(reporters);
                    if (!Properties.Settings.Default.AgentControl)
                    {
                        repgroup = new ReporterGroup() { Main = reporters, Control = new List<long>() };
                    }

                    if (req.Version_request == 0)
                    {
                        foreach (var rep in repgroup.Main)
                        {
                            Message tm = await bot.SendTextMessageAsync(new ChatId(rep), req.Desc_fligth, null, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, req.Id, 0);
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

            var forcancel = Methods.SearchRequestsForCancel(type);
            foreach (var req in forcancel)
            {
                // Сообщение репортеру
                telegram_user urep = new telegram_user();
                if (!string.IsNullOrEmpty(req.Id_reporter))
                {
                    urep = Methods.GetUser(req.Id_reporter);
                }

                // Убираем сообщение с ready/cancel/take
                short mhtype = 1;
                short newstat = 3;
                if (type == CancelType.Void) 
                { 
                    mhtype = 0;            
                    newstat = 6;
                }
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

                Methods.SetRequestStatus(newstat, req.Id);
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
                        await bot.SendTextMessageAsync(new ChatId(urep.id.Value), "You did not respond in the allotted time!");
                    }
                }

                // Сообщение реквестору
                string mestext1 = "";
                string mestext2 = "";
                if (type == CancelType.Void)
                {
                    mestext1 = "Request processing timeout! " + Environment.NewLine + "Возвращено токенов: Subscribe - " + rt.DebtSubscribeTokens + ", Paid - " + rt.DebtNonSubscribeTokens + ". В наличии: Subscribe - " + rt.SubscribeTokens + ", Paid - " + rt.NonSubscribeTokens;
                    mestext2 = "Request processing timeout!";
                }
                else
                {
                    mestext1 = "The reporter did not respond in the allotted time!";
                    mestext2 = "The reporter did not respond in the allotted time to your request " + req.Number_flight + " " + req.Origin + "-" + req.Destination + " at " + req.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + "!";
                }

                if (req.Source == 0)
                {
                    var u = Methods.GetUser(req.Id_requestor);
                    await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), mestext1);
                }
                else
                {
                    var res = Methods.PushStatusRequest(req, mestext2);
                    eventLogBot.WriteEntry("Timeout. " + res);
                }
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

                    //eventLogBot.WriteEntry("key:User=" + userid.Value);

                    /*string commready = "";
                    var readyexist = cache.Contains("Ready" + userid.Value);
                    if (readyexist) commready = (string)cache.Get("Ready" + userid.Value);*/

                    //eventLogBot.WriteEntry("Message: " + message?.Text);

                    //var msg = message?.Text?.Split(' ')[0];

                    try
                    {
                        if (message?.Text?.ToLower() == "\U0001F7e1" || message?.Text?.ToLower() == "U+1F534" || message?.Text?.ToLower() == "/stop")
                        {
                            return;
                        }

                        if (message?.Text?.ToLower() == "/start")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Инструкция, как пользоваться ботом");

                            if (user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                                cache.Add("User" + userid.Value, "entertoken", policyuser);
                            }
                            else if (string.IsNullOrEmpty(user.Nickname))
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Specify your nickname:");

                                cache.Add("User" + userid.Value, "enternick", policyuser);
                            }
                            else if (user.own_ac == "??")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code:");

                                cache.Add("User" + userid.Value, "preset", policyuser);
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Your nickname is " + user.Nickname + Environment.NewLine + "Waiting for new requests...");                                
                            }

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/profile")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");

                            cache.Add("User" + userid.Value, "entertoken", policyuser);

                            return;
                        }
                        else if (message?.Text?.ToLower() == "/preset")
                        {
                            await botClient.SendTextMessageAsync(message.Chat, "Enter your airline's code:");

                            cache.Add("User" + userid.Value, "preset", policyuser);
                            eventLogBot.WriteEntry("cache command User" +userid.Value);
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
                                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac);
                                    }

                                    cache.Remove("User" + message.Chat.Id);

                                    if (string.IsNullOrEmpty(user.Nickname))
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Specify your nickname:");

                                        cache.Add("User" + userid.Value, "enternick", policyuser);
                                    }
                                    else if (user.own_ac == "??")
                                    {
                                        //await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline:", replyMarkup: GetIkmSetAir(user.own_ac));
                                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code:");

                                        cache.Add("User" + userid.Value, "preset", policyuser);
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, alert);
                                    if (user is null || user.id == 0 || user.Token is null)
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");
                                    }
                                    else
                                    {
                                        cache.Remove("User" + message.Chat.Id);
                                    }
                                }
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "The GUID must contain 32 digits and 4 hyphens!");
                                if (user is null || user.Token is null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the token:");
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
                                    await botClient.SendTextMessageAsync(message.Chat, "Your nickname is " + user.Nickname + "!");

                                    if (user.own_ac == "??")
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code:");

                                        cache.Add("User" + userid.Value, "preset", policyuser);
                                    }
                                    else
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Waiting for new requests...");
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, alertnick);
                                }
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "This nickname is already taken!" + Environment.NewLine + "Specify your nickname:");
                            }
                        }
                        

                        if (comm == "preset")
                        {
                            //eventLogBot.WriteEntry("command preset");

                            var ac = message.Text;
                            var test = Methods.TestAC(ac.ToUpper());
                            if (test > 0)
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

                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + ac.ToUpper() + Environment.NewLine + "Waiting for new requests...");
                            }
                            else
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Own ac: " + ac + " not found! Enter the correct code of your airline:");
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

                                int n;
                                bool isNumeric = int.TryParse(message?.Text, out n);
                                if (isNumeric && n >= 0)
                                {
                                    cache.Remove("User" + message.Chat.Id);
                                    try
                                    {
                                        Methods.SetCount(long.Parse(commwithpar[1]), PlaceType.Economy, n);
                                    }
                                    catch (Exception ex) 
                                    {
                                        eventLogBot.WriteEntry("SetCount: " + ex.Message + "..." + ex.StackTrace);
                                    }
                                    cache.Add("User" + message.Chat.Id, "ready2/" + commwithpar[1], policyuser);
                                    await botClient.SendTextMessageAsync(message.Chat, "Number of available seats in business class:");
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the number of seats!");
                                }
                                return;
                            }

                            if (comm.Substring(0, 6) == "ready2")
                            {
                                //eventLogBot.WriteEntry("command ready2");

                                var commwithpar = comm.Split('/');
                                int n;
                                bool isNumeric = int.TryParse(message?.Text, out n);
                                if (isNumeric && n >= 0)
                                {
                                    cache.Remove("User" + message.Chat.Id);
                                    Methods.SetCount(long.Parse(commwithpar[1]), PlaceType.Business, n);
                                    cache.Add("User" + message.Chat.Id, "ready3/" + commwithpar[1], policyuser);
                                    await botClient.SendTextMessageAsync(message.Chat, "Number of SA passengers:");
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the number of seats!");
                                }
                                return;
                            }

                            if (comm.Substring(0, 6) == "ready3")
                            {
                                //eventLogBot.WriteEntry("command ready3");

                                var commwithpar = comm.Split('/');
                                int n;
                                bool isNumeric = int.TryParse(message?.Text, out n);
                                if (isNumeric && n >= 0)
                                {
                                    var id_req = long.Parse(commwithpar[1]);
                                    cache.Remove("User" + message.Chat.Id);
                                    Methods.SetCount(id_req, PlaceType.SA, n);
                                    Request req = Methods.GetRequestStatus(id_req);
                                    Methods.SetRequestStatus(5, id_req);

                                    eventLogBot.WriteEntry("finished request id=" + id_req);
                                    Methods.DelMessageParameters(id_req);

                                    await botClient.SendTextMessageAsync(message.Chat, "Processing is finished!" + Environment.NewLine + Environment.NewLine + req.Desc_fligth + Environment.NewLine + "Economy class: " + req.Economy_count.Value + " seats" + Environment.NewLine + "Business class: " + req.Business_count.Value + " seats" + Environment.NewLine + "SA passengers: " + req.SA_count.Value, null, Telegram.Bot.Types.Enums.ParseMode.Html);

                                    if (req.Source == 1)
                                    {
                                        var res = Methods.PushStatusRequest(req, "Processing is finished");
                                        eventLogBot.WriteEntry("Push. " + res);
                                    }
                                    else
                                    {
                                        var u = Methods.GetUser(req.Id_requestor);
                                        await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), req.Desc_fligth + Environment.NewLine + "Economy class: " + req.Economy_count.Value + " seats" + Environment.NewLine + "Business class: " + req.Business_count.Value + " seats" + Environment.NewLine + "SA passengers: " + req.SA_count.Value + Environment.NewLine + Environment.NewLine + "Processing is finished!", null, Telegram.Bot.Types.Enums.ParseMode.Html);
                                    }

                                    var Coll = await Methods.CredToken(CombineUserId(user));

                                    await botClient.SendTextMessageAsync(message.Chat, "Начислено токенов: " + Coll.DebtNonSubscribeTokens + ". В наличии SubscribeTokens: " + Coll.SubscribeTokens + ", NonSubscribeTokens: " + Coll.NonSubscribeTokens + ". " + Coll.Error);
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter the number of seats!");
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
                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Enter your airline's code:");

                            cache.Add("User" + callbackquery.From.Id, "preset", policyuser);
                            eventLogBot.WriteEntry("cache command User" + callbackquery.From.Id);
                        }
                        else if (message.Substring(0, 5) == "/take")
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Для взятия запроса необходимо авторизоваться (/profile)");
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
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Only one request can be processed at a time!");
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
                                            txt = "Request in processing";
                                        }
                                        else
                                        {
                                            txt = "Request completed";
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
                                        }
                                        Methods.DelMessageParameters(id, 0);

                                        eventLogBot.WriteEntry("take request id=" + id);

                                        var replyKeyboardMarkup = new InlineKeyboardMarkup(new[]
                                        {
                                            new[]
                                            {
                                                InlineKeyboardButton.WithCallbackData("Ready", "/ready " + id),
                                                InlineKeyboardButton.WithCallbackData("Cancel", "/cancel " + id)
                                            },
                                        });

                                        var tmes = await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "The following query is selected:" + Environment.NewLine + Environment.NewLine + request.Desc_fligth, null, Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: replyKeyboardMarkup);
                                        Methods.SaveMessageParameters(tmes.Chat.Id, tmes.MessageId, id, 1);

                                        if (request.Source == 1)
                                        {
                                            var res = Methods.PushStatusRequest(request, "Taken to work");
                                            eventLogBot.WriteEntry("Push. " + res);
                                        }
                                        else
                                        {
                                            var u = Methods.GetUser(request.Id_requestor);
                                            eventLogBot.WriteEntry("GetUser. Id_requestor=" + request.Id_requestor + ", User=" + Newtonsoft.Json.JsonConvert.SerializeObject(u));
                                            await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "Request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:m") + " taken to work!");
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
                            Methods.SetRequestStatus(3, id_request);

                            var mespar = Methods.GetMessageParameters(id_request, 1);
                            foreach (TelMessage tm in mespar)
                            {
                                await botClient.DeleteMessageAsync(new ChatId(tm.ChatId), tm.MessageId);
                            }
                            Methods.DelMessageParameters(id_request, 1);

                            eventLogBot.WriteEntry("cancel request id=" + id_request);

                            cache.Remove("User" + userid.Value);
                            await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Request processing canceled!");

                            if (request.Source == 1)
                            {
                                var res = Methods.PushStatusRequest(request, "Request canceled");
                                eventLogBot.WriteEntry("Push. " + res);
                            }
                            else
                            {
                                var u = Methods.GetUser(request.Id_requestor);
                                await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "The reporter refused to work with your request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:m") + "!");
                            }
                        }
                        else if (message.Substring(0, 6) == "/ready")
                        {
                            if (user == null || user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Для ответа на запрос необходимо авторизоваться (/profile)");
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

                                //cache.Add("Ready" + userid.Value, "1", policyuser);
                                cache.Add("User" + userid.Value, "ready1/" + id_request, policyuser);
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Number of available seats in economy class:");
                            }
                        }

                        /*if (message.Length >= 4 && message.Substring(0, 4) == "/com")
                        {
                            var pars = message.Split(' ');
                            if (pars.Length == 6)
                            {
                                var remreq = AddRequest(callbackquery);

                                DateTime dreq = new DateTime(2000 + int.Parse(pars[3].Substring(4, 2)), int.Parse(pars[3].Substring(2, 2)), int.Parse(pars[3].Substring(0, 2)), int.Parse(pars[4].Substring(0, 2)), int.Parse(pars[4].Substring(2, 2)), 0);

                                var reqpost = "You request " + pars[5].Substring(0, 2) + " " + dreq.ToString("dMMM HH:mm", CultureInfo.CreateSpecificCulture("en-US")) + " posted. You have " + remreq + " requests left";
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, reqpost);
                            }
                        }*/
                    }

                    return;
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
            cache.Remove(keyuser);
            cache.Add(keyuser, user, policyuser);
            try
            {
                eventLogBot.WriteEntry("UpdateUserInCache. keyuser=" + keyuser + ". " + Newtonsoft.Json.JsonConvert.SerializeObject(user));
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
