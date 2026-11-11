using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickShot.UI
{
    /// <summary>
    /// Live Scroll-to-Select Capture Region Session:
    /// Scrolls the underlying window cleanly (via edge-drag, mouse wheel, or Auto-Scroll)
    /// excluding Taskbar and HUD bar, stitching all lower scrolled content smoothly.
    /// </summary>
    public class LiveScrollRegionSession : IDisposable
    {
        private readonly Rectangle _targetRect;
        private readonly int _initialScrollDelta;
        private readonly bool _showCursor;
        private readonly Action<Bitmap, Rectangle> _onCompleted;
        private readonly Action _onCancelled;
        private readonly RecordingBorderForm _borderForm;
        private readonly LiveScrollHudBarForm _barForm;
        private readonly Timer _pollTimer;
        private readonly List<Bitmap> _frames = new List<Bitmap>();
        private readonly List<PreScrollFrameData> _preScrollFrames;
        private readonly List<AnnotationItem> _docAnnotations;
        private VirtualScrollCanvas _canvas;

        private Bitmap _lastPollSnapshot;
        private bool _active = true;
        private bool _autoScrolling;
        private int _estimatedTotalH;

        public LiveScrollRegionSession(
            Rectangle targetRect,
            Bitmap initialFrame,
            int initialScrollDelta,
            bool showCursor,
            Action<Bitmap, Rectangle> onCompleted,
            Action onCancelled,
            List<PreScrollFrameData> preScrollFrames = null,
            List<AnnotationItem> docAnnotations = null)
        {
            _targetRect = targetRect;
            _initialScrollDelta = initialScrollDelta;
            _showCursor = showCursor;
            _onCompleted = onCompleted;
            _onCancelled = onCancelled;
            _preScrollFrames = preScrollFrames;
            _docAnnotations = docAnnotations;

            if ((_preScrollFrames != null && _preScrollFrames.Count > 0) || (_docAnnotations != null && _docAnnotations.Count > 0))
            {
                _canvas = ScrollingCaptureSession.BuildSeededCanvas(_targetRect, _preScrollFrames, initialFrame, _docAnnotations, out _);
                _estimatedTotalH = _canvas.CanvasHeight;
                if (initialFrame != null)
                {
                    _lastPollSnapshot = (Bitmap)initialFrame.Clone();
                    initialFrame.Dispose();
                }
            }
            else if (initialFrame != null)
            {
                _frames.Add(initialFrame);
                _lastPollSnapshot = (Bitmap)initialFrame.Clone();
                _estimatedTotalH = initialFrame.Height;
            }
            else
            {
                _estimatedTotalH = targetRect.Height;
            }

            _borderForm = new RecordingBorderForm(targetRect);
            _barForm = new LiveScrollHudBarForm(targetRect, _canvas != null ? 1 : _frames.Count, _targetRect.Width, _estimatedTotalH);
            _barForm.CaptureClicked += FinishAndStitch;
            _barForm.AutoClicked += () => _ = ToggleAutoScrollAsync();
            _barForm.CancelClicked += Cancel;

            _pollTimer = new Timer { Interval = 210 };
            _pollTimer.Tick += OnPollTick;
        }

        public void Start()
        {
            _borderForm.Show();
            _barForm.Show();
            Application.DoEvents();

            if (_initialScrollDelta != 0)
            {
                int cx = _targetRect.X + _targetRect.Width / 2;
                int cy = _targetRect.Y + _targetRect.Height / 2;
                Rectangle guard = Rectangle.Inflate(_barForm.Bounds, 36, 36);
                NativeMethods.ScrollAtPoint(cx, cy, _initialScrollDelta, guard);
            }

            _pollTimer.Start();
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            if (!_active || _frames.Count >= 90) return;
            try
            {
                // Keep scrolling downward smoothly while user holds Left Mouse near/below WorkingArea bottom edge
                bool leftBtnHeld = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (leftBtnHeld && !_autoScrolling)
                {
                    Screen scr = Screen.FromRectangle(_targetRect);
                    if (Control.MousePosition.Y >= scr.WorkingArea.Bottom - 20)
                    {
                        int cx = _targetRect.X + _targetRect.Width / 2;
                        int cy = _targetRect.Y + _targetRect.Height / 2;
                        Rectangle guard = Rectangle.Inflate(_barForm.Bounds, 36, 36);
                        NativeMethods.ScrollAtPoint(cx, cy, -180, guard);
                    }
                }

                Bitmap cur = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, _showCursor, _barForm.Handle, _borderForm.Handle);
                if (_canvas != null)
                {
                    using (cur)
                    {
                        if (ScrollingCaptureSession.ApplyFrameAndAnchorAnnotations(_canvas, cur, 0, 0, _docAnnotations, out _))
                        {
                            _lastPollSnapshot?.Dispose();
                            _lastPollSnapshot = (Bitmap)cur.Clone();
                            UpdateHudEstimate();
                        }
                    }
                    return;
                }
                if (_lastPollSnapshot == null)
                {
                    _lastPollSnapshot = (Bitmap)cur.Clone();
                    if (_frames.Count == 0) _frames.Add(cur);
                    else cur.Dispose();
                    return;
                }

                Bitmap lastCommitted = _frames[_frames.Count - 1];
                if (!ScrollingCaptureSession.FramesIdentical(lastCommitted, cur))
                {
                    _frames.Add(cur);
                    _lastPollSnapshot.Dispose();
                    _lastPollSnapshot = (Bitmap)cur.Clone();
                    UpdateHudEstimate();
                }
                else
                {
                    cur.Dispose();
                }
            }
            catch { }
        }

        private async Task ToggleAutoScrollAsync()
        {
            if (!_active) return;
            if (_autoScrolling)
            {
                _autoScrolling = false;
                _barForm.SetAutoScrolling(false);
                return;
            }

            _autoScrolling = true;
            _barForm.SetAutoScrolling(true);
            int cx = _targetRect.X + _targetRect.Width / 2;
            int cy = _targetRect.Y + _targetRect.Height / 2;

            for (int step = 0; step < 30 && _autoScrolling && _active; step++)
            {
                Rectangle guard = Rectangle.Inflate(_barForm.Bounds, 36, 36);
                await Task.Run(() => NativeMethods.ScrollAtPoint(cx, cy, -320, guard));
                await Task.Delay(320);
                if (!_autoScrolling || !_active) break;

                Bitmap snap = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, _showCursor, _barForm.Handle, _borderForm.Handle);
                if (_canvas != null)
                {
                    bool moved;
                    int dy;
                    using (snap)
                    {
                        moved = ScrollingCaptureSession.ApplyFrameAndAnchorAnnotations(_canvas, snap, 1, 0, _docAnnotations, out dy);
                        if (moved && dy != 0)
                        {
                            _lastPollSnapshot?.Dispose();
                            _lastPollSnapshot = (Bitmap)snap.Clone();
                        }
                    }
                    if (!moved || dy == 0) break;
                    UpdateHudEstimate();
                    continue;
                }

                bool sameAsLast = _frames.Count > 0 && ScrollingCaptureSession.FramesIdentical(_frames[_frames.Count - 1], snap);
                if (sameAsLast)
                {
                    snap.Dispose();
                    break;
                }
                _frames.Add(snap);
                _lastPollSnapshot?.Dispose();
                _lastPollSnapshot = (Bitmap)snap.Clone();
                UpdateHudEstimate();
            }

            _autoScrolling = false;
            if (!_barForm.IsDisposed) _barForm.SetAutoScrolling(false);
        }

        private void UpdateHudEstimate()
        {
            if (_canvas != null)
            {
                _barForm.UpdateStats(Math.Max(1, _canvas.CanvasHeight / Math.Max(1, _targetRect.Height / 2)), _canvas.CanvasWidth, _canvas.CanvasHeight);
                return;
            }
            if (_frames.Count <= 1)
                _estimatedTotalH = _targetRect.Height;
            else
                _estimatedTotalH = _targetRect.Height + (_frames.Count - 1) * Math.Max(48, (int)(_targetRect.Height * 0.35));

            _barForm.UpdateStats(_frames.Count, _targetRect.Width, _estimatedTotalH);
        }

        private void FinishAndStitch()
        {
            if (!_active) return;
            _active = false;
            _autoScrolling = false;
            _pollTimer.Stop();

            try
            {
                Bitmap finalGrab = ScreenCaptureService.CaptureRegionExcludingWindows(_targetRect, _showCursor, _barForm.Handle, _borderForm.Handle);
                if (_canvas != null)
                {
                    using (finalGrab)
                        ScrollingCaptureSession.ApplyFrameAndAnchorAnnotations(_canvas, finalGrab, 0, 0, _docAnnotations, out _);
                }
                else if (_frames.Count == 0 || !ScrollingCaptureSession.FramesIdentical(_frames[_frames.Count - 1], finalGrab))
                    _frames.Add(finalGrab);
                else
                    finalGrab.Dispose();
            }
            catch { }

            CloseForms();
            Bitmap stitched = _canvas != null
                ? ScrollingCaptureSession.FinalizeCanvasWithAnnotations(_canvas, _docAnnotations)
                : ScrollingCaptureSession.StitchFrames(_frames);
            _canvas?.Dispose();
            _canvas = null;
            ScrollingCaptureSession.DisposePreScrollFrames(_preScrollFrames);
            DisposeFrames();

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
            _pollTimer.Stop();
            CloseForms();
            _canvas?.Dispose();
            _canvas = null;
            ScrollingCaptureSession.DisposePreScrollFrames(_preScrollFrames);
            DisposeFrames();
            _onCancelled?.Invoke();
        }

        private void CloseForms()
        {
            try { if (!_barForm.IsDisposed) _barForm.Close(); } catch { }
            try { if (!_borderForm.IsDisposed) _borderForm.Close(); } catch { }
        }

        private void DisposeFrames()
        {
            _lastPollSnapshot?.Dispose();
            _lastPollSnapshot = null;
            foreach (var f in _frames) f.Dispose();
            _frames.Clear();
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            DisposeFrames();
            if (!_barForm.IsDisposed) _barForm.Dispose();
            if (!_borderForm.IsDisposed) _borderForm.Dispose();
        }
    }

    internal sealed class LiveScrollHudBarForm : Form
    {
        private readonly Label _infoLabel;
        private readonly DarkPillButton _btnAuto;

        public event Action CaptureClicked;
        public event Action AutoClicked;
        public event Action CancelClicked;

        public LiveScrollHudBarForm(Rectangle anchorRect, int views, int width, int estHeight)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Theme.BgHeader;
            Size = new Size(432, 40);

            Screen scr = Screen.FromRectangle(anchorRect);
            Rectangle mon = scr.WorkingArea;
            Rectangle fullMon = scr.Bounds;
            int barX = Math.Max(mon.Left + 8, Math.Min(mon.Right - Width - 8, anchorRect.X + (anchorRect.Width - Width) / 2));
            int barY = (anchorRect.Bottom + 12 + Height <= fullMon.Bottom)
                ? anchorRect.Bottom + 12
                : (anchorRect.Top - 12 - Height >= mon.Top ? anchorRect.Top - 12 - Height : mon.Bottom - Height - 12);
            Location = new Point(barX, barY);

            var dragHandle = new Panel
            {
                Dock = DockStyle.Left,
                Width = 176,
                BackColor = Color.FromArgb(32, 32, 38),
                Cursor = Cursors.SizeAll
            };
            dragHandle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };

            _infoLabel = new Label
            {
                Text = FormatText(views, width, estHeight),
                ForeColor = Color.FromArgb(208, 196, 255),
                Font = Theme.Font(8.4f, FontStyle.Bold),
                Location = new Point(10, 12),
                Size = new Size(162, 18),
                AutoEllipsis = true
            };
            _infoLabel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };
            dragHandle.Controls.Add(_infoLabel);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.BgHeader,
                Padding = new Padding(4, 5, 4, 5)
            };

            var btnCapture = new DarkPillButton("✓ Capture", Theme.BrandDark, Theme.Brand, 30, 86);
            _btnAuto = new DarkPillButton("↓ Auto", Color.FromArgb(42, 38, 66), Theme.BrandDark, 30, 68) { BorderColor = Color.FromArgb(72, 62, 112) };
            var btnCancel = new DarkPillButton("✕ Cancel", Color.FromArgb(38, 38, 48), Color.FromArgb(170, 220, 38, 38), 30, 76) { BorderColor = Color.FromArgb(58, 58, 70), PillFontStyle = FontStyle.Regular };

            btnCapture.Click += (s, e) => CaptureClicked?.Invoke();
            _btnAuto.Click += (s, e) => AutoClicked?.Invoke();
            btnCancel.Click += (s, e) => CancelClicked?.Invoke();

            flow.Controls.AddRange(new Control[] { btnCapture, _btnAuto, btnCancel });
            Controls.Add(flow);
            Controls.Add(dragHandle);
        }

        private static string FormatText(int views, int w, int estH) =>
            $"{views} frame{(views == 1 ? "" : "s")} • {w}×~{estH}px";

        public void UpdateStats(int views, int w, int estH)
        {
            if (!IsDisposed) _infoLabel.Text = FormatText(views, w, estH);
        }

        public void SetAutoScrolling(bool active)
        {
            if (!IsDisposed) _btnAuto.PillText = active ? "⏹ Stop" : "↓ Auto";
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(120, 154, 127, 245), 1.1f);
            Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 8);
        }
    }
}
