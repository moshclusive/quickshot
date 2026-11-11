using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickShot.Core
{
    public static class NativeMethods
    {
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;

        public const uint LWA_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int ULW_ALPHA = 0x00000002;

        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_HWHEEL = 0x1000;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HSCROLL = 0x0114;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        public const int RGN_OR = 2;
        public const int RGN_DIFF = 4;

        public const int CURSOR_SHOWING = 0x00000001;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public Rectangle ToRectangle() => new Rectangle(Left, Top, Right - Left, Bottom - Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx, cy;
            public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
            IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();

        [DllImport("gdi32.dll")]
        public static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, [In] ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll")]
        public static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
            int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr lpPoint);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern bool PaintDesktop(IntPtr hdc);

        [DllImport("user32.dll")]
        public static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, POINT pt, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private static bool? _supportsHardwareExclude;
        public static bool SupportsHardwareExclude
        {
            get
            {
                if (_supportsHardwareExclude.HasValue) return _supportsHardwareExclude.Value;
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    object val = key?.GetValue("CurrentBuild") ?? key?.GetValue("CurrentBuildNumber");
                    if (val != null && int.TryParse(val.ToString(), out int build))
                    {
                        _supportsHardwareExclude = build >= 19041;
                        return _supportsHardwareExclude.Value;
                    }
                }
                catch { }
                _supportsHardwareExclude = Environment.OSVersion.Version.Build >= 19041;
                return _supportsHardwareExclude.Value;
            }
        }

        public static void EnableDarkScrollbars(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                SetWindowTheme(hwnd, "DarkMode_Explorer", null);
            }
            catch { }
        }

        public static void DisableWindowTransitions(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int disabled = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));
            }
            catch { }
        }

        public static bool ExcludeFromCapture(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            // Windows 10 < 2004 (e.g. 18362/1903) turns WDA_EXCLUDEFROMCAPTURE into a solid black box in BitBlt;
            // only enable hardware affinity on Win10 2004+ (19041+) and rely on clean PrintWindow compositing on earlier builds.
            if (!SupportsHardwareExclude) return false;
            try
            {
                return SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { return false; }
        }

        public static void DragMoveWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, 0xA1 /* WM_NCLBUTTONDOWN */, 0x2 /* HTCAPTION */, 0);
        }

        public class WindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; }
            public string ProcessName { get; set; }
            public Rectangle Bounds { get; set; }
        }

        public static bool TryGetCleanWindowRect(IntPtr hWnd, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf(typeof(RECT))) != 0)
            {
                if (!GetWindowRect(hWnd, out r)) return false;
            }
            if (r.Width <= 40 || r.Height <= 30) return false;
            rect = r.ToRectangle();
            return true;
        }

        private static bool IsRealUserWindow(IntPtr hWnd, int excludePid)
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return false;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (excludePid > 0 && pid == (uint)excludePid) return false;

            var titleSb = new StringBuilder(512);
            if (GetWindowText(hWnd, titleSb, titleSb.Capacity) <= 0) return false;
            string title = titleSb.ToString().Trim();
            if (string.IsNullOrEmpty(title)) return false;

            var clsSb = new StringBuilder(256);
            GetClassName(hWnd, clsSb, clsSb.Capacity);
            string cls = clsSb.ToString();
            if ((cls == "Progman" && title == "Program Manager") || cls == "Windows.UI.Core.CoreWindow")
                return false;

            return TryGetCleanWindowRect(hWnd, out _);
        }

        public static List<WindowInfo> GetOpenWindows(int excludePid)
        {
            var result = new List<WindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsRealUserWindow(hWnd, excludePid)) return true;
                var titleSb = new StringBuilder(512);
                GetWindowText(hWnd, titleSb, titleSb.Capacity);
                if (!TryGetCleanWindowRect(hWnd, out Rectangle r)) return true;
                GetWindowThreadProcessId(hWnd, out uint pid);
                string proc = "";
                try { using var p = Process.GetProcessById((int)pid); proc = p.ProcessName; } catch { }
                result.Add(new WindowInfo
                {
                    Hwnd = hWnd,
                    Title = titleSb.ToString().Trim(),
                    ProcessName = proc,
                    Bounds = r
                });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static WindowInfo GetActiveOrTopWindow(int excludePid)
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero || !IsRealUserWindow(hWnd, excludePid))
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows((w, l) =>
                {
                    if (IsRealUserWindow(w, excludePid)) { found = w; return false; }
                    return true;
                }, IntPtr.Zero);
                hWnd = found;
            }
            if (hWnd == IntPtr.Zero || !TryGetCleanWindowRect(hWnd, out Rectangle r)) return null;
            var titleSb = new StringBuilder(512);
            GetWindowText(hWnd, titleSb, titleSb.Capacity);
            return new WindowInfo { Hwnd = hWnd, Title = titleSb.ToString().Trim(), Bounds = r };
        }

        public static Rectangle FocusAndGetWindowRect(IntPtr hWnd)
        {
            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
            System.Threading.Thread.Sleep(150);
            return TryGetCleanWindowRect(hWnd, out Rectangle r) ? r : Rectangle.Empty;
        }

        public static IntPtr GetDeepestChildAt(int x, int y)
        {
            IntPtr hwnd = WindowFromPoint(new POINT { X = x, Y = y });
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr cur = hwnd;
            for (int depth = 0; depth < 8; depth++)
            {
                var pt = new POINT { X = x, Y = y };
                ScreenToClient(cur, ref pt);
                IntPtr child = ChildWindowFromPointEx(cur, pt, 0x0007 /* SKIPINVISIBLE | SKIPDISABLED | SKIPTRANSPARENT */);
                if (child == IntPtr.Zero || child == cur) break;
                cur = child;
            }
            return cur;
        }

        public static IntPtr GetTopScrollTargetRoot(int x, int y, int excludePid)
        {
            var below = GetVisibleWindowsIntersecting(new Rectangle(x, y, 1, 1), excludePid, null);
            return below.Count > 0 ? below[0].Hwnd : IntPtr.Zero;
        }

        public static List<IntPtr> GetScrollTargetsBelowPoint(int x, int y, int excludePid)
        {
            var targets = new List<IntPtr>();
            var below = GetVisibleWindowsIntersecting(new Rectangle(x, y, 1, 1), excludePid, null);
            IntPtr root = below.Count > 0 ? below[0].Hwnd : IntPtr.Zero;
            if (root == IntPtr.Zero) return targets;

            var childCandidates = new List<(IntPtr Hwnd, int Area)>();
            EnumChildWindows(root, (cHwnd, _) =>
            {
                if (!IsWindowVisible(cHwnd)) return true;
                if (GetWindowRect(cHwnd, out RECT cr) && x >= cr.Left && x < cr.Right && y >= cr.Top && y < cr.Bottom)
                {
                    int area = Math.Max(1, cr.Width) * Math.Max(1, cr.Height);
                    childCandidates.Add((cHwnd, area));
                }
                return true;
            }, IntPtr.Zero);

            childCandidates.Sort((a, b) => a.Area.CompareTo(b.Area));
            foreach (var c in childCandidates)
                targets.Add(c.Hwnd);
            if (!targets.Contains(root))
                targets.Add(root);
            return targets;
        }

        public static bool ScrollHardwareThroughHole(
            IntPtr overlayHwnd, int overlayW, int overlayH,
            int holeClientX, int holeClientY,
            int txScreen, int tyScreen,
            int deltaY, int deltaX, int excludePid)
        {
            GetCursorPos(out POINT savedCursor);
            var below = GetVisibleWindowsIntersecting(new Rectangle(txScreen, tyScreen, 1, 1), excludePid, null);
            IntPtr root = below.Count > 0 ? below[0].Hwnd : IntPtr.Zero;

            int hx = Math.Max(10, Math.Min(overlayW - 10, holeClientX));
            int hy = Math.Max(10, Math.Min(overlayH - 10, holeClientY));

            try
            {
                ReleaseCapture();
                IntPtr full = CreateRectRgn(0, 0, overlayW, overlayH);
                IntPtr hole = CreateRectRgn(hx - 8, hy - 8, hx + 8, hy + 8);
                CombineRgn(full, full, hole, RGN_DIFF);
                DeleteObject(hole);
                SetWindowRgn(overlayHwnd, full, false);

                if (root != IntPtr.Zero)
                    SetForegroundWindow(root);

                SetCursorPos(txScreen, tyScreen);
                System.Threading.Thread.Sleep(10);

                if (deltaY != 0)
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)deltaY), UIntPtr.Zero);
                if (deltaX != 0)
                    mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, unchecked((uint)deltaX), UIntPtr.Zero);

                System.Threading.Thread.Sleep(18);
                return true;
            }
            catch { return false; }
            finally
            {
                SetWindowRgn(overlayHwnd, IntPtr.Zero, false);
                SetCursorPos(savedCursor.X, savedCursor.Y);
                SetForegroundWindow(overlayHwnd);
                if ((GetAsyncKeyState(0x01) & 0x8000) != 0)
                    SetCapture(overlayHwnd);
            }
        }

        public static void ScrollWindowBelowPoint(int x, int y, int deltaY, int deltaX, int excludePid, IntPtr overlayHwnd = default)
        {
            var targets = GetScrollTargetsBelowPoint(x, y, excludePid);
            if (targets.Count == 0) return;

            IntPtr primary = targets[0];
            IntPtr parent = GetParent(primary);
            IntPtr root = targets[targets.Count - 1];
            IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

            var rootClsSb = new StringBuilder(128);
            GetClassName(root, rootClsSb, rootClsSb.Capacity);
            string rootCls = rootClsSb.ToString();

            var clsSb = new StringBuilder(128);
            GetClassName(primary, clsSb, clsSb.Capacity);
            string cls = clsSb.ToString();

            bool isChromiumOrGecko =
                rootCls.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rootCls.IndexOf("Mozilla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Mozilla", StringComparison.OrdinalIgnoreCase) >= 0;

            // Explorer CabinetWClass needs inner DirectUIHWND under SHELLDLL_DefView at point
            if (string.Equals(rootCls, "CabinetWClass", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr explorerDui = IntPtr.Zero;
                foreach (IntPtr t in targets)
                {
                    var tSb = new StringBuilder(64);
                    GetClassName(t, tSb, tSb.Capacity);
                    if (string.Equals(tSb.ToString(), "DirectUIHWND", StringComparison.OrdinalIgnoreCase))
                    {
                        var pSb = new StringBuilder(64);
                        GetClassName(GetParent(t), pSb, pSb.Capacity);
                        if (string.Equals(pSb.ToString(), "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
                        {
                            explorerDui = t;
                            break;
                        }
                        if (explorerDui == IntPtr.Zero) explorerDui = t;
                    }
                }
                if (explorerDui != IntPtr.Zero)
                    primary = explorerDui;
            }

            // Chromium/Edge ignores WM_MOUSEWHEEL when WindowFromPoint(lParam) hits an occluding TopMost overlay.
            // Cutting a 2x2 hit-test micro-hole with redraw=false for ~0.1ms during SendMessageTimeout
            // preserves zero screen blink and zero cursor warp while letting Chromium scroll cleanly.
            bool openedHole = false;
            if (overlayHwnd != IntPtr.Zero &&
                !string.Equals(rootCls, "CabinetWClass", StringComparison.OrdinalIgnoreCase) &&
                GetWindowRect(overlayHwnd, out RECT ovRect))
            {
                int ow = Math.Max(4, ovRect.Width);
                int oh = Math.Max(4, ovRect.Height);
                int hx = Math.Max(1, Math.Min(ow - 2, x - ovRect.Left));
                int hy = Math.Max(1, Math.Min(oh - 2, y - ovRect.Top));
                IntPtr full = CreateRectRgn(0, 0, ow, oh);
                IntPtr hole = CreateRectRgn(hx - 1, hy - 1, hx + 1, hy + 1);
                CombineRgn(full, full, hole, RGN_DIFF);
                DeleteObject(hole);
                if (SetWindowRgn(overlayHwnd, full, false) != 0)
                    openedHole = true;
                else
                    DeleteObject(full);
            }

            try
            {
                IntPtr chromiumTarget = root;
                if (isChromiumOrGecko)
                {
                    foreach (IntPtr t in targets)
                    {
                        var tSb = new StringBuilder(64);
                        GetClassName(t, tSb, tSb.Capacity);
                        if (tSb.ToString().IndexOf("RenderWidgetHost", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            chromiumTarget = t;
                            break;
                        }
                    }
                }

                if (deltaY != 0)
                {
                    IntPtr wParam = (IntPtr)((deltaY & 0xFFFF) << 16);
                    if (isChromiumOrGecko)
                    {
                        SendMessageTimeout(chromiumTarget, WM_MOUSEWHEEL, wParam, lParam, 2 /* SMTO_ABORTIFHUNG */, 70, out _);
                    }
                    else
                    {
                        PostMessage(primary, WM_MOUSEWHEEL, wParam, lParam);
                        if (root != primary && !string.Equals(rootCls, "CabinetWClass", StringComparison.OrdinalIgnoreCase))
                            PostMessage(root, WM_MOUSEWHEEL, wParam, lParam);
                    }
                }

                if (deltaX != 0)
                {
                    IntPtr wParamH = (IntPtr)((deltaX & 0xFFFF) << 16);
                    if (isChromiumOrGecko)
                    {
                        SendMessageTimeout(chromiumTarget, WM_MOUSEHWHEEL, wParamH, lParam, 2 /* SMTO_ABORTIFHUNG */, 70, out _);
                    }
                    else
                    {
                        PostMessage(primary, WM_MOUSEHWHEEL, wParamH, lParam);
                        IntPtr sbCmd = (IntPtr)(deltaX > 0 ? 1 /* SB_LINERIGHT */ : 0 /* SB_LINELEFT */);
                        int steps = Math.Max(1, Math.Abs(deltaX) / 60);
                        for (int s = 0; s < steps; s++)
                        {
                            PostMessage(primary, WM_HSCROLL, sbCmd, IntPtr.Zero);
                            if (parent != IntPtr.Zero && parent != primary)
                                PostMessage(parent, WM_HSCROLL, sbCmd, IntPtr.Zero);
                        }
                    }
                }
            }
            finally
            {
                if (openedHole)
                    SetWindowRgn(overlayHwnd, IntPtr.Zero, false);
            }
        }

        public static void ScrollAtPoint(int x, int y, int delta, Rectangle guardRect = default)
        {
            GetCursorPos(out POINT orig);
            bool cursorOnGuard = !guardRect.IsEmpty && guardRect.Contains(orig.X, orig.Y);
            bool leftHeld = (GetAsyncKeyState(0x01) & 0x8000) != 0;

            IntPtr ptWin = WindowFromPoint(new POINT { X = x, Y = y });
            IntPtr root = ptWin != IntPtr.Zero ? GetAncestor(ptWin, 2 /* GA_ROOT */) : IntPtr.Zero;

            // Always try clean WM_MOUSEWHEEL message first without warping cursor when user hovers over Stop/Capture bar
            if (cursorOnGuard || leftHeld)
            {
                IntPtr deep = GetDeepestChildAt(x, y);
                if (root != IntPtr.Zero && !leftHeld)
                    SetForegroundWindow(root);
                if (deep != IntPtr.Zero)
                {
                    IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
                    IntPtr wParam = (IntPtr)((delta & 0xFFFF) << 16);
                    PostMessage(deep, WM_MOUSEWHEEL, wParam, lParam);
                    if (leftHeld)
                    {
                        SendMessage(deep, WM_VSCROLL, delta < 0 ? 1 /* SB_LINEDOWN */ : 0 /* SB_LINEUP */, 0);
                    }
                }
                return;
            }

            if (root != IntPtr.Zero)
                SetForegroundWindow(root);

            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(15);
            int notches = Math.Max(1, Math.Abs(delta) / 120);
            uint step = delta < 0 ? unchecked((uint)-120) : 120u;
            for (int i = 0; i < notches; i++)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, step, UIntPtr.Zero);
                System.Threading.Thread.Sleep(15);
            }
            // Immediately restore user's original cursor position so Stop/Finish remains easy to click
            SetCursorPos(orig.X, orig.Y);
        }

        public static List<WindowInfo> GetVisibleWindowsIntersecting(Rectangle area, int excludePid, HashSet<IntPtr> excludeHwnds)
        {
            var list = new List<WindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (excludeHwnds != null && excludeHwnds.Contains(hWnd)) return true;
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;
                if (excludePid > 0)
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == (uint)excludePid) return true;
                }
                if (!TryGetCleanWindowRect(hWnd, out Rectangle wr)) return true;
                if (!wr.IntersectsWith(area)) return true;

                var clsSb = new StringBuilder(128);
                GetClassName(hWnd, clsSb, clsSb.Capacity);
                string cls = clsSb.ToString();
                if (cls == "Shell_TrayWnd" || cls == "Progman" || cls == "WorkerW" || cls == "Windows.UI.Core.CoreWindow")
                    return true;

                list.Add(new WindowInfo { Hwnd = hWnd, Bounds = wr });
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}

