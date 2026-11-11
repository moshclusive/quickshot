using QuickShot.Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public class PreviewPopupForm : Form
    {
        private readonly CaptureResult _result;
        private readonly StorageService _storage;
        private readonly bool _isMinimal;
        private readonly string _customMessage;
        private readonly Timer _dismissTimer;
        private readonly Timer _clickTimer;
        private bool _hovering;
        private bool _closeHover;
        private int _clickCount;

        public PreviewPopupForm(CaptureResult result, StorageService storage, string style, int durationSec, string customMessage = null)
        {
            _result = result;
            _storage = storage;
            _customMessage = customMessage;
            _isMinimal = style == "minimal" || !string.IsNullOrEmpty(customMessage) || result.PreviewImage == null;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 20, 24);
            Cursor = Cursors.Hand;

            int w = _isMinimal ? 236 : 284;
            int h = _isMinimal ? 46 : 166;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.Right - w - 16, wa.Bottom - h - 16, w, h);

            _dismissTimer = new Timer { Interval = Math.Max(1, durationSec) * 1000 };
            _dismissTimer.Tick += (s, e) => { _dismissTimer.Stop(); Close(); };

            _clickTimer = new Timer { Interval = 230 };
            _clickTimer.Tick += (s, e) =>
            {
                _clickTimer.Stop();
                if (_clickCount == 1 && !string.IsNullOrEmpty(_result?.FilePath))
                {
                    _storage.OpenFile(_result.FilePath);
                    Close();
                }
                _clickCount = 0;
            };
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
            using var path = Theme.CreateRoundedRect(new Rectangle(0, 0, Width, Height), 10);
            Region = new Region(path);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _dismissTimer.Start();
        }

        private Rectangle CloseButtonRect => _isMinimal
            ? new Rectangle(Width - 28, (Height - 22) / 2, 22, 22)
            : new Rectangle(Width - 28, 6, 22, 22);

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovering = true;
            _dismissTimer.Stop();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovering = false;
            _closeHover = false;
            _dismissTimer.Start();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool ch = CloseButtonRect.Contains(e.Location);
            if (ch != _closeHover) { _closeHover = ch; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Close();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if (CloseButtonRect.Contains(e.Location))
                {
                    Close();
                    return;
                }
                _clickCount++;
                if (_clickCount >= 2)
                {
                    _clickTimer.Stop();
                    _clickCount = 0;
                    if (!string.IsNullOrEmpty(_result?.FilePath))
                    {
                        _storage.ShowInFolder(_result.FilePath);
                    }
                    Close();
                }
                else
                {
                    _clickTimer.Start();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (!_isMinimal && _result?.PreviewImage != null)
            {
                DrawImageAspectCover(g, _result.PreviewImage, new Rectangle(0, 0, Width, Height));
            }
            else
            {
                using var bg = new SolidBrush(Color.FromArgb(20, 20, 25));
                g.FillRectangle(bg, ClientRectangle);

                using var dotBr = new SolidBrush(Theme.Brand);
                g.FillEllipse(dotBr, 14, (Height - 8) / 2, 8, 8);

                string label = !string.IsNullOrEmpty(_customMessage)
                    ? _customMessage
                    : (_result?.IsGif == true ? "GIF Capture Done" : "Screenshot taken");

                using var font = Theme.Font(9.5f, FontStyle.Bold);
                using var txtBr = new SolidBrush(Theme.TextPrimary);
                g.DrawString(label, font, txtBr, new RectangleF(30, 0, Width - 62, Height),
                    new StringFormat { LineAlignment = StringAlignment.Center });
            }

            // Close button (shown on hover or always subtle in minimal card)
            if (_hovering || _isMinimal)
            {
                var cr = CloseButtonRect;
                using var cbr = new SolidBrush(_closeHover ? Theme.Danger : Color.FromArgb(185, 15, 15, 20));
                g.FillEllipse(cbr, cr);
                using var f = Theme.Font(8.5f, FontStyle.Bold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("✕", f, Brushes.White, cr, sf);
            }

            // Border
            using var borderPen = new Pen(_hovering ? Theme.Brand : Color.FromArgb(115, 154, 127, 245), 1.5f);
            Theme.DrawRoundedRect(g, borderPen, new Rectangle(0, 0, Width - 1, Height - 1), 10);
        }

        private static void DrawImageAspectCover(Graphics g, Image img, Rectangle dst)
        {
            float scale = Math.Max((float)dst.Width / img.Width, (float)dst.Height / img.Height);
            float sw = dst.Width / scale, sh = dst.Height / scale;
            float sx = (img.Width - sw) / 2f, sy = (img.Height - sh) / 2f;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, dst, new RectangleF(sx, sy, sw, sh), GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dismissTimer?.Dispose();
                _clickTimer?.Dispose();
                _result?.PreviewImage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

