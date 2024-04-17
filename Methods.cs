using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using RestSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace StaffCommunity
{
    public class Methods
    {
        static ObjectCache cache = MemoryCache.Default;
        static CacheItemPolicy policyuser = new CacheItemPolicy() { SlidingExpiration = TimeSpan.Zero, AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10) };

        //public static NpgsqlConnection conn = new NpgsqlConnection("Server = localhost; Port = 5432; User Id = postgres; Password = e4r5t6; Database = sae");
        //public static NpgsqlConnection conn = new NpgsqlConnection("Server = localhost; Port = 5432; User Id = postgres; Password = OVBtoBAX1972; Database = sae");
        public static NpgsqlConnection conn = new NpgsqlConnection(Properties.Settings.Default.ConnectionString);
        public static NpgsqlConnection connProc = new NpgsqlConnection(Properties.Settings.Default.ConnectionString);

        const string username = "sae2";
        const string pwd = "ISTbetweenVAR1999";

        const string SERVER_API_KEY_NEW = "AAAAmhjFn8k:APA91bGQ93j58UPBa_dZ2pkSHP7FPA97Cv9D4i0UqHEGi1__pIm4faBPhL7KcQcUDj-YqxpZMyL-kDwJHpeDddA_GNLPcWRQ4u7T5JsuOafpqq8te3Eg32T6zTpGGbZQSVlW6faSwaKM";
        const string SENDER_ID_NEW = "661840568265";

        public static telegram_user ProfileCommand(long id, string strtoken, EventLog eventLogBot, out string message)
        {
            telegram_user user = new telegram_user() { id = id };
            message = null;

            var token = GetToken(strtoken);

            if (token != null)
            {
                eventLogBot.WriteEntry("GetToken: " + token.type + "/" + token.id_user);

                var exist = TokenAlreadySet(token, id);
                if (exist)
                {
                    eventLogBot.WriteEntry("These authorization parameters have already been assigned to another user!");

                    message = "These authorization parameters have already been assigned to another user!";
                }
                else
                {
                    NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id_user=@id_user", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = token.type + "_" + token.id_user });
                    try
                    {
                        NpgsqlDataReader reader = com.ExecuteReader();

                        var PT = GetProfile(token.type + "_" + token.id_user).Result;
                        //var PT = new ProfileTokens();
                        string new_ac = PT?.OwnAC ?? "??";

                        if (reader.Read())
                        {
                            user = new telegram_user() { first_use = (DateTime)reader["first_use"], own_ac = (new_ac == "??" ? reader["own_ac"].ToString() : new_ac), is_reporter = true, is_requestor = (bool)reader["is_requestor"], Token = token, Nickname = reader["nickname"].ToString() };
                            if (user.id == null)
                            {
                                user.id = id;
                            }

                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            if (new_ac != "??" && !string.IsNullOrEmpty(user.Nickname))
                            {
                                // отправляем событие «когда создаем новую запись в таблице пользователей телеги с is agent = true, или проставляем для существующей записи is agent = true (в обоих случаях должна быть указана а/к пользователя, должен быть линк с профилем и указан ник)» в амплитуд
                                string DataJson = "[{\"user_id\":\"" + token.type + "_" + token.id_user + "\", \"event_type\":\"tg new agent\"," +
                                    "\"user_properties\":{\"is_agent\":\"yes\"," +
                                    "\"ac\":\"" + new_ac + "\", \"nick\":\"" + user.Nickname + "\"}}]";
                                var task = Task.Run(async () => await Methods.AmplitudePOST(DataJson));
                            }

                            NpgsqlCommand com3 = new NpgsqlCommand("update telegram_user set is_reporter=@is_reporter, id=@id, own_ac=@own_ac where id_user=@id_user", conn);
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = new_ac });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.type + "_" + token.id_user });

                            eventLogBot.WriteEntry(com3.CommandText);

                            try
                            {
                                com3.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                            }
                            com3.Dispose();

                            // отправляем событие «успешная линковка» в амплитуд
                            string DataJson2 = "[{\"user_id\":\"" + token.type + "_" + token.id_user + "\", \"event_type\":\"tg link profile\"," +
                                "\"event_properties\":{\"bot\":\"ab\",\"system\":\"" + (token.type == 1 ? "apple" : "google") + "\"}," +
                                "\"user_properties\":{\"id_telegram\":" + id + ",\"is_agent\":\"yes\",\"ac\":\"" + new_ac + "\"}}]";
                            var task2 = Task.Run(async () => await Methods.AmplitudePOST(DataJson2));

                            if (new_ac != "??")
                            {
                                UpdateAirlinesReporter(new_ac, AirlineAction.Add);
                            }
                        }
                        else
                        {
                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            NpgsqlCommand com2 = new NpgsqlCommand("insert into telegram_user (id, first_use, own_ac, is_reporter, is_requestor, id_user) values (@id, @first_use, @own_ac, @is_reporter, @is_requestor, @id_user)", conn);
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "first_use", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = DateTime.Now });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = new_ac });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = false });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.type + "_" + token.id_user });

                            eventLogBot.WriteEntry(com2.CommandText);

                            try
                            {
                                com2.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                var e = ex.StackTrace;
                                eventLogBot.WriteEntry(ex.Message + "..." + e);
                            }
                            com2.Dispose();

                            // отправляем событие «успешная линковка» в амплитуд
                            string DataJson2 = "[{\"user_id\":\"" + id + "\", \"event_type\":\"tg link profile\"," +
                                "\"event_properties\":{\"bot\":\"ab\",\"system\":\"" + (token.type == 1 ? "apple" : "google") + "\"}," +
                                "\"user_properties\":{\"customer_id\":\"" + token.type + "_" + token.id_user + "\",\"ac\":\"" + new_ac + "\"}}]";
                            var task2 = Task.Run(async () => await Methods.AmplitudePOST(DataJson2));

                            if (new_ac != "??")
                            {
                                UpdateAirlinesReporter(new_ac, AirlineAction.Add);
                            }

                            user = new telegram_user() { id = id, first_use = DateTime.Now, own_ac = new_ac, is_reporter = true, is_requestor = false, Token = token };
                        }
                    }
                    catch (Exception ex)
                    {
                        eventLogBot.WriteEntry(ex.Message + "..." + ex.StackTrace);
                    }
                }
            }
            else
            {
                eventLogBot.WriteEntry("A valid token was not found!");

                message = "A valid token was not found!";
            }

            return user;
        }

        public static string SetNickname(string Nickname, string id_user)
        {
            string alert = null;

            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set nickname=@nickname where id_user=@id_user", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "nickname", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = Nickname });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });

            try
            {
                com.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                alert = ex.Message + "..." + ex.StackTrace;
            }

            com.Dispose();

            return alert;
        }

        public static telegram_user GetUser(string id_user)
        {
            telegram_user user = null;

            string keyus = "getuser:" + id_user;
            var usexist = cache.Contains(keyus);
            if (usexist) user = (telegram_user)cache.Get(keyus);
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id_user=@id_user", conn);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
                NpgsqlDataReader reader = com.ExecuteReader();

                if (reader.Read())
                {
                    var sid = reader["id"].ToString();
                    long? iid = null;
                    if (!string.IsNullOrEmpty(sid))
                    {
                        iid = long.Parse(sid);
                    }

                    var id_user_arr = id_user.Split('_');
                    user = new telegram_user() { id = iid, first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), Nickname = reader["nickname"].ToString(), Token = new sign_in() { type = short.Parse(id_user_arr[0]), id_user = id_user_arr[1] } };

                    cache.Add(keyus, user, policyuser);
                }
                reader.Close();
                reader.Dispose();
                com.Dispose();
            }

            return user;
        }

        public static telegram_user GetUser(long id)
        {
            telegram_user user = null;

            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            NpgsqlDataReader reader = com.ExecuteReader();

            if (reader.Read())
            {
                var sid = reader["id"].ToString();
                long? iid = null;
                if (!string.IsNullOrEmpty(sid))
                {
                    iid = long.Parse(sid);
                }
                user = new telegram_user() { id = iid, first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), Nickname = reader["nickname"].ToString(), is_reporter = true, is_requestor = (bool)reader["is_requestor"] };
                var id_user = reader["id_user"].ToString();
                if (!string.IsNullOrEmpty(id_user))
                {
                    user.Token = new sign_in() { type = short.Parse(id_user.Split('_')[0]), id_user = id_user.Split('_')[1] };
                }
                if (!user.is_reporter)
                {
                    if (user.own_ac != "??" && !string.IsNullOrEmpty(user.Nickname) && !string.IsNullOrEmpty(id_user))
                    {
                        // отправляем событие «когда создаем новую запись в таблице пользователей телеги с is agent = true, или проставляем для существующей записи is agent = true (в обоих случаях должна быть указана а/к пользователя, должен быть линк с профилем и указан ник)» в амплитуд
                        string DataJson = "[{\"user_id\":\"" + id_user + "\", \"event_type\":\"tg new agent\"," +
                            "\"user_properties\":{\"is_agent\":\"yes\"," +
                            "\"ac\":\"" + user.own_ac + "\", \"nick\":\"" + user.Nickname + "\"}}]";
                        var task = Task.Run(async () => await Methods.AmplitudePOST(DataJson));
                    }

                    NpgsqlCommand com2 = new NpgsqlCommand("update telegram_user set is_reporter=@is_reporter where id=@id");
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                    com2.ExecuteNonQuery();
                    com2.Dispose();
                }
            }
            else
            {
                user = new telegram_user() { id = id, own_ac = "??", is_reporter = true, is_requestor = false };
            }

            reader.Close();
            reader.Dispose();
            com.Dispose();

            return user;
        }

        public static sign_in GetToken(string token)
        {
            sign_in result = null;
            NpgsqlCommand com = new NpgsqlCommand("select * from tokens where token=@token and ts_valid>now() order by ts_valid limit 1", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "token", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = Guid.Parse(token) });
            NpgsqlDataReader reader = com.ExecuteReader();
            if (reader.Read())
            {
                result = new sign_in();
                result.type = (short)reader["type"];
                result.id_user = reader["id_user"].ToString();
            }
            reader.Close();
            reader.Dispose();
            com.Dispose();

            return result;
        }

        public static bool TokenAlreadySet(sign_in token, long telegram_user)
        {
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where id<>@id and id_user=@id_user", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = telegram_user });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = token.type + "_" + token.id_user });
            var o = com.ExecuteScalar();
            //var cnt = (int)com.ExecuteScalar();
            int cnt = 0;
            if (o != null)
            {
                cnt = int.Parse(o.ToString());
            }
            com.Dispose();
            if (cnt > 0) return true;
            else return false;
        }

        public static bool NickAvailable(string Nickname, string id_user)
        {
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where id_user<>@id_user and nickname=@nickname", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "nickname", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = Nickname });
            var o = com.ExecuteScalar();
            int cnt = 0;
            if (o != null)
            {
                cnt = int.Parse(o.ToString());
            }
            com.Dispose();
            if (cnt > 0) return false;
            else return true;
        }

        private static List<PermittedAC> GetPermittedAC(string code)
        {
            List<PermittedAC> result = new List<PermittedAC>();

            string keyperm = "permac:" + code;
            var permexist = cache.Contains(keyperm);
            if (permexist) result = (List<PermittedAC>)cache.Get(keyperm);
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("select * from permitted_ac where code=@code", conn);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "code", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = code });
                try
                {
                    NpgsqlDataReader reader = com.ExecuteReader();
                    while (reader.Read())
                    {
                        result.Add(new PermittedAC() { Code = code, Permit = reader["permit"].ToString() });
                    }

                    reader.Close();
                    reader.Dispose();
                    com.Dispose();

                    return result;
                }
                catch (Exception ex)
                {
                    string s = "123";
                    return new List<PermittedAC>();
                }
            }

            cache.Add(keyperm, result, policyuser);
            return result;
        }

        public static string TestAC(string ac)
        {
            string result = null;
            NpgsqlCommand com = new NpgsqlCommand("select * from airlines where code=@ac", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
            try
            {
                NpgsqlDataReader reader = com.ExecuteReader();
                if (reader.Read())
                {
                    result = reader["name"].ToString();
                }

                reader.Close();
                reader.Dispose();
                com.Dispose();

                return result;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static void UserBlockChat(long id, AirlineAction action)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set is_reporter=@is_reporter where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = action == AirlineAction.Add });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static void UpdateAirlinesReporter(string ac, AirlineAction action)
        {
            if (!string.IsNullOrEmpty(ac) && ac.Length == 2)
            {
                if (action == AirlineAction.Delete)
                {
                    NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where is_reporter=true and own_ac=@ac", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
                    var cnt = Convert.ToInt32(com.ExecuteScalar());
                    com.Dispose();

                    if (cnt == 0)
                    {
                        com = new NpgsqlCommand("update airlines set reporter=false where code=@ac", conn);
                        com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
                        com.ExecuteNonQuery();
                        com.Dispose();

                        // отправляем событие «change reporters for ac» в амплитуд
                        string DataJson2 = "[{\"event_type\":\"change reporters for ac\"," +
                            "\"event_properties\":{\"ac\":\"" + ac + "\"," +
                            "\"new_status\":\"false\"}}]";
                        var task2 = Task.Run(async () => await AmplitudePOST(DataJson2));
                    }
                }
                else
                {
                    NpgsqlCommand com = new NpgsqlCommand("update airlines set reporter=true where code=@ac", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
                    com.ExecuteNonQuery();
                    com.Dispose();

                    // отправляем событие «change reporters for ac» в амплитуд
                    string DataJson = "[{\"event_type\":\"change reporters for ac\"," +
                        "\"event_properties\":{\"ac\":\"" + ac + "\"," +
                        "\"new_status\":\"true\"}}]";
                    var task = Task.Run(async () => await AmplitudePOST(DataJson));
                }
            }
        }

        public static void UpdateUserAC(long id, string ac, string current_ac)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set own_ac=@own_ac where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
            com.ExecuteNonQuery();
            com.Dispose();

            UpdateAirlinesReporter(current_ac, AirlineAction.Delete);
            UpdateAirlinesReporter(ac, AirlineAction.Add);
        }

        public static void UpdateUserInCache(telegram_user user)
        {
            string keyuser = "teluser:" + user.id;
            cache.Add(keyuser, user, policyuser);
        }

        public static List<Request> SearchRequests()
        {
            List<Request> result = new List<Request>();

            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_request where request_status in (0, 3)", connProc);
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    string date_flight = reader["date_flight"].ToString();
                    string time_flight = reader["time_flight"].ToString();
                    string Id_reporter = reader["id_reporter"].ToString();
                    DateTime DepartureDateTime = new DateTime(int.Parse(date_flight.Substring(4, 2)) + 2000, int.Parse(date_flight.Substring(2, 2)), int.Parse(date_flight.Substring(0, 2)), int.Parse(time_flight.Substring(0, 2)), int.Parse(time_flight.Substring(2, 2)), 0);
                    Request request = new Request() { Id = (long)reader["id"], Id_group = (long)reader["id_group"], Version_request = (short)reader["version_request"], Id_requestor = reader["id_requestor"].ToString(), Origin = reader["origin"].ToString(), Destination = reader["destination"].ToString(), DepartureDateTime = DepartureDateTime, Operating = reader["operating"].ToString(), Number_flight = reader["number_flight"].ToString(), Desc_fligth = reader["desc_flight"].ToString(), Pax = (short)reader["pax"], TS_create = (DateTime)reader["ts_create"] };
                    if (!string.IsNullOrEmpty(Id_reporter))
                    {
                        request.Id_reporter = Id_reporter;
                    }

                    result.Add(request);
                }
            }
            com.Dispose();

            return result;
        }

        public static List<Request> SearchRequestsForCancel(CancelType type)
        {
            List<Request> result = new List<Request>();
            string sqltext = "";
            if (type == CancelType.Ready)
            {
                // Запросы для отмены
                sqltext = "select * from telegram_request where request_status=4 and coalesce(ts_change, now()) < now() - interval '" + Properties.Settings.Default.TimeoutReady + " minute'";
            }
            else if (type == CancelType.Take)
            {
                // Запросы для отмены
                sqltext = "select * from telegram_request where request_status=2 and coalesce(ts_change, now()) < now() - interval '" + Properties.Settings.Default.TimeoutProcess + " minute'";
            }
            else
            {
                // Запрос для закрытия
                sqltext = "select * from telegram_request where request_status in (1,3) and version_request=0 and ts_create < now() - interval '" + Properties.Settings.Default.TimeoutVoid + " minute'";
            }

            NpgsqlCommand com = new NpgsqlCommand(sqltext, connProc);
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    string date_flight = reader["date_flight"].ToString();
                    string time_flight = reader["time_flight"].ToString();
                    string Id_reporter = reader["id_reporter"].ToString();
                    DateTime DepartureDateTime = new DateTime(int.Parse(date_flight.Substring(4, 2)) + 2000, int.Parse(date_flight.Substring(2, 2)), int.Parse(date_flight.Substring(0, 2)), int.Parse(time_flight.Substring(0, 2)), int.Parse(time_flight.Substring(2, 2)), 0);
                    Request request = new Request() { Id = (long)reader["id"], Id_group = (long)reader["id_group"], Id_requestor = reader["id_requestor"].ToString(), Id_reporter = reader["id_reporter"].ToString(), Version_request = (short)reader["version_request"], Origin = reader["origin"].ToString(), Destination = reader["destination"].ToString(), DepartureDateTime = DepartureDateTime, Operating = reader["operating"].ToString(), Number_flight = reader["number_flight"].ToString(), Desc_fligth = reader["desc_flight"].ToString(), Source = (short)reader["source"], Push_id = reader["push_id"].ToString(), SubscribeTokens = (int)reader["subscribe_tokens"], PaidTokens = (int)reader["paid_tokens"] };
                    if (!string.IsNullOrEmpty(Id_reporter))
                    {
                        request.Id_reporter = Id_reporter;
                    }

                    result.Add(request);
                }
                reader.Close();
                reader.Dispose();
            }
            com.Dispose();

            return result;
        }

        public static void SetRequestStatus(short status, long id, string id_reporter)
        {
            if (status == 3)
            {
                id_reporter = null;
            }
            NpgsqlCommand com = new NpgsqlCommand("update telegram_request set request_status=@status, id_reporter=@id_reporter, ts_change=now() where id=@id", connProc);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "status", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = status });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_reporter });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static void SetRequestStatus(short status, Request req)
        {
            if (status == 6)
            {
                NpgsqlCommand com = new NpgsqlCommand("update telegram_request set request_status=6, ts_change=now() where id_group=@id_group", connProc);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_group", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = req.Id_group });
                com.ExecuteNonQuery();
                com.Dispose();
            }
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("update telegram_request set request_status=@status, ts_change=now() where id=@id", connProc);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "status", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = status });
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = req.Id });
                com.ExecuteNonQuery();
                com.Dispose();
            }
        }

        public static void SetCount(long id, PlaceType pt, int cnt)
        {
            string field = "";
            if (pt == PlaceType.Economy) field = "economy_count";
            else if (pt == PlaceType.Business) field = "business_count";
            else field = "sa_count";
            NpgsqlCommand com = new NpgsqlCommand("update telegram_request set " + field + "=@cnt where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "cnt", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = cnt });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static Request GetRequestStatus(long id)
        {
            Request result = new Request();
            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_request where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                if (reader.Read())
                {
                    result.Id = id;
                    result.Origin = reader["origin"].ToString();
                    result.Destination = reader["destination"].ToString();
                    result.Number_flight = reader["number_flight"].ToString();
                    var datef = reader["date_flight"].ToString();
                    var timef = reader["time_flight"].ToString();
                    result.DepartureDateTime = new DateTime(2000 + int.Parse(datef.Substring(4)), int.Parse(datef.Substring(2, 2)), int.Parse(datef.Substring(0, 2)), int.Parse(timef.Substring(0, 2)), int.Parse(timef.Substring(2)), 0);
                    result.Request_status = (short)reader["request_status"];
                    var ec = reader["economy_count"].ToString();
                    var eb = reader["business_count"].ToString();
                    var es = reader["sa_count"].ToString();
                    if (ec != "") result.Economy_count = int.Parse(ec);
                    if (eb != "") result.Business_count = int.Parse(eb);
                    if (es != "") result.SA_count = int.Parse(es);

                    result.Desc_fligth = reader["desc_flight"].ToString();
                    result.Source = short.Parse(reader["source"].ToString());
                    result.Push_id = reader["push_id"].ToString();
                    result.Id_requestor = reader["id_requestor"].ToString();
                    result.Id_reporter = reader["id_reporter"].ToString();
                    result.Id_group = (long)reader["id_group"];
                    result.Version_request = (short)reader["version_request"];
                    result.Operating = reader["operating"].ToString();
                    result.TS_create = (DateTime)reader["ts_create"];
                }
            }
            com.Dispose();

            return result;
        }

        public static bool TakeAvailable(string id_user)
        {
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_request where id_reporter=@id_user and request_status in (2, 4)", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = id_user });
            var cnt = (long)com.ExecuteScalar();
            com.Dispose();
            if (cnt == 0) return true;
            else return false;
        }

        public static List<long> GetReporters(string ac)
        {
            List<long> result = new List<long>();
            NpgsqlCommand com = new NpgsqlCommand("select id from telegram_user where id is not null and is_reporter=true and own_ac=@ac order by first_use", connProc);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add((long)reader["id"]);
                }
            }
            com.Dispose();
            return result;
        }

        public static ReporterGroup GetReporterGroup(List<long> all)
        {
            ReporterGroup result = new ReporterGroup() { Main = new List<long>(), Control = new List<long>() };
            if (all.Count <= 1)
            {
                result.Main.Add(all[0]);
            }
            else
            {
                var half = Convert.ToInt32(Math.Round(all.Count / 2.0));
                result.Main = all.GetRange(0, half);
                result.Control = all.GetRange(half, all.Count - half);
            }
            return result;
        }

        public static void SaveMessageParameters(long chat_id, int message_id, long request_id, short type)
        {
            NpgsqlCommand com = new NpgsqlCommand("insert into telegram_history (chat_id, message_id, request_id, type) values (@chat_id, @message_id, @request_id, @type)", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "chat_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = chat_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "message_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer, Value = message_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "request_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = request_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = type });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static List<TelMessage> GetMessageParameters(long request_id, short type)
        {
            List<TelMessage> result = new List<TelMessage>();
            NpgsqlCommand com = new NpgsqlCommand("select chat_id, message_id from telegram_history where request_id=@request_id and type=@type", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "request_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = request_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = type });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(new TelMessage() { ChatId = (long)reader["chat_id"], MessageId = (int)reader["message_id"] });
                }
            }
            com.Dispose();
            return result;
        }

        public static void DelMessageParameters(long request_id, short type)
        {
            NpgsqlCommand com = new NpgsqlCommand("delete from telegram_history where request_id=@request_id and type=@type", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "request_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = request_id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = type });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static void DelMessageParameters(long request_id)
        {
            NpgsqlCommand com = new NpgsqlCommand("delete from telegram_history where request_id=@request_id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "request_id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = request_id });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        // настройка клиента
        private static HttpClient GetClient()
        {
            HttpClientHandler handler = new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };

            HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "1233");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(
                        System.Text.ASCIIEncoding.ASCII.GetBytes(
                           $"{username}:{pwd}")));
            return client;
        }

        public static async Task<string> AmplitudePOST(string Data)
        {
            string result = null;
            var client = new RestClient("https://api.amplitude.com/httpapi");
            var request = new RestRequest((string)null, Method.Post);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddParameter("api_key", "95be547554caecf51c57c691bafb2640");
            request.AddParameter("event", Data);
            await client.ExecuteAsync(request).ContinueWith(t => { result = t.Result.Content; });
            return result;
        }

        public static string SendPushNotification(string applicationID, string senderId, string deviceId, string sub, string message, string Marketing, string Number, string Origin, string Destination, DateTime DepartureTime, short pax)
        {
            string result = "";
            try
            {
                string Alert = "Отправка пуша: " + message;
                result += Alert + " ";

                WebRequest tRequest = WebRequest.Create("https://fcm.googleapis.com/fcm/send");
                tRequest.Method = "post";
                tRequest.ContentType = "application/json";
                object data = new
                {
                    to = deviceId,
                    priority = "high",
                    direct_boot_ok = true,
                    notification = new
                    {
                        title = Marketing + Number + " " + Origin + "-" + Destination + " " + DepartureTime.ToString("dd-MMM HH:mm"),
                        body = sub + Environment.NewLine + message,
                        sound = "Enabled"
                    },
                    data = new
                    {
                        type = "agent",
                        origin = Origin,
                        destination = Destination,
                        departureDateTime = DepartureTime.ToString("yyyy-MM-ddTHH:mm:ss"), 
                        paxAmount = pax,
                        marketingCarrier = Marketing,
                        flightNumber = Number
                    }
                };

                string json = JsonConvert.SerializeObject(data);
                Byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(json);
                tRequest.Headers.Add(string.Format("Authorization: key={0}", applicationID));
                tRequest.Headers.Add(string.Format("Sender: id={0}", senderId));
                tRequest.ContentLength = byteArray.Length;

                using (Stream dataStream = tRequest.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                    using (WebResponse tResponse = tRequest.GetResponse())
                    {
                        using (Stream dataStreamResponse = tResponse.GetResponseStream())
                        {
                            using (StreamReader tReader = new StreamReader(dataStreamResponse))
                            {
                                string sResponseFromServer = tReader.ReadToEnd();
                                //string str = sResponseFromServer;

                                string Alert2 = "ResponseFromServer: " + sResponseFromServer;
                                result += Alert2 + " ";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string Alert = message + " - " + ex.Message + "..." + ex.StackTrace;
                result += Alert;
            }
            return result;
        }

        public static string PushStatusRequest(Request req, string status)
        {
            var message = "";
            string subtitle = null;
            if (status == "Processing is finished")
            {
                message += Environment.NewLine + "Economy:" + req.Economy_count.Value + " Business:" + req.Business_count.Value + " SA:" + req.SA_count.Value + " (agent just reported this)";

                var ocenka = (req.Economy_count ?? 0) + (req.Business_count ?? 0) - (req.SA_count ?? 0);
                RType Rating = ocenka <= 1 * req.Pax ? RType.Red : (ocenka < 5 + 2 * req.Pax ? RType.Yellow : RType.Green);
                subtitle = (Rating == RType.Green ? "Good" : (Rating == RType.Red ? "Bad" : "So-so"));
            }
            else
            {
                message = status;
            }

            string result = SendPushNotification(SERVER_API_KEY_NEW, SENDER_ID_NEW, req.Push_id, subtitle, message, req.Number_flight.Substring(0, 2), req.Number_flight.Substring(2), req.Origin, req.Destination, req.DepartureDateTime, req.Pax);
            return result;
        }

        public static async Task<TokenCollection> CredToken(string id_user)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/CredToken?id_user=" + id_user + "&type=oper&operation=reward&amount=0";
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        TokenCollection result = JsonConvert.DeserializeObject<TokenCollection>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new TokenCollection() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new TokenCollection();
        }

        public static async Task<ProfileTokens> GetProfile(string id_user)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/Profile?id_user=" + id_user;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        ProfileTokens result = JsonConvert.DeserializeObject<ProfileTokens>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ProfileTokens() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new ProfileTokens();
        }

        public static async Task<TokenCollection> PremiumSub(string id_user, int days)
        {
            TokenCollection result = new TokenCollection();

            using (HttpClient client = GetClient())
            {
                string Uri = Properties.Settings.Default.UrlApi + "/token/PremiumGrant?id_user=" + id_user + "&duration_days=" + days;
                var response = await client.GetAsync(Uri);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    result = JsonConvert.DeserializeObject<TokenCollection>(json);

                    var vpi = "";
                    if (days == 3)
                    {
                        vpi = "3_day_sub_agent";
                    }
                    else if (days == 7)
                    {
                        vpi = "1_week_sub_agent";
                    }
                    else
                    {
                        vpi = "1_month_sub_agent";
                    }

                    //агент купил подписку
                    string DataJson = "[{\"user_id\":\"" + id_user + "\", \"event_type\":\"tg agent buy subscription\"," +
                        "\"user_properties\":{\"paidStatus\":\"premiumAccess\"}," +
                        "\"event_properties\":{\"subscription\":\"" + vpi + "\"}}]";
                    var r = await AmplitudePOST(DataJson);
                }
            }

            return result;
        }

        public static async Task<FlightInfo> GetFlightInfo(string origin, string destination, DateTime date, int pax, string aircompany, string number, string token = "void token")
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/amadeus/GetFlightInfo?origin=" + origin + "&destination=" + destination + "&date=" + date.ToString("yyyy-MM-dd HH:mm") + "&pax=" + pax + "&aircompany=" + aircompany + "&number=" + number + "&now=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "&token=" + token;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        FlightInfo result = JsonConvert.DeserializeObject<FlightInfo>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new FlightInfo() { Alert = ex.Message + "..." + ex.StackTrace };
            }
            return new FlightInfo();
        }

        public static async Task<TokenCollection> DebtToken(string id_user, string type, string operation)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/DebtToken?id_user=" + id_user + "&type=" + type + "&operation=" + operation + "&amount=0";
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        TokenCollection result = JsonConvert.DeserializeObject<TokenCollection>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new TokenCollection() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new TokenCollection();
        }

        public static async Task<TokenCollection> CredToken(string id_user, string type, string operation, int amount)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    string Uri = Properties.Settings.Default.UrlApi + "/token/CredToken?id_user=" + id_user + "&type=" + type + "&operation=" + operation + "&amount=" + amount;
                    var response = await client.GetAsync(Uri);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        TokenCollection result = JsonConvert.DeserializeObject<TokenCollection>(json);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new TokenCollection() { Error = ex.Message + "..." + ex.StackTrace };
            }
            return new TokenCollection();
        }

        public static async Task<TokenCollection> ReturnToken(Request req)
        {
            try
            {
                using (HttpClient client = GetClient())
                {
                    TokenCollection res2 = null;

                    if (req.SubscribeTokens > 0)
                    {
                        string Uri = Properties.Settings.Default.UrlApi + "/token/CredToken?id_user=" + req.Id_requestor + "&type=amount&operation=subscribe&amount=" + req.SubscribeTokens;
                        var response = await client.GetAsync(Uri);
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            var res1 = JsonConvert.DeserializeObject<TokenCollection>(json);
                        }
                    }

                    if (req.PaidTokens > 0)
                    {
                        string Uri = Properties.Settings.Default.UrlApi + "/token/CredToken?id_user=" + req.Id_requestor + "&type=amount&operation=paid&amount=" + req.PaidTokens;
                        var response = await client.GetAsync(Uri);
                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            res2 = JsonConvert.DeserializeObject<TokenCollection>(json);
                        }
                    }

                    TokenCollection result = new TokenCollection() { SubscribeTokens = res2.SubscribeTokens, NonSubscribeTokens = res2.NonSubscribeTokens, DebtSubscribeTokens = req.SubscribeTokens, DebtNonSubscribeTokens = req.PaidTokens };
                    return result;
                }
            }
            catch (Exception ex) 
            {
                return new TokenCollection() { Error = ex.Message + "..." + ex.StackTrace };
            }
            //return new TokenCollection();
        }
    }
}
