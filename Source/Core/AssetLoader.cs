using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace QuickShot.Core
{
    /// <summary>
    /// Loads assets cleanly from disk (if present) or embedded assembly resources
    /// so icons, SVGs, and UI never break even if QuickShot.exe is moved standalone.
    /// </summary>
    public static class AssetLoader
    {
        private static readonly Assembly Asm = typeof(AssetLoader).Assembly;
        private static readonly string[] ResourceNames = Asm.GetManifestResourceNames();

        public static Stream OpenAssetStream(params string[] relativeParts)
        {
            string relPath = Path.Combine(relativeParts);
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath);
            if (File.Exists(diskPath))
            {
                return new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }

            string suffix = "." + string.Join(".", relativeParts).Replace('\\', '.').Replace('/', '.');
            string match = ResourceNames.FirstOrDefault(r => r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return Asm.GetManifestResourceStream(match);
            }
            return null;
        }

        public static string ReadAssetText(params string[] relativeParts)
        {
            using var s = OpenAssetStream(relativeParts);
            if (s == null) return null;
            using var sr = new StreamReader(s, System.Text.Encoding.UTF8);
            return sr.ReadToEnd();
        }

        public static byte[] ReadAssetBytes(params string[] relativeParts)
        {
            using var s = OpenAssetStream(relativeParts);
            if (s == null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public static Icon LoadAppIcon(int size = 32)
        {
            try
            {
                using var s = OpenAssetStream("assets", "Icon", "icon.ico");
                if (s != null) return new Icon(s, size, size);
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Bitmap LoadPngBitmap(params string[] relativeParts)
        {
            try
            {
                using var s = OpenAssetStream(relativeParts);
                if (s == null) return null;
                using var tmp = Image.FromStream(s);
                return new Bitmap(tmp);
            }
            catch { return null; }
        }
    }
}

