using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Caching;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Telegram.Bot;
using Telegram.Bot.Polling;
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
        static CacheItemPolicy policycode = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(15) };

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

                aTimer.Interval = 60000;       // 60000!!!
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
            Methods.SetActive();

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
                            InlineKeyboardButton.WithCallbackData("Reply to request", "/take " + req.Id),
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
                            Message tm = await bot.SendTextMessageAsync(new ChatId(rep), req.Desc_fligth, null, ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, req.Id, 0);

                            eventLogBot.WriteEntry("Save telegram_history. Chat.Id=" + tm.Chat.Id + ", MessageId=" + tm.MessageId + ", req.Id=" + req.Id + ", type=0");

                            var agent = Methods.GetUser(rep);

                            //показали в чате агенту новый запрос
                            string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(agent.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent show request\"," +
                                "\"event_properties\":{\"ac\":\"" + req.Operating + "\",\"requestGroupID\":" + req.Id_group + ",\"version of request\":\"main\",\"requestor\":\"" + req.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - req.TS_create).TotalSeconds) + "}}]";
                            var r = Methods.AmplitudePOST(DataJson);
                        }
                    }
                    else
                    {
                        foreach (var rep in repgroup.Control)
                        {
                            Message tm = await bot.SendTextMessageAsync(new ChatId(rep), req.Desc_fligth, null, ParseMode.Html, replyMarkup: ikm);
                            Methods.SaveMessageParameters(tm.Chat.Id, tm.MessageId, req.Id, 0);

                            var agent = Methods.GetUser(rep);

                            //показали в чате агенту новый запрос
                            string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(agent.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent show request\"," +
                                "\"event_properties\":{\"ac\":\"" + req.Operating + "\",\"requestGroupID\":" + req.Id_group + ",\"version of request\":\"control\",\"requestor\":\"" + req.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - req.TS_create).TotalSeconds) + "}}]";
                            var r = Methods.AmplitudePOST(DataJson);
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
                    string mestext2 = "";
                    if (type == CancelType.Void)
                    {
                        mestext1 = "Your request " + req.Number_flight + " " + req.Origin + "-" + req.Destination + " at " + req.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + " has expired. You can send your request again. Your balance: " + (rt.SubscribeTokens + rt.NonSubscribeTokens) + " token(s)";
                        mestext2 = "Your request has expired";

                        //при направлении клиенту сообщения, что запрос протух
                        string plat = req.Source == 0 ? "telegram" : "app";
                        string DataJson = "[{\"user_id\":\"" + req.Id_requestor + "\",\"platform\":\"Telegram\",\"event_type\":\"tg user request expired message\"," +
                            "\"event_properties\":{\"platform\":\"" + plat + "\",\"ac\":\"" + req.Operating + "\",\"requestGroupID\":" + req.Id_group + "}}]";
                        var r = Methods.AmplitudePOST(DataJson);
                    }
                    else
                    {
                        mestext1 = "The agent " + urep.Nickname + " didn't reply to your request " + req.Number_flight + " " + req.Origin + "-" + req.Destination + " at " + req.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + " in time!";
                        mestext2 = "The agent " + urep.Nickname + " didn't reply to your request in time";
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

                    /*var ruleText0 = "Welcome to the Staff Airlines, agents!" + Environment.NewLine +
                        "This bot is special tool for agents. Agents — airline employees who can provide accurate flight load data to help their colleagues use SA benefits." + Environment.NewLine +
                        "To get started, you need to link your Telegram account with your profile in the Staff Airlines app. After this, you will receive requests from users for your airline's flights. " +
                        "For each answer you will receive a reward - 1 token. " +
                        "You can use the received tokens for your requests in the Staff Airlines app. Or you can purchase a premium subscription Staff Airlines with the <b>/sub</b> command." + Environment.NewLine +
                        "Premium subscription cost:" + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_1_month_sub + " tokens for 1 month." + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_1_week_sub + " tokens for 1 week." + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_3_day_sub + " tokens for 3 days." + Environment.NewLine + Environment.NewLine +
                        "Main commands:" + Environment.NewLine +
                        "<b>/sub</b> purchase Premium subscription" + Environment.NewLine +
                        "<b>/nick</b> change your nickname" + Environment.NewLine +
                        "<b>/airline</b> change your airline" + Environment.NewLine +
                        "<b>/help</b> description of all commands" + Environment.NewLine +
                        "<b>/balance</b> balance of tokens on the account";*/
                    var ruleText = "Welcome to Staff Airlines, agent!" + Environment.NewLine +
                        "This bot is a special tool for agents.Agents are airline employees who share flight load data with their colleagues and help them maximize the benefits of staff travel." + Environment.NewLine + Environment.NewLine +
                        "To get started, you need to link your Telegram account to your Staff Airlines app profile.Once you've linked your profile, you'll be ready to accept requests from app users for flights on the airline you represent." + Environment.NewLine + Environment.NewLine +
                        "Earned tokens can be spent on load requests from other agents in the Staff Airlines app or exchanged for a premium subscription using the / sub command." + Environment.NewLine + Environment.NewLine +
                        "The cost of premium subscription:" + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_1_month_sub + " tokens for 1 month." + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_1_week_sub + " tokens for 1 week." + Environment.NewLine +
                        Properties.Settings.Default.TokensFor_3_day_sub + " tokens for 3 days." + Environment.NewLine + Environment.NewLine +
                        "Bot commands:" + Environment.NewLine +
                        "<b>/sub</b> Exchange tokens for premium subscription" + Environment.NewLine +
                        "<b>/nick</b> Change nickname" + Environment.NewLine +
                        "<b>/airline</b> Change airline" + Environment.NewLine +
                        "<b>/help</b> Help with bot commands" + Environment.NewLine +
                        "<b>/balance</b> Check token balance";

                    try
                    {
                        var arrmsg = message?.Text?.Split(' ');
                        var msg = arrmsg[0].ToLower();

                        if (msg == "/start" && message != null && !string.IsNullOrEmpty(message.Text) && arrmsg.Length == 2)
                        {
                            cache.Add("start" + userid, message.Text, new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10) });
                        }

                        if (message?.Text?.ToLower() == "\U0001F7e1" || message?.Text?.ToLower() == "U+1F534" || message?.Text?.ToLower() == "/stop")
                        {
                            return;
                        }

                        string idus = message.Chat.Id.ToString();
                        if (user.Token != null)
                        {
                            idus = Methods.GetUserID(user.Token);
                        }

                        string comm = "";
                        var commexist = cache.Contains("User" + userid.Value);
                        if (commexist) comm = (string)cache.Get("User" + userid.Value);

                        if (string.IsNullOrEmpty(comm))
                        {
                            if (msg == "/start")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, ruleText, null, parseMode: ParseMode.Html);

                                // отправляем событие «первое появление  пользователя в поисковом боте (/start)» в амплитуд
                                string DataJson = "[{\"user_id\":\"" + message.Chat.Id + "\",\"platform\":\"Telegram\",\"event_type\":\"tg ab join\"," +
                                    "\"user_properties\":{\"is_agent\":\"no\"," +
                                    "\"id_telegram\":\"" + message.Chat.Id + "\"}}]";
                                var r = Methods.AmplitudePOST(DataJson);

                                if (arrmsg.Length == 1)
                                {
                                    var startexist = cache.Contains("start" + userid);
                                    if (startexist)
                                    {
                                        var startmsg = (string)cache.Get("start" + userid);
                                        arrmsg = startmsg.Split(' ');
                                        cache.Remove("start" + userid);
                                    }
                                }

                                if (arrmsg.Length == 2)
                                {
                                    cache.Remove("start" + userid);

                                    var payload = arrmsg[1].ToLower();
                                    Guid gu;
                                    bool isGuid0 = Guid.TryParse(payload, out gu);
                                    if (isGuid0)
                                    {
                                        string alert = null;
                                        user = Methods.ProfileCommand(userid.Value, payload, eventLogBot, out alert);
                                        UpdateUserInCache(user);
                                    }
                                }

                                if (user.Token == null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "To link your Staff Airlines profile to your Telegram ID, log into the Staff Airlines app (now only for iOS users, coming soon for Android users) in the “Profile” section and click “For registration as an agent if you are going to post flight load data and earn tokens”" + Environment.NewLine);

                                    //UpdateCommandInCache(userid.Value, "entertoken");
                                }
                                else if (string.IsNullOrEmpty(user.Nickname))
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Specify your nickname." + Environment.NewLine + "It can be your real name or just a nickname:");

                                    UpdateCommandInCache(userid.Value, "enternick");
                                }
                                else if (user.own_ac == "??")
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                    UpdateCommandInCache(userid.Value, "preset");
                                }
                                else if (Properties.Settings.Default.VerifyEmail && string.IsNullOrEmpty(user.Email))
                                {
                                    // отправляем событие tg agent verification start
                                    string DataJson00 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent verification start\"}]";                                        
                                    var r00 = Methods.AmplitudePOST(DataJson00);

                                    await botClient.SendTextMessageAsync(message.Chat, "Enter your corporate email to verify your account");

                                    UpdateCommandInCache(userid.Value, "enteremail");
                                }
                                else
                                {
                                    string nameac = Methods.TestAC(user.own_ac);
                                    await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + Environment.NewLine + "Airline: " + nameac + " (" + user.own_ac + ")" + Environment.NewLine + "Agent is online. Waiting for requests...");
                                }

                                return;
                            }
                            else if (user.Token == null)
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "To link your Staff Airlines profile to your Telegram ID, log into the Staff Airlines app (now only for iOS users, coming soon for Android users) in the “Profile” section and click “For registration as an agent if you are going to post flight load data and earn tokens”" + Environment.NewLine);

                                return;
                            }
                            else if (message?.Text?.ToLower() == "/help")
                            {
                                //Methods.SendEmailWithCode("1", "2");

                                await botClient.SendTextMessageAsync(message.Chat, ruleText, parseMode: ParseMode.Html);
                                return;
                            }
                            else if (message?.Text?.ToLower() == "/profile")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Enter the UID." + Environment.NewLine + "Generate and copy the UID from the Profile section of Staff Airlines app, after logging in:" + Environment.NewLine);

                                // отправляем событие «запрос uid для линковки профиля (/profile)» в амплитуд
                                string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg start link profile\"," +
                                    "\"event_properties\":{\"bot\":\"ab\"}}]";
                                var r = Methods.AmplitudePOST(DataJson);

                                UpdateCommandInCache(userid.Value, "entertoken");

                                return;
                            }
                            else if (message?.Text?.ToLower() == "/airline")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Specify your airline. Enter your airline's code (for example: AA):");

                                string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg start set ac\"," +
                                    "\"event_properties\":{\"bot\":\"ab\"}}]";
                                var r = Methods.AmplitudePOST(DataJson);

                                UpdateCommandInCache(userid.Value, "preset");

                                return;
                            }
                            else if (message?.Text?.ToLower() == "/nick")
                            {
                                await botClient.SendTextMessageAsync(message.Chat, "Specify your nickname." + Environment.NewLine + "It can be your real name or just a nickname:");

                                UpdateCommandInCache(userid.Value, "enternick");

                                return;
                            }
                            else if (message?.Text?.ToLower() ==  "/pushfcm")
                            {
                                Methods.SendPushNotification("fXkf6sAPQDy3u4Ge4gO_qT:APA91bHDO82EoM_sBaj7XuY1U8jKwOnd0rxTb_Z4sWw1sRT5LgowylxTd5VFUl0XHkxhsOQNbPVnpj7Zq6EUPXzTPmKLys-0B5hYzCu6QnBPE_p2rjYQKY5d6WmYv9s79UazYkqWSwEv", "sub", "message", "AF", "1234", "BER", "PAR", DateTime.Now, 2);
                            }
                            else if (message?.Text.ToLower() == "/balance")
                            {
                                if (user == null || user.Token == null)
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "To view the balance, you need to log in (/profile)");
                                }
                                else
                                {
                                    var ProfTok = Methods.GetProfile(Methods.GetUserID(user.Token)).Result;
                                    await botClient.SendTextMessageAsync(message.Chat, "Your balance: " + (ProfTok.NonSubscribeTokens + ProfTok.SubscribeTokens) + " token(s)");                                    
                                }
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
                                    var ProfTok = Methods.GetProfile(Methods.GetUserID(user.Token)).Result;

                                    string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"void\"," +
                                        "\"user_properties\":{\"paidStatus\":\"" + (ProfTok.Premium ? "premiumAccess" : "free plan") + "\"}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

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
                        }
                        else
                        {
                            eventLogBot.WriteEntry("comm: " + comm);

                            var messageText = message.Text.Replace("/", "");

                            if (comm == "entertoken" && !string.IsNullOrEmpty(messageText))
                            {
                                Guid gu;
                                bool isGuid0 = Guid.TryParse(messageText, out gu);

                                if (isGuid0)
                                {
                                    string alert = null;
                                    user = Methods.ProfileCommand(userid.Value, messageText, eventLogBot, out alert);

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
                                            await botClient.SendTextMessageAsync(message.Chat, "Specify your nickname." + Environment.NewLine + "It can be your real name or just a nickname:");

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

                            if (comm == "enternick" && user.Token != null && !string.IsNullOrEmpty(messageText))
                            {
                                string[] messageWords = messageText.ToLower().Split(' ');
                                if (messageWords[0] != "start")
                                {
                                    var NickAvail = Methods.NickAvailable(messageText, Methods.GetUserID(user.Token));

                                    if (NickAvail)
                                    {
                                        var alertnick = Methods.SetNickname(messageText, user);

                                        if (string.IsNullOrEmpty(alertnick))
                                        {
                                            user.Nickname = messageText;
                                            UpdateUserInCache(user);
                                            cache.Remove("User" + message.Chat.Id);

                                            string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg set agent nick\"," +
                                                "\"user_properties\":{\"nick\":\"" + user.Nickname + "\"}}]";
                                            var r = Methods.AmplitudePOST(DataJson);

                                            if (user.own_ac == "??")
                                            {
                                                await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + "!");
                                                await botClient.SendTextMessageAsync(message.Chat, "Airline: " + user.own_ac + Environment.NewLine + "Specify your airline. Enter your airline's code (for example: AA):");

                                                UpdateCommandInCache(userid.Value, "preset");
                                            }
                                            else if (Properties.Settings.Default.VerifyEmail && string.IsNullOrEmpty(user.Email))
                                            {
                                                await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + "!");
                                                await botClient.SendTextMessageAsync(message.Chat, "Enter your corporate email to verify your account");

                                                // отправляем событие tg agent verification start
                                                string DataJson00 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent verification start\"}]";
                                                var r00 = Methods.AmplitudePOST(DataJson00);

                                                UpdateCommandInCache(userid.Value, "enteremail");
                                            }
                                            else
                                            {
                                                //await botClient.SendTextMessageAsync(message.Chat, "Agent is online. Waiting for requests...");
                                                string nameac = Methods.TestAC(user.own_ac);
                                                await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + Environment.NewLine + "Airline: " + nameac + " (" + user.own_ac + ")" + Environment.NewLine + "Agent is online. Waiting for requests...");

                                            }
                                        }
                                        else
                                        {
                                            await botClient.SendTextMessageAsync(message.Chat, alertnick);
                                        }
                                    }
                                    else
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "This nickname is already in use by another agent. Please choose another one:");
                                    }
                                }

                                return;
                            }

                            if (comm == "enteremail" && user.Token != null && !string.IsNullOrEmpty(messageText))
                            {
                                var email = messageText.Trim();
                                if (!Methods.IsPublicEmail(email, user))
                                {
                                    /*var codeverify = Methods.GenCodeForVerify(message.Chat.Id);
                                    PutCodeInCache(userid.Value, codeverify);
                                    PutEmailInCache(userid.Value, email);
                                    var MailError = Methods.SendEmailWithCode(email, codeverify, user);
                                    if (string.IsNullOrEmpty(MailError))
                                    {
                                        UpdateCommandInCache(userid.Value, "entercode");

                                        // отправляем событие tg agent verification work email
                                        string DataJson00 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent verification work email\"}]";
                                        var r00 = Methods.AmplitudePOST(DataJson00);

                                        await botClient.SendTextMessageAsync(message.Chat, "We have sent a verification code by email. Check your mail from Staff Airlines and enter the code sent");
                                    }
                                    else
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, MailError);
                                    }*/
                                    Methods.SaveEmail(email, user);

                                    UpdateUserInCache(user);

                                    cache.Remove("User" + message.Chat.Id);

                                    string nameac = Methods.TestAC(user.own_ac);
                                    await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + Environment.NewLine + "Airline: " + nameac + " (" + user.own_ac + ")" + Environment.NewLine + "Agent is online. Waiting for requests...");

                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Looks like it's not corporate email. Public email services (gmail, yahoo, aol, outlook, etc.) are not allowed for agent registration." + Environment.NewLine + "Enter your corporate email to verify your account");
                                } 

                                return;
                            }

                            if (comm == "entercode" && user.Token != null && !string.IsNullOrEmpty(messageText))
                            {
                                var codecache = "";
                                var email33 = "";
                                var keycode = "Code" + userid.Value;
                                var emailcode = "Email" + userid.Value;
                                var codeexist = cache.Contains(keycode);
                                if (codeexist) codecache = (string)cache.Get(keycode);
                                var emailexist = cache.Contains(emailcode);
                                if (emailexist) email33 = (string)cache.Get(emailcode);

                                eventLogBot.WriteEntry("entercode. keycode=" + keycode + ", code=" + codecache + ", message=" + messageText);

                                if (messageText == codecache)
                                {
                                    Methods.SaveEmail(email33, user);

                                    user.Email = email33;
                                    UpdateUserInCache(user);

                                    cache.Remove("User" + message.Chat.Id);
                                    cache.Remove("Code" + message.Chat.Id);
                                    cache.Remove("Email" + message.Chat.Id);

                                    string nameac = Methods.TestAC(user.own_ac);
                                    await botClient.SendTextMessageAsync(message.Chat, "Agent nickname: " + user.Nickname + Environment.NewLine + "Airline: " + nameac + " (" + user.own_ac + ")" + Environment.NewLine + "Agent is online. Waiting for requests...");
                                }
                                else
                                {
                                    // отправляем событие error code
                                    string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent verification error code\"," +
                                        "\"user_properties\":{\"email\":\"" + email33 + "\"}," +
                                        "\"event_properties\":{\"cache\":\"" + codecache + "\",\"code\":\"" + messageText + "\"}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

                                    await botClient.SendTextMessageAsync(message.Chat, "The code is invalid or has expired");
                                    await botClient.SendTextMessageAsync(message.Chat, "Enter your corporate email to verify your account");

                                    UpdateCommandInCache(userid.Value, "enteremail");
                                }

                                return;
                            }

                            if (comm == "preset")
                            {
                                //eventLogBot.WriteEntry("command preset");

                                var ac = messageText;
                                var nameac = Methods.TestAC(ac.ToUpper());
                                if (!string.IsNullOrEmpty(nameac))
                                {
                                    try
                                    {
                                        eventLogBot.WriteEntry("UpdateUserAC. " + ac.ToUpper() + "/" + user.own_ac.ToUpper() + "/" + message.Chat.Id);
                                        Methods.UpdateUserAC(message.Chat.Id, ac.ToUpper(), user.own_ac.ToUpper(), user);
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

                                    string DataJson = "[{\"user_id\":\"" + idus + "\",\"platform\":\"Telegram\",\"event_type\":\"tg set ac\"," +
                                        "\"user_properties\":{\"ac\":\"" + ac.ToUpper() + "\"}," +
                                        "\"event_properties\":{\"bot\":\"ab\"}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

                                    if (Properties.Settings.Default.VerifyEmail && string.IsNullOrEmpty(user.Email))
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Airline: " + nameac + " (" + ac.ToUpper() + ")");
                                        await botClient.SendTextMessageAsync(message.Chat, "Enter your corporate email to verify your account");

                                        // отправляем событие tg agent verification start
                                        string DataJson00 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent verification start\"}]";
                                        var r00 = Methods.AmplitudePOST(DataJson00);

                                        UpdateCommandInCache(userid.Value, "enteremail");
                                    }
                                    else
                                    {
                                        await botClient.SendTextMessageAsync(message.Chat, "Airline: " + nameac + " (" + ac.ToUpper() + ")" + Environment.NewLine + "Agent is online. Waiting for requests...");
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(message.Chat, "Airline code " + ac.ToUpper() + " not found! Enter your airline's code:");
                                }
                                return;
                            }

                            //eventLogBot.WriteEntry("command=" + comm + ", chat=" + message.Chat.Id);

                            if (comm.Substring(0, 6) == "ready1")
                            {
                                var commwithpar = comm.Split('/');

                                eventLogBot.WriteEntry("command ready1. id: " + commwithpar[1]);

                                short n;
                                bool isNumeric = short.TryParse(messageText, out n);
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
                                bool isNumeric = short.TryParse(messageText, out n);
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
                                bool isNumeric = short.TryParse(messageText, out n);
                                if (isNumeric)
                                {
                                    var id_req = long.Parse(commwithpar[1]);
                                    cache.Remove("User" + message.Chat.Id);
                                    Methods.SetCount(id_req, PlaceType.SA, n);
                                    Request req = Methods.GetRequestStatus(id_req);
                                    Methods.SetRequestStatus(5, req);

                                    //агент отправил данные по загрузке
                                    string DataJson0 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent send info\"," +
                                        "\"event_properties\":{\"ac\":\"" + req.Operating + "\",\"requestGroupID\":" + req.Id_group + ",\"version of request\":\"main\",\"requestor\":\"" + req.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - req.TS_create).TotalSeconds) + "}}]";
                                    var r0 = Methods.AmplitudePOST(DataJson0);

                                    eventLogBot.WriteEntry("finished request id=" + id_req);
                                    Methods.DelMessageParameters(id_req);

                                    await botClient.SendTextMessageAsync(message.Chat, req.Desc_fligth + Environment.NewLine + "Classes available: Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value, null, ParseMode.Html);

                                    if (req.Source == 1)
                                    {
                                        var res = Methods.PushStatusRequest(req, "Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value + " (agent " + user.Nickname + " just reported this)");
                                        eventLogBot.WriteEntry("Push. " + res);
                                    }
                                    else
                                    {
                                        var u = Methods.GetUser(req.Id_requestor);
                                        await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), req.Desc_fligth + Environment.NewLine + "Classes available: Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value + " (agent " + user.Nickname + " just reported this)", null, ParseMode.Html);
                                    }

                                    //при направлении клиенту сообщения с данными по загрузке от агента
                                    string plat = req.Source == 0 ? "telegram" : "app";
                                    string DataJson = "[{\"user_id\":\"" + req.Id_requestor + "\",\"platform\":\"Telegram\",\"event_type\":\"tg user agent answer message\"," +
                                        "\"event_properties\":{\"platform\":\"" + plat + "\",\"ac\":\"" + req.Operating + "\",\"requestGroupID\":" + req.Id_group + ",\"agent\":\"" + Methods.GetUserID(user.Token) + "\",\"nick\":\"" + user.Nickname + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - req.TS_create).TotalSeconds) + ",\"economy\":" + req.Economy_count.Value + ",\"business\":" + req.Business_count.Value + ",\"sa\":" + req.SA_count.Value + "}}]";
                                    var r = Methods.AmplitudePOST(DataJson);

                                    var Coll = await Methods.CredToken(Methods.GetUserID(user.Token));

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
                            var ProfTok = Methods.GetProfile(Methods.GetUserID(user.Token)).Result;

                            if (ProfTok.Premium)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have an active subscription!");
                            }
                            else if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_1_month_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(Methods.GetUserID(user.Token), 30);
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
                            var ProfTok = Methods.GetProfile(Methods.GetUserID(user.Token)).Result;

                            if (ProfTok.Premium)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have an active subscription!");
                            }
                            else if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_1_week_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(Methods.GetUserID(user.Token), 7);
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
                            var ProfTok = Methods.GetProfile(Methods.GetUserID(user.Token)).Result;

                            if (ProfTok.Premium)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have an active subscription!");
                            }
                            else if (user.TokenSet.NonSubscribeTokens < Properties.Settings.Default.TokensFor_3_day_sub)
                            {
                                await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "Not enough tokens to purchase the selected subscription!");
                            }
                            else
                            {
                                var TC = await Methods.PremiumSub(Methods.GetUserID(user.Token), 3);
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

                                    ta = Methods.TakeAvailable(Methods.GetUserID(user.Token));
                                }
                                catch (Exception ex)
                                {
                                    eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                                }

                                //eventLogBot.WriteEntry("TakeAvailable=" + ta.ToString());

                                if (!ta)
                                {
                                    await botClient.SendTextMessageAsync(callbackquery.Message.Chat, "You already have one request in the progress! An agent is able to process only one request at a time.");
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
                                        Methods.SetRequestStatus(2, id, Methods.GetUserID(user.Token));
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

                                        //агент взял запрос в работу
                                        string DataJson0 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent take request\"," +
                                            "\"event_properties\":{\"ac\":\"" + request.Operating + "\",\"requestGroupID\":" + request.Id_group + ",\"version of request\":\"main\",\"requestor\":\"" + request.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - request.TS_create).TotalSeconds) + "}}]";
                                        var r0 = Methods.AmplitudePOST(DataJson0);

                                        if (request.Source == 1)
                                        {
                                            var res = Methods.PushStatusRequest(request, "The request has been taken by " + user.Nickname);
                                            eventLogBot.WriteEntry("Push. " + res);
                                        }
                                        else
                                        {
                                            var u = Methods.GetUser(request.Id_requestor);
                                            eventLogBot.WriteEntry("GetUser. Id_requestor=" + request.Id_requestor + ", User=" + Newtonsoft.Json.JsonConvert.SerializeObject(u));
                                            await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "The request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + " has been taken by " + user.Nickname);
                                        }

                                        //при направлении клиенту сообщения, что агент взял запрос в работу
                                        string plat = request.Source == 0 ? "telegram" : "app";

                                        string DataJson = "[{\"user_id\":\"" + request.Id_requestor + "\",\"platform\":\"Telegram\",\"event_type\":\"tg user agent take request message\"," +
                                            "\"event_properties\":{\"platform\":\"" + plat + "\",\"ac\":\"" + request.Operating + "\",\"requestGroupID\":" + request.Id_group + ",\"agent\":\"" + Methods.GetUserID(user.Token) + "\",\"nick\":\"" + user.Nickname + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - request.TS_create).TotalSeconds) + "}}]";
                                        var r = Methods.AmplitudePOST(DataJson);
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

                            //агент вернул запрос без выполнения
                            string DataJson0 = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent return request\"," +
                                "\"event_properties\":{\"ac\":\"" + request.Operating + "\",\"requestGroupID\":" + request.Id_group + ",\"version of request\":\"main\",\"requestor\":\"" + request.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - request.TS_create).TotalSeconds) + "}}]";
                            var r0 = Methods.AmplitudePOST(DataJson0);

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
                                var res = Methods.PushStatusRequest(request, "The agent " + user.Nickname + " refused to take your request!");
                                eventLogBot.WriteEntry("Push. " + res);
                            }
                            else
                            {
                                var u = Methods.GetUser(request.Id_requestor);
                                await botSearch.SendTextMessageAsync(new ChatId(u.id.Value), "The agent " + user.Nickname + " refused to take your request " + request.Number_flight + " " + request.Origin + "-" + request.Destination + " at " + request.DepartureDateTime.ToString("dd-MM-yyyy HH:mm") + "!");
                            }

                            //при направлении клиенту сообщения, что агент вернул запрос без исполнения
                            string plat = request.Source == 0 ? "telegram" : "app";
                            string DataJson = "[{\"user_id\":\"" + request.Id_requestor + "\",\"platform\":\"Telegram\",\"event_type\":\"tg user agent return request message\"," +
                                "\"event_properties\":{\"platform\":\"" + plat + "\",\"ac\":\"" + request.Operating + "\",\"requestGroupID\":" + request.Id_group + ",\"agent\":\"" + Methods.GetUserID(user.Token) + "\",\"nick\":\"" + user.Nickname + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - request.TS_create).TotalSeconds) + "}}]";
                            var r = Methods.AmplitudePOST(DataJson);
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
                                var request = Methods.GetRequestStatus(id_request);
                                Methods.SetRequestStatus(4, id_request, Methods.GetUserID(user.Token));

                                var mespar = Methods.GetMessageParameters(id_request, 1);
                                foreach (TelMessage tm in mespar)
                                {
                                    await botClient.EditMessageReplyMarkupAsync(new ChatId(tm.ChatId), tm.MessageId, replyMarkup: null);
                                }

                                eventLogBot.WriteEntry("ready request id=" + id_request + ", User=" + userid.Value);

                                //агент готов вводить данные по загрузке
                                string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg agent ready request\"," +
                                    "\"event_properties\":{\"ac\":\"" + request.Operating + "\",\"requestGroupID\":" + request.Id_group + ",\"version of request\":\"main\",\"requestor\":\"" + request.Id_requestor + "\",\"workTime\":" + Convert.ToInt32((DateTime.Now - request.TS_create).TotalSeconds) + "}}]";
                                var r = Methods.AmplitudePOST(DataJson);

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
                        Methods.UserBlockChat(userid.Value, AirlineAction.Delete, user);

                        cache.Remove("User" + userid.Value);

                        if (user.is_reporter)
                        {
                            //пользователь покинул агентский бот
                            string DataJson = "[{\"user_id\":\"" + Methods.GetUserID(user.Token) + "\",\"platform\":\"Telegram\",\"event_type\":\"tg left agent\"," +
                                "\"user_properties\":{\"is_agent\":\"no\"}," +
                                "\"event_properties\":{\"ac\":\"" + user.own_ac + "\"}}]";
                            var r = Methods.AmplitudePOST(DataJson);

                            if (user.own_ac != "??")
                            {
                                Methods.UpdateAirlinesReporter(user.own_ac, AirlineAction.Delete);
                            }
                        }

                        user.is_reporter = false;
                        UpdateUserInCache(user);

                    }
                    else if (CM.Status == ChatMemberStatus.Member) // Разблокировал чат
                    {
                        Methods.UserBlockChat(userid.Value, AirlineAction.Add, user);

                        //user.is_reporter = true;
                        UpdateUserInCache(user);

                        if (user.is_reporter && user.own_ac != "??")
                        {
                            Methods.UpdateAirlinesReporter(user.own_ac, AirlineAction.Add);
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                eventLogBot.WriteEntry("Exception: " + ex.Message + "..." + ex.StackTrace);
                return;
            }
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

        private static void PutCodeInCache(long user, string code)
        {
            string keycode = "Code" + user;

            var commexist = cache.Contains(keycode);
            if (commexist)
            {
                cache.Set(keycode, code, policyuser);
            }
            else
            {
                cache.Add(keycode, code, policyuser);
            }
            try
            {
                eventLogBot.WriteEntry("PutCodeInCache. keycode=" + keycode + ", command=" + code);
            }
            catch { }
        }

        private static void PutEmailInCache(long user, string email)
        {
            string keycode = "Email" + user;

            var commexist = cache.Contains(keycode);
            if (commexist)
            {
                cache.Set(keycode, email, policyuser);
            }
            else
            {
                cache.Add(keycode, email, policyuser);
            }
            try
            {
                eventLogBot.WriteEntry("PutEmailInCache. keycode=" + keycode + ", command=" + email);
            }
            catch { }
        }

        public static async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // Некоторые действия
            eventLogBot.WriteEntry(Newtonsoft.Json.JsonConvert.SerializeObject(exception));
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
