using Npgsql;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
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
        public static NpgsqlConnection conn = new NpgsqlConnection("Server = localhost; Port = 5432; User Id = postgres; Password = OVBtoBAX1972; Database = sae");

        public static telegram_user ProfileCommand(long id, string strtoken, EventLog eventLogBot, telegram_user user, out string message)
        {
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
                    NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id=@id", conn);
                    com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                    try
                    {
                        NpgsqlDataReader reader = com.ExecuteReader();

                        if (reader.Read())
                        {
                            user = new telegram_user() { id = (long)reader["id"], first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), is_reporter = true, is_requestor = (bool)reader["is_requestor"], Token = token };

                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            NpgsqlCommand com3 = new NpgsqlCommand("update telegram_user set is_reporter=@is_reporter, type=@type, id_user=@id_user where id=@id", conn);
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = token.type });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.id_user });

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
                        }
                        else
                        {
                            reader.Close();
                            reader.Dispose();
                            com.Dispose();

                            NpgsqlCommand com2 = new NpgsqlCommand("insert into telegram_user (id, first_use, own_ac, is_reporter, is_requestor, type, id_user) values (@id, @first_use, @own_ac, @is_reporter, @is_requestor, @type, @id_user)", conn);
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "first_use", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = DateTime.Now });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = "??" });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = false });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = token.type });
                            com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.id_user });

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

                            user = new telegram_user() { id = id, first_use = DateTime.Now, own_ac = "??", is_reporter = true, is_requestor = false, Token = token };
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

            /*if (user != null && user.own_ac != "??")
            {
                var permitted = GetPermittedAC(user.own_ac);
                user.permitted_ac = string.Join("-", permitted.Select(p => p.Permit));
            }*/

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
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_user where id<>@id and type=@type and id_user=@id_user", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = telegram_user });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "type", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = token.type });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_user", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = token.id_user });
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

        /*public static telegram_user AddNewRequestor(long id)
        {
            telegram_user result = new telegram_user();

            string keyuser = "teluser:" + id;
            var userexist = cache.Contains(keyuser);
            if (userexist) result = (telegram_user)cache.Get(keyuser);
            else
            {
                NpgsqlCommand com = new NpgsqlCommand("select * from telegram_user where id=@id", conn);
                //eventLogBot.WriteEntry("Sql: " + com.CommandText + ", id=" + id);
                com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                try
                {
                    NpgsqlDataReader reader = com.ExecuteReader();

                    if (reader.Read())
                    {
                        result = new telegram_user() { id = (long)reader["id"], first_use = (DateTime)reader["first_use"], own_ac = reader["own_ac"].ToString(), is_reporter = (bool)reader["is_reporter"], is_requestor = (bool)reader["is_requestor"] };

                        //eventLogBot.WriteEntry("user exist: " + result.id + "-" + result.own_ac + "-" + result.is_requestor + "-" + result.is_reporter);

                        reader.Close();
                        reader.Dispose();
                        com.Dispose();

                        if (!result.is_reporter)
                        {
                            NpgsqlCommand com3 = new NpgsqlCommand("update telegram_user set is_reporter=@is_reporter where id=@id", conn);
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                            com3.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                            com3.ExecuteNonQuery();

                            result.is_reporter = true;
                        }
                    }
                    else
                    {
                        reader.Close();
                        reader.Dispose();
                        com.Dispose();

                        NpgsqlCommand com2 = new NpgsqlCommand("insert into telegram_user (id, first_use, own_ac, is_reporter, is_requestor) values (@id, @first_use, @own_ac, @is_reporter, @is_requestor)", conn);
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "first_use", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp, Value = DateTime.Now });
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = "??" });
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = true });
                        com2.Parameters.Add(new NpgsqlParameter() { ParameterName = "is_requestor", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean, Value = false });
                        try
                        {
                            com2.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            var e = ex.StackTrace;
                        }
                        com2.Dispose();

                        result = new telegram_user() { id = id, first_use = DateTime.Now, own_ac = "??", is_reporter = false, is_requestor = true };

                        //eventLogBot.WriteEntry("new user: " + result.id + "-" + result.own_ac + "-" + result.is_requestor + "-" + result.is_reporter);
                    }
                }
                catch (Exception ex)
                {
                    string s = "123";
                    //eventLogBot.WriteEntry("Error: " + ex.Message + "..." + ex.StackTrace);
                }

                if (result.own_ac != "??")
                {
                    var permitted = GetPermittedAC(result.own_ac);
                    result.permitted_ac = string.Join("-", permitted.Select(p => p.Permit));
                }
            }

            cache.Add(keyuser, result, policyuser);
            return result;
        }*/

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

        public static int TestAC(string ac)
        {
            int result = 0;
            NpgsqlCommand com = new NpgsqlCommand("select * from airlines where code=@ac", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = ac });
            try
            {
                NpgsqlDataReader reader = com.ExecuteReader();
                if (reader.Read())
                {
                    result = (int)reader["id"];
                }

                reader.Close();
                reader.Dispose();
                com.Dispose();

                return result;
            }
            catch (Exception ex)
            {
                string s = "123";
                return 0;
            }
        }

        public static void UpdateUserAC(long id, string ac)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_user set own_ac=@own_ac where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "own_ac", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = ac });
            try
            {
                com.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                string s = "123";
            }
            com.Dispose();
        }

        public static void UpdateUserInCache(telegram_user user)
        {
            string keyuser = "teluser:" + user.id;
            cache.Add(keyuser, user, policyuser);
        }

        public static List<Request> SearchRequests()
        {
            List<Request> result = new List<Request>();

            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_request where request_status in (0, 3)", conn);
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    string date_flight = reader["date_flight"].ToString();
                    string time_flight = reader["time_flight"].ToString();
                    string Id_reporter = reader["id_reporter"].ToString();
                    DateTime DepartureDateTime = new DateTime(int.Parse(date_flight.Substring(4, 2)) + 2000, int.Parse(date_flight.Substring(2, 2)), int.Parse(date_flight.Substring(0, 2)), int.Parse(time_flight.Substring(0, 2)), int.Parse(time_flight.Substring(2, 2)), 0);
                    Request request = new Request() { Id = (long)reader["id"], Id_requestor = (long)reader["id_requestor"], Origin = reader["origin"].ToString(), Destination = reader["destination"].ToString(), DepartureDateTime = DepartureDateTime, Number_flight = reader["number_flight"].ToString(), Desc_fligth = reader["desc_flight"].ToString() };
                    if (!string.IsNullOrEmpty(Id_reporter))
                    {
                        request.Id_reporter = long.Parse(Id_reporter);
                    }

                    result.Add(request);
                }
            }
            com.Dispose();

            return result;
        }

        public static List<Request> SearchRequestsForCancel()
        {
            List<Request> result = new List<Request>();

            NpgsqlCommand com = new NpgsqlCommand("select * from telegram_request where request_status in (2,4) and coalesce(ts_change, now()) < now() - interval '15 minute'", conn);
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    string date_flight = reader["date_flight"].ToString();
                    string time_flight = reader["time_flight"].ToString();
                    string Id_reporter = reader["id_reporter"].ToString();
                    DateTime DepartureDateTime = new DateTime(int.Parse(date_flight.Substring(4, 2)) + 2000, int.Parse(date_flight.Substring(2, 2)), int.Parse(date_flight.Substring(0, 2)), int.Parse(time_flight.Substring(0, 2)), int.Parse(time_flight.Substring(2, 2)), 0);
                    Request request = new Request() { Id = (long)reader["id"], Id_requestor = (long)reader["id_requestor"], Origin = reader["origin"].ToString(), Destination = reader["destination"].ToString(), DepartureDateTime = DepartureDateTime, Number_flight = reader["number_flight"].ToString(), Desc_fligth = reader["desc_flight"].ToString() };
                    if (!string.IsNullOrEmpty(Id_reporter))
                    {
                        request.Id_reporter = long.Parse(Id_reporter);
                    }

                    result.Add(request);
                }
            }
            com.Dispose();

            return result;
        }

        public static void SetRequestStatus(short status, long id, long id_reporter)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_request set request_status=@status, id_reporter=@id_reporter, ts_change=now() where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "status", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = status });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id_reporter", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id_reporter });
            com.ExecuteNonQuery();
            com.Dispose();
        }

        public static void SetRequestStatus(short status, long id)
        {
            NpgsqlCommand com = new NpgsqlCommand("update telegram_request set request_status=@status, ts_change=now() where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "status", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Smallint, Value = status });
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            com.ExecuteNonQuery();
            com.Dispose();
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
            NpgsqlCommand com = new NpgsqlCommand("select request_status, economy_count, business_count, sa_count, desc_flight from telegram_request where id=@id", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            using (NpgsqlDataReader reader = com.ExecuteReader())
            {
                if (reader.Read())
                {
                    result.Request_status = (short)reader["request_status"];
                    var ec = reader["economy_count"].ToString();
                    var eb = reader["business_count"].ToString();
                    var es = reader["sa_count"].ToString();
                    if (ec != "") result.Economy_count = int.Parse(ec);
                    if (eb != "") result.Business_count = int.Parse(eb);
                    if (es != "") result.SA_count = int.Parse(es);

                    result.Desc_fligth = reader["desc_flight"].ToString();
                }
            }
            com.Dispose();

            return result;
        }

        public static bool TakeAvailable(long id)
        {
            NpgsqlCommand com = new NpgsqlCommand("select count(*) from telegram_request where id_reporter=@id and request_status in (2, 4)", conn);
            com.Parameters.Add(new NpgsqlParameter() { ParameterName = "id", NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = id });
            var cnt = (long)com.ExecuteScalar();
            com.Dispose();
            if (cnt == 0) return true;
            else return false;
        }

        public static List<long> GetReporters(string ac)
        {
            List<long> result = new List<long>();
            NpgsqlCommand com = new NpgsqlCommand("select id from telegram_user where is_reporter=true and own_ac=@ac", conn);
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
    }
}
