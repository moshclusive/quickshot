using QuickShot.Core;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public sealed class TrayController : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly StorageService _storage;
        private readonly HotkeyManager _hotkeys;
        private readonly MainForm _mainForm;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly Icon _normalIcon;
        private readonly Icon _recordingIcon;
        private readonly Action _onQuitRequested;

        private CaptureOverlayForm _activeOverlay;
        private GifRecorderSession _activeGifSession;
        private ScrollingCaptureSession _activeScrollSession;
        private LiveScrollRegionSession _activeLiveScrollSession;
        private PreviewPopupForm _activePreview;
        private bool _isCapturing;
        private bool _restoreMainAfterCapture;

        public TrayController(AppSettings settings, StorageService storage, HotkeyManager hotkeys,
            MainForm mainForm, bool startInTray, Action onQuitRequested)
        {
            _settings = settings;
            _storage = storage;
            _hotkeys = hotkeys;
            _mainForm = mainForm;
            _onQuitRequested = onQuitRequested;

            _normalIcon = AssetLoader.LoadAppIcon(32);
            _recordingIcon = CreateRedRecordingIcon();

            _trayMenu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                BackColor = Theme.BgHeader,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Font(9f, FontStyle.Regular),
                ShowImageMargin = false
            };
            _trayMenu.Opening += (s, e) => PopulateTrayMenu();

            _trayIcon = new NotifyIcon
            {
                Icon = _normalIcon,
                Text = "QuickShot - Fast Screenshot, GIF & OCR",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_activeGifSession != null)
                    {
                        _activeGifSession.StopAndSave();
                    }
                }
            };
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            _hotkeys.HotkeyTriggered += OnHotkeyTriggered;
            _hotkeys.ApplySettings(_settings.Hotkeys);

            if (!startInTray)
            {
                _mainForm.Show();
            }
        }

        private static Icon CreateRedRecordingIcon()
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var br = new SolidBrush(Theme.Danger);
                g.FillEllipse(br, 1, 1, 14, 14);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void ShowMainWindow()
        {
            if (_mainForm.WindowState == FormWindowState.Minimized)
                _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Show();
            _mainForm.Activate();
            _mainForm.RefreshHistory();
        }

        private void OnHotkeyTriggered(string task)
        {
            if (_activeGifSession != null && (task == "recordGif" || task == "stopRecording"))
            {
                _activeGifSession.StopAndSave();
                return;
            }
            if (task == "pauseRecording")
            {
                if (_activeGifSession != null) { _activeGifSession.TogglePause(); return; }
            }
            switch (task)
            {
                case "captureRegion": TriggerCapture("region"); break;
                case "captureScrolling": TriggerCapture("scrolling"); break;
                case "recordGif": TriggerCapture("gif"); break;
                case "ocrSnip": TriggerCapture("ocr"); break;
            }
        }

        private void PopulateTrayMenu()
        {
            _trayMenu.Items.Clear();

            if (_activeGifSession != null)
            {
                _trayMenu.Items.Add("⏹ Stop & Save GIF (F9)", null, (s, e) => _activeGifSession?.StopAndSave());
                _trayMenu.Items.Add("⏸ Pause / Resume GIF", null, (s, e) => _activeGifSession?.TogglePause());
                _trayMenu.Items.Add("✕ Abort GIF", null, (s, e) => _activeGifSession?.Abort());
                _trayMenu.Items.Add(new ToolStripSeparator());
            }

            _trayMenu.Items.Add("Open QuickShot", null, (s, e) => ShowMainWindow());
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayMenu.Items.Add("Capture Region...", null, (s, e) => TriggerCapture("region"));
            _trayMenu.Items.Add("Record GIF...", null, (s, e) => TriggerCapture("gif"));
            _trayMenu.Items.Add("Scrolling Capture...", null, (s, e) => TriggerCapture("scrolling"));
            _trayMenu.Items.Add("Extract Text (OCR)...", null, (s, e) => TriggerCapture("ocr"));

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Open Captures Folder", null, (s, e) => _storage.ShowInFolder());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Quit QuickShot", null, (s, e) => _onQuitRequested?.Invoke());
        }

        public async void TriggerCapture(string mode, int delaySec = 0, IntPtr targetHwnd = default, int monitorIdx = -1)
        {
            if (_activeOverlay != null && !_activeOverlay.IsDisposed)
            {
                _activeOverlay.Activate();
                return;
            }
            if (_isCapturing) return;
            _isCapturing = true;

            try
            {
                bool wasMainVisible = _mainForm.Visible;
                _restoreMainAfterCapture = wasMainVisible;
                if (wasMainVisible)
                {
                    _mainForm.Hide();
                    Application.DoEvents();
                }

                if (delaySec > 0)
                {
                    await Task.Delay(delaySec * 1000);
                }
                else if (wasMainVisible)
                {
                    await Task.Delay(60);
                }

                if (mode == "window")
                {
                    Rectangle winRect = targetHwnd != IntPtr.Zero
                        ? NativeMethods.FocusAndGetWindowRect(targetHwnd)
                        : (NativeMethods.GetActiveOrTopWindow(Process.GetCurrentProcess().Id)?.Bounds ?? Rectangle.Empty);

                    Bitmap bmp = !winRect.IsEmpty
                        ? ScreenCaptureService.CaptureRegion(winRect, _settings.ShowCursor)
                        : ScreenCaptureService.CaptureVirtualScreen(_settings.ShowCursor);
                    using (bmp)
                    {
                        var saved = _storage.SaveImageCapture(bmp, winRect.IsEmpty ? (Rectangle?)null : winRect);
                        ShowCaptureNotification(saved);
                    }
                    _isCapturing = false;
                    RestoreMainWindowIfNeeded();
                    return;
                }

                if (mode == "last-region" && _settings.LastRegion != null && _settings.LastRegion.IsValid)
                {
                    Rectangle lr = _settings.LastRegion.ToRectangle();
                    using Bitmap bmp = ScreenCaptureService.CaptureRegion(lr, _settings.ShowCursor);
                    var saved = _storage.SaveImageCapture(bmp, lr);
                    ShowCaptureNotification(saved);
                    _isCapturing = false;
                    RestoreMainWindowIfNeeded();
                    return;
                }

                if (mode == "fullscreen")
                {
                    Screen targetScreen = (monitorIdx >= 0 && monitorIdx < Screen.AllScreens.Length)
                        ? Screen.AllScreens[monitorIdx]
                        : Screen.FromPoint(Cursor.Position);
                    using Bitmap bmp = ScreenCaptureService.CaptureMonitor(targetScreen, _settings.ShowCursor);
                    var saved = _storage.SaveImageCapture(bmp);
                    ShowCaptureNotification(saved);
                    _isCapturing = false;
                    RestoreMainWindowIfNeeded();
                    return;
                }

                Rectangle vBounds = SystemInformation.VirtualScreen;
                Bitmap screenShot = ScreenCaptureService.CaptureVirtualScreen(_settings.ShowCursor);

                OverlayCaptureMode ovMode = mode switch
                {
                    "ocr" => OverlayCaptureMode.Ocr,
                    "gif" => OverlayCaptureMode.Gif,
                    "scrolling" => OverlayCaptureMode.Scrolling,
                    _ => OverlayCaptureMode.Region
                };

                _activeOverlay = new CaptureOverlayForm(screenShot, vBounds, ovMode, _settings.QuickCapture);
                _activeOverlay.CaptureCompleted += OnOverlayCompleted;
                _activeOverlay.CaptureCancelled += (s, e) => { _activeOverlay = null; _isCapturing = false; RestoreMainWindowIfNeeded(); };
                _activeOverlay.FormClosed += (s, e) => { _activeOverlay = null; _isCapturing = false; };
                _activeOverlay.Show();
            }
            catch
            {
                _isCapturing = false;
                RestoreMainWindowIfNeeded();
            }
        }

        private void RestoreMainWindowIfNeeded(bool activateMain = true)
        {
            if (!_restoreMainAfterCapture) return;
            _restoreMainAfterCapture = false;
            if (_mainForm == null || _mainForm.IsDisposed) return;
            _mainForm.Show();
            if (activateMain)
            {
                if (_mainForm.WindowState == FormWindowState.Minimized)
                    _mainForm.WindowState = FormWindowState.Normal;
                _mainForm.Activate();
            }
            _mainForm.RefreshHistory();
        }

        private void OnOverlayCompleted(object sender, OverlayCompletionEventArgs e)
        {
            _activeOverlay = null;
            _isCapturing = false;

            if (e.Mode == OverlayCaptureMode.Region && e.CapturedBitmap != null)
            {
                using (e.CapturedBitmap)
                {
                    var saved = _storage.SaveImageCapture(e.CapturedBitmap, e.ScreenRect);
                    ShowCaptureNotification(saved);
                }
                RestoreMainWindowIfNeeded(activateMain: true);
            }
            else if (e.Mode == OverlayCaptureMode.LiveScrollRegion)
            {
                StartLiveScrollRegionSession(e.ScreenRect, e.CapturedBitmap, e.InitialScrollDelta, e.PreScrollFrames, e.DocAnnotations);
            }
            else if (e.Mode == OverlayCaptureMode.Ocr && e.CapturedBitmap != null)
            {
                var ocrForm = new OcrResultForm();
                RestoreMainWindowIfNeeded(activateMain: false);
                ocrForm.Show();
                ocrForm.Activate();
                ocrForm.RunOcrAsync(e.CapturedBitmap);
            }
            else if (e.Mode == OverlayCaptureMode.Gif)
            {
                StartGifSession(e.ScreenRect);
            }
            else if (e.Mode == OverlayCaptureMode.Scrolling)
            {
                StartScrollingSession(e.ScreenRect, e.PreScrollFrames, e.DocAnnotations);
            }
        }

        private void StartLiveScrollRegionSession(
            Rectangle rect,
            Bitmap initialFrame,
            int initialDelta,
            System.Collections.Generic.List<PreScrollFrameData> preScrollFrames = null,
            System.Collections.Generic.List<AnnotationItem> docAnnotations = null)
        {
            _activeLiveScrollSession?.Dispose();
            _activeLiveScrollSession = new LiveScrollRegionSession(
                rect,
                initialFrame,
                initialDelta,
                _settings.ShowCursor,
                onCompleted: (stitchedBmp, box) =>
                {
                    _activeLiveScrollSession?.Dispose();
                    _activeLiveScrollSession = null;
                    using (stitchedBmp)
                    {
                        var saved = _storage.SaveImageCapture(stitchedBmp, box);
                        ShowCaptureNotification(saved);
                    }
                    RestoreMainWindowIfNeeded(activateMain: true);
                },
                onCancelled: () =>
                {
                    _activeLiveScrollSession?.Dispose();
                    _activeLiveScrollSession = null;
                    RestoreMainWindowIfNeeded(activateMain: true);
                },
                preScrollFrames: preScrollFrames,
                docAnnotations: docAnnotations);
            _activeLiveScrollSession.Start();
        }

        private void StartGifSession(Rectangle rect)
        {
            _activeGifSession?.Dispose();
            SetRecordingTrayState(true);
            _activeGifSession = new GifRecorderSession(
                rect,
                showCursor: true,
                onCompleted: (gifBytes, box) =>
                {
                    SetRecordingTrayState(false);
                    _activeGifSession?.Dispose();
                    _activeGifSession = null;
                    if (gifBytes != null && gifBytes.Length > 0)
                    {
                        var saved = _storage.SaveGifCapture(gifBytes, box);
                        ShowCaptureNotification(saved, "GIF Capture Done");
                    }
                    RestoreMainWindowIfNeeded(activateMain: true);
                },
                onAborted: () =>
                {
                    SetRecordingTrayState(false);
                    _activeGifSession?.Dispose();
                    _activeGifSession = null;
                    RestoreMainWindowIfNeeded(activateMain: true);
                });
            _activeGifSession.Start();
        }

        private void StartScrollingSession(
            Rectangle rect,
            System.Collections.Generic.List<PreScrollFrameData> preScrollFrames = null,
            System.Collections.Generic.List<AnnotationItem> docAnnotations = null)
        {
            _activeScrollSession?.Dispose();
            _activeScrollSession = new ScrollingCaptureSession(
                rect,
                onCompleted: (stitchedBmp, box) =>
                {
                    _activeScrollSession?.Dispose();
                    _activeScrollSession = null;
                    using (stitchedBmp)
                    {
                        var saved = _storage.SaveImageCapture(stitchedBmp, box);
                        ShowCaptureNotification(saved);
                    }
                    RestoreMainWindowIfNeeded(activateMain: true);
                },
                onCancelled: () =>
                {
                    _activeScrollSession?.Dispose();
                    _activeScrollSession = null;
                    RestoreMainWindowIfNeeded(activateMain: true);
                },
                preScrollFrames: preScrollFrames,
                docAnnotations: docAnnotations);
            _activeScrollSession.Start();
        }

        private void SetRecordingTrayState(bool recording)
        {
            _hotkeys.ApplySettings(_settings.Hotkeys, includeF9Stop: recording);
            _trayIcon.Icon = recording ? _recordingIcon : _normalIcon;
            _trayIcon.Text = recording
                ? "QuickShot - RECORDING GIF (Click Tray or Press F9 to Stop)"
                : "QuickShot - Fast Screenshot, GIF & OCR";
        }

        private void ShowCaptureNotification(CaptureResult saved, string customMsg = null)
        {
            _mainForm.RefreshHistory();

            string style = _settings.NotificationStyle ?? "preview";
            if (style == "none" && string.IsNullOrEmpty(customMsg))
            {
                saved.PreviewImage?.Dispose();
                return;
            }

            try
            {
                if (_activePreview != null && !_activePreview.IsDisposed)
                    _activePreview.Close();
            }
            catch { }

            _activePreview = new PreviewPopupForm(saved, _storage, style, _settings.NotificationDuration, customMsg);
            _activePreview.Show();
        }

        public void Dispose()
        {
            try { _activeOverlay?.Close(); } catch { }
            try { _activeGifSession?.Abort(); _activeGifSession?.Dispose(); } catch { }
            try { _activeScrollSession?.Dispose(); } catch { }
            try { _activeLiveScrollSession?.Dispose(); } catch { }
            try { _activePreview?.Close(); } catch { }
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu?.Dispose();
            _normalIcon?.Dispose();
            _recordingIcon?.Dispose();
        }

        internal sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder => Color.FromArgb(64, 64, 76);
            public override Color MenuItemBorder => Theme.Brand;
            public override Color MenuItemSelected => Color.FromArgb(46, 154, 127, 245);
            public override Color ToolStripDropDownBackground => Theme.BgHeader;
            public override Color ImageMarginGradientBegin => Theme.BgHeader;
            public override Color ImageMarginGradientMiddle => Theme.BgHeader;
            public override Color ImageMarginGradientEnd => Theme.BgHeader;
            public override Color SeparatorDark => Color.FromArgb(48, 48, 56);
            public override Color SeparatorLight => Color.Transparent;
        }

        internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextMuted;
                base.OnRenderItemText(e);
            }
        }
    }
}
