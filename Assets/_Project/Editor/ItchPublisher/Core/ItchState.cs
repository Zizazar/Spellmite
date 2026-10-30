using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GameJam.ItchPublisher
{
    /// <summary>What happened to one channel locally and on itch.io.</summary>
    [Serializable]
    public class ChannelRecord
    {
        public string channel;

        public string lastBuildUtc;
        public bool lastBuildOk;
        public long lastBuildBytes;
        public float lastBuildSeconds;
        public string lastBuildVersion;
        public string lastBuildError;
        public bool lastBuildDevelopment;

        // Last upload attempt (a skipped "unchanged" push counts as an attempt).
        public string lastPushUtc;
        public bool lastPushOk;
        public bool lastPushSkipped;
        public string lastPushVersion;
        public string lastPushError;

        // Last push that really sent a new build to itch.io.
        public string lastUploadedUtc;
        public string lastUploadedVersion;

        public string remoteVersion;
        public string remoteBuild;
        public string remoteUpload;
    }

    [Serializable]
    public class HistoryEntry
    {
        public string utc;
        public string action;
        public string version;
        public bool success;
        public bool dryRun;
        public float seconds;
        public string summary;
        public List<string> details = new();
    }

    /// <summary>
    /// Local, per-user state (last builds, upload results, history):
    /// UserSettings/ItchPublisher.json, not committed. Main thread only.
    /// </summary>
    public static class ItchState
    {
        [Serializable]
        class Data
        {
            public List<ChannelRecord> channels = new();
            public List<HistoryEntry> history = new();
            public string remoteCheckedUtc;
            public string remoteError;
        }

        const int MaxHistory = 200;
        static readonly string FilePath = Path.Combine(ItchProjectSettings.ProjectRoot, "UserSettings", "ItchPublisher.json");
        static Data data;

        public static event Action Changed;

        static Data D
        {
            get
            {
                if (data != null) return data;
                try
                {
                    if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Itch] Couldn't read " + FilePath + ": " + e.Message);
                }
                return data ??= new Data();
            }
        }

        public static IReadOnlyList<HistoryEntry> History => D.history;

        public static DateTime RemoteCheckedUtc => ParseUtc(D.remoteCheckedUtc);

        public static string RemoteError => D.remoteError;

        public static ChannelRecord Get(string channel)
        {
            var record = D.channels.FirstOrDefault(c => c.channel == channel);
            if (record != null) return record;
            record = new ChannelRecord { channel = channel };
            D.channels.Add(record);
            return record;
        }

        public static ChannelRecord Find(string channel) => D.channels.FirstOrDefault(c => c.channel == channel);

        public static void SetRemote(List<ChannelStatus> remote, string error)
        {
            D.remoteError = error;
            if (remote != null)
            {
                D.remoteCheckedUtc = FormatUtc(DateTime.UtcNow);
                foreach (var record in D.channels)
                {
                    record.remoteVersion = record.remoteBuild = record.remoteUpload = null;
                }
                foreach (var status in remote)
                {
                    var record = Get(status.channel);
                    record.remoteVersion = status.version;
                    record.remoteBuild = status.build;
                    record.remoteUpload = status.upload;
                }
            }
            Save();
        }

        /// <summary>Channels that exist on itch.io but aren't configured in this project.</summary>
        public static IEnumerable<ChannelRecord> RemoteOnly(IEnumerable<string> configured)
        {
            var set = new HashSet<string>(configured);
            return D.channels.Where(c => !set.Contains(c.channel) && !string.IsNullOrEmpty(c.remoteUpload));
        }

        public static void AddHistory(HistoryEntry entry)
        {
            D.history.Insert(0, entry);
            if (D.history.Count > MaxHistory) D.history.RemoveRange(MaxHistory, D.history.Count - MaxHistory);
            Save();
        }

        public static void ClearHistory()
        {
            D.history.Clear();
            Save();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonUtility.ToJson(D, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Itch] Couldn't save " + FilePath + ": " + e.Message);
            }
            Changed?.Invoke();
        }

        public static string FormatUtc(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

        public static DateTime ParseUtc(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToUniversalTime()
                : DateTime.MinValue;

        public static string Ago(string utcText)
        {
            var t = ParseUtc(utcText);
            return t == DateTime.MinValue ? "never" : Ago(t);
        }

        public static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} d ago";
            return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        public static string Size(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
