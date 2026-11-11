using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using QuickShot.Core;

namespace QuickShot.UI
{
    internal sealed class OverlayIconButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private bool _active;
        private Bitmap _normalBmp;
        private Bitmap _activeBmp;

        public string SvgName { get; }
        public bool DangerHover { get; set; }
        public string GlyphText { get; set; }

        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                Invalidate();
            }
        }

        public OverlayIconButton(string svgName = null, string glyphText = null)
        {
            SvgName = svgName;
            GlyphText = glyphText;
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            if (!string.IsNullOrEmpty(svgName))
            {
                _normalBmp = SvgIconRenderer.RenderToolIcon(svgName, 18);
                _activeBmp = SvgIconRenderer.RenderToolIcon(svgName, 18, Color.White);
            }
        }

        public void PerformClick() => OnClick(EventArgs.Empty);

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            if (_active)
            {
                using var bg = new SolidBrush(Color.FromArgb(72, 154, 127, 245));
                Theme.FillRoundedRect(g, bg, rect, 6);
                using var pen = new Pen(Theme.Brand, 1.25f);
                Theme.DrawRoundedRect(g, pen, rect, 6);
            }
            else if (_hover)
            {
                Color hoverColor = DangerHover
                    ? Color.FromArgb(_pressed ? 175 : 135, 220, 38, 38)
                    : Color.FromArgb(_pressed ? 255 : 220, 44, 44, 54);
                using var bg = new SolidBrush(hoverColor);
                Theme.FillRoundedRect(g, bg, rect, 6);
            }

            if (!string.IsNullOrEmpty(GlyphText))
            {
                Color fg = (_hover && DangerHover) ? Color.White : (_active || _hover ? Theme.TextPrimary : Theme.TextSecondary);
                using var font = Theme.Font(9.5f, FontStyle.Bold);
                using var br = new SolidBrush(fg);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.DrawString(GlyphText, font, br, new RectangleF(0, -0.5f, Width, Height), sf);
                return;
            }

            Bitmap bmp = (_active || _hover) ? _activeBmp : _normalBmp;
            if (bmp != null)
            {
                int ix = (Width - 18) / 2;
                int iy = (Height - 18) / 2;
                g.DrawImage(bmp, ix, iy, 18, 18);
            }
        }
    }

    internal sealed class DarkColorTriggerButton : Control
    {
        private bool _hover;
        private Color _selectedColor = Color.FromArgb(255, 59, 48);

        public Color SelectedColor
        {
            get => _selectedColor;
            set
            {
                _selectedColor = value;
                Invalidate();
            }
        }

        public DarkColorTriggerButton()
        {
            Size = new Size(40, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(Color.FromArgb(_hover ? 44 : 34, _hover ? 44 : 34, _hover ? 54 : 40)))
                Theme.FillRoundedRect(g, bg, box, 6);
            using (var border = new Pen(_hover ? Theme.Brand : Color.FromArgb(62, 62, 72), 1f))
                Theme.DrawRoundedRect(g, border, box, 6);

            using (var dotBr = new SolidBrush(_selectedColor))
                g.FillEllipse(dotBr, 7, 7, 13, 13);
            using (var ring = new Pen(Color.FromArgb(190, 255, 255, 255), 1.2f))
                g.DrawEllipse(ring, 7, 7, 13, 13);

            // Crisp small down chevron
            using var chevPen = new Pen(Theme.TextSecondary, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(chevPen, new[]
            {
                new PointF(26.5f, 12.2f),
                new PointF(29.8f, 15.6f),
                new PointF(33.1f, 12.2f)
            });
        }
    }

    internal sealed class DarkStrokeDropdownPill : Control
    {
        private bool _hover;
        private string _label = "2 pt";

        public string LabelText
        {
            get => _label;
            set
            {
                _label = value;
                Invalidate();
            }
        }

        public DarkStrokeDropdownPill()
        {
            Size = new Size(58, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(Color.FromArgb(_hover ? 44 : 34, _hover ? 44 : 34, _hover ? 54 : 40)))
                Theme.FillRoundedRect(g, bg, box, 6);
            using (var border = new Pen(_hover ? Theme.Brand : Color.FromArgb(62, 62, 72), 1f))
                Theme.DrawRoundedRect(g, border, box, 6);

            using (var font = Theme.Font(8.5f, FontStyle.Regular))
            using (var br = new SolidBrush(Theme.TextPrimary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(_label, font, br, new RectangleF(8, 0, Width - 22, Height), sf);
            }

            using var chevPen = new Pen(Theme.TextSecondary, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(chevPen, new[]
            {
                new PointF(Width - 15.5f, 12.2f),
                new PointF(Width - 12.2f, 15.6f),
                new PointF(Width - 8.9f, 12.2f)
            });
        }
    }

    internal sealed class DarkPillButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private bool _selected;
        private string _pillText;
        private Color _fillColor;
        private Color _hoverColor;

        public Color FillColor
        {
            get => _fillColor;
            set { _fillColor = value; Invalidate(); }
        }

        public Color HoverColor
        {
            get => _hoverColor;
            set { _hoverColor = value; Invalidate(); }
        }

        public Color PillTextColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.Transparent;

        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public string PillText
        {
            get => _pillText;
            set
            {
                _pillText = value;
                RecalcAutoWidth();
                Invalidate();
            }
        }

        public int MinPillWidth { get; set; } = 60;
        public int PillHeight { get; set; } = 28;
        public FontStyle PillFontStyle { get; set; } = FontStyle.Bold;

        public DarkPillButton(string text, Color fill, Color hover, int height = 28, int minWidth = 54)
        {
            _pillText = text;
            _fillColor = fill;
            _hoverColor = hover;
            PillHeight = height;
            MinPillWidth = minWidth;
            Cursor = Cursors.Hand;
            Margin = new Padding(2, 0, 2, 0);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            RecalcAutoWidth();
        }

        private void RecalcAutoWidth()
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            using var font = Theme.Font(8.5f, PillFontStyle);
            SizeF sz = g.MeasureString(_pillText ?? "", font);
            Size = new Size(Math.Max(MinPillWidth, (int)Math.Ceiling(sz.Width) + 18), PillHeight);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (_pressed) { _pressed = false; Invalidate(); } base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _selected
                ? Color.FromArgb(95, 154, 127, 245)
                : (_pressed ? ControlPaint.Dark(_hoverColor, 0.08f) : (_hover ? _hoverColor : _fillColor));

            if (fill.A > 0)
            {
                using var bg = new SolidBrush(fill);
                Theme.FillRoundedRect(g, bg, rect, 6);
            }

            Color border = _selected ? Theme.Brand : BorderColor;
            if (border.A > 0)
            {
                using var pen = new Pen(border, 1f);
                Theme.DrawRoundedRect(g, pen, rect, 6);
            }

            using var font = Theme.Font(8.5f, PillFontStyle);
            using var txtBr = new SolidBrush(PillTextColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_pillText ?? "", font, txtBr, new RectangleF(0, 0, Width, Height), sf);
        }
    }

    internal static class DarkToolTipHelper
    {
        public static void Configure(ToolTip tip)
        {
            tip.OwnerDraw = true;
            tip.InitialDelay = 280;
            tip.ReshowDelay = 120;
            tip.AutoPopDelay = 4000;
            tip.Popup += (s, e) =>
            {
                string txt = tip.GetToolTip(e.AssociatedControl) ?? "";
                using var font = Theme.Font(8.5f, FontStyle.Regular);
                Size sz = TextRenderer.MeasureText(txt, font);
                e.ToolTipSize = new Size(sz.Width + 14, sz.Height + 8);
            };
            tip.Draw += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var rect = new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                using var bg = new SolidBrush(Color.FromArgb(24, 24, 30));
                using var border = new Pen(Color.FromArgb(110, 154, 127, 245), 1f);
                e.Graphics.FillRectangle(bg, e.Bounds);
                e.Graphics.DrawRectangle(border, rect);
                using var font = Theme.Font(8.5f, FontStyle.Regular);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, e.Bounds, Color.FromArgb(244, 244, 248),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
    }
}
