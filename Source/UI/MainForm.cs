using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly StorageService _storage;
        private readonly HotkeyManager _hotkeys;
        private readonly Action<string, int> _triggerCapture;
        private readonly Action _quitApp;
        private readonly Bitmap _logoBmp;

        // 3 Clean Tabs: History, Hotkeys, Settings (NO Capture tab)
        private readonly StudioTabButton[] _tabs = new StudioTabButton[3];
        private readonly Panel[] _tabPanels = new Panel[3];
        private int _activeTab;

        // History Tab (Modular Grid)
        private Label _historyCountLabel;
        private FlowLayoutPanel _historyFlow;

        // Hotkeys Tab
        private readonly Dictionary<string, StudioActionCard> _hotkeyCards = new Dictionary<string, StudioActionCard>();

        // Settings Tab Controls
        private Label _folderPathLabel;
        private StudioDropdownPill _stylePill, _durationPill, _retentionPill;

        // Floating Dropdown Popup
        private Panel _activePopup;

        public bool AllowClose { get; set; }

        public MainForm(AppSettings settings, StorageService storage, HotkeyManager hotkeys,
            Action<string, int> triggerCapture, Action quitApp)
        {
            _settings = settings;
            _storage = storage;
            _hotkeys = hotkeys;
            _triggerCapture = triggerCapture;
            _quitApp = quitApp;
            _logoBmp = AssetLoader.LoadPngBitmap("assets", "Icon", "icon48.png");

            Text = "QuickShot";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(480, 580);
            BackColor = Theme.BgWindow;
            Font = Theme.Font(9f, FontStyle.Regular);
            DoubleBuffered = true;
            Icon = AssetLoader.LoadAppIcon(48);

            BuildShell();
            LoadSettingsIntoUI();
            RefreshHistory();
            SwitchTab(0);
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
            using var pen = new Pen(Color.FromArgb(62, 62, 76), 1.2f);
            Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 11);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            DismissPopup();
        }

        private void BuildShell()
        {
            // 1. Custom Draggable Header (0..52)
            var header = new Panel
            {
                Bounds = new Rectangle(1, 1, Width - 2, 50),
                BackColor = Color.FromArgb(24, 24, 31)
            };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                if (_logoBmp != null)
                    g.DrawImage(_logoBmp, 16, 13, 24, 24);

                using (var titleFont = Theme.Font(11.2f, FontStyle.Bold))
                using (var br = new SolidBrush(Color.FromArgb(246, 246, 252)))
                    g.DrawString("QuickShot", titleFont, br, 48, 14);

                var badgeRect = new Rectangle(133, 16, 38, 19);
                using (var bBg = new SolidBrush(Color.FromArgb(46, 38, 72)))
                    Theme.FillRoundedRect(g, bBg, badgeRect, 5);
                using (var bPen = new Pen(Color.FromArgb(120, 154, 127, 245), 1f))
                    Theme.DrawRoundedRect(g, bPen, badgeRect, 5);
                using (var bFont = Theme.Font(7.6f, FontStyle.Bold))
                using (var bTxt = new SolidBrush(Color.FromArgb(204, 188, 255)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("v1.0", bFont, bTxt, badgeRect, sf);

                using var sep = new Pen(Color.FromArgb(40, 40, 50), 1f);
                g.DrawLine(sep, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            header.MouseDown += (s, e) =>
            {
                DismissPopup();
                if (e.Button == MouseButtons.Left) NativeMethods.DragMoveWindow(Handle);
            };

            var btnMin = new StudioHeaderButton(StudioHeaderButton.Kind.Minimize) { Location = new Point(Width - 76, 11) };
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;

            var btnClose = new StudioHeaderButton(StudioHeaderButton.Kind.Close) { Location = new Point(Width - 40, 11) };
            btnClose.Click += (s, e) => Hide();

            header.Controls.AddRange(new Control[] { btnMin, btnClose });
            Controls.Add(header);

            // 2. Segmented 3-Tab Bar (58..98)
            var tabStrip = new Panel
            {
                Bounds = new Rectangle(16, 58, Width - 32, 40),
                BackColor = Color.FromArgb(22, 22, 28)
            };
            tabStrip.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(Color.FromArgb(22, 22, 28));
                using var border = new Pen(Color.FromArgb(44, 44, 56), 1f);
                Theme.FillRoundedRect(e.Graphics, bg, new Rectangle(0, 0, tabStrip.Width - 1, tabStrip.Height - 1), 9);
                Theme.DrawRoundedRect(e.Graphics, border, new Rectangle(0, 0, tabStrip.Width - 1, tabStrip.Height - 1), 9);
            };

            string[] tabLabels = { "History", "Hotkeys", "Settings" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var tb = new StudioTabButton(tabLabels[i])
                {
                    Size = new Size(144, 32),
                    Location = new Point(4 + i * 148, 4)
                };
                tb.Click += (s, e) => { DismissPopup(); SwitchTab(idx); };
                _tabs[i] = tb;
                tabStrip.Controls.Add(tb);
            }
            Controls.Add(tabStrip);

            // 3. Tab View Panels (106..512)
            Rectangle viewRect = new Rectangle(1, 106, Width - 2, Height - 156);
            for (int i = 0; i < 3; i++)
            {
                _tabPanels[i] = new Panel
                {
                    Bounds = viewRect,
                    BackColor = Theme.BgWindow,
                    Visible = (i == 0)
                };
                _tabPanels[i].MouseDown += (s, e) => DismissPopup();
                Controls.Add(_tabPanels[i]);
            }

            BuildHistoryTab(_tabPanels[0]);
            BuildHotkeysTab(_tabPanels[1]);
            BuildSettingsTab(_tabPanels[2]);

            // 4. Clean Footer Bar (Height - 50)
            var footer = new Panel
            {
                Bounds = new Rectangle(1, Height - 50, Width - 2, 49),
                BackColor = Color.FromArgb(24, 24, 31)
            };
            footer.Paint += (s, e) =>
            {
                using var sep = new Pen(Color.FromArgb(44, 44, 54), 1f);
                e.Graphics.DrawLine(sep, 0, 0, footer.Width, 0);
            };
            footer.MouseDown += (s, e) => DismissPopup();

            var btnOpenDir = new DarkPillButton("Open Captures Folder", Color.FromArgb(34, 34, 44), Color.FromArgb(52, 52, 68), 28, 148)
            {
                Location = new Point(15, 10),
                BorderColor = Color.FromArgb(56, 56, 70),
                PillFontStyle = FontStyle.Regular
            };
            btnOpenDir.Click += (s, e) => _storage.ShowInFolder();

            var btnQuitFooter = new DarkPillButton("Quit", Color.FromArgb(42, 28, 34), Color.FromArgb(185, 220, 38, 38), 28, 62)
            {
                Location = new Point(Width - 78, 10),
                PillTextColor = Color.FromArgb(248, 178, 178),
                BorderColor = Color.FromArgb(95, 220, 38, 38)
            };
            btnQuitFooter.Click += (s, e) => _quitApp?.Invoke();

            footer.Controls.AddRange(new Control[] { btnOpenDir, btnQuitFooter });
            Controls.Add(footer);
        }

        private void BuildHistoryTab(Panel host)
        {
            var topBar = new Panel { Bounds = new Rectangle(15, 4, 448, 34), BackColor = Color.Transparent };
            _historyCountLabel = new Label
            {
                Text = "0 captures",
                ForeColor = Color.FromArgb(164, 164, 188),
                Font = Theme.Font(9f, FontStyle.Bold),
                Location = new Point(2, 7),
                AutoSize = true
            };

            var btnFolder = new DarkPillButton("Open Folder", Color.FromArgb(34, 34, 44), Color.FromArgb(52, 52, 66), 28, 92)
            {
                Location = new Point(272, 3),
                BorderColor = Color.FromArgb(56, 56, 70),
                PillFontStyle = FontStyle.Regular
            };
            btnFolder.Click += (s, e) => _storage.ShowInFolder();

            var btnRefresh = new DarkPillButton("Refresh", Color.FromArgb(42, 36, 66), Theme.Brand, 28, 76)
            {
                Location = new Point(370, 3),
                BorderColor = Color.FromArgb(80, 154, 127, 245)
            };
            btnRefresh.Click += (s, e) => RefreshHistory();

            topBar.Controls.AddRange(new Control[] { _historyCountLabel, btnFolder, btnRefresh });

            _historyFlow = new FlowLayoutPanel
            {
                Bounds = new Rectangle(15, 42, 448, host.Height - 46),
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Theme.BgWindow
            };
            _historyFlow.HandleCreated += (s, e) => NativeMethods.EnableDarkScrollbars(_historyFlow.Handle);

            host.Controls.AddRange(new Control[] { topBar, _historyFlow });
        }

        private void BuildHotkeysTab(Panel host)
        {
            host.AutoScroll = true;
            var defs = new[]
            {
                ("captureRegion", "Capture Region", "Drag area, freehand lasso or scroll extend", "rectangular"),
                ("captureScrolling", "Scrolling Capture", "Auto-scroll & stitch full webpage or window", "scrollcapture"),
                ("recordGif", "Record GIF", "Animated area GIF (clean toolbar excluded)", "gif"),
                ("stopRecording", "Stop Recording (GIF)", "Stop & save active GIF recording", "stop"),
                ("pauseRecording", "Pause / Resume GIF", "Toggle pause while recording GIF", "pause"),
                ("ocrSnip", "Extract Text (OCR)", "Auto-detect & extract any language text", "ocr")
            };

            int y = 8;
            foreach (var (keyId, title, sub, svg) in defs)
            {
                string capKeyId = keyId;
                string capTitle = title;
                var card = new StudioActionCard(title, sub, svg, height: 68)
                {
                    Location = new Point(15, y),
                    Width = 446
                };
                card.Click += (s, e) => EditHotkey(capKeyId, capTitle);
                _hotkeyCards[keyId] = card;
                host.Controls.Add(card);
                y += 76;
            }
        }

        private void BuildSettingsTab(Panel host)
        {
            int y = 8;

            // Card 1: Storage Folder
            var cFolder = CreateSettingsCard(host, 15, y, 76, 446);
            AddCardHeader(cFolder, "Captures Storage Folder", 14, 12);
            _folderPathLabel = new Label
            {
                Text = _storage.GetScreenshotsDir(),
                ForeColor = Color.FromArgb(164, 164, 188),
                Font = Theme.Font(8.4f, FontStyle.Regular),
                Location = new Point(14, 38),
                Size = new Size(326, 20),
                AutoEllipsis = true
            };
            var btnBrowse = new DarkPillButton("Browse...", Color.FromArgb(36, 36, 48), Theme.Brand, 28, 82)
            {
                Location = new Point(350, 24),
                BorderColor = Color.FromArgb(62, 62, 78)
            };
            btnBrowse.Click += (s, e) => BrowseFolder();
            cFolder.Controls.AddRange(new Control[] { _folderPathLabel, btnBrowse });
            y += 88;

            // Card 2: Notifications & Auto-clean Retention
            var cNotif = CreateSettingsCard(host, 15, y, 124, 446);
            _stylePill = AddDropdownRow(cNotif, 10, "Capture Notification Style", FormatStyleLabel(_settings.NotificationStyle), 124, 446);
            _stylePill.Click += (s, e) => OpenOptionsPopup(
                _stylePill,
                new[] { ("Preview Popup", "preview"), ("Minimal Toast", "minimal"), ("Disabled", "none") },
                _settings.NotificationStyle,
                val =>
                {
                    _settings.NotificationStyle = val;
                    _stylePill.DisplayText = FormatStyleLabel(val);
                    _settings.Save();
                });

            _durationPill = AddDropdownRow(cNotif, 48, "Notification Display Duration", $"{_settings.NotificationDuration} sec", 124, 446);
            _durationPill.Click += (s, e) => OpenOptionsPopup(
                _durationPill,
                new[] { ("2 sec", "2"), ("3 sec", "3"), ("5 sec", "5"), ("8 sec", "8") },
                _settings.NotificationDuration.ToString(),
                val =>
                {
                    if (int.TryParse(val, out int sec))
                    {
                        _settings.NotificationDuration = sec;
                        _durationPill.DisplayText = $"{sec} sec";
                        _settings.Save();
                    }
                });

            _retentionPill = AddDropdownRow(cNotif, 86, "Auto-clean old captures after", FormatRetentionLabel(_settings.BackupRetentionDays), 124, 446);
            _retentionPill.Click += (s, e) => OpenOptionsPopup(
                _retentionPill,
                new[] { ("1 day", "1"), ("3 days", "3"), ("7 days", "7"), ("30 days", "30"), ("Never", "0") },
                _settings.BackupRetentionDays.ToString(),
                val =>
                {
                    if (int.TryParse(val, out int days))
                    {
                        _settings.BackupRetentionDays = days;
                        _retentionPill.DisplayText = FormatRetentionLabel(days);
                        _settings.Save();
                    }
                });
            y += 138;

            // Card 3: Actions
            var actionRow = new Panel { Location = new Point(15, y), Size = new Size(446, 40), BackColor = Color.Transparent };
            var btnReset = new DarkPillButton("Reset Defaults", Color.FromArgb(36, 36, 46), Color.FromArgb(56, 56, 70), 32, 128)
            {
                Location = new Point(0, 4),
                BorderColor = Color.FromArgb(62, 62, 76),
                PillFontStyle = FontStyle.Regular
            };
            btnReset.Click += (s, e) => ResetDefaults();

            var btnQuitAll = new DarkPillButton("Quit QuickShot Completely", Color.FromArgb(52, 26, 32), Color.FromArgb(195, 220, 38, 38), 32, 216)
            {
                Location = new Point(230, 4),
                PillTextColor = Color.FromArgb(252, 185, 185),
                BorderColor = Color.FromArgb(115, 220, 38, 38)
            };
            btnQuitAll.Click += (s, e) => _quitApp?.Invoke();

            actionRow.Controls.AddRange(new Control[] { btnReset, btnQuitAll });
            host.Controls.Add(actionRow);
        }

        private Panel CreateSettingsCard(Panel parent, int x, int y, int height, int width = 446)
        {
            var card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.FromArgb(24, 24, 31)
            };
            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(46, 46, 58), 1f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
            };
            parent.Controls.Add(card);
            return card;
        }

        private static void AddCardHeader(Panel card, string title, int x, int y)
        {
            card.Controls.Add(new Label
            {
                Text = title,
                ForeColor = Color.FromArgb(244, 244, 250),
                Font = Theme.Font(9.4f, FontStyle.Bold),
                Location = new Point(x, y),
                AutoSize = true
            });
        }

        private StudioDropdownPill AddDropdownRow(Panel card, int y, string label, string valueText, int pillWidth, int cardWidth = 446)
        {
            card.Controls.Add(new Label
            {
                Text = label,
                ForeColor = Color.FromArgb(228, 228, 238),
                Font = Theme.Font(8.8f, FontStyle.Regular),
                Location = new Point(14, y + 6),
                Size = new Size(cardWidth - pillWidth - 36, 20)
            });
            var pill = new StudioDropdownPill(valueText, pillWidth) { Location = new Point(cardWidth - pillWidth - 14, y + 2) };
            card.Controls.Add(pill);
            return pill;
        }

        private void SwitchTab(int index)
        {
            _activeTab = index;
            for (int i = 0; i < 3; i++)
            {
                _tabs[i].Active = (i == index);
                _tabPanels[i].Visible = (i == index);
            }
            if (index == 0) RefreshHistory();
        }

        private void EditHotkey(string keyId, string title)
        {
            string current = keyId switch
            {
                "captureRegion" => _settings.Hotkeys.CaptureRegion,
                "captureScrolling" => _settings.Hotkeys.CaptureScrolling,
                "recordGif" => _settings.Hotkeys.RecordGif,
                "stopRecording" => _settings.Hotkeys.StopRecording,
                "pauseRecording" => _settings.Hotkeys.PauseResumeRecording,
                "ocrSnip" => _settings.Hotkeys.OcrSnip,
                _ => ""
            };

            _hotkeys.Pause();
            try
            {
                using var dlg = new HotkeyCaptureDialog(title, current);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    switch (keyId)
                    {
                        case "captureRegion": _settings.Hotkeys.CaptureRegion = dlg.CapturedHotkey; break;
                        case "captureScrolling": _settings.Hotkeys.CaptureScrolling = dlg.CapturedHotkey; break;
                        case "recordGif": _settings.Hotkeys.RecordGif = dlg.CapturedHotkey; break;
                        case "stopRecording": _settings.Hotkeys.StopRecording = dlg.CapturedHotkey; break;
                        case "pauseRecording": _settings.Hotkeys.PauseResumeRecording = dlg.CapturedHotkey; break;
                        case "ocrSnip": _settings.Hotkeys.OcrSnip = dlg.CapturedHotkey; break;
                    }
                    _settings.Save();
                    _hotkeys.ApplySettings(_settings.Hotkeys);
                    LoadSettingsIntoUI();
                }
            }
            finally
            {
                _hotkeys.Resume();
            }
        }

        private void BrowseFolder()
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select QuickShot Captures Save Folder",
                SelectedPath = _storage.GetScreenshotsDir(),
                ShowNewFolderButton = true
            };
            if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                _settings.BackupDirectory = fbd.SelectedPath;
                _settings.Save();
                LoadSettingsIntoUI();
                RefreshHistory();
            }
        }

        private void ResetDefaults()
        {
            var fresh = AppSettings.CreateDefault();
            _settings.BackupDirectory = fresh.BackupDirectory;
            _settings.BackupRetentionDays = fresh.BackupRetentionDays;
            _settings.NotificationStyle = fresh.NotificationStyle;
            _settings.NotificationDuration = fresh.NotificationDuration;
            _settings.Hotkeys = fresh.Hotkeys;
            _settings.AutoCopyClipboard = true;
            _settings.AutoSaveFile = true;
            _settings.QuickCapture = true;
            _settings.ShowCursor = false;
            _settings.CaptureDelay = 0;
            _settings.Save();
            _hotkeys.ApplySettings(_settings.Hotkeys);
            LoadSettingsIntoUI();
        }

        private void OpenOptionsPopup(Control anchor, (string Label, string Value)[] options, string currentVal,
            Action<string> onSelected, bool openUpward = false)
        {
            DismissPopup();
            int itemH = 30;
            int popW = Math.Max(anchor.Width + 24, 132);
            int popH = options.Length * itemH + 10;

            Point anchorScreen = anchor.PointToScreen(Point.Empty);
            Point anchorClient = PointToClient(anchorScreen);
            int px = Math.Max(10, Math.Min(Width - popW - 10, anchorClient.X));
            int py = openUpward
                ? Math.Max(10, anchorClient.Y - popH - 4)
                : Math.Min(Height - popH - 10, anchorClient.Y + anchor.Height + 4);

            _activePopup = new Panel
            {
                Bounds = new Rectangle(px, py, popW, popH),
                BackColor = Color.FromArgb(26, 26, 35)
            };
            _activePopup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Brand, 1.25f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _activePopup.Width - 1, _activePopup.Height - 1), 8);
            };

            int iy = 5;
            foreach (var (lbl, val) in options)
            {
                string capturedVal = val;
                bool sel = string.Equals(val, currentVal, StringComparison.OrdinalIgnoreCase);
                var pill = new DarkPillButton(lbl, Color.FromArgb(26, 26, 35), Color.FromArgb(46, 46, 62), itemH - 2, popW - 10)
                {
                    Location = new Point(5, iy),
                    Size = new Size(popW - 10, itemH - 2),
                    PillFontStyle = FontStyle.Regular,
                    Selected = sel
                };
                pill.Click += (s, e) =>
                {
                    DismissPopup();
                    onSelected(capturedVal);
                };
                _activePopup.Controls.Add(pill);
                iy += itemH;
            }

            Controls.Add(_activePopup);
            _activePopup.BringToFront();
        }

        private void DismissPopup()
        {
            if (_activePopup != null)
            {
                Controls.Remove(_activePopup);
                _activePopup.Dispose();
                _activePopup = null;
            }
        }

        private static string FormatStyleLabel(string s) => s switch
        {
            "minimal" => "Minimal Toast",
            "none" => "Disabled",
            _ => "Preview Popup"
        };

        private static string FormatRetentionLabel(int days) => days <= 0 ? "Never" : (days == 1 ? "1 day" : $"{days} days");

        public void LoadSettingsIntoUI()
        {
            var hk = _settings.Hotkeys ?? new HotkeySettings();
            void SetHk(string k, string v)
            {
                if (_hotkeyCards.TryGetValue(k, out var c)) c.HotkeyText = v;
            }
            SetHk("captureRegion", hk.CaptureRegion);
            SetHk("captureScrolling", hk.CaptureScrolling);
            SetHk("recordGif", hk.RecordGif);
            SetHk("stopRecording", hk.StopRecording ?? "F9");
            SetHk("pauseRecording", hk.PauseResumeRecording ?? "F10");
            SetHk("ocrSnip", hk.OcrSnip);

            if (_folderPathLabel != null) _folderPathLabel.Text = _storage.GetScreenshotsDir();
            if (_stylePill != null) _stylePill.DisplayText = FormatStyleLabel(_settings.NotificationStyle);
            if (_durationPill != null) _durationPill.DisplayText = $"{_settings.NotificationDuration} sec";
            if (_retentionPill != null) _retentionPill.DisplayText = FormatRetentionLabel(_settings.BackupRetentionDays);
        }

        public void RefreshHistory()
        {
            if (_historyFlow == null) return;
            _historyFlow.SuspendLayout();
            try
            {
                foreach (Control old in _historyFlow.Controls) old.Dispose();
                _historyFlow.Controls.Clear();

                var items = _storage.GetHistory();
                _historyCountLabel.Text = items.Count == 1 ? "1 capture" : $"{items.Count} captures";

                if (items.Count == 0)
                {
                    var empty = new Panel { Size = new Size(424, 92), BackColor = Color.FromArgb(24, 24, 31) };
                    empty.Paint += (s, e) =>
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                        using var pen = new Pen(Color.FromArgb(46, 46, 58), 1f);
                        Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, empty.Width - 1, empty.Height - 1), 10);
                        using var f1 = Theme.Font(9.8f, FontStyle.Bold);
                        using var b1 = new SolidBrush(Color.FromArgb(236, 236, 244));
                        using var sf = new StringFormat { Alignment = StringAlignment.Center };
                        e.Graphics.DrawString("No captures yet", f1, b1, new RectangleF(0, 22, empty.Width, 22), sf);
                        using var f2 = Theme.Font(8.5f, FontStyle.Regular);
                        using var b2 = new SolidBrush(Color.FromArgb(146, 146, 168));
                        e.Graphics.DrawString("Press PrintScreen to capture & build your history grid.", f2, b2, new RectangleF(0, 48, empty.Width, 20), sf);
                    };
                    _historyFlow.Controls.Add(empty);
                    return;
                }

                foreach (var item in items.Take(40))
                {
                    CaptureHistoryItem capItem = item;
                    var card = new StudioHistoryGridCard(
                        capItem,
                        onCopy: () => StorageService.CopyExistingFileToClipboard(capItem.FilePath),
                        onOpen: () => _storage.OpenFile(capItem.FilePath),
                        onDelete: () =>
                        {
                            if (_storage.DeleteFile(capItem.FilePath))
                                RefreshHistory();
                        });
                    _historyFlow.Controls.Add(card);
                }
            }
            finally
            {
                _historyFlow.ResumeLayout();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logoBmp?.Dispose();
                _activePopup?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
