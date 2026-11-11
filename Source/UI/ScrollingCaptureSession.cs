using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public class ScrollingCaptureSession : IDisposable
    {
        private readonly Rectangle _targetRect;
        private readonly Action<Bitmap, Rectangle> _onCompleted;
        private readonly Action _onCancelled;
        private readonly RecordingBorderForm _borderForm;
        private readonly ScrollingControlBarForm _barForm;
        private readonly List<Bitmap> _frames = new List<Bitmap>();
        private readonly List<PreScrollFrameData> _preScrollFrames;
        private readonly List<AnnotationItem> _docAnnotations;
        private readonly Timer _manualPollTimer;
        private VirtualScrollCanvas _canvas;
        private int _capturedMoves = 1;
        private bool _active = true;
        private bool _autoScrolling;

        public ScrollingCaptureSession(
            Rectangle targetRect,
            Action<Bitmap, Rectangle> onCompleted,
            Action onCancelled,
            List<PreScrollFrameData> preScrollFrames = null,
            List<AnnotationItem> docAnnotations = null)
        {
            _targetRect = targetRect;
            _onCompleted = onCompleted;
            _onCancelled = onCancelled;
            _preScrollFrames = preScrollFrames;
            _docAnnotations = docAnnotations;

            _borderForm = new RecordingBorderForm(targetRect);
            _barForm = new ScrollingControlBarForm(targetRect);
            _barForm.AutoScrollClicked += () => _ = ToggleAutoScrollAsync();
            _barForm.FinishClicked += FinishAndStitch;
            _barForm.CancelClicked += Cancel;

            _manualPollTimer = new Timer { Interval = 180 };
            _manualPollTimer.Tick += (s, e) => PollManualViewport(0, 0);
        }

        public void Start()
        {
            _borderForm.Show();
            _barForm.Show();
            Application.DoEvents();
            using Bitmap initial = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, false, _barForm.Handle, _borderForm.Handle);
            _canvas = BuildSeededCanvas(_targetRect, _preScrollFrames, initial, _docAnnotations, out _capturedMoves);
            _barForm.SetSizeLabel(_canvas.CanvasWidth, _canvas.CanvasHeight);
            _manualPollTimer.Start();
        }

        private void PollManualViewport(int hintY, int hintX)
        {
            if (!_active || _autoScrolling || _canvas == null) return;
            try
            {
                using Bitmap snap = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, false, _barForm.Handle, _borderForm.Handle);
                if (ApplyFrameAndAnchorAnnotations(_canvas, snap, hintY, hintX, _docAnnotations, out _))
                {
                    _capturedMoves++;
                    _barForm.SetSizeLabel(_canvas.CanvasWidth, _canvas.CanvasHeight);
                }
            }
            catch { }
        }

        private async Task ToggleAutoScrollAsync()
        {
            if (!_active || _canvas == null) return;
            if (_autoScrolling)
            {
                _autoScrolling = false;
                _barForm.SetAutoScrolling(false);
                return;
            }

            _autoScrolling = true;
            _barForm.SetAutoScrolling(true);

            int cx = _targetRect.X + (int)(_targetRect.Width * 0.45);
            int cy = _targetRect.Y + (int)(_targetRect.Height * 0.45);

            int scrollDelta = _targetRect.Height < 540 ? -240 : -360;
            for (int step = 0; step < 30 && _autoScrolling && _active; step++)
            {
                Rectangle guardRect = Rectangle.Inflate(_barForm.Bounds, 36, 36);
                await Task.Run(() => NativeMethods.ScrollAtPoint(cx, cy, scrollDelta, guardRect));
                await Task.Delay(380);
                if (!_autoScrolling || !_active || _canvas == null) break;

                using Bitmap next = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, false, _barForm.Handle, _borderForm.Handle);
                bool moved = ApplyFrameAndAnchorAnnotations(_canvas, next, 1, 0, _docAnnotations, out int dy);
                if (!moved || dy == 0)
                    break;

                _capturedMoves++;
                _barForm.SetSizeLabel(_canvas.CanvasWidth, _canvas.CanvasHeight);
            }

            _autoScrolling = false;
            if (!_barForm.IsDisposed) _barForm.SetAutoScrolling(false);
            if (_active && _capturedMoves > 1)
            {
                FinishAndStitch();
            }
        }

        private void FinishAndStitch()
        {
            if (!_active) return;
            _active = false;
            _autoScrolling = false;
            _manualPollTimer.Stop();

            if (_canvas != null)
            {
                try
                {
                    using Bitmap finalGrab = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, false, _barForm.Handle, _borderForm.Handle);
                    ApplyFrameAndAnchorAnnotations(_canvas, finalGrab, 0, 0, _docAnnotations, out _);
                }
                catch { }
            }

            CloseForms();
            Bitmap stitched = _canvas != null
                ? FinalizeCanvasWithAnnotations(_canvas, _docAnnotations)
                : StitchFrames(_frames);

            _canvas?.Dispose();
            _canvas = null;
            DisposePreScrollFrames(_preScrollFrames);
            foreach (var f in _frames) f.Dispose();
            _frames.Clear();

            if (stitched != null)
                _onCompleted?.Invoke(stitched, _targetRect);
            else
                _onCancelled?.Invoke();
        }

        private void Cancel()
        {
            if (!_active) return;
            _active = false;
            _autoScrolling = false;
            _manualPollTimer.Stop();
            CloseForms();
            _canvas?.Dispose();
            _canvas = null;
            DisposePreScrollFrames(_preScrollFrames);
            foreach (var f in _frames) f.Dispose();
            _frames.Clear();
            _onCancelled?.Invoke();
        }

        internal static void DisposePreScrollFrames(IList<PreScrollFrameData> preFrames)
        {
            if (preFrames == null) return;
            foreach (var pf in preFrames) pf?.Dispose();
            preFrames.Clear();
        }

        internal static VirtualScrollCanvas BuildSeededCanvas(
            Rectangle targetRect,
            IList<PreScrollFrameData> preFrames,
            Bitmap liveInitial,
            IList<AnnotationItem> annotations,
            out int moves)
        {
            moves = 1;
            VirtualScrollCanvas canvas;
            if (preFrames != null && preFrames.Count > 0)
            {
                canvas = new VirtualScrollCanvas(targetRect, preFrames[0].Frame);
                for (int i = 1; i < preFrames.Count; i++)
                {
                    if (canvas.ApplyViewportFrame(preFrames[i].Frame, preFrames[i].HintY, preFrames[i].HintX, out _, out _, out int pL, out int pT))
                    {
                        moves++;
                    }
                }
                if (liveInitial != null && canvas.ApplyViewportFrame(liveInitial, 0, 0, out _, out _, out int initPL, out int initPT))
                {
                    moves++;
                }
                DisposePreScrollFrames(preFrames);
            }
            else if (liveInitial != null)
            {
                canvas = new VirtualScrollCanvas(targetRect, liveInitial);
            }
            else
            {
                return null;
            }

            return canvas;
        }

        internal static bool ApplyFrameAndAnchorAnnotations(
            VirtualScrollCanvas canvas,
            Bitmap frame,
            int hintY,
            int hintX,
            IList<AnnotationItem> annotations,
            out int dy)
        {
            bool moved = canvas.ApplyViewportFrame(frame, hintY, hintX, out _, out dy, out int pL, out int pT);
            if (moved && annotations != null && (pL != 0 || pT != 0))
            {
                foreach (var a in annotations)
                    a.Translate(pL, pT);
            }
            return moved;
        }

        internal static Bitmap FinalizeCanvasWithAnnotations(VirtualScrollCanvas canvas, IList<AnnotationItem> annotations)
        {
            if (canvas == null) return null;
            Bitmap stitched = canvas.CropDocumentRect(new Rectangle(0, 0, canvas.CanvasWidth, canvas.CanvasHeight));
            if (stitched != null && annotations != null && annotations.Count > 0)
            {
                using var g = Graphics.FromImage(stitched);
                AnnotationRenderer.RenderAnnotations(g, annotations, stitched, -1, PointF.Empty);
            }
            return stitched;
        }

        private void CloseForms()
        {
            try { if (!_barForm.IsDisposed) _barForm.Close(); } catch { }
            try { if (!_borderForm.IsDisposed) _borderForm.Close(); } catch { }
        }

        private static byte[] ExtractPixels(Bitmap bmp, out int stride)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(bd.Stride);
                byte[] data = new byte[stride * bmp.Height];
                Marshal.Copy(bd.Scan0, data, 0, data.Length);
                return data;
            }
            finally { bmp.UnlockBits(bd); }
        }

        internal static bool FramesIdentical(Bitmap b1, Bitmap b2)
        {
            if (b1.Width != b2.Width || b1.Height != b2.Height) return false;
            byte[] d1 = ExtractPixels(b1, out int s1);
            byte[] d2 = ExtractPixels(b2, out int s2);
            int xStart = (int)(b1.Width * 0.15);
            int xEnd = (int)(b1.Width * 0.80);
            int diffs = 0;
            for (int y = 10; y < b1.Height - 10; y += 3)
            {
                for (int x = xStart; x < xEnd; x += 4)
                {
                    int i1 = y * s1 + x * 4;
                    int i2 = y * s2 + x * 4;
                    if (Math.Abs(d1[i1] - d2[i2]) + Math.Abs(d1[i1 + 1] - d2[i2 + 1]) + Math.Abs(d1[i1 + 2] - d2[i2 + 2]) > 18)
                    {
                        diffs++;
                        if (diffs > 25) return false;
                    }
                }
            }
            return true;
        }

        internal static Bitmap StitchFrames(List<Bitmap> frames)
        {
            if (frames.Count == 0) return null;
            if (frames.Count == 1) return (Bitmap)frames[0].Clone();

            int w = frames[0].Width, h = frames[0].Height;
            int xStart = w > 60 ? (int)(w * 0.12) : 4;
            int xEnd = w > 60 ? (int)(w * 0.84) : Math.Max(xStart + 4, w - 4);

            var cachedPixels = new List<byte[]>(frames.Count);
            var cachedStrides = new List<int>(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                cachedPixels.Add(ExtractPixels(frames[i], out int st));
                cachedStrides.Add(st);
            }

            // Pick a genuinely scrolled comparison frame so sticky header/footer detection doesn't misread stationary frames
            int cmpIdx = 1;
            for (int i = 1; i < frames.Count; i++)
            {
                if (!FramesIdentical(frames[0], frames[i])) { cmpIdx = i; break; }
            }

            byte[] d0 = cachedPixels[0];
            byte[] dCmp = cachedPixels[cmpIdx];
            int s0 = cachedStrides[0];
            int sCmp = cachedStrides[cmpIdx];

            int stickyHeaderH = 0;
            int maxHeader = (int)(h * 0.24);
            for (int y = 0; y < maxHeader; y++)
            {
                double diff = ComputeRowDiff(d0, s0, dCmp, sCmp, y, xStart, xEnd);
                if (diff < 10.0)
                {
                    stickyHeaderH = y + 1;
                }
                else
                {
                    // Tolerate up to 2 thin hairline/shadow rows inside a real fixed header
                    bool continuesMatching = (y + 2 < maxHeader) &&
                        (ComputeRowDiff(d0, s0, dCmp, sCmp, y + 1, xStart, xEnd) < 10.0 ||
                         ComputeRowDiff(d0, s0, dCmp, sCmp, y + 2, xStart, xEnd) < 10.0);
                    if (!continuesMatching) break;
                }
            }
            if (stickyHeaderH > h * 0.21) stickyHeaderH = 0;

            int stickyFooterH = 0;
            int minFooter = (int)(h * 0.76);
            for (int y = h - 1; y >= minFooter; y--)
            {
                if (ComputeRowDiff(d0, s0, dCmp, sCmp, y, xStart, xEnd) < 10.0) stickyFooterH++;
                else break;
            }
            if (stickyFooterH > h * 0.21) stickyFooterH = 0;

            int contentBottom = h - stickyFooterH;
            int bodyH = Math.Max(40, contentBottom - stickyHeaderH);
            int sampleH = Math.Min(34, Math.Max(14, (int)(bodyH * 0.14)));

            var slices = new List<(Bitmap Bmp, int SrcY, int SrcH)> { (frames[0], 0, contentBottom) };
            int[] anchorFractions = { 84, 58, 95 };

            int lastRefIdx = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                if (FramesIdentical(frames[lastRefIdx], frames[i])) continue;

                byte[] pData = cachedPixels[lastRefIdx];
                byte[] cData = cachedPixels[i];
                int pStride = cachedStrides[lastRefIdx];
                int cStride = cachedStrides[i];

                int matchedDelta = 0;
                bool matched = false;

                foreach (int pct in anchorFractions)
                {
                    int anchorY = pct >= 95
                        ? Math.Max(stickyHeaderH, contentBottom - sampleH - 4)
                        : stickyHeaderH + Math.Max(6, (int)(bodyH * (pct / 100.0)) - sampleH);
                    int maxSearchY = Math.Min(contentBottom - sampleH, anchorY + 12);
                    var (bestY, bestErr) = FindBestStripMatch(pData, pStride, cData, cStride, anchorY, sampleH, stickyHeaderH, maxSearchY, xStart, xEnd);

                    if (bestY != -1 && bestErr < 20.0)
                    {
                        int deltaScrolled = anchorY - bestY;
                        if (deltaScrolled <= 3)
                        {
                            matched = true;
                            matchedDelta = 0;
                            break;
                        }

                        // Cross-verify on a secondary strip to prevent coincidental solid-background false matches
                        int secOffset = Math.Min(sampleH + 10, bestY - stickyHeaderH);
                        if (secOffset >= 10)
                        {
                            double secErr = EvalStripPairErr(pData, pStride, cData, cStride, anchorY - secOffset, bestY - secOffset, sampleH, xStart, xEnd);
                            if (secErr > 28.0) continue;
                        }

                        matched = true;
                        matchedDelta = deltaScrolled;
                        break;
                    }
                }

                if (matched)
                {
                    if (matchedDelta <= 3) continue;
                    int startRow = Math.Max(stickyHeaderH, contentBottom - matchedDelta);
                    int endRow = (i == frames.Count - 1) ? h : contentBottom;
                    int appendH = endRow - startRow;
                    if (appendH > 3)
                    {
                        slices.Add((frames[i], startRow, appendH));
                        lastRefIdx = i;
                    }
                }
            }

            int totalH = 0;
            foreach (var sl in slices) totalH += sl.SrcH;
            totalH = Math.Min(18000, totalH);

            var finalBmp = new Bitmap(w, totalH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(finalBmp))
            {
                int curY = 0;
                foreach (var sl in slices)
                {
                    if (curY + sl.SrcH > finalBmp.Height) break;
                    g.DrawImage(sl.Bmp, new Rectangle(0, curY, w, sl.SrcH), new Rectangle(0, sl.SrcY, w, sl.SrcH), GraphicsUnit.Pixel);
                    curY += sl.SrcH;
                }
            }
            return finalBmp;
        }

        private static double ComputeRowDiff(byte[] d0, int s0, byte[] d1, int s1, int y, int xStart, int xEnd)
        {
            double diff = 0;
            int cnt = 0;
            for (int x = xStart; x < xEnd; x += 3)
            {
                int i0 = y * s0 + x * 4, i1 = y * s1 + x * 4;
                diff += Math.Abs(d0[i0] - d1[i1]) + Math.Abs(d0[i0 + 1] - d1[i1 + 1]) + Math.Abs(d0[i0 + 2] - d1[i1 + 2]);
                cnt++;
            }
            return diff / Math.Max(1, cnt);
        }

        private static double EvalStripPairErr(byte[] pData, int pStride, byte[] cData, int cStride, int yPrevStart, int yCurStart, int sampleH, int xStart, int xEnd)
        {
            double err = 0;
            int samples = 0;
            for (int dy = 0; dy < sampleH; dy += 2)
            {
                int yP = yPrevStart + dy;
                int yC = yCurStart + dy;
                for (int x = xStart; x < xEnd; x += 5)
                {
                    int iP = yP * pStride + x * 4;
                    int iC = yC * cStride + x * 4;
                    err += Math.Abs(pData[iP] - cData[iC]) + Math.Abs(pData[iP + 1] - cData[iC + 1]) + Math.Abs(pData[iP + 2] - cData[iC + 2]);
                    samples++;
                }
            }
            return err / Math.Max(1, samples);
        }

        private static (int BestY, double BestErr) FindBestStripMatch(
            byte[] pData, int pStride, byte[] cData, int cStride,
            int prevAnchorY, int sampleH, int minY, int maxY, int xStart, int xEnd)
        {
            int bestY = -1;
            double bestErr = double.MaxValue;
            for (int cy = minY; cy <= maxY; cy++)
            {
                double err = 0;
                int samples = 0;
                for (int dy = 0; dy < sampleH; dy += 2)
                {
                    int yP = prevAnchorY + dy;
                    int yC = cy + dy;
                    for (int x = xStart; x < xEnd; x += 5)
                    {
                        int iP = yP * pStride + x * 4;
                        int iC = yC * cStride + x * 4;
                        err += Math.Abs(pData[iP] - cData[iC]) + Math.Abs(pData[iP + 1] - cData[iC + 1]) + Math.Abs(pData[iP + 2] - cData[iC + 2]);
                        samples++;
                    }
                    if (err / Math.Max(1, samples) > bestErr) break;
                }
                double avgErr = err / Math.Max(1, samples);
                if (avgErr < bestErr) { bestErr = avgErr; bestY = cy; }
            }
            return (bestY, bestErr);
        }

        public void Dispose()
        {
            _manualPollTimer?.Dispose();
            _canvas?.Dispose();
            _canvas = null;
            foreach (var f in _frames) f.Dispose();
            _frames.Clear();
            if (!_barForm.IsDisposed) _barForm.Dispose();
            if (!_borderForm.IsDisposed) _borderForm.Dispose();
        }
    }

    internal class ScrollingControlBarForm : Form
    {
        private readonly Label _countLabel;
        private readonly DarkPillButton _btnAuto;
        private readonly DarkPillButton _btnFinish;
        private readonly DarkPillButton _btnCancel;

        public event Action AutoScrollClicked;
        public event Action FinishClicked;
        public event Action CancelClicked;

        public ScrollingControlBarForm(Rectangle anchorRect)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Theme.BgHeader;
            Size = new Size(358, 40);

            Screen scr = Screen.FromRectangle(anchorRect);
            Rectangle mon = scr.WorkingArea;
            Rectangle fullMon = scr.Bounds;
            int barX = Math.Max(mon.Left + 8, Math.Min(mon.Right - Width - 8, anchorRect.X + (anchorRect.Width - Width) / 2));
            int barY = (anchorRect.Bottom + 14 + Height <= fullMon.Bottom)
                ? anchorRect.Bottom + 14
                : (anchorRect.Top - 14 - Height >= mon.Top ? anchorRect.Top - 14 - Height : mon.Bottom - Height - 12);
            Location = new Point(barX, barY);

            var dragHandle = new Panel { Dock = DockStyle.Left, Width = 100, BackColor = Color.FromArgb(32, 32, 38), Cursor = Cursors.SizeAll };
            dragHandle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };
            _countLabel = new Label
            {
                Text = "0 frames",
                ForeColor = Theme.TextPrimary,
                Font = Theme.Font(8.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 12)
            };
            _countLabel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };
            dragHandle.Controls.Add(_countLabel);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.BgHeader,
                Padding = new Padding(4, 5, 4, 5)
            };

            _btnAuto = new DarkPillButton("▶ Auto Scroll", Theme.BrandDark, Theme.Brand, 30, 96);
            _btnFinish = new DarkPillButton("✓ Finish", Theme.Success, Color.FromArgb(22, 163, 74), 30, 74);
            _btnCancel = new DarkPillButton("✕ Cancel", Color.FromArgb(38, 38, 48), Color.FromArgb(170, 220, 38, 38), 30, 72) { BorderColor = Color.FromArgb(58, 58, 70) };

            _btnAuto.Click += (s, e) => AutoScrollClicked?.Invoke();
            _btnFinish.Click += (s, e) => FinishClicked?.Invoke();
            _btnCancel.Click += (s, e) => CancelClicked?.Invoke();

            flow.Controls.AddRange(new Control[] { _btnAuto, _btnFinish, _btnCancel });
            Controls.Add(flow);
            Controls.Add(dragHandle);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.DisableWindowTransitions(Handle);
            NativeMethods.ExcludeFromCapture(Handle);
            using var path = Theme.CreateRoundedRect(new Rectangle(0, 0, Width, Height), 8);
            Region = new Region(path);
        }

        public void SetFrameCount(int n) => _countLabel.Text = $"{n} frames";
        public void SetSizeLabel(int w, int h) => _countLabel.Text = $"{w}×{h}px";
        public void SetAutoScrolling(bool active) => _btnAuto.PillText = active ? "⏸ Stop Auto" : "▶ Auto Scroll";
    }
}

