﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class SpecialUsersCollection
    {
        private JObject json;
        private string path;

        public ulong Owner { get => json["owner"].Value<ulong>(); }
        private JArray BannedUsers { get => json["banned"] as JArray; }

        public SpecialUsersCollection(string path)
        {
            if (File.Exists(path))
                json = JObject.Parse(File.ReadAllText(path));
            else
                json = new JObject();

            if (json["owner"] == null)
                json["owner"] = 0ul;
            if (json["banned"] == null)
                json["banned"] = new JArray();

            this.path = path;
        }

        public bool IsUserBanned(ulong userID)
        {
            return userID != Owner && BannedUsers.FirstOrDefault(
                (t) => t.ToString() == userID.ToString()) != null;
        }
        public bool BanUser(ulong userID)
        {
            if (!IsUserBanned(userID))
            {
                BannedUsers.Add(userID);
                Save();
                return true;
            }
            return false;
        }
        public bool UnbanUser(ulong userID)
        {
            if (IsUserBanned(userID))
            {
                BannedUsers.FirstOrDefault((t) => t.ToString() == userID.ToString()).Remove();
                Save();
                return true;
            }
            return false;
        }

        private void Save()
        {
            File.WriteAllText(path, json.ToString());
        }
    }
}
