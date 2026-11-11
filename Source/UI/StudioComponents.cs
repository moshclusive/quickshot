using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace QuickShot.UI
{
    internal sealed class StudioHeaderButton : Control
    {
        public enum Kind { Minimize, Close }
        private readonly Kind _kind;
        private bool _hover;
        private bool _pressed;

        public StudioHeaderButton(Kind kind)
        {
            _kind = kind;
            Size = new Size(32, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (_pressed) { _pressed = false; Invalidate(); } base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            if (_hover)
            {
                Color bgColor = _kind == Kind.Close
                    ? Color.FromArgb(_pressed ? 235 : 205, 220, 38, 38)
                    : Color.FromArgb(_pressed ? 255 : 220, 46, 46, 58);
                using var bg = new SolidBrush(bgColor);
                Theme.FillRoundedRect(g, bg, rect, 6);
            }

            Color fg = _hover ? Color.White : Color.FromArgb(182, 182, 200);
            using var pen = new Pen(fg, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = Width / 2f;
            float cy = Height / 2f;
            if (_kind == Kind.Minimize)
            {
                g.DrawLine(pen, cx - 5f, cy + 1f, cx + 5f, cy + 1f);
            }
            else
            {
                g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                g.DrawLine(pen, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
            }
        }
    }

    internal sealed class StudioTabButton : Control
    {
        private bool _hover;
        private bool _active;

        public string Label { get; set; }
        public bool Active
        {
            get => _active;
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        public StudioTabButton(string label)
        {
            Label = label;
            Size = new Size(102, 34);
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

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            if (_active)
            {
                using var bg = new SolidBrush(Color.FromArgb(52, 154, 127, 245));
                Theme.FillRoundedRect(g, bg, rect, 7);
                using var border = new Pen(Color.FromArgb(180, 154, 127, 245), 1.15f);
                Theme.DrawRoundedRect(g, border, rect, 7);
            }
            else if (_hover)
            {
                using var bg = new SolidBrush(Color.FromArgb(34, 34, 44));
                Theme.FillRoundedRect(g, bg, rect, 7);
            }

            Color txtColor = _active
                ? Color.FromArgb(248, 248, 252)
                : (_hover ? Color.FromArgb(228, 228, 240) : Color.FromArgb(156, 156, 178));

            using var font = Theme.Font(9.2f, _active ? FontStyle.Bold : FontStyle.Regular);
            using var txtBr = new SolidBrush(txtColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Label, font, txtBr, new RectangleF(0, 0, Width, Height), sf);
        }
    }

    internal sealed class StudioActionCard : Control
    {
        private bool _hover;
        private bool _pressed;
        private string _hotkeyText = "";
        private Bitmap _iconBmp;

        public string TitleText { get; set; }
        public string SubtitleText { get; set; }
        public bool UseFolderGlyph { get; set; }
        public bool UseScrollGlyph { get; set; }
        public bool ShowEmptyHotkey { get; set; } = true;

        public string HotkeyText
        {
            get => _hotkeyText;
            set { _hotkeyText = value ?? ""; Invalidate(); }
        }

        public StudioActionCard(string title, string subtitle, string svgToolName = null, bool useFolderGlyph = false, bool useScrollGlyph = false, int height = 66)
        {
            TitleText = title;
            SubtitleText = subtitle;
            UseFolderGlyph = useFolderGlyph;
            UseScrollGlyph = useScrollGlyph;
            Size = new Size(448, height);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            if (!string.IsNullOrEmpty(svgToolName))
            {
                _iconBmp = SvgIconRenderer.RenderToolIcon(svgToolName, 20, Color.FromArgb(196, 181, 253));
            }
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
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var cardRect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _pressed
                ? Color.FromArgb(36, 33, 52)
                : (_hover ? Color.FromArgb(31, 31, 41) : Color.FromArgb(25, 25, 32));
            using (var bg = new SolidBrush(fill))
                Theme.FillRoundedRect(g, bg, cardRect, 10);

            Color borderCol = _hover ? Theme.Brand : Color.FromArgb(46, 46, 58);
            using (var pen = new Pen(borderCol, _hover ? 1.25f : 1f))
                Theme.DrawRoundedRect(g, pen, cardRect, 10);

            // Icon well on left (vertically centered)
            int wellY = (Height - 38) / 2;
            var wellRect = new Rectangle(13, wellY, 38, 38);
            using (var wellBg = new SolidBrush(_hover ? Color.FromArgb(56, 154, 127, 245) : Color.FromArgb(38, 34, 56)))
                Theme.FillRoundedRect(g, wellBg, wellRect, 8);
            using (var wellPen = new Pen(_hover ? Color.FromArgb(140, 154, 127, 245) : Color.FromArgb(62, 54, 92), 1f))
                Theme.DrawRoundedRect(g, wellPen, wellRect, 8);

            if (UseFolderGlyph)
            {
                DrawFolderVector(g, wellRect);
            }
            else if (UseScrollGlyph)
            {
                DrawScrollVector(g, wellRect);
            }
            else if (_iconBmp != null)
            {
                g.DrawImage(_iconBmp, wellRect.X + 9, wellRect.Y + 9, 20, 20);
            }

            // Right hotkey badge measure first so text never overlaps badge
            int rightReserve = 16;
            string badgeTxt = !string.IsNullOrWhiteSpace(_hotkeyText) ? _hotkeyText : (ShowEmptyHotkey ? "None" : "");
            if (!string.IsNullOrEmpty(badgeTxt))
            {
                using var hkFont = Theme.Font(8f, FontStyle.Bold);
                SizeF hkSize = g.MeasureString(badgeTxt, hkFont);
                int pillW = Math.Max(46, (int)Math.Ceiling(hkSize.Width) + 18);
                int pillH = 24;
                var pillRect = new Rectangle(Width - pillW - 14, (Height - pillH) / 2, pillW, pillH);
                bool hasKey = !string.IsNullOrWhiteSpace(_hotkeyText);

                using (var pBg = new SolidBrush(hasKey ? Color.FromArgb(40, 154, 127, 245) : Color.FromArgb(32, 32, 42)))
                    Theme.FillRoundedRect(g, pBg, pillRect, 6);
                using (var pBorder = new Pen(hasKey ? Color.FromArgb(130, 154, 127, 245) : Color.FromArgb(54, 54, 68), 1f))
                    Theme.DrawRoundedRect(g, pBorder, pillRect, 6);
                using (var pTxt = new SolidBrush(hasKey ? Color.FromArgb(208, 194, 255) : Color.FromArgb(120, 120, 140)))
                using (var sfC = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(badgeTxt, hkFont, pTxt, pillRect, sfC);

                rightReserve = pillW + 26;
            }

            // Crisp bright typography (NO Enabled=false labels, NO & mnemonic parsing!)
            int textLeft = 63;
            int textWidth = Math.Max(80, Width - textLeft - rightReserve);
            int topTextY = (Height - 38) / 2 - 1;

            using (var titleFont = Theme.Font(10f, FontStyle.Bold))
            using (var titleBr = new SolidBrush(Color.FromArgb(244, 244, 250)))
            using (var sfT = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(TitleText ?? "", titleFont, titleBr, new RectangleF(textLeft, topTextY, textWidth, 20), sfT);
            }

            using (var subFont = Theme.Font(8.4f, FontStyle.Regular))
            using (var subBr = new SolidBrush(Color.FromArgb(156, 156, 178)))
            using (var sfS = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(SubtitleText ?? "", subFont, subBr, new RectangleF(textLeft, topTextY + 21, textWidth, 18), sfS);
            }
        }

        private static void DrawFolderVector(Graphics g, Rectangle well)
        {
            float x = well.X + 9;
            float y = well.Y + 11;
            using var path = new GraphicsPath();
            path.AddLine(x, y + 2, x, y + 15);
            path.AddLine(x, y + 15, x + 20, y + 15);
            path.AddLine(x + 20, y + 15, x + 20, y + 4);
            path.AddLine(x + 20, y + 4, x + 10, y + 4);
            path.AddLine(x + 10, y + 4, x + 7.5f, y + 1);
            path.AddLine(x + 7.5f, y + 1, x + 1.5f, y + 1);
            path.CloseFigure();

            using var br = new SolidBrush(Color.FromArgb(72, 196, 181, 253));
            using var pen = new Pen(Color.FromArgb(196, 181, 253), 1.6f) { LineJoin = LineJoin.Round };
            g.FillPath(br, path);
            g.DrawPath(pen, path);
        }

        private static void DrawScrollVector(Graphics g, Rectangle well)
        {
            float cx = well.X + well.Width / 2f;
            float cy = well.Y + well.Height / 2f;
            using var framePen = new Pen(Color.FromArgb(196, 181, 253), 1.55f) { LineJoin = LineJoin.Round };
            Theme.DrawRoundedRect(g, framePen, new Rectangle(well.X + 10, well.Y + 8, 18, 22), 3);
            using var arrowPen = new Pen(Color.FromArgb(228, 218, 255), 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(arrowPen, cx, cy - 6, cx, cy + 6);
            g.DrawLines(arrowPen, new[] { new PointF(cx - 3.2f, cy + 2.8f), new PointF(cx, cy + 6f), new PointF(cx + 3.2f, cy + 2.8f) });
            g.DrawLines(arrowPen, new[] { new PointF(cx - 3.2f, cy - 2.8f), new PointF(cx, cy - 6f), new PointF(cx + 3.2f, cy - 2.8f) });
        }
    }

    internal sealed class StudioSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
            }
        }

        public StudioSwitch()
        {
            Size = new Size(42, 22);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            _checked = !_checked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new Rectangle(1, 1, Width - 3, Height - 3);
            Color fill = _checked ? Theme.BrandDark : Color.FromArgb(46, 46, 58);
            using (var bg = new SolidBrush(fill))
                Theme.FillRoundedRect(g, bg, track, 10);

            Color border = _hover ? Color.FromArgb(196, 181, 253) : (_checked ? Theme.Brand : Color.FromArgb(68, 68, 84));
            using (var pen = new Pen(border, 1f))
                Theme.DrawRoundedRect(g, pen, track, 10);

            int knobSize = Height - 8;
            int knobX = _checked ? Width - knobSize - 5 : 4;
            int knobY = (Height - knobSize) / 2;
            using var knobBr = new SolidBrush(Color.White);
            g.FillEllipse(knobBr, knobX, knobY, knobSize, knobSize);
        }
    }

    internal sealed class StudioCheckChip : Control
    {
        private bool _checked;
        private bool _hover;
        private string _label;

        public event EventHandler CheckedChanged;

        public string Label
        {
            get => _label;
            set { _label = value; RecalcWidth(); Invalidate(); }
        }

        public bool Checked
        {
            get => _checked;
            set { if (_checked != value) { _checked = value; Invalidate(); } }
        }

        public StudioCheckChip(string label, bool initial = false)
        {
            _label = label;
            _checked = initial;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            RecalcWidth();
        }

        private void RecalcWidth()
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            using var f = Theme.Font(8.8f, FontStyle.Regular);
            SizeF sz = g.MeasureString(_label ?? "", f);
            Size = new Size(28 + (int)Math.Ceiling(sz.Width) + 6, 28);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            _checked = !_checked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(3, 6, 16, 16);
            using (var bg = new SolidBrush(_checked ? Theme.BrandDark : Color.FromArgb(30, 30, 38)))
                Theme.FillRoundedRect(g, bg, box, 4);
            using (var pen = new Pen(_checked ? Theme.Brand : (_hover ? Color.FromArgb(140, 154, 127, 245) : Color.FromArgb(74, 74, 92)), 1.25f))
                Theme.DrawRoundedRect(g, pen, box, 4);

            if (_checked)
            {
                using var chk = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(chk, new[]
                {
                    new PointF(box.X + 3.8f, box.Y + 8.3f),
                    new PointF(box.X + 6.8f, box.Y + 11.4f),
                    new PointF(box.X + 12.2f, box.Y + 4.8f)
                });
            }

            using var font = Theme.Font(8.8f, FontStyle.Regular);
            using var br = new SolidBrush(_hover || _checked ? Color.FromArgb(240, 240, 248) : Color.FromArgb(196, 196, 212));
            using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(_label ?? "", font, br, new RectangleF(24, 0, Width - 24, Height), sf);
        }
    }

    internal sealed class StudioDropdownPill : Control
    {
        private bool _hover;
        private string _displayText;

        public string DisplayText
        {
            get => _displayText;
            set { _displayText = value; Invalidate(); }
        }

        public StudioDropdownPill(string text, int width = 76)
        {
            _displayText = text;
            Size = new Size(width, 28);
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
            using (var bg = new SolidBrush(_hover ? Color.FromArgb(42, 42, 54) : Color.FromArgb(30, 30, 38)))
                Theme.FillRoundedRect(g, bg, box, 6);
            using (var pen = new Pen(_hover ? Theme.Brand : Color.FromArgb(62, 62, 76), 1f))
                Theme.DrawRoundedRect(g, pen, box, 6);

            using (var font = Theme.Font(8.6f, FontStyle.Regular))
            using (var br = new SolidBrush(Theme.TextPrimary))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(_displayText ?? "", font, br, new RectangleF(9, 0, Width - 24, Height), sf);

            using var chevPen = new Pen(Theme.TextSecondary, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(chevPen, new[]
            {
                new PointF(Width - 15.5f, Height / 2f - 1.5f),
                new PointF(Width - 12.2f, Height / 2f + 2f),
                new PointF(Width - 8.9f, Height / 2f - 1.5f)
            });
        }
    }

    internal sealed class HotkeyCaptureDialog : Form
    {
        private readonly Label _previewLabel;
        public string CapturedHotkey { get; private set; }

        public HotkeyCaptureDialog(string actionName, string currentKey)
        {
            CapturedHotkey = currentKey ?? "";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(344, 188);
            BackColor = Color.FromArgb(22, 22, 29);
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;

            var title = new Label
            {
                Text = $"Shortcut for {actionName}",
                ForeColor = Color.FromArgb(244, 244, 250),
                Font = Theme.Font(10.5f, FontStyle.Bold),
                Location = new Point(20, 18),
                Size = new Size(304, 24)
            };

            var sub = new Label
            {
                Text = "Press any key combination (e.g. PrintScreen, Ctrl+Shift+A)",
                ForeColor = Color.FromArgb(156, 156, 178),
                Font = Theme.Font(8.5f, FontStyle.Regular),
                Location = new Point(20, 44),
                Size = new Size(304, 20)
            };

            _previewLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(CapturedHotkey) ? "Press keys..." : CapturedHotkey,
                ForeColor = Color.FromArgb(208, 194, 255),
                BackColor = Color.FromArgb(32, 30, 46),
                Font = Theme.Font(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 74),
                Size = new Size(304, 42)
            };

            var btnSave = new DarkPillButton("Save", Theme.BrandDark, Theme.Brand, 30, 82) { Location = new Point(20, 136) };
            var btnClear = new DarkPillButton("Clear", Color.FromArgb(38, 38, 48), Color.FromArgb(56, 56, 68), 30, 72) { Location = new Point(110, 136), BorderColor = Color.FromArgb(60, 60, 74) };
            var btnCancel = new DarkPillButton("Cancel", Color.FromArgb(38, 38, 48), Color.FromArgb(170, 220, 38, 38), 30, 76) { Location = new Point(248, 136), BorderColor = Color.FromArgb(60, 60, 74) };

            btnSave.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            btnClear.Click += (s, e) => { CapturedHotkey = ""; DialogResult = DialogResult.OK; Close(); };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, sub, _previewLabel, btnSave, btnClear, btnCancel });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using var path = Theme.CreateRoundedRect(new Rectangle(0, 0, Width, Height), 10);
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Brand, 1.4f);
            Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 10);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.None)
            {
                return true;
            }

            var parts = new List<string>();
            if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
            if ((keyData & Keys.Shift) != 0) parts.Add("Shift");

            string keyName = key switch
            {
                Keys.Snapshot => "PrintScreen",
                Keys.Back => "Backspace",
                Keys.Return => "Enter",
                Keys.Next => "PageDown",
                Keys.Prior => "PageUp",
                _ => key.ToString()
            };
            if (key >= Keys.D0 && key <= Keys.D9) keyName = ((char)('0' + (key - Keys.D0))).ToString();
            parts.Add(keyName);

            CapturedHotkey = string.Join("+", parts);
            _previewLabel.Text = CapturedHotkey;
            return true;
        }
    }

    internal sealed class StudioHistoryCard : Control
    {
        private readonly CaptureHistoryItem _item;
        private readonly Image _thumb;
        private bool _hover;

        public StudioHistoryCard(CaptureHistoryItem item, Action onCopy, Action onOpen, Action onDelete)
        {
            _item = item;
            Size = new Size(422, 66);
            Margin = new Padding(0, 0, 0, 8);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _thumb = LoadThumbSafe(item.FilePath);

            var btnCopy = new DarkPillButton("", Color.FromArgb(40, 36, 62), Color.FromArgb(154, 127, 245), 26, 32)
            {
                Location = new Point(Width - 116, 20),
                BorderColor = Color.FromArgb(72, 64, 110),
                IconImage = SvgIconRenderer.RenderToolIcon("copy", 14, Color.FromArgb(235, 235, 248))
            };
            btnCopy.Click += (s, e) => onCopy?.Invoke();

            var btnOpen = new DarkPillButton("", Color.FromArgb(34, 34, 44), Color.FromArgb(52, 52, 68), 26, 32)
            {
                Location = new Point(Width - 76, 20),
                BorderColor = Color.FromArgb(56, 56, 70),
                IconImage = SvgIconRenderer.RenderToolIcon("open", 14, Color.FromArgb(235, 235, 248))
            };
            btnOpen.Click += (s, e) => onOpen?.Invoke();

            var btnDel = new DarkPillButton("", Color.FromArgb(34, 34, 44), Color.FromArgb(180, 220, 38, 38), 26, 32)
            {
                Location = new Point(Width - 38, 20),
                BorderColor = Color.FromArgb(56, 56, 70),
                IconImage = SvgIconRenderer.RenderToolIcon("delete", 14, Color.FromArgb(255, 180, 180))
            };
            btnDel.Click += (s, e) => onDelete?.Invoke();

            Controls.AddRange(new Control[] { btnCopy, btnOpen, btnDel });
        }

        private static Image LoadThumbSafe(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var img = Image.FromStream(fs);
                var thumb = new Bitmap(52, 42);
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.FromArgb(18, 18, 24));
                float scale = Math.Max(52f / img.Width, 42f / img.Height);
                float dw = img.Width * scale, dh = img.Height * scale;
                g.DrawImage(img, (52 - dw) / 2f, (42 - dh) / 2f, dw, dh);
                return thumb;
            }
            catch { return null; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var cardRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(_hover ? Color.FromArgb(30, 30, 40) : Color.FromArgb(24, 24, 31)))
                Theme.FillRoundedRect(g, bg, cardRect, 9);
            using (var pen = new Pen(_hover ? Color.FromArgb(110, 154, 127, 245) : Color.FromArgb(44, 44, 56), 1f))
                Theme.DrawRoundedRect(g, pen, cardRect, 9);

            var thumbBox = new Rectangle(11, 12, 52, 42);
            if (_thumb != null)
            {
                using var clip = Theme.CreateRoundedRect(thumbBox, 6);
                var state = g.Save();
                g.SetClip(clip);
                g.DrawImage(_thumb, thumbBox);
                g.Restore(state);
            }
            else
            {
                using var tbBg = new SolidBrush(Color.FromArgb(34, 30, 48));
                Theme.FillRoundedRect(g, tbBg, thumbBox, 6);
            }
            using (var tbPen = new Pen(Color.FromArgb(62, 62, 78), 1f))
                Theme.DrawRoundedRect(g, tbPen, thumbBox, 6);

            int textLeft = 74;
            int textW = Width - textLeft - 152;
            using (var fnFont = Theme.Font(9.2f, FontStyle.Bold))
            using (var fnBr = new SolidBrush(Color.FromArgb(244, 244, 250)))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(_item.FileName ?? "", fnFont, fnBr, new RectangleF(textLeft, 14, textW, 20), sf);

            string sizeStr = _item.FileSize >= 1024 * 1024
                ? $"{_item.FileSize / (1024.0 * 1024.0):F1} MB"
                : $"{Math.Max(1, _item.FileSize / 1024)} KB";
            string meta = $"{_item.Ext.ToUpperInvariant()}  •  {sizeStr}  •  {_item.CreatedAt:MM-dd HH:mm}";

            using (var mFont = Theme.Font(8.1f, FontStyle.Regular))
            using (var mBr = new SolidBrush(Color.FromArgb(150, 150, 172)))
            using (var sfM = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(meta, mFont, mBr, new RectangleF(textLeft, 36, textW, 18), sfM);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _thumb?.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class StudioHistoryGridCard : Control
    {
        private readonly CaptureHistoryItem _item;
        private readonly Image _thumb;
        private readonly Action _onCopy;
        private readonly Action _onOpen;
        private readonly Action _onDelete;
        private bool _hover;
        private int _hoverAction; // 0 = none, 1 = Copy, 2 = Open, 3 = Close/Delete
        private bool _copiedFlash;

        private static readonly Rectangle RectCopy = new Rectangle(30, 75, 42, 24);
        private static readonly Rectangle RectOpen = new Rectangle(80, 75, 42, 24);
        private static readonly Rectangle RectDel = new Rectangle(130, 75, 42, 24);

        public StudioHistoryGridCard(CaptureHistoryItem item, Action onCopy, Action onOpen, Action onDelete)
        {
            _item = item;
            _onCopy = onCopy;
            _onOpen = onOpen;
            _onDelete = onDelete;
            Size = new Size(204, 150);
            Margin = new Padding(0, 0, 10, 10);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _thumb = LoadThumbSafe(item.FilePath, 186, 98);
        }

        private static Image LoadThumbSafe(string path, int tw, int th)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var img = Image.FromStream(fs);
                var thumb = new Bitmap(tw, th);
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.FromArgb(18, 18, 24));
                float scale = Math.Max((float)tw / img.Width, (float)th / img.Height);
                float dw = img.Width * scale, dh = img.Height * scale;
                g.DrawImage(img, (tw - dw) / 2f, (th - dh) / 2f, dw, dh);
                return thumb;
            }
            catch { return null; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverAction = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = RectCopy.Contains(e.Location) ? 1 : (RectOpen.Contains(e.Location) ? 2 : (RectDel.Contains(e.Location) ? 3 : 0));
            if (next != _hoverAction)
            {
                _hoverAction = next;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            if (RectCopy.Contains(e.Location))
            {
                _onCopy?.Invoke();
                _copiedFlash = true;
                Invalidate();
                var t = new Timer { Interval = 1100 };
                t.Tick += (s, ev) => { t.Stop(); t.Dispose(); _copiedFlash = false; if (!IsDisposed) Invalidate(); };
                t.Start();
            }
            else if (RectOpen.Contains(e.Location))
            {
                _onOpen?.Invoke();
            }
            else if (RectDel.Contains(e.Location))
            {
                _onDelete?.Invoke();
            }
            else
            {
                _onOpen?.Invoke();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var cardRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(_hover ? Color.FromArgb(30, 30, 40) : Color.FromArgb(24, 24, 31)))
                Theme.FillRoundedRect(g, bg, cardRect, 10);
            using (var pen = new Pen(_hover ? Theme.Brand : Color.FromArgb(46, 46, 58), _hover ? 1.15f : 1f))
                Theme.DrawRoundedRect(g, pen, cardRect, 10);

            var thumbBox = new Rectangle(9, 9, 186, 98);
            if (_thumb != null)
            {
                using var clip = Theme.CreateRoundedRect(thumbBox, 7);
                var state = g.Save();
                g.SetClip(clip);
                g.DrawImage(_thumb, thumbBox);
                g.Restore(state);
            }
            else
            {
                using var tbBg = new SolidBrush(Color.FromArgb(32, 28, 46));
                Theme.FillRoundedRect(g, tbBg, thumbBox, 7);
            }
            using (var tbPen = new Pen(Color.FromArgb(62, 62, 78), 1f))
                Theme.DrawRoundedRect(g, tbPen, thumbBox, 7);

            // Format pill badge top-right
            string ext = (_item.Ext ?? "png").ToUpperInvariant();
            var badgeRect = new Rectangle(thumbBox.Right - 36, thumbBox.Y + 6, 30, 17);
            using (var bBg = new SolidBrush(Color.FromArgb(215, 20, 20, 28)))
                Theme.FillRoundedRect(g, bBg, badgeRect, 4);
            using (var bPen = new Pen(_item.IsGif ? Color.FromArgb(190, 154, 127, 245) : Color.FromArgb(140, 110, 110, 140), 1f))
                Theme.DrawRoundedRect(g, bPen, badgeRect, 4);
            using (var bFont = Theme.Font(6.8f, FontStyle.Bold))
            using (var bTxt = new SolidBrush(Color.FromArgb(236, 236, 248)))
            using (var sfB = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(ext, bFont, bTxt, badgeRect, sfB);

            // Hover-only Copy / Open / Close bar inside bottom of thumbnail
            if (_hover)
            {
                var barRect = new Rectangle(thumbBox.X + 4, thumbBox.Bottom - 36, thumbBox.Width - 8, 30);
                using (var barBg = new SolidBrush(Color.FromArgb(228, 16, 16, 22)))
                    Theme.FillRoundedRect(g, barBg, barRect, 7);
                using (var barPen = new Pen(Color.FromArgb(95, 154, 127, 245), 1f))
                    Theme.DrawRoundedRect(g, barPen, barRect, 7);

                DrawHoverActionPill(g, RectCopy, _copiedFlash ? "check" : "copy",
                    _hoverAction == 1 || _copiedFlash ? Theme.Brand : Color.FromArgb(44, 38, 68),
                    Color.FromArgb(88, 76, 132));
                DrawHoverActionPill(g, RectOpen, "open",
                    _hoverAction == 2 ? Color.FromArgb(56, 56, 72) : Color.FromArgb(36, 36, 46),
                    Color.FromArgb(66, 66, 82));
                DrawHoverActionPill(g, RectDel, "delete",
                    _hoverAction == 3 ? Color.FromArgb(195, 220, 38, 38) : Color.FromArgb(36, 36, 46),
                    Color.FromArgb(66, 66, 82));
            }

            using (var fnFont = Theme.Font(8.6f, FontStyle.Bold))
            using (var fnBr = new SolidBrush(Color.FromArgb(244, 244, 250)))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(_item.FileName ?? "", fnFont, fnBr, new RectangleF(9, 112, 186, 17), sf);

            string sizeStr = _item.FileSize >= 1024 * 1024
                ? $"{_item.FileSize / (1024.0 * 1024.0):F1} MB"
                : $"{Math.Max(1, _item.FileSize / 1024)} KB";
            string meta = $"{sizeStr} • {_item.CreatedAt:MM-dd HH:mm}";

            using (var mFont = Theme.Font(7.8f, FontStyle.Regular))
            using (var mBr = new SolidBrush(Color.FromArgb(150, 150, 172)))
            using (var sfM = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(meta, mFont, mBr, new RectangleF(9, 129, 186, 15), sfM);
        }

        private static void DrawHoverActionPill(Graphics g, Rectangle r, string iconName, Color fill, Color border)
        {
            using (var bg = new SolidBrush(fill))
                Theme.FillRoundedRect(g, bg, r, 5);
            using (var pen = new Pen(border, 1f))
                Theme.DrawRoundedRect(g, pen, r, 5);

            if (iconName == "check")
            {
                using var f = Theme.Font(9f, FontStyle.Bold);
                using var br = new SolidBrush(Color.White);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("✓", f, br, r, sf);
            }
            else
            {
                var icon = SvgIconRenderer.RenderToolIcon(iconName, 14, Color.FromArgb(235, 235, 248));
                if (icon != null)
                {
                    int ix = r.X + (r.Width - 14) / 2;
                    int iy = r.Y + (r.Height - 14) / 2;
                    g.DrawImage(icon, ix, iy, 14, 14);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _thumb?.Dispose();
            base.Dispose(disposing);
        }
    }
}
