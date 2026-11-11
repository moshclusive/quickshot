using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace QuickShot.Core
{
    public class HotkeySettings
    {
        public string CaptureRegion { get; set; } = "PrintScreen";
        public string CaptureScrolling { get; set; } = "";
        public string RecordGif { get; set; } = "";
        public string OcrSnip { get; set; } = "";
        public string StopRecording { get; set; } = "F9";
        public string PauseResumeRecording { get; set; } = "F10";
    }

    public class RectData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool IsValid => W > 8 && H > 8;
        public Rectangle ToRectangle() => new Rectangle(X, Y, W, H);
    }

    public class AppSettings
    {
        public string BackupDirectory { get; set; }
        public int BackupRetentionDays { get; set; } = 3;
        public bool AutoCopyClipboard { get; set; } = true;
        public bool AutoSaveFile { get; set; } = true;
        public bool ShowCursor { get; set; } = false;
        public int CaptureDelay { get; set; } = 0;
        public string NotificationStyle { get; set; } = "preview"; // "preview" | "minimal" | "none"
        public int NotificationDuration { get; set; } = 2;
        public bool QuickCapture { get; set; } = true; // Instant capture on drag release (zeros popups)
        public bool LaunchOnStartup { get; set; } = false;
        public RectData LastRegion { get; set; } = null;
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();

        public static string DefaultBackupDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "QuickShot");

        public static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QuickShot",
            "quickshot-settings.json");

        public static AppSettings CreateDefault()
        {
            return new AppSettings
            {
                BackupDirectory = DefaultBackupDir,
                BackupRetentionDays = 3,
                AutoCopyClipboard = true,
                AutoSaveFile = true,
                ShowCursor = false,
                CaptureDelay = 0,
                NotificationStyle = "preview",
                NotificationDuration = 2,
                QuickCapture = true,
                LaunchOnStartup = IsStartupEnabled(),
                Hotkeys = new HotkeySettings()
            };
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.BackupDirectory))
                            loaded.BackupDirectory = DefaultBackupDir;
                        if (loaded.Hotkeys == null)
                            loaded.Hotkeys = new HotkeySettings();
                        if (loaded.NotificationDuration <= 0)
                            loaded.NotificationDuration = 2;
                        if (string.IsNullOrWhiteSpace(loaded.NotificationStyle))
                            loaded.NotificationStyle = "preview";
                        loaded.AutoCopyClipboard = true;
                        loaded.AutoSaveFile = true;
                        loaded.QuickCapture = true;
                        loaded.ShowCursor = false;
                        loaded.CaptureDelay = 0;
                        loaded.LaunchOnStartup = IsStartupEnabled();
                        return loaded;
                    }
                }
            }
            catch { }
            var def = CreateDefault();
            def.Save();
            return def;
        }

        public void Save()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(BackupDirectory))
                    BackupDirectory = DefaultBackupDir;
                string dir = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(ConfigFilePath, FormatJsonPretty(json));
            }
            catch { }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("QuickShot") != null;
            }
            catch { return false; }
        }

        public static void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue("QuickShot", $"\"{exePath}\" --tray");
                }
                else
                {
                    key.DeleteValue("QuickShot", false);
                }
            }
            catch { }
        }

        private static string FormatJsonPretty(string json)
        {
            // Minimal safe indentation for readable settings JSON
            var sb = new System.Text.StringBuilder();
            int indent = 0;
            bool inQuotes = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inQuotes = !inQuotes;
                if (!inQuotes)
                {
                    if (c == '{' || c == '[')
                    {
                        sb.Append(c).AppendLine().Append(new string(' ', ++indent * 2));
                        continue;
                    }
                    if (c == '}' || c == ']')
                    {
                        sb.AppendLine().Append(new string(' ', --indent * 2)).Append(c);
                        continue;
                    }
                    if (c == ',')
                    {
                        sb.Append(c).AppendLine().Append(new string(' ', indent * 2));
                        continue;
                    }
                    if (c == ':')
                    {
                        sb.Append(": ");
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

