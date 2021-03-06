﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    static class PR2_Utilities
    {
        const string keysPath = "files/keys.txt";

        private static HttpClient httpClient = new HttpClient();
        const string getPMUrl = "https://pr2hub.com/messages_get.php?count=999";
        const string serverStatusUrl = "http://pr2hub.com/files/server_status_2.txt";
        const string loginUrl = "https://pr2hub.com/login.php";

        public static string[] groups = new string[] { "Guest", "Member", "Moderator", "Admin" };

        #region "Encryption"
        private static ICryptoTransform AESEncryptor(string Key, string IV)
        {
            AesManaged aes = new AesManaged();
            aes.Key = Convert.FromBase64String(Key);
            aes.IV = Convert.FromBase64String(IV);
            aes.Padding = PaddingMode.Zeros;
            return aes.CreateEncryptor();
        }
        private static byte[] Crypt(ICryptoTransform cryptor, byte[] cipherData)
        {
            MemoryStream ms = new MemoryStream();
            CryptoStream cs = new CryptoStream(ms, cryptor, CryptoStreamMode.Write);
            cs.Write(cipherData, 0, cipherData.Length);
            cs.Close();
            byte[] decryptedData = ms.ToArray();
            return decryptedData;
        }
        private static string Encrypt(ICryptoTransform encryptor, string data)
        {
            return Convert.ToBase64String(Crypt(encryptor, Encoding.UTF8.GetBytes(data)));
        }

        public static string EncryptLoginData(string data)
        {
            JObject obj = JObject.Parse(File.ReadAllText(keysPath));
            ICryptoTransform encryptor = AESEncryptor((string)obj["login_key"], (string)obj["login_iv"]);
            return Encrypt(encryptor, data);
        }
        #endregion

        public async static Task<string> ObtainLoginToken(string username, string password)
        {
            string version = "24-dec-2013-v1", login_code = "eisjI1dHWG4vVTAtNjB0Xw";
            JObject loginData = new JObject();

            JObject server = new JObject();
            server["server_id"] = 1;
            loginData["user_name"] = username;
            loginData["user_pass"] = password;
            loginData["server"] = server;
            loginData["version"] = version;
            loginData["remember"] = true;
            loginData["domain"] = "cdn.jiggmin.com";
            loginData["login_code"] = login_code;

            string str = loginData.ToString();
            string loginData_encrypted = EncryptLoginData(str);

            Dictionary<string, string> values = new Dictionary<string, string>();
            values.Add("i", loginData_encrypted);
            values.Add("version", version);
            FormUrlEncodedContent content = new FormUrlEncodedContent(values);
            HttpResponseMessage response = await httpClient.PostAsync(loginUrl, content);

            JObject jResponse = JObject.Parse(await response.Content.ReadAsStringAsync());
            if ((string)jResponse["status"] != "success")
                return null;

            return (string)jResponse["token"];
        }

        public delegate void HHList(List<JToken> list);
        public static event HHList HHStarted;
        public static bool watchingHH { get; private set; }
        public static void BeginWatchingHH()
        {
            if (!watchingHH)
            {
                watchingHH = true;
                Thread t = new Thread(WatchHH);
                t.Start();
            }
        }
        public static void StopWatchingHH()
        {
            watchingHH = false;
        }
        private static async void WatchHH()
        {
            HashSet<int> happyServers = new HashSet<int>();
            while (watchingHH)
            {
                JObject servers = await GetServers();
                IEnumerable<JToken> currentHappyServers = servers["servers"].Where((t) => (int)t["happy_hour"] == 1);

                List<JToken> newHappyServers = new List<JToken>();
                foreach (JToken server in currentHappyServers)
                {
                    if (!happyServers.Contains((int)server["server_id"]))
                        newHappyServers.Add(server);
                }
                bool hhChanged = newHappyServers.Count > 0 || happyServers.Count != currentHappyServers.Count();
                happyServers = currentHappyServers.Select((s) => (int)s["server_id"]).ToHashSet();

                if (newHappyServers.Count > 0)
                    HHStarted.Invoke(newHappyServers);

                // Wait one minute before checking again. Admins can start HHs whenever they want.
                Thread.Sleep(1000 * 60);
            }

        }

        #region "get info"
        public static bool IsResponseNotLoggedInError(JObject jObject)
        {
            return jObject.ContainsKey("error") &&
                ((string)jObject["error"] == "You are not logged in." || ((string)jObject["error"]).StartsWith("No token found."));
        }

        private static char[] validUsernameChars = new char[] { ' ', '!', '-', '.', ':', ';', '=', '?', '~' };
        private static char[] previouslyValidUsernameChars = new char[] { '#', '*', '(', ')', '<', '>' };
        public static bool IsUsernameValid(string username)
        {
            if (username == null || username.Length < 1 || username.Length > 20)
            {
                return false;
            }

            for (int i = 0; i < username.Length; i++)
            {
                if (!char.IsLetterOrDigit(username[i]) && !validUsernameChars.Contains(username[i]) && !previouslyValidUsernameChars.Contains(username[i]))
                    return false;
            }

            return true;
        }

        public async static Task<JObject> ViewPlayer(string username)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            values.Add("name", username);
            FormUrlEncodedContent content = new FormUrlEncodedContent(values);
            HttpResponseMessage response = await httpClient.PostAsync("http://pr2hub.com/get_player_info_2.php", content);

            string str = await response.Content.ReadAsStringAsync();
            str = str.Replace("&lt;", "<").Replace("&gt;", ">");
            return JObject.Parse(str);
        }

        public async static Task<JObject> GetArtifactHint()
        {
            HttpResponseMessage response = await httpClient.GetAsync("http://pr2hub.com/files/artifact_hint.txt");

            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        public async static Task<JObject> GetPrivateMessages(string pr2_token)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            values.Add("token", pr2_token);
            FormUrlEncodedContent content = new FormUrlEncodedContent(values);
            HttpResponseMessage response = await httpClient.PostAsync(getPMUrl, content);

            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        public async static Task<JObject> GetServers()
        {
            HttpResponseMessage response = await httpClient.GetAsync(serverStatusUrl);
            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        public static int ExpToRankUpFrom(int rank)
        {
            if (rank == 0)
                return 1;

            double exp = 30 * Math.Pow(1.25, rank);
            if (exp > int.MaxValue)
                return -1;
            else
                return (int)Math.Round(exp);
        }
        public static long ExpFromRankTo(int from, int to)
        {
            if (from >= to || to > 82 || from < 0) // cannot rank up past 82 due to int.MaxValue
                return -1;

            long totalExp = 0;
            if (from == 0)
            {
                totalExp = 1;
                from = 1;
            }

            double exp = 30 * Math.Pow(1.25, from - 1);
            while (from < to)
            {
                exp *= 1.25;
                totalExp += (long)Math.Round(exp);
                from++;
            }

            return totalExp;
        }
        #endregion
    }
}
