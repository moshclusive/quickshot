using QuickShot.Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public class OcrResultForm : Form
    {
        private readonly Bitmap _logoBmp;
        private readonly TextBox _editor;
        private readonly StudioDropdownPill _caseDropdown;
        private Panel _casePopup;

        private string _rawText = "";
        private string _caseMode = "default"; // Default opened state: preserves original extracted text formatting
        private bool _updatingEditor;

        public OcrResultForm()
        {
            _logoBmp = AssetLoader.LoadPngBitmap("assets", "Icon", "icon48.png");

            Text = "QuickShot Text Extractor";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(512, 224);
            MinimumSize = new Size(420, 190);
            BackColor = Theme.BgWindow;
            DoubleBuffered = true;
            TopMost = true;
            Icon = AssetLoader.LoadAppIcon(48);

            // Compact Minimal Header (0..44) with Normal/Default/All caps dropdown
            var header = new Panel
            {
                Bounds = new Rectangle(1, 1, Width - 2, 44),
                BackColor = Color.FromArgb(24, 24, 31)
            };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                if (_logoBmp != null)
                    g.DrawImage(_logoBmp, 14, 11, 20, 20);

                using (var titleFont = Theme.Font(10.2f, FontStyle.Bold))
                using (var titleBr = new SolidBrush(Color.FromArgb(244, 244, 250)))
                    g.DrawString("Text Extractor", titleFont, titleBr, 42, 12);

                using var sep = new Pen(Color.FromArgb(44, 44, 56), 1f);
                g.DrawLine(sep, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            header.MouseDown += (s, e) =>
            {
                DismissPopup();
                if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle);
            };

            _caseDropdown = new StudioDropdownPill("Default", 94)
            {
                Location = new Point(Width - 180, 8)
            };
            _caseDropdown.Click += (s, e) => ToggleCasePopup();

            var btnMin = new StudioHeaderButton(StudioHeaderButton.Kind.Minimize) { Location = new Point(Width - 74, 8) };
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            var btnClose = new StudioHeaderButton(StudioHeaderButton.Kind.Close) { Location = new Point(Width - 38, 8) };
            btnClose.Click += (s, e) => Close();
            header.Controls.AddRange(new Control[] { _caseDropdown, btnMin, btnClose });

            // Clean Full-Height Dark Editor Surface
            var editorWrap = new Panel
            {
                Bounds = new Rectangle(14, 54, Width - 28, Height - 68),
                BackColor = Color.FromArgb(24, 24, 32),
                Padding = new Padding(12, 10, 12, 10)
            };
            editorWrap.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(62, 62, 80), 1.15f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, editorWrap.Width - 1, editorWrap.Height - 1), 9);
            };

            _editor = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 32),
                ForeColor = Color.FromArgb(244, 244, 250),
                Font = Theme.Font(10.5f, FontStyle.Regular),
                Text = "Scanning..."
            };
            _editor.HandleCreated += (s, e) => NativeMethods.EnableDarkScrollbars(_editor.Handle);
            _editor.MouseDown += (s, e) => DismissPopup();
            _editor.TextChanged += (s, e) =>
            {
                if (!_updatingEditor && _caseMode == "default")
                    _rawText = _editor.Text;
            };
            editorWrap.Controls.Add(_editor);

            Controls.AddRange(new Control[] { header, editorWrap });
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            DismissPopup();
        }

        private void ToggleCasePopup()
        {
            if (_casePopup != null)
            {
                DismissPopup();
                return;
            }

            var options = new[]
            {
                ("Default", "default"),
                ("Normal", "normal"),
                ("All caps", "caps")
            };

            int itemH = 30;
            int popW = 114;
            int popH = options.Length * itemH + 10;
            _casePopup = new Panel
            {
                Bounds = new Rectangle(_caseDropdown.Left - 10, _caseDropdown.Bottom + 6, popW, popH),
                BackColor = Color.FromArgb(26, 26, 35)
            };
            _casePopup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Brand, 1.25f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _casePopup.Width - 1, _casePopup.Height - 1), 8);
            };

            int iy = 5;
            foreach (var (lbl, val) in options)
            {
                string capturedVal = val;
                var pill = new DarkPillButton(lbl, Color.FromArgb(26, 26, 35), Color.FromArgb(46, 46, 62), itemH - 2, popW - 10)
                {
                    Location = new Point(5, iy),
                    Size = new Size(popW - 10, itemH - 2),
                    PillFontStyle = FontStyle.Regular,
                    Selected = string.Equals(val, _caseMode, StringComparison.OrdinalIgnoreCase)
                };
                pill.Click += (s, e) =>
                {
                    DismissPopup();
                    SetCaseMode(capturedVal);
                };
                _casePopup.Controls.Add(pill);
                iy += itemH;
            }

            Controls.Add(_casePopup);
            _casePopup.BringToFront();
        }

        private void DismissPopup()
        {
            if (_casePopup != null)
            {
                Controls.Remove(_casePopup);
                _casePopup.Dispose();
                _casePopup = null;
            }
        }

        public void SetCaseMode(string mode)
        {
            _caseMode = mode;
            _caseDropdown.DisplayText = mode switch
            {
                "default" => "Default",
                "caps" => "All caps",
                _ => "Normal"
            };

            if (string.IsNullOrEmpty(_rawText)) return;

            string formatted = mode switch
            {
                "default" => _rawText,
                "caps" => _rawText.ToUpper(),
                _ => ToClearNormalCase(_rawText)
            };

            _updatingEditor = true;
            _editor.Text = formatted;
            _editor.SelectionStart = 0;
            _editor.SelectionLength = 0;
            _updatingEditor = false;

            if (!string.IsNullOrWhiteSpace(formatted))
            {
                StorageService.CopyTextToClipboard(formatted);
            }
        }

        /// <summary>
        /// Clears ragged OCR/PDF line-wrap formatting cleanly and normalizes ALL CAPS text to crisp sentence case.
        /// </summary>
        public static string ToClearNormalCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Normalize line endings cleanly
            string clean = text.Replace("\r\n", "\n").Replace('\r', '\n');
            // Rejoin hyphenated line breaks cleanly
            clean = Regex.Replace(clean, @"-\n+", "-");
            // Preserve double-newline paragraph breaks, unwrap ragged single-line OCR wraps into single spaces
            clean = Regex.Replace(clean, @"\n{2,}", "\u0001");
            clean = Regex.Replace(clean, @"[ \t]*\n[ \t]*", " ");
            clean = clean.Replace("\u0001", Environment.NewLine + Environment.NewLine);
            clean = Regex.Replace(clean, @"[ \t]{2,}", " ").Trim();

            return ToNormalSentenceCase(clean);
        }

        public static string ToNormalSentenceCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Only lowercase words/lines that are ALL CAPS; preserve intentional mixed-case words (e.g. QuickShot, Node.js) in Bengali+English text
            var result = Regex.Replace(text, @"[A-Za-z]+", m =>
            {
                string w = m.Value;
                bool hasLower = false;
                for (int i = 0; i < w.Length; i++)
                {
                    if (char.IsLower(w[i])) { hasLower = true; break; }
                }
                return hasLower ? w : w.ToLower();
            });

            var chars = result.ToCharArray();
            bool capitalizeNext = true;

            for (int i = 0; i < chars.Length; i++)
            {
                char ch = chars[i];
                if (char.IsLetter(ch))
                {
                    if (capitalizeNext)
                    {
                        if (ch >= 'a' && ch <= 'z')
                            chars[i] = char.ToUpper(ch);
                        capitalizeNext = false;
                    }
                }
                else if (ch == '.' || ch == '!' || ch == '?' || ch == '\n' || ch == '\r' || ch == '।')
                {
                    capitalizeNext = true;
                }
            }
            return new string(chars);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using var path = Theme.CreateRoundedRect(new Rectangle(0, 0, Width, Height), 11);
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Brand, 1.3f);
            Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 11);
        }

        public async void RunOcrAsync(Bitmap croppedBitmap)
        {
            if (croppedBitmap == null) return;
            try
            {
                _updatingEditor = true;
                _editor.Text = "Scanning text...";
                _updatingEditor = false;

                var res = await OcrEngineService.ExtractAnyLanguageAsync(croppedBitmap, null);
                if (IsDisposed) return;

                if (res.Success && !string.IsNullOrWhiteSpace(res.Text))
                {
                    _rawText = res.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                    SetCaseMode("default"); // Default preserves exact layout unless user switches to Normal/All caps
                }
                else
                {
                    _rawText = "";
                    _updatingEditor = true;
                    _editor.Text = res.Error ?? "No readable text found.";
                    _updatingEditor = false;
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    _updatingEditor = true;
                    _editor.Text = "OCR failed: " + ex.Message;
                    _updatingEditor = false;
                }
            }
            finally
            {
                croppedBitmap.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logoBmp?.Dispose();
                _casePopup?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
