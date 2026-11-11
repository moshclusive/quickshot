using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public class GifRecorderSession : IDisposable
    {
        private readonly Rectangle _captureRect;
        private readonly bool _showCursor;
        private readonly Action<byte[], Rectangle> _onCompleted;
        private readonly Action _onAborted;
        private readonly RecordingBorderForm _borderForm;
        private readonly GifControlBarForm _barForm;
        private readonly Timer _captureTimer;
        private readonly List<GifEncoder.GifFrame> _frames = new List<GifEncoder.GifFrame>();

        private bool _isRecording = true;
        private bool _isPaused;
        private int _ticks;
        private int _seconds;

        public GifRecorderSession(Rectangle captureRect, bool showCursor, Action<byte[], Rectangle> onCompleted, Action onAborted)
        {
            _captureRect = captureRect;
            _showCursor = showCursor;
            _onCompleted = onCompleted;
            _onAborted = onAborted;

            _borderForm = new RecordingBorderForm(captureRect);
            _barForm = new GifControlBarForm(captureRect);
            _barForm.StopClicked += () => _ = StopAndSaveAsync();
            _barForm.PauseClicked += TogglePause;
            _barForm.RestartClicked += Restart;
            _barForm.AbortClicked += Abort;

            _captureTimer = new Timer { Interval = 100 }; // 10 FPS
            _captureTimer.Tick += OnCaptureTick;
        }

        public void Start()
        {
            _borderForm.Show();
            _barForm.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(120);
            Application.DoEvents();
            if (!_isRecording) return;

            GrabFrame();
            _captureTimer.Start();
        }

        public void StopAndSave() => _ = StopAndSaveAsync();
        public void TogglePause()
        {
            if (!_isRecording) return;
            _isPaused = !_isPaused;
            _barForm.SetPaused(_isPaused);
            _borderForm.BorderColor = _isPaused ? Theme.Warning : Theme.Brand;
        }

        public void Restart()
        {
            if (!_isRecording) return;
            _frames.Clear();
            _ticks = 0;
            _seconds = 0;
            _isPaused = false;
            _barForm.SetPaused(false);
            _barForm.SetTime(0);
            _borderForm.BorderColor = Theme.Brand;
            GrabFrame();
        }

        public void Abort()
        {
            if (!_isRecording) return;
            _isRecording = false;
            _captureTimer.Stop();
            _frames.Clear();
            CloseForms();
            _onAborted?.Invoke();
        }

        private void OnCaptureTick(object sender, EventArgs e)
        {
            if (!_isRecording || _isPaused) return;
            _ticks++;
            if (_ticks % 10 == 0)
            {
                _seconds++;
                _barForm.SetTime(_seconds);
            }
            if (_frames.Count < 450)
            {
                GrabFrame();
            }
        }

        private void GrabFrame()
        {
            try
            {
                using Bitmap raw = ScreenCaptureService.CaptureRegionExcludingWindows(_captureRect, false, _barForm.Handle, _borderForm.Handle);
                if (_showCursor)
                {
                    DrawMouseHighlight(raw, _captureRect);
                }

                if (raw.Width > 1920)
                {
                    int outW = 1920;
                    int outH = Math.Max(16, (int)Math.Round(raw.Height * (1920.0 / raw.Width)));
                    using var scaled = new Bitmap(outW, outH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(raw, 0, 0, outW, outH);
                    }
                    _frames.Add(GifEncoder.CaptureFrameFromBitmap(scaled, 10));
                }
                else
                {
                    _frames.Add(GifEncoder.CaptureFrameFromBitmap(raw, 10));
                }
            }
            catch { }
        }

        private static void DrawMouseHighlight(Bitmap frame, Rectangle captureRect)
        {
            Point mouse = Cursor.Position;
            int relX = mouse.X - captureRect.X;
            int relY = mouse.Y - captureRect.Y;

            if (relX < -30 || relY < -30 || relX > frame.Width + 30 || relY > frame.Height + 30)
                return;

            using (var g = Graphics.FromImage(frame))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Subtle light yellow filled circle, NO stroke border
                int radius = 22;
                using (var brush = new SolidBrush(Color.FromArgb(85, 255, 235, 59)))
                {
                    g.FillEllipse(brush, relX - radius, relY - radius, radius * 2, radius * 2);
                }

                try
                {
                    Cursors.Default.Draw(g, new Rectangle(relX, relY, 32, 32));
                }
                catch { }
            }
        }

        private async Task StopAndSaveAsync()
        {
            if (!_isRecording) return;
            _isRecording = false;
            _captureTimer.Stop();
            var captured = new List<GifEncoder.GifFrame>(_frames);
            _frames.Clear();
            CloseForms();

            if (captured.Count == 0)
            {
                _onAborted?.Invoke();
                return;
            }

            try
            {
                byte[] gifBytes = await Task.Run(() => GifEncoder.Encode(captured));
                _onCompleted?.Invoke(gifBytes, _captureRect);
            }
            catch
            {
                _onAborted?.Invoke();
            }
        }

        private void CloseForms()
        {
            try { if (!_barForm.IsDisposed) _barForm.Close(); } catch { }
            try { if (!_borderForm.IsDisposed) _borderForm.Close(); } catch { }
        }

        public void Dispose()
        {
            _captureTimer?.Dispose();
            if (!_barForm.IsDisposed) _barForm.Dispose();
            if (!_borderForm.IsDisposed) _borderForm.Dispose();
        }
    }

    internal class GifControlBarForm : Form
    {
        private readonly Panel _dragHandle;
        private readonly Panel _dot;
        private readonly Label _timeLabel;
        private readonly DarkPillButton _btnStop;
        private readonly DarkPillButton _btnPause;
        private readonly DarkPillButton _btnRestart;
        private readonly DarkPillButton _btnAbort;
        private bool _paused;

        public event Action StopClicked;
        public event Action PauseClicked;
        public event Action RestartClicked;
        public event Action AbortClicked;

        public GifControlBarForm(Rectangle anchorRect)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Theme.BgHeader;
            Size = new Size(406, 40);

            // Position cleanly OUTSIDE the capture rectangle (including over taskbar if box ends above it!)
            Screen scr = Screen.FromRectangle(anchorRect);
            Rectangle mon = scr.WorkingArea;
            Rectangle fullMon = scr.Bounds;
            int barX = Math.Max(mon.Left + 8, Math.Min(mon.Right - Width - 8, anchorRect.X + (anchorRect.Width - Width) / 2));
            int barY;
            if (anchorRect.Bottom + 14 + Height <= fullMon.Bottom)
                barY = anchorRect.Bottom + 14;
            else if (anchorRect.Top - 14 - Height >= mon.Top)
                barY = anchorRect.Top - 14 - Height;
            else
                barY = mon.Bottom - Height - 12;

            Location = new Point(barX, barY);

            _dragHandle = new Panel
            {
                Dock = DockStyle.Left,
                Width = 104,
                BackColor = Color.FromArgb(32, 32, 38),
                Cursor = Cursors.SizeAll
            };
            _dragHandle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };

            _dot = new Panel { Size = new Size(8, 8), Location = new Point(12, 16), BackColor = Theme.Danger };
            _dot.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Color.FromArgb(32, 32, 38));
                using var br = new SolidBrush(_paused ? Theme.Warning : Theme.Danger);
                e.Graphics.FillEllipse(br, 0, 0, 7, 7);
            };
            _dot.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };

            _timeLabel = new Label
            {
                Text = "00:00:00",
                ForeColor = Theme.TextPrimary,
                Font = Theme.Font(9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(26, 12)
            };
            _timeLabel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle); };

            _dragHandle.Controls.Add(_dot);
            _dragHandle.Controls.Add(_timeLabel);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.BgHeader,
                Padding = new Padding(4, 5, 4, 5)
            };

            _btnStop = new DarkPillButton("⏹ Stop", Color.FromArgb(185, 38, 48), Color.FromArgb(220, 48, 60), 30, 68);
            _btnPause = new DarkPillButton("⏸ Pause", Color.FromArgb(38, 38, 48), Color.FromArgb(54, 54, 68), 30, 72) { BorderColor = Color.FromArgb(58, 58, 70), PillFontStyle = FontStyle.Regular };
            _btnRestart = new DarkPillButton("↺ Restart", Color.FromArgb(38, 38, 48), Color.FromArgb(54, 54, 68), 30, 72) { BorderColor = Color.FromArgb(58, 58, 70), PillFontStyle = FontStyle.Regular };
            _btnAbort = new DarkPillButton("✕ Abort", Color.FromArgb(38, 38, 48), Color.FromArgb(170, 220, 38, 38), 30, 66) { BorderColor = Color.FromArgb(58, 58, 70), PillFontStyle = FontStyle.Regular };

            _btnStop.Click += (s, e) => StopClicked?.Invoke();
            _btnPause.Click += (s, e) => PauseClicked?.Invoke();
            _btnRestart.Click += (s, e) => RestartClicked?.Invoke();
            _btnAbort.Click += (s, e) => AbortClicked?.Invoke();

            flow.Controls.AddRange(new Control[] { _btnStop, _btnPause, _btnRestart, _btnAbort });

            Controls.Add(flow);
            Controls.Add(_dragHandle);
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ExcludeFromCapture(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
            Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 8);
        }

        public void SetTime(int totalSec)
        {
            var ts = TimeSpan.FromSeconds(totalSec);
            _timeLabel.Text = ts.ToString(@"hh\:mm\:ss");
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _btnPause.PillText = paused ? "▶ Resume" : "⏸ Pause";
            _dot.Invalidate();
        }
    }
}

