using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QuickShot.Core
{
    public class CaptureHistoryItem
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string DateFolder { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Ext { get; set; }
        public bool IsGif => string.Equals(Ext, "gif", StringComparison.OrdinalIgnoreCase);
    }

    public class CaptureResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Ext { get; set; }
        public bool IsGif => string.Equals(Ext, "gif", StringComparison.OrdinalIgnoreCase);
        public Image PreviewImage { get; set; }
    }

    public class StorageService
    {
        private AppSettings _settings;

        public StorageService(AppSettings settings)
        {
            _settings = settings;
            EnsureDirectory(GetScreenshotsDir());
            CleanupOldBackups();
        }

        public AppSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                EnsureDirectory(GetScreenshotsDir());
                CleanupOldBackups();
            }
        }

        public string GetScreenshotsDir()
        {
            string dir = string.IsNullOrWhiteSpace(_settings?.BackupDirectory)
                ? AppSettings.DefaultBackupDir
                : _settings.BackupDirectory;
            EnsureDirectory(dir);
            return dir;
        }

        public static void EnsureDirectory(string dir)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static string GetDateFolder() => DateTime.Now.ToString("yyyy-MM-dd");

        public static string FormatTimestamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff");

        public void CleanupOldBackups()
        {
            try
            {
                int retentionDays = _settings?.BackupRetentionDays ?? 3;
                if (retentionDays <= 0) return;

                string baseDir = GetScreenshotsDir();
                if (!Directory.Exists(baseDir)) return;

                DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (string subDir in Directory.GetDirectories(baseDir))
                {
                    string name = Path.GetFileName(subDir);
                    if (Regex.IsMatch(name, @"^\d{4}-\d{2}-\d{2}$"))
                    {
                        if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime folderDate))
                        {
                            if (folderDate.Date.AddDays(1).AddSeconds(-1) < cutoff)
                            {
                                try { Directory.Delete(subDir, true); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public CaptureResult SaveImageCapture(Bitmap bmp, Rectangle? cropBounds = null, bool copyClipboardOverride = false)
        {
            if (bmp == null) throw new ArgumentNullException(nameof(bmp));

            if (_settings.AutoCopyClipboard || copyClipboardOverride)
            {
                CopyImageToClipboard(bmp);
            }

            string targetDir = Path.Combine(GetScreenshotsDir(), GetDateFolder());
            EnsureDirectory(targetDir);
            string fileName = $"QuickShot_{FormatTimestamp()}.png";
            string filePath = Path.Combine(targetDir, fileName);

            bmp.Save(filePath, ImageFormat.Png);
            long size = new FileInfo(filePath).Length;

            if (cropBounds.HasValue && cropBounds.Value.Width > 8 && cropBounds.Value.Height > 8)
            {
                _settings.LastRegion = new RectData
                {
                    X = cropBounds.Value.X,
                    Y = cropBounds.Value.Y,
                    W = cropBounds.Value.Width,
                    H = cropBounds.Value.Height
                };
                _settings.Save();
            }

            CleanupOldBackups();

            return new CaptureResult
            {
                Success = true,
                FilePath = filePath,
                FileName = fileName,
                FileSize = size,
                Ext = "png",
                PreviewImage = (Bitmap)bmp.Clone()
            };
        }

        public CaptureResult SaveGifCapture(byte[] gifBytes, Rectangle? cropBounds = null)
        {
            if (gifBytes == null || gifBytes.Length == 0) throw new ArgumentException("Empty GIF data");

            string targetDir = Path.Combine(GetScreenshotsDir(), GetDateFolder());
            EnsureDirectory(targetDir);
            string fileName = $"QuickShot_{FormatTimestamp()}.gif";
            string filePath = Path.Combine(targetDir, fileName);

            File.WriteAllBytes(filePath, gifBytes);
            long size = new FileInfo(filePath).Length;

            if (_settings.AutoCopyClipboard)
            {
                CopyFileDropToClipboard(filePath);
            }

            if (cropBounds.HasValue && cropBounds.Value.Width > 8 && cropBounds.Value.Height > 8)
            {
                _settings.LastRegion = new RectData
                {
                    X = cropBounds.Value.X,
                    Y = cropBounds.Value.Y,
                    W = cropBounds.Value.Width,
                    H = cropBounds.Value.Height
                };
                _settings.Save();
            }

            CleanupOldBackups();

            Image thumb = null;
            try
            {
                using var ms = new MemoryStream(gifBytes);
                using var loaded = Image.FromStream(ms);
                thumb = new Bitmap(loaded);
            }
            catch { }

            return new CaptureResult
            {
                Success = true,
                FilePath = filePath,
                FileName = fileName,
                FileSize = size,
                Ext = "gif",
                PreviewImage = thumb
            };
        }

        public static void CopyImageToClipboard(Image img)
        {
            if (img == null) return;
            try
            {
                Clipboard.SetImage(img);
            }
            catch { }
        }

        public static void CopyFileDropToClipboard(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            try
            {
                var sc = new StringCollection { filePath };
                Clipboard.SetFileDropList(sc);
            }
            catch { }
        }

        public static bool CopyExistingFileToClipboard(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                if (filePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    CopyFileDropToClipboard(filePath);
                    return true;
                }
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var img = Image.FromStream(fs);
                Clipboard.SetImage(new Bitmap(img));
                return true;
            }
            catch { return false; }
        }

        public static void CopyTextToClipboard(string text)
        {
            if (text == null) return;
            try { Clipboard.SetText(text); } catch { }
        }

        public List<CaptureHistoryItem> GetHistory()
        {
            var list = new List<CaptureHistoryItem>();
            string baseDir = GetScreenshotsDir();
            if (!Directory.Exists(baseDir)) return list;

            var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
            try
            {
                foreach (string folder in Directory.GetDirectories(baseDir).OrderByDescending(d => d))
                {
                    string folderName = Path.GetFileName(folder);
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        string ext = Path.GetExtension(file);
                        if (!allowedExts.Contains(ext)) continue;
                        try
                        {
                            var fi = new FileInfo(file);
                            list.Add(new CaptureHistoryItem
                            {
                                FilePath = file,
                                FileName = fi.Name,
                                DateFolder = folderName,
                                FileSize = fi.Length,
                                CreatedAt = fi.LastWriteTime,
                                Ext = ext.TrimStart('.').ToLowerInvariant()
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return list.OrderByDescending(x => x.CreatedAt).ToList();
        }

        public bool DeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void ShowInFolder(string filePath = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                else
                {
                    Process.Start("explorer.exe", $"\"{GetScreenshotsDir()}\"");
                }
            }
            catch { }
        }

        public void OpenFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}

