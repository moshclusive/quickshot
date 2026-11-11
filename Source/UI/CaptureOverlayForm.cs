using QuickShot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace QuickShot.UI
{
    public enum OverlayCaptureMode
    {
        Region,
        Ocr,
        Gif,
        Scrolling,
        LiveScrollRegion
    }

    public sealed class PreScrollFrameData : IDisposable
    {
        public Bitmap Frame { get; set; }
        public int HintY { get; set; }
        public int HintX { get; set; }

        public void Dispose() => Frame?.Dispose();
    }

    public class OverlayCompletionEventArgs : EventArgs
    {
        public OverlayCaptureMode Mode { get; set; }
        public Rectangle ScreenRect { get; set; }
        public Bitmap CapturedBitmap { get; set; }
        public int InitialScrollDelta { get; set; }
        public List<PreScrollFrameData> PreScrollFrames { get; set; }
        public List<AnnotationItem> DocAnnotations { get; set; }
    }

    public class CaptureOverlayForm : Form, IMessageFilter
    {
        private readonly Bitmap _screenBitmap;
        private readonly Rectangle _virtualBounds;
        private readonly bool _quickCapture;
        private OverlayCaptureMode _mode;

        private string _selectMode = "rect"; // "rect" | "free"
        private string _currentTool = null;  // null means selection mode
        private Color _currentColor = Color.FromArgb(255, 59, 48);
        private bool _userPickedColor;
        private int _strokeWidth = 2;
        private int _stepCounter = 1;

        private readonly List<AnnotationItem> _annotations = new List<AnnotationItem>();
        private readonly Stack<AnnotationItem> _redoStack = new Stack<AnnotationItem>();
        private AnnotationItem _activeAnnotation;
        private int _selectedAnnotationIndex = -1;

        private bool _isMouseDown;
        private bool _hasDragged;
        private bool _isDrawing;
        private bool _isMovingAnnotation;
        private bool _isMovingBox;
        private RectangleF _moveStartBox;
        private bool _isResizingBox;
        private string _resizeHandle;
        private RectangleF _resizeStartBox;
        private Point _dragStart;
        private Point _prevMouse;
        private Point _lastMouse;
        private bool _isSpaceDown;

        private readonly List<PointF> _freePoints = new List<PointF>();
        private RectangleF? _selectionRect;

        // Transparent Inline Text Input (offscreen IME host + canvas-rendered text & caret)
        private TextBox _inlineTextBox;
        private bool _inlineTextEditing;
        private Point _inlineTextPos;
        private readonly Timer _caretTimer;
        private bool _caretVisible = true;

        // Floating Top Toolbar + Popovers
        private Panel _topToolbar;
        private readonly Dictionary<string, OverlayIconButton> _toolButtons = new Dictionary<string, OverlayIconButton>();
        private OverlayIconButton _btnModeRect, _btnModeFree, _btnOcr, _btnGif, _btnScrolling;
        private DarkColorTriggerButton _btnColorTrigger;
        private DarkStrokeDropdownPill _strokePill;
        private Panel _colorPopover;
        private Panel _strokePopover;
        private readonly List<DarkPillButton> _strokeOptionButtons = new List<DarkPillButton>();
        private Panel _confirmActionBar;
        private readonly ToolTip _toolTip = new ToolTip();

        // High-FPS Render & Pixel Caches + Seamless 2D Live-Scroll Selection Canvas
        private Bitmap _fastScreenBmp;
        private Bitmap _fastDimmedBmp;
        private int[] _screenPixels;
        private Font _dimPillFont;
        private Font _hintFont;
        private Font _magFont;
        private Bitmap _magZoomBmp;
        private readonly Timer _liveCaptureTimer;
        private readonly Timer _pendingFirstLiveWheelTimer;
        private int _pendingFirstLiveWheelDy;
        private int _pendingFirstLiveWheelDx;
        private Point _pendingFirstWheelTarget;
        private bool _liveScrollActive;
        private bool _syntheticLeftUpSent;
        private bool _finishingLiveScroll;
        private int _scrollActivityDeadlineTick;
        private int _lastScrollHintY;
        private int _lastScrollHintX;
        private IntPtr _scrollTargetRoot;
        private IntPtr _mouseHook;
        private IntPtr _kbHook;
        private NativeMethods.LowLevelMouseProc _mouseHookProc;
        private NativeMethods.LowLevelKeyboardProc _kbHookProc;
        private VirtualScrollCanvas _scrollCanvas;
        private Rectangle _scrollMonWa;
        private Rectangle _scrollMonLocal;
        private PointF _docDragStart;
        private RectangleF _docSelRect;
        private Bitmap _lastLiveFreshBmp;
        private bool _isLiveCapturing;

        // Pre-drag / Toolbar-open wheel scroll & document-anchored annotation history
        private readonly Timer _preScrollPollTimer;
        private bool _isPreScrollPolling;
        private int _preScrollDeadlineTick;
        private int _preScrollHintY;
        private int _preScrollHintX;
        private IntPtr _preScrollTargetRoot;
        private Rectangle _preScrollMonWa;
        private Rectangle _preScrollMonLocal;
        private VirtualScrollCanvas _preScrollCanvas;
        private Bitmap _lastPreFreshBmp;
        private readonly List<(byte[] Png, int HintY, int HintX)> _preScrollKeyframes = new List<(byte[] Png, int HintY, int HintX)>();
        private int _lastKeyframeViewX;
        private int _lastKeyframeViewY;

        public event EventHandler<OverlayCompletionEventArgs> CaptureCompleted;
        public event EventHandler CaptureCancelled;

        public CaptureOverlayForm(Bitmap screenBitmap, Rectangle virtualBounds, OverlayCaptureMode initialMode, bool quickCapture)
        {
            _screenBitmap = screenBitmap ?? throw new ArgumentNullException(nameof(screenBitmap));
            _virtualBounds = virtualBounds;
            _mode = initialMode;
            _quickCapture = quickCapture;

            BuildRenderCache();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            DarkToolTipHelper.Configure(_toolTip);
            _liveCaptureTimer = new Timer { Interval = 28 };
            _liveCaptureTimer.Tick += OnLiveCaptureTick;
            _pendingFirstLiveWheelTimer = new Timer { Interval = 28 };
            _pendingFirstLiveWheelTimer.Tick += OnFlushPendingFirstLiveWheel;
            _preScrollPollTimer = new Timer { Interval = 22 };
            _preScrollPollTimer.Tick += OnPreScrollPollTick;
            _caretTimer = new Timer { Interval = 530 };
            _caretTimer.Tick += (s, e) => { _caretVisible = !_caretVisible; Invalidate(); };
            Application.AddMessageFilter(this);

            _lastMouse = PointToClient(Cursor.Position);
            BuildFloatingToolbar();
            BuildColorPopover();
            BuildStrokePopover();
            BuildConfirmActionBar();
            UpdateModeVisuals();
        }

        private void BuildRenderCache()
        {
            int w = _screenBitmap.Width, h = _screenBitmap.Height;
            _dimPillFont = Theme.Font(8.5f, FontStyle.Bold);
            _hintFont = Theme.Font(9f, FontStyle.Bold);
            _magFont = Theme.Font(7.5f, FontStyle.Bold);

            _fastScreenBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_fastScreenBmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_screenBitmap, 0, 0);
            }

            _fastDimmedBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_fastDimmedBmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                var cm = new ColorMatrix(new[]
                {
                    new float[] { 0.72f, 0, 0, 0, 0 },
                    new float[] { 0, 0.72f, 0, 0, 0 },
                    new float[] { 0, 0, 0.72f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                });
                using var ia = new ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(_screenBitmap, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
            }

            var bd = _screenBitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(bd.Stride) / 4;
                _screenPixels = new int[w * h];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        IntPtr.Add(bd.Scan0, y * Math.Abs(bd.Stride)),
                        _screenPixels, y * w, w);
                }
            }
            finally
            {
                _screenBitmap.UnlockBits(bd);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.DisableWindowTransitions(Handle);
            NativeMethods.ExcludeFromCapture(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ExcludeFromCapture(Handle);
            Bounds = _virtualBounds;
            PositionToolbarOnActiveMonitor();
            InstallEscapeKeyboardHook();
            InstallMouseReleaseHook();

            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint fgTid = fg != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(fg, out _) : 0;
            uint curTid = NativeMethods.GetCurrentThreadId();
            bool attached = fgTid != 0 && fgTid != curTid && NativeMethods.AttachThreadInput(fgTid, curTid, true);
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
            Focus();
            if (attached) NativeMethods.AttachThreadInput(fgTid, curTid, false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Intentionally blank: prevents white/black flash so screen appears completely frozen seamlessly!
        }

        private void PositionToolbarOnActiveMonitor()
        {
            Screen activeScreen = Screen.FromPoint(Cursor.Position);
            Rectangle sb = activeScreen.Bounds;
            int localX = sb.X - _virtualBounds.X + (sb.Width - _topToolbar.Width) / 2;
            int localY = sb.Y - _virtualBounds.Y + 26;
            _topToolbar.Location = new Point(
                Math.Max(8, Math.Min(Width - _topToolbar.Width - 8, localX)),
                Math.Max(8, localY));
        }

        private void BuildFloatingToolbar()
        {
            _topToolbar = new Panel
            {
                Height = 40,
                BackColor = Color.FromArgb(24, 24, 27),
                Padding = new Padding(4, 4, 4, 4)
            };
            _topToolbar.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(56, 56, 62), 1f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _topToolbar.Width - 1, _topToolbar.Height - 1), 8);
            };

            int x = 6;

            // Grip handle
            var grip = new Label
            {
                Text = "⋮⋮",
                ForeColor = Color.FromArgb(113, 113, 122),
                Font = Theme.Font(10f, FontStyle.Bold),
                Size = new Size(18, 32),
                Location = new Point(x, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.SizeAll
            };
            Point dragOffset = Point.Empty;
            bool draggingBar = false;
            grip.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    draggingBar = true;
                    dragOffset = e.Location;
                }
            };
            grip.MouseMove += (s, e) =>
            {
                if (!draggingBar) return;
                Point pt = _topToolbar.Parent.PointToClient(grip.PointToScreen(e.Location));
                int nx = Math.Max(8, Math.Min(Width - _topToolbar.Width - 8, pt.X - dragOffset.X));
                int ny = Math.Max(8, Math.Min(Height - _topToolbar.Height - 8, pt.Y - dragOffset.Y));
                _topToolbar.Location = new Point(nx, ny);
                if (_colorPopover.Visible) _colorPopover.Visible = false;
                if (_strokePopover.Visible) _strokePopover.Visible = false;
            };
            grip.MouseUp += (s, e) => draggingBar = false;
            _topToolbar.Controls.Add(grip);
            x += 20;

            _btnModeRect = AddToolbarIconButton("rectangular", "Rectangular Selection (R)", ref x, () =>
            {
                _selectMode = "rect";
                _currentTool = null;
                _mode = OverlayCaptureMode.Region;
                UpdateToolbarHighlights();
                Cursor = Cursors.Cross;
                if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5) PositionConfirmBar();
                Invalidate();
            });
            _btnModeFree = AddToolbarIconButton("lasso", "Freehand Lasso Selection (F)", ref x, () =>
            {
                _selectMode = "free";
                _currentTool = null;
                _mode = OverlayCaptureMode.Region;
                UpdateToolbarHighlights();
                Cursor = Cursors.Cross;
                if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5) PositionConfirmBar();
                Invalidate();
            });

            AddToolbarDivider(ref x);

            var tools = new[]
            {
                ("select", "select", "Move Tool (V)"),
                ("rect", "rect", "Rectangle (U)"),
                ("circle", "circle", "Circle / Ellipse (C)"),
                ("line", "line", "Line (L)"),
                ("arrow", "arrow", "Arrow (A)"),
                ("brush", "brush", "Brush (P)"),
                ("highlighter", "highlighter", "Highlighter Marker (H)"),
                ("number", "number", "Step Number (N)"),
                ("text", "text", "Text Tool (T)"),
                ("blur", "blur", "Blur / Redact (B)"),
                ("spotlight", "spotlight", "Spotlight Highlight (S)"),
                ("zoom", "magnify", "Magnify Callout (Z)"),
                ("eraser", "eraser", "Eraser (Del)")
            };
            foreach (var (toolKey, svg, tip) in tools)
            {
                string capturedTool = toolKey;
                var btn = AddToolbarIconButton(svg, tip, ref x, () => SelectAnnotationTool(capturedTool));
                _toolButtons[toolKey] = btn;
            }

            AddToolbarDivider(ref x);

            // OCR, GIF & Scrolling Capture right next to each other (NO Video!)
            _btnOcr = AddToolbarIconButton("ocr", "Extract Text (OCR)", ref x, () =>
            {
                if (_inlineTextEditing) CommitInlineText();
                _mode = _mode == OverlayCaptureMode.Ocr ? OverlayCaptureMode.Region : OverlayCaptureMode.Ocr;
                if (_mode == OverlayCaptureMode.Ocr) _currentTool = null;
                UpdateModeVisuals();
            });
            _btnGif = AddToolbarIconButton("gif", "Screen Recording (GIF)", ref x, () =>
            {
                if (_inlineTextEditing) CommitInlineText();
                _mode = _mode == OverlayCaptureMode.Gif ? OverlayCaptureMode.Region : OverlayCaptureMode.Gif;
                if (_mode == OverlayCaptureMode.Gif) _currentTool = null;
                UpdateModeVisuals();
            });
            _btnScrolling = AddToolbarIconButton("scrollcapture", "Scrolling Capture", ref x, () =>
            {
                if (_inlineTextEditing) CommitInlineText();
                if (_selectionRect.HasValue && _selectionRect.Value.Width >= 20 && _selectionRect.Value.Height >= 20)
                {
                    FinalizeScrollingSelection();
                    return;
                }
                _mode = _mode == OverlayCaptureMode.Scrolling ? OverlayCaptureMode.Region : OverlayCaptureMode.Scrolling;
                if (_mode == OverlayCaptureMode.Scrolling) _currentTool = null;
                UpdateModeVisuals();
            });

            AddToolbarDivider(ref x);

            // Color Dropdown Trigger
            _btnColorTrigger = new DarkColorTriggerButton
            {
                SelectedColor = _currentColor,
                Location = new Point(x, 6)
            };
            _toolTip.SetToolTip(_btnColorTrigger, "Annotation Color");
            _btnColorTrigger.Click += (s, e) =>
            {
                _strokePopover.Visible = false;
                int popY = _topToolbar.Bottom + 6;
                if (popY + _colorPopover.Height > Height - 8) popY = _topToolbar.Top - _colorPopover.Height - 6;
                _colorPopover.Location = new Point(
                    Math.Max(8, Math.Min(Width - _colorPopover.Width - 8, _topToolbar.Left + _btnColorTrigger.Left - (_colorPopover.Width - _btnColorTrigger.Width) / 2)),
                    popY);
                _colorPopover.Visible = !_colorPopover.Visible;
                if (_colorPopover.Visible) _colorPopover.BringToFront();
            };
            _topToolbar.Controls.Add(_btnColorTrigger);
            x += 46;

            AddToolbarDivider(ref x);

            // Custom Stroke Width Dropdown Pill
            _strokePill = new DarkStrokeDropdownPill
            {
                LabelText = $"{_strokeWidth} pt",
                Location = new Point(x, 6)
            };
            _toolTip.SetToolTip(_strokePill, "Stroke Width / Text Size");
            _strokePill.Click += (s, e) =>
            {
                _colorPopover.Visible = false;
                int popY = _topToolbar.Bottom + 6;
                if (popY + _strokePopover.Height > Height - 8) popY = _topToolbar.Top - _strokePopover.Height - 6;
                _strokePopover.Location = new Point(
                    Math.Max(8, Math.Min(Width - _strokePopover.Width - 8, _topToolbar.Left + _strokePill.Left - (_strokePopover.Width - _strokePill.Width) / 2)),
                    popY);
                _strokePopover.Visible = !_strokePopover.Visible;
                if (_strokePopover.Visible) _strokePopover.BringToFront();
            };
            _topToolbar.Controls.Add(_strokePill);
            x += 64;

            AddToolbarDivider(ref x);

            AddToolbarIconButton("undo", "Undo (Ctrl+Z)", ref x, Undo);
            AddToolbarIconButton("redo", "Redo (Ctrl+Y)", ref x, Redo);
            AddToolbarIconButton("clear", "Clear Annotations", ref x, ClearAnnotations);

            AddToolbarDivider(ref x);

            // Cancel button
            var btnClose = new OverlayIconButton(glyphText: "✕")
            {
                DangerHover = true,
                Size = new Size(30, 30),
                Location = new Point(x, 5)
            };
            _toolTip.SetToolTip(btnClose, "Cancel Capture (Esc)");
            btnClose.Click += (s, e) => CancelAndClose();
            _topToolbar.Controls.Add(btnClose);
            x += 34;

            _topToolbar.Width = x + 4;
            Controls.Add(_topToolbar);
        }

        private OverlayIconButton AddToolbarIconButton(string svgName, string tooltip, ref int x, Action onClick)
        {
            var btn = new OverlayIconButton(svgName)
            {
                Size = new Size(30, 30),
                Location = new Point(x, 5)
            };
            _toolTip.SetToolTip(btn, tooltip);
            btn.Click += (s, e) =>
            {
                _colorPopover.Visible = false;
                _strokePopover.Visible = false;
                onClick?.Invoke();
                if (!IsDisposed && !_inlineTextEditing) Focus();
            };
            _topToolbar.Controls.Add(btn);
            x += 32;
            return btn;
        }

        private void AddToolbarDivider(ref int x)
        {
            var div = new Panel
            {
                Width = 1,
                Height = 20,
                BackColor = Color.FromArgb(52, 52, 58),
                Location = new Point(x + 3, 10)
            };
            _topToolbar.Controls.Add(div);
            x += 8;
        }

        private void BuildColorPopover()
        {
            _colorPopover = new Panel
            {
                Size = new Size(228, 38),
                BackColor = Color.FromArgb(24, 24, 30),
                Visible = false
            };
            _colorPopover.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Brand, 1.2f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _colorPopover.Width - 1, _colorPopover.Height - 1), 8);
            };

            Color[] swatches =
            {
                Color.FromArgb(255, 59, 48),   // Red
                Color.FromArgb(255, 255, 0),   // Neon Yellow
                Color.FromArgb(154, 127, 245), // QuickShot Violet
                Color.FromArgb(0, 229, 255),   // Cyan
                Color.FromArgb(34, 197, 94),   // Green
                Color.White,
                Color.Black
            };

            int x = 10;
            foreach (var sw in swatches)
            {
                Color c = sw;
                var dot = new Panel
                {
                    Size = new Size(20, 20),
                    Location = new Point(x, 9),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                dot.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using var br = new SolidBrush(c);
                    e.Graphics.FillEllipse(br, 2, 2, 16, 16);
                    bool selected = _currentColor.ToArgb() == c.ToArgb();
                    using var ring = new Pen(selected ? Theme.Brand : Color.FromArgb(150, 255, 255, 255), selected ? 1.8f : 1.1f);
                    e.Graphics.DrawEllipse(ring, 2, 2, 16, 16);
                };
                dot.Click += (s, e) =>
                {
                    _userPickedColor = true;
                    SetColor(c);
                    _colorPopover.Visible = false;
                };
                _colorPopover.Controls.Add(dot);
                x += 26;
            }

            var customBtn = new DarkPillButton("+", Theme.BgControl, Color.FromArgb(52, 52, 66), 24, 24)
            {
                Location = new Point(x + 2, 7),
                BorderColor = Theme.Brand
            };
            customBtn.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _currentColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _userPickedColor = true;
                    SetColor(cd.Color);
                }
                _colorPopover.Visible = false;
            };
            _colorPopover.Controls.Add(customBtn);
            Controls.Add(_colorPopover);
        }

        private void BuildStrokePopover()
        {
            int[] widths = { 2, 4, 8, 14, 20 };
            _strokePopover = new Panel
            {
                Size = new Size(272, 38),
                BackColor = Color.FromArgb(24, 24, 30),
                Visible = false
            };
            _strokePopover.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Brand, 1.2f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _strokePopover.Width - 1, _strokePopover.Height - 1), 8);
            };

            int x = 8;
            foreach (int w in widths)
            {
                int capturedW = w;
                var pill = new DarkPillButton($"{w} pt", Color.FromArgb(34, 34, 42), Color.FromArgb(50, 50, 62), 26, 48)
                {
                    Location = new Point(x, 6),
                    BorderColor = Color.FromArgb(58, 58, 70),
                    PillFontStyle = FontStyle.Regular,
                    Selected = (_strokeWidth == w)
                };
                pill.Click += (s, e) =>
                {
                    _strokeWidth = capturedW;
                    _strokePill.LabelText = $"{capturedW} pt";
                    foreach (var p in _strokeOptionButtons)
                        p.Selected = p == pill;
                    _strokePopover.Visible = false;
                };
                _strokeOptionButtons.Add(pill);
                _strokePopover.Controls.Add(pill);
                x += 52;
            }
            Controls.Add(_strokePopover);
        }

        private void BuildConfirmActionBar()
        {
            _confirmActionBar = new Panel
            {
                Size = new Size(442, 38),
                BackColor = Color.FromArgb(24, 24, 30),
                Visible = false
            };
            _confirmActionBar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Brand, 1.2f);
                Theme.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, _confirmActionBar.Width - 1, _confirmActionBar.Height - 1), 8);
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6, 5, 6, 5),
                WrapContents = false,
                BackColor = Color.Transparent
            };

            var bSave = new DarkPillButton("✓ Capture", Theme.BrandDark, Theme.Brand, 28, 78);
            bSave.Click += (s, e) => FinalizeRegionCapture();

            var bScroll = new DarkPillButton("↕ Scroll", Color.FromArgb(38, 38, 48), Color.FromArgb(56, 56, 70), 28, 68) { BorderColor = Color.FromArgb(62, 62, 74) };
            bScroll.Click += (s, e) => FinalizeLiveScrollRegionSelection(0);

            var bOcr = new DarkPillButton("OCR", Color.FromArgb(38, 38, 48), Color.FromArgb(56, 56, 70), 28, 52) { BorderColor = Color.FromArgb(62, 62, 74) };
            bOcr.Click += (s, e) => { _mode = OverlayCaptureMode.Ocr; FinalizeOcrSelection(); };

            var bGif = new DarkPillButton("GIF", Color.FromArgb(38, 38, 48), Color.FromArgb(56, 56, 70), 28, 52) { BorderColor = Color.FromArgb(62, 62, 74) };
            bGif.Click += (s, e) => { _mode = OverlayCaptureMode.Gif; FinalizeGifSelection(); };

            var bCancel = new DarkPillButton("✕", Color.FromArgb(38, 38, 48), Color.FromArgb(175, 220, 38, 38), 28, 36) { BorderColor = Color.FromArgb(62, 62, 74) };
            bCancel.Click += (s, e) => CancelAndClose();

            flow.Controls.AddRange(new Control[] { bSave, bScroll, bOcr, bGif, bCancel });
            _confirmActionBar.Controls.Add(flow);
            Controls.Add(_confirmActionBar);
        }

        private void SetColor(Color c)
        {
            _currentColor = c;
            _btnColorTrigger.SelectedColor = c;
            _colorPopover.Invalidate(true);
            if (_inlineTextEditing)
                Invalidate();
        }

        private void SelectAnnotationTool(string tool)
        {
            if (_inlineTextEditing && tool != "text")
                CommitInlineText();
            _mode = OverlayCaptureMode.Region;
            _confirmActionBar.Visible = false;

            if (_currentTool == tool)
            {
                _currentTool = null;
                Cursor = Cursors.Cross;
                if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5)
                    PositionConfirmBar();
            }
            else
            {
                _currentTool = tool;
                if (tool == "highlighter" && !_userPickedColor && _currentColor.R == 255 && _currentColor.G == 59 && _currentColor.B == 48)
                {
                    SetColor(Color.FromArgb(255, 255, 0));
                }
                Cursor = tool == "select" ? Cursors.Default : (tool == "text" ? Cursors.IBeam : Cursors.Cross);
            }
            UpdateModeVisuals();
        }

        private void UpdateModeVisuals()
        {
            UpdateToolbarHighlights();
            Invalidate();
        }

        private void UpdateToolbarHighlights()
        {
            _btnModeRect.Active = _currentTool == null && _mode == OverlayCaptureMode.Region && _selectMode == "rect";
            _btnModeFree.Active = _currentTool == null && _mode == OverlayCaptureMode.Region && _selectMode == "free";
            _btnOcr.Active = _mode == OverlayCaptureMode.Ocr;
            _btnGif.Active = _mode == OverlayCaptureMode.Gif;
            _btnScrolling.Active = _mode == OverlayCaptureMode.Scrolling;

            foreach (var kv in _toolButtons)
                kv.Value.Active = (_currentTool == kv.Key);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_colorPopover.Visible) _colorPopover.Visible = false;
            if (_strokePopover.Visible) _strokePopover.Visible = false;

            if (e.Button == MouseButtons.Right)
            {
                if (_isDrawing || _isMouseDown || _selectionRect.HasValue || _liveScrollActive)
                {
                    _isMouseDown = false;
                    _isDrawing = false;
                    _activeAnnotation = null;
                    _selectionRect = null;
                    _freePoints.Clear();
                    _confirmActionBar.Visible = false;
                    ResetLiveScroll();
                    _topToolbar.Visible = true;
                    Invalidate();
                }
                else
                {
                    CancelAndClose();
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_inlineTextEditing || (_inlineTextBox != null && _inlineTextBox.Visible))
                CommitInlineText();

            _preScrollPollTimer.Stop();
            _isMouseDown = true;
            _hasDragged = false;
            _dragStart = e.Location;
            _prevMouse = e.Location;
            _lastMouse = e.Location;

            // Check resize handles, edge borders & inside-box move if selection exists
            if (!_quickCapture && _selectionRect.HasValue && _currentTool == null && _selectMode != "free")
            {
                string hit = HitTestResizeHandle(_selectionRect.Value, e.Location);
                if (hit != null)
                {
                    _isResizingBox = true;
                    _resizeHandle = hit;
                    _resizeStartBox = _selectionRect.Value;
                    _confirmActionBar.Visible = false;
                    return;
                }
                if (_selectionRect.Value.Contains(e.Location))
                {
                    _isMovingBox = true;
                    _moveStartBox = _selectionRect.Value;
                    _confirmActionBar.Visible = false;
                    Cursor = Cursors.SizeAll;
                    return;
                }
            }

            if (_currentTool == "select")
            {
                _selectedAnnotationIndex = AnnotationRenderer.FindAnnotationAt(_annotations, e.X, e.Y);
                if (_selectedAnnotationIndex != -1)
                {
                    _isMovingAnnotation = true;
                    Cursor = Cursors.SizeAll;
                    Invalidate();
                    return;
                }
            }

            if (_currentTool != null)
            {
                if (_currentTool == "text")
                {
                    SpawnInlineTextInput(e.Location);
                    _isMouseDown = false;
                    return;
                }
                if (_currentTool == "eraser")
                {
                    EraseAt(e.Location);
                    _isMouseDown = false;
                    return;
                }

                _isDrawing = true;
                if (_currentTool == "brush" || _currentTool == "highlighter")
                {
                    _activeAnnotation = new AnnotationItem
                    {
                        Type = _currentTool == "highlighter" ? AnnotationType.Highlighter : AnnotationType.Brush,
                        Color = _currentColor,
                        StrokeWidth = _currentTool == "highlighter" ? Math.Max(18f, _strokeWidth * 5f) : _strokeWidth,
                        Points = new List<PointF> { e.Location }
                    };
                }
                else if (_currentTool == "number")
                {
                    RecalcStepCounter();
                    _activeAnnotation = new AnnotationItem
                    {
                        Type = AnnotationType.Number,
                        X = e.X,
                        Y = e.Y,
                        Number = _stepCounter,
                        Color = _currentColor,
                        Radius = Math.Max(14f, _strokeWidth * 3f)
                    };
                    Invalidate();
                }
                else
                {
                    _activeAnnotation = new AnnotationItem
                    {
                        Type = ParseToolType(_currentTool),
                        X1 = e.X,
                        Y1 = e.Y,
                        X2 = e.X,
                        Y2 = e.Y,
                        Color = _currentColor,
                        StrokeWidth = _strokeWidth
                    };
                }
                return;
            }

            // Starting region selection: hide top bar during active drag (Snapmark style) so top edge stays clear for UP scrolling
            ResetLiveScroll();
            _confirmActionBar.Visible = false;
            _topToolbar.Visible = false;
            _freePoints.Clear();
            _freePoints.Add(e.Location);
            if (_selectMode == "rect" || _mode != OverlayCaptureMode.Region)
                _selectionRect = new RectangleF(e.X, e.Y, 0, 0);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int stepDx = e.X - _prevMouse.X;
            int stepDy = e.Y - _prevMouse.Y;
            _prevMouse = e.Location;
            _lastMouse = e.Location;

            if (_isResizingBox)
            {
                float dx = e.X - _dragStart.X;
                float dy = e.Y - _dragStart.Y;
                var b = _resizeStartBox;
                float nx = b.X, ny = b.Y, nw = b.Width, nh = b.Height;
                if (_resizeHandle.Contains("e")) nw = Math.Max(16, Math.Min(Width - b.X, b.Width + dx));
                if (_resizeHandle.Contains("s")) nh = Math.Max(16, Math.Min(Height - b.Y, b.Height + dy));
                if (_resizeHandle.Contains("w")) { float right = b.Right; nw = Math.Max(16, Math.Min(right, b.Width - dx)); nx = right - nw; }
                if (_resizeHandle.Contains("n")) { float bottom = b.Bottom; nh = Math.Max(16, Math.Min(bottom, b.Height - dy)); ny = bottom - nh; }
                _selectionRect = new RectangleF(nx, ny, nw, nh);
                Invalidate();
                return;
            }

            if (_isMovingBox)
            {
                float dx = e.X - _dragStart.X;
                float dy = e.Y - _dragStart.Y;
                var b = _moveStartBox;
                float nx = Math.Max(0, Math.Min(Width - b.Width, b.X + dx));
                float ny = Math.Max(0, Math.Min(Height - b.Height, b.Y + dy));
                _selectionRect = new RectangleF(nx, ny, b.Width, b.Height);
                Invalidate();
                return;
            }

            if (_isMovingAnnotation && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _annotations.Count)
            {
                _annotations[_selectedAnnotationIndex].Translate(stepDx, stepDy);
                Invalidate();
                return;
            }

            if (_isDrawing && _activeAnnotation != null)
            {
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                bool ctrl = (ModifierKeys & Keys.Control) != 0;

                if (_isSpaceDown)
                {
                    _activeAnnotation.Translate(stepDx, stepDy);
                    _dragStart = new Point(_dragStart.X + stepDx, _dragStart.Y + stepDy);
                }
                else if (_activeAnnotation.Type == AnnotationType.Number)
                {
                    _activeAnnotation.X = e.X;
                    _activeAnnotation.Y = e.Y;
                }
                else if (_activeAnnotation.Type == AnnotationType.Brush || _activeAnnotation.Type == AnnotationType.Highlighter)
                {
                    if (shift)
                    {
                        bool lockH = Math.Abs(e.X - _dragStart.X) >= Math.Abs(e.Y - _dragStart.Y);
                        _activeAnnotation.Points.Add(lockH ? new PointF(e.X, _dragStart.Y) : new PointF(_dragStart.X, e.Y));
                    }
                    else
                    {
                        _activeAnnotation.Points.Add(e.Location);
                    }
                }
                else if (_activeAnnotation.Type == AnnotationType.Line || _activeAnnotation.Type == AnnotationType.Arrow)
                {
                    if (shift)
                    {
                        double dx = e.X - _activeAnnotation.X1, dy = e.Y - _activeAnnotation.Y1;
                        double len = Math.Sqrt(dx * dx + dy * dy);
                        double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                        _activeAnnotation.X2 = (float)(_activeAnnotation.X1 + Math.Cos(ang) * len);
                        _activeAnnotation.Y2 = (float)(_activeAnnotation.Y1 + Math.Sin(ang) * len);
                    }
                    else
                    {
                        _activeAnnotation.X2 = e.X;
                        _activeAnnotation.Y2 = e.Y;
                    }
                }
                else if (shift || ctrl)
                {
                    float dx = e.X - _activeAnnotation.X1, dy = e.Y - _activeAnnotation.Y1;
                    float side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    _activeAnnotation.X2 = _activeAnnotation.X1 + (dx >= 0 ? side : -side);
                    _activeAnnotation.Y2 = _activeAnnotation.Y1 + (dy >= 0 ? side : -side);
                }
                else
                {
                    _activeAnnotation.X2 = e.X;
                    _activeAnnotation.Y2 = e.Y;
                }
                Invalidate();
                return;
            }

            if (_isMouseDown && _currentTool == null)
            {
                if (_liveScrollActive && _scrollCanvas != null)
                {
                    _hasDragged = true;
                    RecalcLiveScrollSelection(e.Location);
                    Invalidate();
                    return;
                }

                double dist = Math.Sqrt(Math.Pow(e.X - _dragStart.X, 2) + Math.Pow(e.Y - _dragStart.Y, 2));
                if (dist > 3 || _isSpaceDown)
                {
                    _hasDragged = true;
                    if (_selectMode == "free" && _mode == OverlayCaptureMode.Region)
                    {
                        var last = _freePoints[_freePoints.Count - 1];
                        if (Math.Sqrt(Math.Pow(e.X - last.X, 2) + Math.Pow(e.Y - last.Y, 2)) >= 3)
                            _freePoints.Add(e.Location);

                        float minX = _freePoints.Min(p => p.X), maxX = _freePoints.Max(p => p.X);
                        float minY = _freePoints.Min(p => p.Y), maxY = _freePoints.Max(p => p.Y);
                        _selectionRect = new RectangleF(minX, minY, Math.Max(6, maxX - minX), Math.Max(6, maxY - minY));
                    }
                    else if (_isSpaceDown && _selectionRect.HasValue)
                    {
                        var r = _selectionRect.Value;
                        float nx = Math.Max(0, Math.Min(Width - r.Width, r.X + stepDx));
                        float ny = Math.Max(0, Math.Min(Height - r.Height, r.Y + stepDy));
                        _selectionRect = new RectangleF(nx, ny, r.Width, r.Height);
                        _dragStart = new Point(_dragStart.X + stepDx, _dragStart.Y + stepDy);
                    }
                    else
                    {
                        float w = Math.Abs(e.X - _dragStart.X);
                        float h = Math.Abs(e.Y - _dragStart.Y);
                        if ((ModifierKeys & Keys.Shift) != 0)
                        {
                            float side = Math.Max(w, h);
                            w = side; h = side;
                        }
                        float x = e.X >= _dragStart.X ? _dragStart.X : _dragStart.X - w;
                        float y = e.Y >= _dragStart.Y ? _dragStart.Y : _dragStart.Y - h;
                        _selectionRect = new RectangleF(x, y, w, h);
                    }
                    Invalidate();
                }
                return;
            }

            if (!_isMouseDown && _currentTool == null)
            {
                if (!_quickCapture && _selectionRect.HasValue && _selectMode != "free")
                {
                    string h = HitTestResizeHandle(_selectionRect.Value, e.Location);
                    if (h != null)
                    {
                        Cursor = GetCursorForHandle(h);
                        return;
                    }
                    if (_selectionRect.Value.Contains(e.Location))
                    {
                        if (Cursor != Cursors.SizeAll) Cursor = Cursors.SizeAll;
                        return;
                    }
                    if (Cursor != Cursors.Cross) Cursor = Cursors.Cross;
                    return;
                }
                Invalidate(); // Smoothly update live ShareX pixel magnifier HUD!
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_LBUTTONUP && _syntheticLeftUpSent)
            {
                // Ignore synthetic LEFTUP emitted once to unlock Chrome/Explorer wheel handling while physically dragging
                _syntheticLeftUpSent = false;
                if (!IsDisposed && IsHandleCreated && _liveScrollActive)
                    NativeMethods.SetCapture(Handle);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == NativeMethods.WM_MOUSEHWHEEL)
            {
                short hwDelta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                if (hwDelta != 0)
                {
                    HandleOverlayMouseWheel(0, hwDelta, Cursor.Position);
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (IsDisposed || !Visible || !IsHandleCreated) return false;
            if (m.Msg != NativeMethods.WM_MOUSEWHEEL && m.Msg != NativeMethods.WM_MOUSEHWHEEL)
                return false;

            Control target = Control.FromHandle(m.HWnd);
            if (target != this && (target == null || target.FindForm() != this))
                return false;

            long lp = m.LParam.ToInt64();
            Point scrPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            if (scrPt.X == 0 && scrPt.Y == 0) scrPt = Cursor.Position;
            Point clientPt = PointToClient(scrPt);
            if ((_colorPopover.Visible && _colorPopover.Bounds.Contains(clientPt)) ||
                (_strokePopover.Visible && _strokePopover.Bounds.Contains(clientPt)))
                return false;

            short delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            if (delta == 0) return false;

            bool hwheel = m.Msg == NativeMethods.WM_MOUSEHWHEEL;
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            int dy = (!hwheel && !shift) ? delta : 0;
            int dx = hwheel ? delta : (shift ? -delta : 0);

            HandleOverlayMouseWheel(dy, dx, scrPt);
            return true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;

            bool shift = (ModifierKeys & Keys.Shift) != 0;
            int dy = shift ? 0 : e.Delta;
            int dx = shift ? -e.Delta : 0;
            HandleOverlayMouseWheel(dy, dx, PointToScreen(e.Location));
        }

        private void HandleOverlayMouseWheel(int dy, int dx, Point scrPt)
        {
            if ((dy == 0 && dx == 0) || IsDisposed || _finishingLiveScroll) return;
            _lastMouse = PointToClient(scrPt);

            if (_isMouseDown && _currentTool == null && _selectMode == "rect" && !_isMovingBox && !_isResizingBox &&
                (_mode == OverlayCaptureMode.Region || _mode == OverlayCaptureMode.Scrolling || _mode == OverlayCaptureMode.Ocr))
            {
                _hasDragged = true;
                StartOrContinueLiveScroll(dy, dx);
                return;
            }

            if (!_isMouseDown)
            {
                StartOrContinuePreScroll(dy, dx, scrPt);
            }
        }

        private Point GetSafeScrollTargetScreenPoint()
        {
            // Target active cursor inside the target window when valid (handles split panes like Explorer File List),
            // falling back to selection center or monitor center if cursor moved off-window/onto taskbar.
            int mx = Math.Max(_scrollMonLocal.Left + 28, Math.Min(_scrollMonLocal.Right - 28, _lastMouse.X));
            int my = Math.Max(_scrollMonLocal.Top + 48, Math.Min(_scrollMonLocal.Bottom - 48, _lastMouse.Y));
            Point mouseScr = PointToScreen(new Point(mx, my));

            if (_selectionRect.HasValue && _selectionRect.Value.Width > 32 && _selectionRect.Value.Height > 32)
            {
                if (_scrollTargetRoot != IntPtr.Zero)
                {
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    if (NativeMethods.GetTopScrollTargetRoot(mouseScr.X, mouseScr.Y, pid) == _scrollTargetRoot)
                        return mouseScr;
                }
                var r = _selectionRect.Value;
                int cx = (int)Math.Max(_scrollMonLocal.Left + 28, Math.Min(_scrollMonLocal.Right - 28, r.X + r.Width / 2f));
                int cy = (int)Math.Max(_scrollMonLocal.Top + 48, Math.Min(_scrollMonLocal.Bottom - 48, r.Y + r.Height / 2f));
                return PointToScreen(new Point(cx, cy));
            }
            return mouseScr;
        }

        private void EnsureLiveScrollInitialized()
        {
            if (_liveScrollActive && _scrollCanvas != null) return;
            _preScrollPollTimer.Stop();

            Screen mon = Screen.FromPoint(PointToScreen(_dragStart));
            _scrollMonWa = mon.WorkingArea;
            _scrollMonLocal = new Rectangle(
                _scrollMonWa.X - _virtualBounds.X,
                _scrollMonWa.Y - _virtualBounds.Y,
                _scrollMonWa.Width,
                _scrollMonWa.Height);

            if (_preScrollCanvas != null && _preScrollMonWa == _scrollMonWa)
            {
                _scrollCanvas = _preScrollCanvas;
                _preScrollCanvas = null;
                _docDragStart = new PointF(
                    Math.Max(0, Math.Min(_scrollMonWa.Width, _dragStart.X - _scrollMonLocal.X + _scrollCanvas.ViewX)),
                    Math.Max(0, Math.Min(_scrollMonWa.Height, _dragStart.Y - _scrollMonLocal.Y + _scrollCanvas.ViewY)));
            }
            else
            {
                using Bitmap slice = _screenBitmap.Clone(
                    Rectangle.Intersect(new Rectangle(0, 0, _screenBitmap.Width, _screenBitmap.Height), _scrollMonLocal),
                    PixelFormat.Format32bppArgb);
                _scrollCanvas = new VirtualScrollCanvas(_scrollMonWa, slice);
                _docDragStart = new PointF(
                    Math.Max(0, Math.Min(_scrollMonWa.Width, _dragStart.X - _scrollMonLocal.X)),
                    Math.Max(0, Math.Min(_scrollMonWa.Height, _dragStart.Y - _scrollMonLocal.Y)));
            }
            _liveScrollActive = true;
            _finishingLiveScroll = false;

            Point tx = GetSafeScrollTargetScreenPoint();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            _scrollTargetRoot = NativeMethods.GetTopScrollTargetRoot(tx.X, tx.Y, pid);

            InstallMouseReleaseHook();

            // Release OS logical VK_LBUTTON state once so Chrome and Explorer process WM_MOUSEWHEEL cleanly during drag
            if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0)
            {
                _syntheticLeftUpSent = true;
                NativeMethods.ReleaseCapture();
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                NativeMethods.SetCapture(Handle);
            }

            _topToolbar.Visible = false;
            _confirmActionBar.Visible = false;
            Cursor = Cursors.Cross;
            RecalcLiveScrollSelection(_lastMouse);
            _liveCaptureTimer.Start();
        }

        private void StartOrContinueLiveScroll(int deltaY, int deltaX, bool _unused = false, bool __unused = false, bool ___unused = false)
        {
            if (IsDisposed || _finishingLiveScroll) return;
            if (deltaY != 0 && deltaX == 0)
            {
                _lastScrollHintY = deltaY < 0 ? 1 : -1;
                _lastScrollHintX = 0;
            }
            else if (deltaX != 0 && deltaY == 0)
            {
                _lastScrollHintX = deltaX > 0 ? 1 : -1;
                _lastScrollHintY = 0;
            }
            else
            {
                if (deltaY != 0) _lastScrollHintY = deltaY < 0 ? 1 : -1;
                if (deltaX != 0) _lastScrollHintX = deltaX > 0 ? 1 : -1;
            }
            _scrollActivityDeadlineTick = Environment.TickCount + 340;

            bool wasActive = _liveScrollActive;
            EnsureLiveScrollInitialized();

            Point tx = GetSafeScrollTargetScreenPoint();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            if (_scrollTargetRoot == IntPtr.Zero)
                _scrollTargetRoot = NativeMethods.GetTopScrollTargetRoot(tx.X, tx.Y, pid);

            if ((!wasActive && _syntheticLeftUpSent) || _pendingFirstLiveWheelTimer.Enabled)
            {
                // Delay Notch 1 briefly (~28ms) so Windows drains raw MOUSEEVENTF_LEFTUP (VK_LBUTTON == 0)
                // before Chrome/Explorer handles WM_MOUSEWHEEL.
                _pendingFirstLiveWheelDy += deltaY;
                _pendingFirstLiveWheelDx += deltaX;
                _pendingFirstWheelTarget = tx;
                if (!_pendingFirstLiveWheelTimer.Enabled)
                    _pendingFirstLiveWheelTimer.Start();
            }
            else
            {
                NativeMethods.ScrollWindowBelowPoint(tx.X, tx.Y, deltaY, deltaX, pid, Handle);
            }
        }

        private void OnFlushPendingFirstLiveWheel(object sender, EventArgs e)
        {
            _pendingFirstLiveWheelTimer.Stop();
            int dy = _pendingFirstLiveWheelDy;
            int dx = _pendingFirstLiveWheelDx;
            _pendingFirstLiveWheelDy = 0;
            _pendingFirstLiveWheelDx = 0;
            if (IsDisposed || _finishingLiveScroll || !_liveScrollActive || (dy == 0 && dx == 0))
                return;

            _scrollActivityDeadlineTick = Environment.TickCount + 420;
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            NativeMethods.ScrollWindowBelowPoint(_pendingFirstWheelTarget.X, _pendingFirstWheelTarget.Y, dy, dx, pid, Handle);
        }

        private void OnLiveCaptureTick(object sender, EventArgs e)
        {
            if (!_liveScrollActive || _finishingLiveScroll || _scrollCanvas == null || IsDisposed)
            {
                _liveCaptureTimer.Stop();
                return;
            }
            if (unchecked(Environment.TickCount - _scrollActivityDeadlineTick) <= 280)
            {
                CaptureAndApplyLiveScrollFrame();
            }
        }

        private void CaptureAndApplyLiveScrollFrame()
        {
            if (!_liveScrollActive || _scrollCanvas == null || IsDisposed || _isLiveCapturing) return;
            _isLiveCapturing = true;
            try
            {
                Bitmap fresh = null;
                if (NativeMethods.SupportsHardwareExclude)
                {
                    fresh = ScreenCaptureService.CaptureRegionFast(_scrollMonWa);
                }
                if (fresh == null && _scrollTargetRoot != IntPtr.Zero)
                {
                    fresh = ScreenCaptureService.CaptureMonitorWithWindowPrint(_screenBitmap, _virtualBounds, _scrollMonWa, _scrollTargetRoot);
                }
                if (fresh == null) return;

                using (fresh)
                {
                    bool visualChanged = VirtualScrollCanvas.HasSignificantVisualChange(_lastLiveFreshBmp, fresh);
                    if (!visualChanged) return;

                    int oldVx = _scrollCanvas.ViewX, oldVy = _scrollCanvas.ViewY;
                    int signY = _lastScrollHintY;
                    int signX = _lastScrollHintX;
                    bool moved = _scrollCanvas.ApplyViewportFrameWithFallback(
                        fresh, signY, signX,
                        out _, out _, out int pL, out int pT,
                        fallbackAdvanceY: 0,
                        fallbackAdvanceX: 0);

                    if (moved)
                    {
                        if (pT > 0) _docDragStart.Y += pT;
                        if (pL > 0) _docDragStart.X += pL;
                        SyncAnnotationsToCanvas(_scrollCanvas.ViewX, _scrollCanvas.ViewY, pL, pT);
                    }

                    // Always render exact live fresh window pixels to the overlay bitmaps so the visible
                    // screen never freezes or diverges behind stale stitched tiles during smooth/grid/inner scrolling.
                    _lastLiveFreshBmp?.Dispose();
                    _lastLiveFreshBmp = (Bitmap)fresh.Clone();
                    StampFreshFrameToOverlay(fresh, _scrollMonLocal);
                    RecalcLiveScrollSelection(_lastMouse);
                    _scrollActivityDeadlineTick = Environment.TickCount + 220;
                    Invalidate();
                    Update();
                }
            }
            finally
            {
                _isLiveCapturing = false;
            }
        }

        private void InstallEscapeKeyboardHook()
        {
            if (_kbHook != IntPtr.Zero) return;
            _kbHookProc = GlobalKeyboardHookCallback;
            using var mod = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            IntPtr hMod = NativeMethods.GetModuleHandle(mod?.ModuleName);
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbHookProc, hMod, 0);
        }

        private void InstallMouseReleaseHook()
        {
            if (_mouseHook != IntPtr.Zero) return;
            _mouseHookProc = GlobalMouseHookCallback;
            using var mod = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            IntPtr hMod = NativeMethods.GetModuleHandle(mod?.ModuleName);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, hMod, 0);
        }

        private void UninstallHooks()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_kbHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
            }
        }

        private IntPtr GlobalMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Visible && !_finishingLiveScroll && !IsDisposed)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_MOUSEWHEEL || msg == NativeMethods.WM_MOUSEHWHEEL)
                {
                    var ms = (NativeMethods.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    if (_virtualBounds.Contains(ms.pt.X, ms.pt.Y))
                    {
                        short delta = (short)((ms.mouseData >> 16) & 0xFFFF);
                        if (delta != 0)
                        {
                            bool hwheel = msg == NativeMethods.WM_MOUSEHWHEEL;
                            bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
                            int dy = (!hwheel && !shift) ? delta : 0;
                            int dx = hwheel ? delta : (shift ? -delta : 0);
                            Point ptCopy = new Point(ms.pt.X, ms.pt.Y);
                            try { BeginInvoke(new Action(() => HandleOverlayMouseWheel(dy, dx, ptCopy))); }
                            catch { }
                            return (IntPtr)1;
                        }
                    }
                }
                else if (_liveScrollActive && (msg == NativeMethods.WM_LBUTTONUP || msg == NativeMethods.WM_RBUTTONDOWN))
                {
                    var ms = (NativeMethods.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    bool injected = (ms.flags & 0x01) != 0;

                    if (msg == NativeMethods.WM_LBUTTONUP && !injected)
                    {
                        var ptCopy = ms.pt;
                        try { BeginInvoke(new Action(() => FinishLiveScrollCapture(new Point(ptCopy.X, ptCopy.Y)))); }
                        catch { }
                        return (IntPtr)1;
                    }
                    if (msg == NativeMethods.WM_RBUTTONDOWN && !injected)
                    {
                        try { BeginInvoke(new Action(CancelAndClose)); }
                        catch { }
                        return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr GlobalKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_finishingLiveScroll && !IsDisposed)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var kb = (NativeMethods.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    int vk = (int)kb.vkCode;
                    if (vk == (int)Keys.Escape)
                    {
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (_inlineTextEditing) CancelInlineText();
                                else CancelAndClose();
                            }));
                        }
                        catch { }
                        return (IntPtr)1;
                    }
                    if (_liveScrollActive && (vk == (int)Keys.Down || vk == (int)Keys.Up || vk == (int)Keys.Left || vk == (int)Keys.Right))
                    {
                        int dy = vk == (int)Keys.Down ? -180 : (vk == (int)Keys.Up ? 180 : 0);
                        int dx = vk == (int)Keys.Right ? 120 : (vk == (int)Keys.Left ? -120 : 0);
                        try { BeginInvoke(new Action(() => StartOrContinueLiveScroll(dy, dx))); }
                        catch { }
                        return (IntPtr)1;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void FinishLiveScrollCapture(Point ptScreen)
        {
            if (_finishingLiveScroll || !_liveScrollActive || IsDisposed) return;
            _finishingLiveScroll = true;
            _isMouseDown = false;
            _isSpaceDown = false;

            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _liveCaptureTimer.Stop();
            NativeMethods.ReleaseCapture();

            if (_pendingFirstLiveWheelTimer.Enabled)
                OnFlushPendingFirstLiveWheel(this, EventArgs.Empty);

            if (unchecked(_scrollActivityDeadlineTick - Environment.TickCount) > 60)
                System.Threading.Thread.Sleep(75);

            CaptureAndApplyLiveScrollFrame();
            Point curClient = PointToClient(ptScreen);
            RecalcLiveScrollSelection(curClient);

            Rectangle finalDoc = Rectangle.Round(_docSelRect);
            Bitmap output = null;
            if (finalDoc.Width > 5 && finalDoc.Height > 5 && _scrollCanvas != null)
            {
                bool fitsInLiveViewport =
                    finalDoc.Left >= _scrollCanvas.ViewX &&
                    finalDoc.Top >= _scrollCanvas.ViewY &&
                    finalDoc.Right <= _scrollCanvas.ViewX + _scrollMonWa.Width &&
                    finalDoc.Bottom <= _scrollCanvas.ViewY + _scrollMonWa.Height;

                if (fitsInLiveViewport)
                {
                    var liveSrc = new Rectangle(
                        _scrollMonLocal.X + (finalDoc.X - _scrollCanvas.ViewX),
                        _scrollMonLocal.Y + (finalDoc.Y - _scrollCanvas.ViewY),
                        finalDoc.Width,
                        finalDoc.Height);
                    output = ScreenCaptureService.CropBitmap(_screenBitmap, liveSrc);
                }
                if (output == null)
                    output = _scrollCanvas.CropDocumentRect(finalDoc);
            }

            Rectangle scrRect = Rectangle.Empty;
            if (output != null && _scrollCanvas != null)
            {
                scrRect = new Rectangle(
                    _scrollMonWa.X + Math.Max(0, finalDoc.X - _scrollCanvas.ViewX),
                    _scrollMonWa.Y + Math.Max(0, finalDoc.Y - _scrollCanvas.ViewY),
                    output.Width,
                    output.Height);

                if (_annotations.Count > 0)
                {
                    var docAnnos = new List<AnnotationItem>(_annotations.Count);
                    float offX = _scrollMonLocal.X + finalDoc.X - _scrollCanvas.ViewX;
                    float offY = _scrollMonLocal.Y + finalDoc.Y - _scrollCanvas.ViewY;
                    foreach (var a in _annotations)
                    {
                        var clone = a.Clone();
                        clone.Translate(-offX, -offY);
                        docAnnos.Add(clone);
                    }
                    using var g = Graphics.FromImage(output);
                    AnnotationRenderer.RenderAnnotations(g, docAnnos, output, -1, PointF.Empty);
                }
            }

            ResetLiveScroll();
            Hide();

            if (output != null)
            {
                CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
                {
                    Mode = OverlayCaptureMode.Region,
                    ScreenRect = scrRect,
                    CapturedBitmap = output
                });
            }
            else
            {
                CaptureCancelled?.Invoke(this, EventArgs.Empty);
            }
            Close();
        }



        private void StartOrContinuePreScroll(int deltaY, int deltaX, Point scrPt)
        {
            if (IsDisposed || _isMouseDown) return;
            try
            {
                Screen mon = Screen.FromPoint(scrPt);
                Rectangle monWa = mon.WorkingArea;
                Rectangle localWa = new Rectangle(monWa.X - _virtualBounds.X, monWa.Y - _virtualBounds.Y, monWa.Width, monWa.Height);

                Point clientPt = PointToClient(scrPt);
                bool onUiBar = _topToolbar.Bounds.Contains(clientPt) ||
                               (_confirmActionBar.Visible && _confirmActionBar.Bounds.Contains(clientPt));
                int minTy = onUiBar ? monWa.Top + Math.Max(140, _topToolbar.Height + 64) : monWa.Top + 48;
                int tx = Math.Max(monWa.Left + 24, Math.Min(monWa.Right - 24, scrPt.X));
                int ty = Math.Max(minTy, Math.Min(monWa.Bottom - 48, scrPt.Y));

                if (_preScrollCanvas == null || _preScrollMonWa != monWa)
                {
                    _preScrollCanvas?.Dispose();
                    _preScrollKeyframes.Clear();
                    _preScrollMonWa = monWa;
                    _preScrollMonLocal = localWa;

                    Rectangle clip = Rectangle.Intersect(new Rectangle(0, 0, _screenBitmap.Width, _screenBitmap.Height), localWa);
                    using Bitmap initSlice = _screenBitmap.Clone(clip, PixelFormat.Format32bppArgb);
                    _preScrollCanvas = new VirtualScrollCanvas(monWa, initSlice);
                    _lastPreFreshBmp?.Dispose();
                    _lastPreFreshBmp = (Bitmap)initSlice.Clone();
                    _preScrollKeyframes.Add((EncodePngBytes(initSlice), 0, 0));
                    _lastKeyframeViewX = 0;
                    _lastKeyframeViewY = 0;
                }

                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                _preScrollTargetRoot = NativeMethods.GetTopScrollTargetRoot(tx, ty, pid);

                _preScrollHintY = deltaY == 0 ? 0 : (deltaY < 0 ? 1 : -1);
                _preScrollHintX = deltaX == 0 ? 0 : (deltaX > 0 ? 1 : -1);

                NativeMethods.ScrollWindowBelowPoint(tx, ty, deltaY, deltaX, pid, Handle);
                _preScrollDeadlineTick = Environment.TickCount + 450;
                if (!_preScrollPollTimer.Enabled)
                    _preScrollPollTimer.Start();
            }
            catch { }
        }

        private void OnPreScrollPollTick(object sender, EventArgs e)
        {
            if (IsDisposed || _isMouseDown || _liveScrollActive || _preScrollCanvas == null)
            {
                _preScrollPollTimer.Stop();
                return;
            }
            if (unchecked(Environment.TickCount - _preScrollDeadlineTick) > 0)
            {
                _preScrollPollTimer.Stop();
                return;
            }
            if (_isPreScrollPolling) return;
            _isPreScrollPolling = true;
            try
            {
                Bitmap fresh = null;
                if (NativeMethods.SupportsHardwareExclude)
                {
                    fresh = ScreenCaptureService.CaptureRegionFast(_preScrollMonWa);
                }
                if (fresh == null && _preScrollTargetRoot != IntPtr.Zero)
                {
                    fresh = ScreenCaptureService.CaptureMonitorWithWindowPrint(_screenBitmap, _virtualBounds, _preScrollMonWa, _preScrollTargetRoot);
                }
                if (fresh == null) return;

                using (fresh)
                {
                    int oldVx = _preScrollCanvas.ViewX;
                    int oldVy = _preScrollCanvas.ViewY;
                    bool moved = _preScrollCanvas.ApplyViewportFrameWithFallback(
                        fresh, _preScrollHintY, _preScrollHintX,
                        out _, out _, out int pL, out int pT,
                        fallbackAdvanceY: 0,
                        fallbackAdvanceX: 0);

                    if (moved)
                    {
                        SyncAnnotationsToCanvas(_preScrollCanvas.ViewX, _preScrollCanvas.ViewY, pL, pT);
                        RecordPreScrollKeyframe(fresh, _preScrollHintY, _preScrollHintX, pL, pT);
                        _lastPreFreshBmp?.Dispose();
                        _lastPreFreshBmp = (Bitmap)fresh.Clone();
                        StampFreshFrameToOverlay(fresh, _preScrollMonLocal);
                        _preScrollDeadlineTick = Math.Max(_preScrollDeadlineTick, Environment.TickCount + 280);
                        Invalidate();
                        Update();
                    }
                }
            }
            catch { }
            finally
            {
                _isPreScrollPolling = false;
            }
        }

        private void SyncAnnotationsToCanvas(float viewX, float viewY, float prependX = 0, float prependY = 0)
        {
            foreach (var a in _annotations)
            {
                a.EnsureDocCoords(viewX, viewY);
                if (prependX > 0 || prependY > 0) a.ApplyDocShift(prependX, prependY);
                a.SyncToViewport(viewX, viewY);
            }
            foreach (var a in _redoStack)
            {
                a.EnsureDocCoords(viewX, viewY);
                if (prependX > 0 || prependY > 0) a.ApplyDocShift(prependX, prependY);
                a.SyncToViewport(viewX, viewY);
            }
            if (_activeAnnotation != null)
            {
                _activeAnnotation.EnsureDocCoords(viewX, viewY);
                if (prependX > 0 || prependY > 0) _activeAnnotation.ApplyDocShift(prependX, prependY);
                _activeAnnotation.SyncToViewport(viewX, viewY);
            }
        }

        private void ShiftAllAnnotations(float shiftX, float shiftY)
        {
            if (Math.Abs(shiftX) < 0.01f && Math.Abs(shiftY) < 0.01f) return;
            foreach (var a in _annotations) a.Translate(shiftX, shiftY);
            foreach (var a in _redoStack) a.Translate(shiftX, shiftY);
            _activeAnnotation?.Translate(shiftX, shiftY);
            if (_inlineTextEditing)
            {
                _inlineTextPos = new Point(
                    _inlineTextPos.X + (int)Math.Round(shiftX),
                    _inlineTextPos.Y + (int)Math.Round(shiftY));
            }
        }

        private void RecordPreScrollKeyframe(Bitmap frame, int hintY, int hintX, int pL, int pT)
        {
            if (_preScrollCanvas == null) return;
            if (pL > 0) _lastKeyframeViewX += pL;
            if (pT > 0) _lastKeyframeViewY += pT;

            int dist = Math.Abs(_preScrollCanvas.ViewX - _lastKeyframeViewX) +
                       Math.Abs(_preScrollCanvas.ViewY - _lastKeyframeViewY);
            byte[] png = EncodePngBytes(frame);
            var lastKf = _preScrollKeyframes.Count > 0 ? _preScrollKeyframes[_preScrollKeyframes.Count - 1] : default;
            if (_preScrollKeyframes.Count > 1 && dist < 110 && lastKf.HintY == hintY && lastKf.HintX == hintX)
            {
                _preScrollKeyframes[_preScrollKeyframes.Count - 1] = (png, hintY, hintX);
            }
            else
            {
                if (_preScrollKeyframes.Count >= 28)
                    _preScrollKeyframes.RemoveAt(1); // Keep initial frame #0 and newest history bounded
                _preScrollKeyframes.Add((png, hintY, hintX));
                _lastKeyframeViewX = _preScrollCanvas.ViewX;
                _lastKeyframeViewY = _preScrollCanvas.ViewY;
            }
        }

        private static byte[] EncodePngBytes(Bitmap bmp)
        {
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private void StampFreshFrameToOverlay(Bitmap fresh, Rectangle localRect)
        {
            if (fresh == null) return;
            using (var g = Graphics.FromImage(_screenBitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(fresh, localRect.X, localRect.Y);
            }
            UpdateOverlayBitmapSlice(fresh, localRect);
        }

        private void UpdateOverlayBitmapSlice(Bitmap slice, Rectangle localRect)
        {
            if (_fastScreenBmp != null)
            {
                using var g = Graphics.FromImage(_fastScreenBmp);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(slice, localRect.X, localRect.Y);
            }
            if (_fastDimmedBmp != null)
            {
                using var g = Graphics.FromImage(_fastDimmedBmp);
                g.CompositingMode = CompositingMode.SourceCopy;
                var cm = new ColorMatrix(new[]
                {
                    new float[] { 0.72f, 0, 0, 0, 0 },
                    new float[] { 0, 0.72f, 0, 0, 0 },
                    new float[] { 0, 0, 0.72f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                });
                using var ia = new ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(slice, localRect, 0, 0, slice.Width, slice.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private void RecalcLiveScrollSelection(Point clientPt)
        {
            if (_scrollCanvas == null) return;
            int monRelX = clientPt.X - _scrollMonLocal.X;
            int monRelY = clientPt.Y - _scrollMonLocal.Y;
            float curDocX = _scrollCanvas.ViewX + monRelX;
            float curDocY = _scrollCanvas.ViewY + monRelY;

            _docSelRect = new RectangleF(
                Math.Min(_docDragStart.X, curDocX),
                Math.Min(_docDragStart.Y, curDocY),
                Math.Abs(curDocX - _docDragStart.X),
                Math.Abs(curDocY - _docDragStart.Y));

            float vx1 = _scrollMonLocal.X + (_docSelRect.Left - _scrollCanvas.ViewX);
            float vy1 = _scrollMonLocal.Y + (_docSelRect.Top - _scrollCanvas.ViewY);
            float vx2 = _scrollMonLocal.X + (_docSelRect.Right - _scrollCanvas.ViewX);
            float vy2 = _scrollMonLocal.Y + (_docSelRect.Bottom - _scrollCanvas.ViewY);

            float cvx1 = Math.Max(_scrollMonLocal.Left, Math.Min(_scrollMonLocal.Right, vx1));
            float cvy1 = Math.Max(_scrollMonLocal.Top, Math.Min(_scrollMonLocal.Bottom, vy1));
            float cvx2 = Math.Max(_scrollMonLocal.Left, Math.Min(_scrollMonLocal.Right, vx2));
            float cvy2 = Math.Max(_scrollMonLocal.Top, Math.Min(_scrollMonLocal.Bottom, vy2));
            _selectionRect = new RectangleF(cvx1, cvy1, Math.Max(2, cvx2 - cvx1), Math.Max(2, cvy2 - cvy1));
        }

        private void ResetLiveScroll()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _liveCaptureTimer.Stop();
            _pendingFirstLiveWheelTimer.Stop();
            _pendingFirstLiveWheelDy = 0;
            _pendingFirstLiveWheelDx = 0;
            _liveScrollActive = false;
            _finishingLiveScroll = false;
            _isLiveCapturing = false;
            _syntheticLeftUpSent = false;
            _lastScrollHintY = 0;
            _lastScrollHintX = 0;
            _scrollActivityDeadlineTick = 0;
            _scrollTargetRoot = IntPtr.Zero;
            _docSelRect = RectangleF.Empty;
            _lastLiveFreshBmp?.Dispose();
            _lastLiveFreshBmp = null;
            _scrollCanvas?.Dispose();
            _scrollCanvas = null;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && _selectionRect.HasValue && _selectionRect.Value.Width > 8 && _selectionRect.Value.Height > 8)
            {
                if (_mode == OverlayCaptureMode.Region) FinalizeRegionCapture();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_isMouseDown || e.Button != MouseButtons.Left) return;

            if (_liveScrollActive && _scrollCanvas != null)
            {
                if (_mouseHook != IntPtr.Zero)
                    return; // Real hardware release is handled strictly via WH_MOUSE_LL (!injected)
                FinishLiveScrollCapture(PointToScreen(e.Location));
                return;
            }

            _isMouseDown = false;
            _isSpaceDown = false;

            if (_isResizingBox)
            {
                _isResizingBox = false;
                _resizeHandle = null;
                PositionConfirmBar();
                Invalidate();
                return;
            }

            if (_isMovingBox)
            {
                _isMovingBox = false;
                PositionConfirmBar();
                Invalidate();
                return;
            }

            if (_isMovingAnnotation)
            {
                _isMovingAnnotation = false;
                Cursor = Cursors.Default;
                if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5) PositionConfirmBar();
                Invalidate();
                return;
            }

            if (_isDrawing && _activeAnnotation != null)
            {
                float curVx = _scrollCanvas?.ViewX ?? _preScrollCanvas?.ViewX ?? 0;
                float curVy = _scrollCanvas?.ViewY ?? _preScrollCanvas?.ViewY ?? 0;
                _activeAnnotation.EnsureDocCoords(curVx, curVy);
                _annotations.Add(_activeAnnotation);
                _redoStack.Clear();
                _activeAnnotation = null;
                _isDrawing = false;
                RecalcStepCounter();
                if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5) PositionConfirmBar();
                Invalidate();
                return;
            }

            if (_currentTool == null && _hasDragged && _selectionRect.HasValue &&
                _selectionRect.Value.Width > 5 && _selectionRect.Value.Height > 5)
            {
                if (_mode == OverlayCaptureMode.Ocr)
                {
                    FinalizeOcrSelection();
                }
                else if (_mode == OverlayCaptureMode.Gif)
                {
                    FinalizeGifSelection();
                }
                else if (_mode == OverlayCaptureMode.Scrolling)
                {
                    FinalizeScrollingSelection();
                }
                else if (_quickCapture)
                {
                    FinalizeRegionCapture();
                }
                else
                {
                    PositionConfirmBar();
                    Invalidate();
                }
                return;
            }

            _selectionRect = null;
            _freePoints.Clear();
            _topToolbar.Visible = true;
            Invalidate();
        }

        private void PositionConfirmBar()
        {
            if (!_selectionRect.HasValue || _quickCapture)
            {
                _confirmActionBar.Visible = false;
                return;
            }
            var r = _selectionRect.Value;
            Screen mon = Screen.FromPoint(PointToScreen(new Point((int)r.Right, (int)r.Bottom)));
            Rectangle wa = new Rectangle(mon.WorkingArea.X - _virtualBounds.X, mon.WorkingArea.Y - _virtualBounds.Y, mon.WorkingArea.Width, mon.WorkingArea.Height);
            int bx = (int)Math.Max(wa.Left + 8, Math.Min(wa.Right - _confirmActionBar.Width - 8, r.Right - _confirmActionBar.Width));
            int by = (int)(r.Bottom + 10);
            if (by + _confirmActionBar.Height > wa.Bottom - 8)
                by = (int)Math.Max(wa.Top + 8, r.Top - _confirmActionBar.Height - 10);
            _confirmActionBar.Location = new Point(bx, by);
            _confirmActionBar.Visible = true;
            _confirmActionBar.BringToFront();
        }

        private void FinalizeRegionCapture()
        {
            var box = _selectionRect ?? new RectangleF(0, 0, Width, Height);
            Rectangle localRect = Rectangle.Intersect(
                new Rectangle(0, 0, Width, Height),
                Rectangle.Round(box));
            if (localRect.Width < 2 || localRect.Height < 2) return;

            Bitmap output = BuildCompositedCrop(localRect);
            Rectangle screenRect = new Rectangle(
                _virtualBounds.X + localRect.X,
                _virtualBounds.Y + localRect.Y,
                localRect.Width,
                localRect.Height);

            Hide();
            CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
            {
                Mode = OverlayCaptureMode.Region,
                ScreenRect = screenRect,
                CapturedBitmap = output
            });
            Close();
        }



        private void FinalizeOcrSelection()
        {
            if (!_selectionRect.HasValue) return;
            Rectangle localRect = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Round(_selectionRect.Value));
            if (localRect.Width < 4 || localRect.Height < 4) return;
            Bitmap crop = BuildCompositedCrop(localRect);
            Hide();
            CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
            {
                Mode = OverlayCaptureMode.Ocr,
                ScreenRect = new Rectangle(_virtualBounds.X + localRect.X, _virtualBounds.Y + localRect.Y, localRect.Width, localRect.Height),
                CapturedBitmap = crop
            });
            Close();
        }



        private void FinalizeGifSelection()
        {
            if (!_selectionRect.HasValue) return;
            Rectangle localRect = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Round(_selectionRect.Value));
            if (localRect.Width < 12 || localRect.Height < 12) return;
            Rectangle screenRect = new Rectangle(_virtualBounds.X + localRect.X, _virtualBounds.Y + localRect.Y, localRect.Width, localRect.Height);
            Hide();
            CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
            {
                Mode = OverlayCaptureMode.Gif,
                ScreenRect = screenRect
            });
            Close();
        }





        private (List<PreScrollFrameData> Frames, List<AnnotationItem> Annotations) BuildScrollSessionSeed(Rectangle localRect)
        {
            _preScrollPollTimer.Stop();
            List<PreScrollFrameData> frames = null;
            if (_preScrollCanvas != null && _preScrollKeyframes.Count > 0)
            {
                Rectangle rel = Rectangle.Intersect(
                    new Rectangle(0, 0, _preScrollMonWa.Width, _preScrollMonWa.Height),
                    new Rectangle(localRect.X - _preScrollMonLocal.X, localRect.Y - _preScrollMonLocal.Y, localRect.Width, localRect.Height));

                if (rel.Width >= 10 && rel.Height >= 10)
                {
                    frames = new List<PreScrollFrameData>(_preScrollKeyframes.Count + 1);
                    foreach (var kf in _preScrollKeyframes)
                    {
                        using var ms = new System.IO.MemoryStream(kf.Png);
                        using var fullBmp = new Bitmap(ms);
                        Rectangle safeRel = Rectangle.Intersect(new Rectangle(0, 0, fullBmp.Width, fullBmp.Height), rel);
                        if (safeRel.Width >= 10 && safeRel.Height >= 10)
                        {
                            frames.Add(new PreScrollFrameData
                            {
                                Frame = fullBmp.Clone(safeRel, PixelFormat.Format32bppArgb),
                                HintY = kf.HintY,
                                HintX = kf.HintX
                            });
                        }
                    }

                    Rectangle safeScreenLocal = Rectangle.Intersect(new Rectangle(0, 0, _screenBitmap.Width, _screenBitmap.Height), localRect);
                    if (safeScreenLocal.Width >= 10 && safeScreenLocal.Height >= 10)
                    {
                        frames.Add(new PreScrollFrameData
                        {
                            Frame = _screenBitmap.Clone(safeScreenLocal, PixelFormat.Format32bppArgb),
                            HintY = _preScrollHintY,
                            HintX = _preScrollHintX
                        });
                    }
                }
            }

            List<AnnotationItem> docAnnos = null;
            if (_annotations.Count > 0)
            {
                docAnnos = new List<AnnotationItem>(_annotations.Count);
                float curVx = _scrollCanvas?.ViewX ?? _preScrollCanvas?.ViewX ?? 0;
                float curVy = _scrollCanvas?.ViewY ?? _preScrollCanvas?.ViewY ?? 0;
                float relX = localRect.X - (_preScrollMonLocal.Width > 0 ? _preScrollMonLocal.X : 0);
                float relY = localRect.Y - (_preScrollMonLocal.Height > 0 ? _preScrollMonLocal.Y : 0);

                foreach (var a in _annotations)
                {
                    a.EnsureDocCoords(curVx, curVy);
                    var clone = a.Clone();
                    clone.X1 = clone.DocX1 - relX;
                    clone.Y1 = clone.DocY1 - relY;
                    clone.X2 = clone.DocX2 - relX;
                    clone.Y2 = clone.DocY2 - relY;
                    clone.X = clone.DocX - relX;
                    clone.Y = clone.DocY - relY;
                    if (clone.DocPoints != null && clone.DocPoints.Count > 0)
                    {
                        clone.Points = new List<PointF>(clone.DocPoints.Count);
                        foreach (var dp in clone.DocPoints)
                            clone.Points.Add(new PointF(dp.X - relX, dp.Y - relY));
                    }
                    docAnnos.Add(clone);
                }
            }

            return (frames, docAnnos);
        }

        private void FinalizeScrollingSelection()
        {
            if (!_selectionRect.HasValue) return;
            Rectangle localRect = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Round(_selectionRect.Value));
            if (localRect.Width < 20 || localRect.Height < 20) return;
            Rectangle screenRect = new Rectangle(_virtualBounds.X + localRect.X, _virtualBounds.Y + localRect.Y, localRect.Width, localRect.Height);
            var (frames, docAnnos) = BuildScrollSessionSeed(localRect);
            Hide();
            CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
            {
                Mode = OverlayCaptureMode.Scrolling,
                ScreenRect = screenRect,
                PreScrollFrames = frames,
                DocAnnotations = docAnnos
            });
            Close();
        }



        private void FinalizeLiveScrollRegionSelection(int initialWheelDelta)
        {
            if (!_selectionRect.HasValue) return;
            Rectangle localRect = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Round(_selectionRect.Value));
            if (localRect.Width < 20 || localRect.Height < 20) return;

            Rectangle screenRect = new Rectangle(_virtualBounds.X + localRect.X, _virtualBounds.Y + localRect.Y, localRect.Width, localRect.Height);
            // Exclude Windows Taskbar when scroll-extending a capture region started above Taskbar
            Screen scr = Screen.FromRectangle(screenRect);
            if (screenRect.Y < scr.WorkingArea.Bottom - 20 && screenRect.Bottom > scr.WorkingArea.Bottom)
            {
                int clampedH = Math.Max(20, scr.WorkingArea.Bottom - screenRect.Y);
                localRect.Height = clampedH;
                screenRect.Height = clampedH;
            }

            var (frames, docAnnos) = BuildScrollSessionSeed(localRect);
            Bitmap firstFrame = BuildCompositedCrop(localRect);
            Hide();
            CaptureCompleted?.Invoke(this, new OverlayCompletionEventArgs
            {
                Mode = OverlayCaptureMode.LiveScrollRegion,
                ScreenRect = screenRect,
                CapturedBitmap = firstFrame,
                InitialScrollDelta = initialWheelDelta,
                PreScrollFrames = frames,
                DocAnnotations = docAnnos
            });
            Close();
        }



        private Bitmap BuildCompositedCrop(Rectangle localRect)
        {
            var outBmp = new Bitmap(localRect.Width, localRect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outBmp))
            {
                if (_selectMode == "free" && _freePoints.Count > 2)
                {
                    g.Clear(Color.Transparent);
                    using var path = new GraphicsPath();
                    path.AddPolygon(_freePoints.Select(p => new PointF(p.X - localRect.X, p.Y - localRect.Y)).ToArray());
                    g.SetClip(path);
                }
                g.DrawImage(_screenBitmap, new Rectangle(0, 0, localRect.Width, localRect.Height), localRect, GraphicsUnit.Pixel);

                if (_annotations.Count > 0)
                {
                    AnnotationRenderer.RenderAnnotations(g, _annotations, _screenBitmap, -1, new PointF(localRect.X, localRect.Y));
                }
            }
            return outBmp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            bool hasActiveSelection = _selectionRect.HasValue && (_hasDragged || _isMouseDown || !_quickCapture) &&
                                      _selectionRect.Value.Width > 1 && _selectionRect.Value.Height > 1;

            if (!hasActiveSelection)
            {
                g.DrawImageUnscaled(_fastScreenBmp ?? _screenBitmap, 0, 0);
            }
            else
            {
                var r = _selectionRect.Value;
                g.DrawImageUnscaled(_fastDimmedBmp ?? _screenBitmap, 0, 0);

                if (_selectMode == "free" && _freePoints.Count > 2 && _mode == OverlayCaptureMode.Region)
                {
                    var state = g.Save();
                    using (var clipPath = new GraphicsPath())
                    {
                        clipPath.AddPolygon(_freePoints.ToArray());
                        g.SetClip(clipPath);
                        Rectangle freeBounds = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Ceiling(r));
                        if (freeBounds.Width > 0 && freeBounds.Height > 0)
                            g.DrawImage(_fastScreenBmp ?? _screenBitmap, freeBounds, freeBounds, GraphicsUnit.Pixel);
                    }
                    g.Restore(state);
                }
                else
                {
                    Rectangle selBounds = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), Rectangle.Round(r));
                    if (selBounds.Width > 0 && selBounds.Height > 0)
                        g.DrawImage(_fastScreenBmp ?? _screenBitmap, selBounds, selBounds, GraphicsUnit.Pixel);
                }
            }

            // Render annotations on the frozen screen
            if (_annotations.Count > 0 || _activeAnnotation != null)
            {
                var list = new List<AnnotationItem>(_annotations);
                if (_activeAnnotation != null) list.Add(_activeAnnotation);
                AnnotationRenderer.RenderAnnotations(g, list, _screenBitmap,
                    _currentTool == "select" ? _selectedAnnotationIndex : -1, PointF.Empty);
            }

            // Transparent direct on-canvas text preview & blinking caret (no opaque black box)
            if (_inlineTextEditing && _inlineTextBox != null)
            {
                DrawTransparentInlineText(g);
            }

            // Crisp Cyan solid selection border, handles & dimensions pill (zero dashlines)
            if (hasActiveSelection)
            {
                var r = _selectionRect.Value;
                using (var solidPen = new Pen(Color.FromArgb(0, 229, 255), 1.0f))
                {
                    if (_selectMode == "free" && _freePoints.Count > 2 && _mode == OverlayCaptureMode.Region)
                    {
                        g.DrawPolygon(solidPen, _freePoints.ToArray());
                    }
                    else
                    {
                        g.DrawRectangle(solidPen, r.X, r.Y, r.Width, r.Height);
                    }
                }

                if (!_quickCapture && !_isMouseDown && _selectMode != "free")
                {
                    DrawResizeHandles(g, r);
                }

                DrawDimensionsPill(g, r);
            }

            // Mode Hint Pill at bottom center (OCR / GIF / Scrolling only — zero clutter in Region mode)
            if (_mode != OverlayCaptureMode.Region && !_isMouseDown && _currentTool == null)
            {
                string hint = _mode == OverlayCaptureMode.Ocr
                    ? "Drag area to extract text (any language) • ESC to cancel"
                    : (_mode == OverlayCaptureMode.Gif
                        ? "Select Area to record GIF • ESC to cancel"
                        : "Drag scrollable area • ESC to cancel");
                DrawBottomHintPill(g, hint);
            }

            // ShareX-style Pixel Magnifier HUD when selecting region (not drawing annotations & not inside toolbar)
            if (_currentTool == null && !_topToolbar.Bounds.Contains(_lastMouse) &&
                (_isMouseDown || _mode == OverlayCaptureMode.Region || _mode == OverlayCaptureMode.Ocr ||
                 _mode == OverlayCaptureMode.Gif || _mode == OverlayCaptureMode.Scrolling))
            {
                if (!(!_quickCapture && _selectionRect.HasValue && !_isMouseDown))
                    DrawShareXPixelMagnifier(g, _lastMouse);
            }
        }

        private void DrawDimensionsPill(Graphics g, RectangleF r)
        {
            RectangleF metric = (_liveScrollActive && _docSelRect.Width > 1 && _docSelRect.Height > 1) ? _docSelRect : r;
            string text = $"{(int)Math.Round(metric.Width)} × {(int)Math.Round(metric.Height)} px";
            SizeF sz = g.MeasureString(text, _dimPillFont);
            int pw = (int)sz.Width + 12, ph = 22;
            int px = (int)Math.Max(6, Math.Min(Width - pw - 6, r.X));
            int py = (int)(r.Y - ph - 6);
            if (py < 6) py = (int)Math.Min(Height - ph - 6, r.Bottom + 6);

            var box = new Rectangle(px, py, pw, ph);
            using var bg = new SolidBrush(Color.FromArgb(232, 15, 14, 24));
            using var pen = new Pen(Color.Cyan, 1.1f);
            Theme.FillRoundedRect(g, bg, box, 4);
            Theme.DrawRoundedRect(g, pen, box, 4);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var txtBr = new SolidBrush(Color.FromArgb(170, 255, 255));
            g.DrawString(text, _dimPillFont, txtBr, box, sf);
        }

        private void DrawBottomHintPill(Graphics g, string text)
        {
            Screen mon = Screen.FromPoint(Cursor.Position);
            Rectangle mb = mon.Bounds;
            SizeF sz = g.MeasureString(text, _hintFont);
            int pw = (int)sz.Width + 28, ph = 30;
            int px = mb.X - _virtualBounds.X + (mb.Width - pw) / 2;
            int py = mb.Bottom - _virtualBounds.Y - ph - 26;
            var box = new Rectangle(px, py, pw, ph);

            using var bg = new SolidBrush(Color.FromArgb(238, 20, 18, 28));
            using var pen = new Pen(Theme.Brand, 1f);
            Theme.FillRoundedRect(g, bg, box, 15);
            Theme.DrawRoundedRect(g, pen, box, 15);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var br = new SolidBrush(Theme.TextPrimary);
            g.DrawString(text, _hintFont, br, box, sf);
        }

        private void DrawShareXPixelMagnifier(Graphics g, Point cursor)
        {
            const int gridCount = 15; // odd
            const int cell = 7;
            int magSize = gridCount * cell; // 105px
            int half = gridCount / 2;

            Screen mon = Screen.FromPoint(new Point(_virtualBounds.X + cursor.X, _virtualBounds.Y + cursor.Y));
            Rectangle mb = new Rectangle(mon.Bounds.X - _virtualBounds.X, mon.Bounds.Y - _virtualBounds.Y, mon.Bounds.Width, mon.Bounds.Height);

            int hudW = magSize + 10, hudH = magSize + 36;
            int ox = cursor.X + 20;
            int oy = cursor.Y + 20;
            if (ox + hudW > mb.Right - 8) ox = cursor.X - hudW - 20;
            if (oy + hudH > mb.Bottom - 8) oy = cursor.Y - hudH - 20;

            int cx = Math.Max(0, Math.Min(_screenBitmap.Width - 1, cursor.X));
            int cy = Math.Max(0, Math.Min(_screenBitmap.Height - 1, cursor.Y));
            Color centerCol = (!_liveScrollActive && _preScrollCanvas == null && _screenPixels != null)
                ? Color.FromArgb(_screenPixels[cy * _screenBitmap.Width + cx])
                : (_fastScreenBmp ?? _screenBitmap).GetPixel(cx, cy);

            if (_magZoomBmp == null || _magZoomBmp.Width != magSize)
                _magZoomBmp = new Bitmap(magSize, magSize, PixelFormat.Format32bppPArgb);

            using (var mg = Graphics.FromImage(_magZoomBmp))
            {
                mg.Clear(Color.Black);
                mg.InterpolationMode = InterpolationMode.NearestNeighbor;
                mg.PixelOffsetMode = PixelOffsetMode.Half;
                int sx = cx - half, sy = cy - half;
                Rectangle srcR = Rectangle.Intersect(new Rectangle(0, 0, _screenBitmap.Width, _screenBitmap.Height), new Rectangle(sx, sy, gridCount, gridCount));
                if (srcR.Width > 0 && srcR.Height > 0)
                {
                    int dx = (srcR.X - sx) * cell;
                    int dy = (srcR.Y - sy) * cell;
                    mg.DrawImage(_fastScreenBmp ?? _screenBitmap, new Rectangle(dx, dy, srcR.Width * cell, srcR.Height * cell), srcR, GraphicsUnit.Pixel);
                }
            }

            var circleRect = new Rectangle(ox + 5, oy + 4, magSize, magSize);
            var prevSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var state = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(circleRect);
                g.SetClip(clip);
                g.DrawImageUnscaled(_magZoomBmp, circleRect.Location);

                int centerBoxX = circleRect.X + half * cell;
                int centerBoxY = circleRect.Y + half * cell;
                using var cPen = new Pen(Theme.Brand, 1.3f);
                g.DrawRectangle(cPen, centerBoxX, centerBoxY, cell, cell);
            }
            g.Restore(state);

            using (var outerWhite = new Pen(Color.White, 2f))
                g.DrawEllipse(outerWhite, circleRect);
            using (var outerBorder = new Pen(Color.FromArgb(40, 40, 48), 1f))
                g.DrawEllipse(outerBorder, circleRect.X - 1, circleRect.Y - 1, circleRect.Width + 2, circleRect.Height + 2);

            // Info pill below magnifier
            string info = $"X:{cx} Y:{cy}  #{centerCol.R:X2}{centerCol.G:X2}{centerCol.B:X2}";
            var infoRect = new Rectangle(ox, circleRect.Bottom + 4, hudW, 22);
            using (var bg = new SolidBrush(Color.FromArgb(230, 20, 20, 25)))
                Theme.FillRoundedRect(g, bg, infoRect, 5);
            using (var pen = new Pen(Color.FromArgb(90, 154, 127, 245), 1f))
                Theme.DrawRoundedRect(g, pen, infoRect, 5);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(info, _magFont, Brushes.White, infoRect, sf);

            g.SmoothingMode = prevSmooth;
        }

        private static Dictionary<string, PointF> GetBoxHandles(RectangleF b)
        {
            float mx = b.X + b.Width / 2f, my = b.Y + b.Height / 2f;
            return new Dictionary<string, PointF>
            {
                ["nw"] = new PointF(b.Left, b.Top),
                ["n"] = new PointF(mx, b.Top),
                ["ne"] = new PointF(b.Right, b.Top),
                ["e"] = new PointF(b.Right, my),
                ["se"] = new PointF(b.Right, b.Bottom),
                ["s"] = new PointF(mx, b.Bottom),
                ["sw"] = new PointF(b.Left, b.Bottom),
                ["w"] = new PointF(b.Left, my)
            };
        }

        private static string HitTestResizeHandle(RectangleF b, Point pt)
        {
            foreach (var kv in GetBoxHandles(b))
            {
                if (Math.Sqrt(Math.Pow(pt.X - kv.Value.X, 2) + Math.Pow(pt.Y - kv.Value.Y, 2)) <= 9)
                    return kv.Key;
            }
            // Allow grabbing anywhere along the dashed perimeter edges smoothly
            const float edgeTol = 5f;
            bool inX = pt.X >= b.Left - edgeTol && pt.X <= b.Right + edgeTol;
            bool inY = pt.Y >= b.Top - edgeTol && pt.Y <= b.Bottom + edgeTol;
            if (inX && Math.Abs(pt.Y - b.Top) <= edgeTol) return "n";
            if (inX && Math.Abs(pt.Y - b.Bottom) <= edgeTol) return "s";
            if (inY && Math.Abs(pt.X - b.Left) <= edgeTol) return "w";
            if (inY && Math.Abs(pt.X - b.Right) <= edgeTol) return "e";
            return null;
        }

        private static Cursor GetCursorForHandle(string h) => h switch
        {
            "nw" or "se" => Cursors.SizeNWSE,
            "ne" or "sw" => Cursors.SizeNESW,
            "n" or "s" => Cursors.SizeNS,
            "e" or "w" => Cursors.SizeWE,
            _ => Cursors.Cross
        };

        private static void DrawResizeHandles(Graphics g, RectangleF b)
        {
            if (b.Width < 18 || b.Height < 18) return;
            foreach (var h in GetBoxHandles(b).Values)
            {
                using var pen = new Pen(Theme.Brand, 1.3f);
                g.FillEllipse(Brushes.White, h.X - 4, h.Y - 4, 8, 8);
                g.DrawEllipse(pen, h.X - 4, h.Y - 4, 8, 8);
            }
        }

        private float GetInlineTextFontSize() => Math.Max(12f, _strokeWidth * 2.8f);

        private void DrawTransparentInlineText(Graphics g)
        {
            string txt = _inlineTextBox.Text ?? "";
            float fontSize = GetInlineTextFontSize();
            using var f = Theme.Font(fontSize, FontStyle.Bold, GraphicsUnit.Point);

            if (!string.IsNullOrEmpty(txt))
            {
                using var br = new SolidBrush(_currentColor);
                g.DrawString(txt, f, br, _inlineTextPos.X, _inlineTextPos.Y);
            }

            if (_caretVisible)
            {
                int sel = Math.Max(0, Math.Min(txt.Length, _inlineTextBox.SelectionStart));
                string prefix = txt.Substring(0, sel);
                float caretOffsetX = 0f;
                if (!string.IsNullOrEmpty(prefix))
                {
                    // Measure with sentinel character so trailing spaces move the caret accurately
                    SizeF withSentinel = g.MeasureString(prefix + "|", f);
                    SizeF sentinelOnly = g.MeasureString("|", f);
                    caretOffsetX = Math.Max(0f, withSentinel.Width - sentinelOnly.Width);
                }
                float lineH = Math.Max(16f, f.GetHeight(g));
                float cx = _inlineTextPos.X + caretOffsetX;
                using var caretPen = new Pen(_currentColor, 1.8f);
                g.DrawLine(caretPen, cx, _inlineTextPos.Y + 2f, cx, _inlineTextPos.Y + lineH - 2f);
            }
        }

        private void SpawnInlineTextInput(Point pos)
        {
            if (_inlineTextBox == null)
            {
                _inlineTextBox = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    Location = new Point(-2000, -2000),
                    Size = new Size(1, 1),
                    TabStop = false
                };
                _inlineTextBox.TextChanged += (s, e) =>
                {
                    _caretVisible = true;
                    Invalidate();
                };
                _inlineTextBox.KeyUp += (s, e) =>
                {
                    _caretVisible = true;
                    Invalidate();
                };
                _inlineTextBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitInlineText(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelInlineText(); }
                };
                Controls.Add(_inlineTextBox);
            }
            _inlineTextPos = pos;
            _inlineTextEditing = true;
            _caretVisible = true;
            _inlineTextBox.Location = new Point(-2000, -2000);
            _inlineTextBox.Text = "";
            _inlineTextBox.Visible = true;
            _inlineTextBox.Focus();
            _caretTimer.Start();
            Invalidate();
        }

        private void CancelInlineText()
        {
            _inlineTextEditing = false;
            _caretTimer.Stop();
            if (_inlineTextBox != null)
            {
                _inlineTextBox.Visible = false;
                _inlineTextBox.Text = "";
            }
            if (!IsDisposed) Focus();
            Invalidate();
        }

        private void CommitInlineText()
        {
            if (!_inlineTextEditing && (_inlineTextBox == null || !_inlineTextBox.Visible)) return;
            _inlineTextEditing = false;
            _caretTimer.Stop();
            string txt = _inlineTextBox != null ? _inlineTextBox.Text.Trim() : "";
            if (_inlineTextBox != null)
            {
                _inlineTextBox.Visible = false;
                _inlineTextBox.Text = "";
            }
            if (string.IsNullOrEmpty(txt))
            {
                Invalidate();
                return;
            }

            using var g = CreateGraphics();
            using var f = Theme.Font(GetInlineTextFontSize(), FontStyle.Bold, GraphicsUnit.Point);
            SizeF sz = g.MeasureString(txt, f);
            var item = new AnnotationItem
            {
                Type = AnnotationType.Text,
                X = _inlineTextPos.X,
                Y = _inlineTextPos.Y,
                Text = txt,
                Color = _currentColor,
                FontSize = f.Size / 0.75f,
                Width = sz.Width,
                Height = sz.Height
            };
            float curVx = _scrollCanvas?.ViewX ?? _preScrollCanvas?.ViewX ?? 0;
            float curVy = _scrollCanvas?.ViewY ?? _preScrollCanvas?.ViewY ?? 0;
            item.EnsureDocCoords(curVx, curVy);
            _annotations.Add(item);
            _redoStack.Clear();
            if (!_quickCapture && _selectionRect.HasValue && _selectionRect.Value.Width > 5)
                PositionConfirmBar();
            Invalidate();
        }

        private void EraseAt(Point pt)
        {
            int idx = AnnotationRenderer.FindAnnotationAt(_annotations, pt.X, pt.Y);
            if (idx != -1)
            {
                _redoStack.Push(_annotations[idx]);
                _annotations.RemoveAt(idx);
                RecalcStepCounter();
                Invalidate();
            }
        }

        private void Undo()
        {
            if (_annotations.Count > 0)
            {
                var item = _annotations[_annotations.Count - 1];
                _annotations.RemoveAt(_annotations.Count - 1);
                _redoStack.Push(item);
                RecalcStepCounter();
                Invalidate();
            }
        }

        private void Redo()
        {
            if (_redoStack.Count > 0)
            {
                _annotations.Add(_redoStack.Pop());
                RecalcStepCounter();
                Invalidate();
            }
        }

        private void ClearAnnotations()
        {
            if (_annotations.Count > 0)
            {
                _annotations.Clear();
                _redoStack.Clear();
                RecalcStepCounter();
                Invalidate();
            }
        }

        private void RecalcStepCounter()
        {
            int max = 0;
            foreach (var a in _annotations)
                if (a.Type == AnnotationType.Number && a.Number > max) max = a.Number;
            _stepCounter = max + 1;
        }

        private static AnnotationType ParseToolType(string tool) => tool switch
        {
            "rect" => AnnotationType.Rect,
            "circle" => AnnotationType.Circle,
            "line" => AnnotationType.Line,
            "arrow" => AnnotationType.Arrow,
            "brush" => AnnotationType.Brush,
            "highlighter" => AnnotationType.Highlighter,
            "number" => AnnotationType.Number,
            "text" => AnnotationType.Text,
            "blur" => AnnotationType.Blur,
            "spotlight" => AnnotationType.Spotlight,
            "zoom" => AnnotationType.Zoom,
            _ => AnnotationType.Rect
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_inlineTextBox != null && _inlineTextBox.Visible && _inlineTextBox.Focused)
                return base.ProcessCmdKey(ref msg, keyData);

            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            bool ctrl = (keyData & Keys.Control) != 0;

            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
            {
                if (_isMouseDown && _mode == OverlayCaptureMode.Region && _currentTool == null && _selectMode == "rect")
                {
                    if (key == Keys.Down) StartOrContinueLiveScroll(-180, 0);
                    else if (key == Keys.Up) StartOrContinueLiveScroll(180, 0);
                    else if (key == Keys.Right) StartOrContinueLiveScroll(0, 120);
                    else if (key == Keys.Left) StartOrContinueLiveScroll(0, -120);
                    return true;
                }
                if (_selectionRect.HasValue && _currentTool == null && _selectMode != "free")
                {
                    int step = shift ? 10 : 1;
                    var r = _selectionRect.Value;
                    float nx = r.X, ny = r.Y, nw = r.Width, nh = r.Height;

                    if (ctrl)
                    {
                        if (key == Keys.Right) nw = Math.Min(Width - r.X, r.Width + step);
                        else if (key == Keys.Left) nw = Math.Max(16, r.Width - step);
                        else if (key == Keys.Down) nh = Math.Min(Height - r.Y, r.Height + step);
                        else if (key == Keys.Up) nh = Math.Max(16, r.Height - step);
                    }
                    else
                    {
                        if (key == Keys.Left) nx = Math.Max(0, r.X - step);
                        else if (key == Keys.Right) nx = Math.Min(Width - r.Width, r.X + step);
                        else if (key == Keys.Up) ny = Math.Max(0, r.Y - step);
                        else if (key == Keys.Down) ny = Math.Min(Height - r.Height, r.Y + step);
                    }

                    _selectionRect = new RectangleF(nx, ny, nw, nh);
                    PositionConfirmBar();
                    Invalidate();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_inlineTextBox != null && _inlineTextBox.Visible && _inlineTextBox.Focused)
                return;

            if (e.KeyCode == Keys.Space)
            {
                _isSpaceDown = true;
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                CancelAndClose();
                return;
            }
            if (e.Control && e.KeyCode == Keys.C && _selectionRect.HasValue && _selectionRect.Value.Width > 5 && _selectionRect.Value.Height > 5)
            {
                if (_mode == OverlayCaptureMode.Ocr) FinalizeOcrSelection();
                else FinalizeRegionCapture();
                return;
            }
            if (e.Control && e.KeyCode == Keys.Z)
            {
                if (e.Shift) Redo(); else Undo();
                return;
            }
            if (e.Control && e.KeyCode == Keys.Y)
            {
                Redo();
                return;
            }
            if (e.KeyCode == Keys.Delete && _currentTool == "select" && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _annotations.Count)
            {
                _redoStack.Push(_annotations[_selectedAnnotationIndex]);
                _annotations.RemoveAt(_selectedAnnotationIndex);
                _selectedAnnotationIndex = -1;
                RecalcStepCounter();
                Invalidate();
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                if (_selectionRect.HasValue && _selectionRect.Value.Width > 5 && _selectionRect.Value.Height > 5)
                {
                    if (_mode == OverlayCaptureMode.Ocr) FinalizeOcrSelection();
                    else if (_mode == OverlayCaptureMode.Gif) FinalizeGifSelection();
                    else if (_mode == OverlayCaptureMode.Scrolling) FinalizeScrollingSelection();
                    else FinalizeRegionCapture();
                }
                else if (_mode == OverlayCaptureMode.Region)
                {
                    Screen mon = Screen.FromPoint(Cursor.Position);
                    _selectionRect = new RectangleF(mon.Bounds.X - _virtualBounds.X, mon.Bounds.Y - _virtualBounds.Y, mon.Bounds.Width, mon.Bounds.Height);
                    FinalizeRegionCapture();
                }
                return;
            }

            // Quick single-letter tool shortcuts
            if (!e.Control && !e.Alt)
            {
                switch (e.KeyCode)
                {
                    case Keys.R: _btnModeRect.PerformClick(); break;
                    case Keys.F: _btnModeFree.PerformClick(); break;
                    case Keys.V: SelectAnnotationTool("select"); break;
                    case Keys.U: SelectAnnotationTool("rect"); break;
                    case Keys.C: SelectAnnotationTool("circle"); break;
                    case Keys.L: SelectAnnotationTool("line"); break;
                    case Keys.A: SelectAnnotationTool("arrow"); break;
                    case Keys.P: SelectAnnotationTool("brush"); break;
                    case Keys.H: SelectAnnotationTool("highlighter"); break;
                    case Keys.N: SelectAnnotationTool("number"); break;
                    case Keys.T: SelectAnnotationTool("text"); break;
                    case Keys.B: SelectAnnotationTool("blur"); break;
                    case Keys.S: SelectAnnotationTool("spotlight"); break;
                    case Keys.Z: SelectAnnotationTool("zoom"); break;
                }
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) _isSpaceDown = false;
        }

        private void CancelAndClose()
        {
            _preScrollPollTimer.Stop();
            _caretTimer.Stop();
            ResetLiveScroll();
            UninstallHooks();
            Hide();
            CaptureCancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }



        protected override void Dispose(bool disposing)
        {
            Application.RemoveMessageFilter(this);
            UninstallHooks();
            if (disposing)
            {
                _liveCaptureTimer?.Dispose();
                _pendingFirstLiveWheelTimer?.Dispose();
                _preScrollPollTimer?.Dispose();
                _caretTimer?.Dispose();
                _scrollCanvas?.Dispose();
                _preScrollCanvas?.Dispose();
                _lastLiveFreshBmp?.Dispose();
                _lastPreFreshBmp?.Dispose();
                _preScrollKeyframes.Clear();
                _toolTip?.Dispose();
                _magZoomBmp?.Dispose();
                _fastDimmedBmp?.Dispose();
                _fastScreenBmp?.Dispose();
                _dimPillFont?.Dispose();
                _hintFont?.Dispose();
                _magFont?.Dispose();
                _screenBitmap?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

