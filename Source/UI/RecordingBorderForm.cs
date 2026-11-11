using QuickShot.Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickShot.UI
{
    /// <summary>
    /// Hollow 2px click-through dashed border around an active GIF or Scrolling capture region.
    /// Excluded from capture via WDA_EXCLUDEFROMCAPTURE so it NEVER appears inside recordings.
    /// </summary>
    public class RecordingBorderForm : Form
    {
        private readonly Rectangle _captureRect;
        private Color _borderColor = Theme.Brand;

        public RecordingBorderForm(Rectangle captureScreenRect)
        {
            _captureRect = captureScreenRect;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0.99;
            BackColor = Color.Black;
            Bounds = new Rectangle(captureScreenRect.X - 2, captureScreenRect.Y - 2, captureScreenRect.Width + 4, captureScreenRect.Height + 4);
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST |
                              NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                m.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.DisableWindowTransitions(Handle);
            NativeMethods.ExcludeFromCapture(Handle);
            ApplyHollowRegion();
        }

        private void ApplyHollowRegion()
        {
            IntPtr outer = NativeMethods.CreateRectRgn(0, 0, Width, Height);
            IntPtr inner = NativeMethods.CreateRectRgn(2, 2, Width - 2, Height - 2);
            NativeMethods.CombineRgn(outer, outer, inner, NativeMethods.RGN_DIFF);
            NativeMethods.DeleteObject(inner);
            if (NativeMethods.SetWindowRgn(Handle, outer, true) == 0)
            {
                NativeMethods.DeleteObject(outer);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 24));
            e.Graphics.FillRectangle(bgBrush, ClientRectangle);
            using var pen = new Pen(_borderColor, 2f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
        }
    }
}

