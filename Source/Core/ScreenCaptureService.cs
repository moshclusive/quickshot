using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickShot.Core
{
    public static class ScreenCaptureService
    {
        public static Bitmap CaptureVirtualScreen(bool includeCursor)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            return CaptureRegion(bounds, includeCursor);
        }

        public static Bitmap CaptureMonitor(Screen screen, bool includeCursor)
        {
            return CaptureRegion(screen.Bounds, includeCursor);
        }

        public static Bitmap CaptureRegion(Rectangle screenRect, bool includeCursor) =>
            CaptureRegionExcludingWindows(screenRect, includeCursor);

        public static Bitmap CaptureRegionFast(Rectangle screenRect) =>
            CaptureRegionExcludingWindows(screenRect, false);

        public static Bitmap CaptureRegionExcludingWindows(Rectangle screenRect, bool includeCursor, params IntPtr[] excludeHwnds)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0)
                throw new ArgumentException("Invalid capture rectangle");

            var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdcDest = g.GetHdc();
                IntPtr hdcSrc = NativeMethods.GetDC(IntPtr.Zero);
                try
                {
                    NativeMethods.BitBlt(
                        hdcDest, 0, 0, screenRect.Width, screenRect.Height,
                        hdcSrc, screenRect.X, screenRect.Y,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcSrc);
                    g.ReleaseHdc(hdcDest);
                }

                // Patch over excluded overlay/control bars cleanly without flicker or dark black cutouts on pre-2004 Windows 10
                if (excludeHwnds != null && excludeHwnds.Length > 0 && !NativeMethods.SupportsHardwareExclude)
                {
                    PatchExcludedWindows(g, screenRect, excludeHwnds);
                }

                if (includeCursor)
                {
                    IntPtr hCurDc = g.GetHdc();
                    try { DrawCursorOnDc(hCurDc, screenRect.X, screenRect.Y); }
                    finally { g.ReleaseHdc(hCurDc); }
                }
            }
            return bmp;
        }

        private static void PatchExcludedWindows(Graphics destGraphics, Rectangle screenRect, IntPtr[] excludeHwnds)
        {
            var excludeSet = new System.Collections.Generic.HashSet<IntPtr>();
            foreach (IntPtr h in excludeHwnds)
                if (h != IntPtr.Zero) excludeSet.Add(h);
            if (excludeSet.Count == 0) return;

            foreach (IntPtr hEx in excludeSet)
            {
                if (!NativeMethods.IsWindowVisible(hEx)) continue;
                if (!NativeMethods.TryGetCleanWindowRect(hEx, out Rectangle exRect)) continue;
                Rectangle overlap = Rectangle.Intersect(screenRect, exRect);
                if (overlap.IsEmpty) continue;

                using var patchBmp = new Bitmap(overlap.Width, overlap.Height, PixelFormat.Format32bppArgb);
                using (var pg = Graphics.FromImage(patchBmp))
                {
                    pg.Clear(Color.FromArgb(24, 24, 30));
                    IntPtr pHdc = pg.GetHdc();
                    try
                    {
                        NativeMethods.SetViewportOrgEx(pHdc, -overlap.X, -overlap.Y, IntPtr.Zero);
                        NativeMethods.PaintDesktop(pHdc);
                        NativeMethods.SetViewportOrgEx(pHdc, 0, 0, IntPtr.Zero);
                    }
                    finally { pg.ReleaseHdc(pHdc); }

                    var windowsBelow = NativeMethods.GetVisibleWindowsIntersecting(overlap, 0, excludeSet);
                    for (int i = windowsBelow.Count - 1; i >= 0; i--)
                    {
                        var w = windowsBelow[i];
                        Rectangle wInter = Rectangle.Intersect(overlap, w.Bounds);
                        if (wInter.IsEmpty || w.Bounds.Width <= 0 || w.Bounds.Height <= 0) continue;

                        try
                        {
                            using var wBmp = new Bitmap(w.Bounds.Width, w.Bounds.Height, PixelFormat.Format32bppArgb);
                            bool rendered;
                            using (var wg = Graphics.FromImage(wBmp))
                            {
                                IntPtr wHdc = wg.GetHdc();
                                try
                                {
                                    rendered = NativeMethods.PrintWindow(w.Hwnd, wHdc, NativeMethods.PW_RENDERFULLCONTENT);
                                    if (!rendered) rendered = NativeMethods.PrintWindow(w.Hwnd, wHdc, 0);
                                }
                                finally { wg.ReleaseHdc(wHdc); }
                            }
                            if (rendered)
                            {
                                var srcSub = new Rectangle(wInter.X - w.Bounds.X, wInter.Y - w.Bounds.Y, wInter.Width, wInter.Height);
                                var dstSub = new Rectangle(wInter.X - overlap.X, wInter.Y - overlap.Y, wInter.Width, wInter.Height);
                                pg.DrawImage(wBmp, dstSub, srcSub, GraphicsUnit.Pixel);
                            }
                        }
                        catch { }
                    }
                }

                destGraphics.DrawImageUnscaled(patchBmp, overlap.X - screenRect.X, overlap.Y - screenRect.Y);
            }
        }

        public static void DrawCursorOnDc(IntPtr hdcDest, int originX, int originY)
        {
            var ci = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO)) };
            if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero)
                return;

            int drawX = ci.ptScreenPos.X - originX;
            int drawY = ci.ptScreenPos.Y - originY;
            if (NativeMethods.GetIconInfo(ci.hCursor, out NativeMethods.ICONINFO ii))
            {
                drawX -= ii.xHotspot;
                drawY -= ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
            }
            NativeMethods.DrawIconEx(hdcDest, drawX, drawY, ci.hCursor, 0, 0, 0, IntPtr.Zero, 0x0003 /* DI_NORMAL */);
        }

        public static Bitmap CaptureMonitorWithWindowPrint(Bitmap baselineVirtual, Rectangle virtualBounds, Rectangle monWa, IntPtr targetRoot)
        {
            if (monWa.Width <= 0 || monWa.Height <= 0) return null;
            var outBmp = new Bitmap(monWa.Width, monWa.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outBmp))
            {
                if (baselineVirtual != null)
                {
                    Rectangle srcMon = Rectangle.Intersect(
                        new Rectangle(0, 0, baselineVirtual.Width, baselineVirtual.Height),
                        new Rectangle(monWa.X - virtualBounds.X, monWa.Y - virtualBounds.Y, monWa.Width, monWa.Height));
                    if (srcMon.Width > 0 && srcMon.Height > 0)
                        g.DrawImage(baselineVirtual, new Rectangle(0, 0, srcMon.Width, srcMon.Height), srcMon, GraphicsUnit.Pixel);
                }

                if (targetRoot != IntPtr.Zero && NativeMethods.TryGetCleanWindowRect(targetRoot, out Rectangle winBounds))
                {
                    Rectangle wInter = Rectangle.Intersect(monWa, winBounds);
                    if (!wInter.IsEmpty && winBounds.Width > 0 && winBounds.Height > 0)
                    {
                        using var wBmp = new Bitmap(winBounds.Width, winBounds.Height, PixelFormat.Format32bppArgb);
                        bool rendered;
                        using (var wg = Graphics.FromImage(wBmp))
                        {
                            IntPtr wHdc = wg.GetHdc();
                            try
                            {
                                rendered = NativeMethods.PrintWindow(targetRoot, wHdc, NativeMethods.PW_RENDERFULLCONTENT);
                                if (!rendered) rendered = NativeMethods.PrintWindow(targetRoot, wHdc, 0);
                            }
                            finally { wg.ReleaseHdc(wHdc); }
                        }
                        if (rendered)
                        {
                            var srcSub = new Rectangle(wInter.X - winBounds.X, wInter.Y - winBounds.Y, wInter.Width, wInter.Height);
                            var dstSub = new Rectangle(wInter.X - monWa.X, wInter.Y - monWa.Y, wInter.Width, wInter.Height);
                            g.DrawImage(wBmp, dstSub, srcSub, GraphicsUnit.Pixel);
                            return outBmp;
                        }
                    }
                }
            }
            outBmp.Dispose();
            return null;
        }

        public static Bitmap CropBitmap(Bitmap source, Rectangle rect)
        {
            Rectangle clipped = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), rect);
            if (clipped.Width < 1 || clipped.Height < 1) return null;
            return source.Clone(clipped, PixelFormat.Format32bppArgb);
        }
    }
}

