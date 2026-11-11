using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickShot.Core
{
    public static class Theme
    {
        private static readonly object _fontLock = new object();
        private static PrivateFontCollection _pfc;
        private static FontFamily _interFamily;
        private static readonly List<IntPtr> _pinnedFontPtrs = new List<IntPtr>();
        private static readonly List<IntPtr> _gdiFontHandles = new List<IntPtr>();
        private static bool _fontsInitAttempted;

        public static readonly Color BgWindow = Color.FromArgb(18, 18, 20);      // #121214
        public static readonly Color BgHeader = Color.FromArgb(24, 24, 28);      // #18181c
        public static readonly Color BgCard = Color.FromArgb(26, 26, 31);        // #1a1a1f
        public static readonly Color BgCardHover = Color.FromArgb(34, 34, 42);   // #22222a
        public static readonly Color BgInput = Color.FromArgb(14, 14, 17);       // #0e0e11
        public static readonly Color BgControl = Color.FromArgb(36, 36, 42);     // #24242a

        public static readonly Color Brand = Color.FromArgb(154, 127, 245);      // #9a7ff5
        public static readonly Color BrandDark = Color.FromArgb(124, 92, 230);   // #7c5ce6
        public static readonly Color BrandSoft = Color.FromArgb(38, 154, 127, 245);

        public static readonly Color BorderSubtle = Color.FromArgb(28, 255, 255, 255);
        public static readonly Color BorderCard = Color.FromArgb(51, 51, 60);
        public static readonly Color TextPrimary = Color.FromArgb(241, 245, 249); // #f1f5f9
        public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184); // #94a3b8
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);   // #64748b

        public static readonly Color Danger = Color.FromArgb(239, 68, 68);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color Success = Color.FromArgb(16, 185, 129);

        public static FontFamily FontFamily
        {
            get
            {
                EnsureFontsLoaded();
                return _interFamily ?? System.Drawing.FontFamily.GenericSansSerif;
            }
        }

        public static void EnsureFontsLoaded()
        {
            if (_fontsInitAttempted && _interFamily != null) return;
            lock (_fontLock)
            {
                if (_fontsInitAttempted) return;
                _fontsInitAttempted = true;
                try
                {
                    _pfc = new PrivateFontCollection();
                    string[] fontFiles =
                    {
                        "Inter-Regular.ttf",
                        "Inter-Bold.ttf"
                    };

                    foreach (string f in fontFiles)
                    {
                        byte[] data = AssetLoader.ReadAssetBytes("assets", "Fonts", f);
                        if (data == null || data.Length == 0)
                        {
                            string userFont = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Microsoft", "Windows", "Fonts", f);
                            if (File.Exists(userFont))
                                data = File.ReadAllBytes(userFont);
                        }
                        if (data == null || data.Length == 0) continue;

                        IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, mem, data.Length);
                        _pinnedFontPtrs.Add(mem);
                        _pfc.AddMemoryFont(mem, data.Length);

                        IntPtr gdiH = NativeMethods.AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, out _);
                        if (gdiH != IntPtr.Zero)
                            _gdiFontHandles.Add(gdiH);
                    }

                    foreach (var fam in _pfc.Families)
                    {
                        if (fam.Name.IndexOf("Inter", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _interFamily = fam;
                            break;
                        }
                    }
                    if (_interFamily == null && _pfc.Families.Length > 0)
                        _interFamily = _pfc.Families[0];
                }
                catch { }
            }
        }

        public static Font Font(float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            EnsureFontsLoaded();
            if (_interFamily != null)
            {
                try
                {
                    if (_interFamily.IsStyleAvailable(style))
                        return new Font(_interFamily, size, style, unit);
                    FontStyle baseStyle = style & (FontStyle.Bold | FontStyle.Italic);
                    if (_interFamily.IsStyleAvailable(baseStyle))
                        return new Font(_interFamily, size, baseStyle, unit);
                    if (_interFamily.IsStyleAvailable(FontStyle.Regular))
                        return new Font(_interFamily, size, FontStyle.Regular, unit);
                }
                catch { }
            }
            return new Font("Segoe UI", size, style, unit);
        }

        public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRect(Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            using (var path = CreateRoundedRect(bounds, radius))
                g.FillPath(brush, path);
        }

        public static void DrawRoundedRect(Graphics g, Pen pen, Rectangle bounds, int radius)
        {
            using (var path = CreateRoundedRect(bounds, radius))
                g.DrawPath(pen, path);
        }
    }
}

