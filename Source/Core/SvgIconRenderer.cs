using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QuickShot.Core
{
    /// <summary>
    /// Lightweight zero-dependency SVG icon renderer for QuickShot's Illustrator tool SVGs.
    /// Reads from disk or EmbeddedResource seamlessly so icons always render.
    /// </summary>
    public static class SvgIconRenderer
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static Bitmap RenderToolIcon(string svgName, int size, Color? tintOverride = null)
        {
            string fileName = svgName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? svgName : svgName + ".svg";
            string key = $"{fileName}|{size}|{tintOverride?.ToArgb() ?? 0}";
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out Bitmap cached)) return cached;
            }

            Bitmap bmp;
            using (var stream = AssetLoader.OpenAssetStream("assets", "Tools", fileName))
            {
                bmp = stream != null ? RenderSvgStream(stream, size, tintOverride) : new Bitmap(size, size);
            }

            lock (Cache)
            {
                Cache[key] = bmp;
            }
            return bmp;
        }

        public static Bitmap RenderSvgFile(string filePath, int targetSize, Color? tintOverride = null)
        {
            if (!File.Exists(filePath)) return new Bitmap(targetSize, targetSize);
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return RenderSvgStream(fs, targetSize, tintOverride);
        }

        public static Bitmap RenderSvgStream(Stream stream, int targetSize, Color? tintOverride = null)
        {
            var bmp = new Bitmap(targetSize, targetSize);
            try
            {
                var doc = XDocument.Load(stream);
                var root = doc.Root;
                if (root == null) return bmp;

                float vbX = 0, vbY = 0, vbW = 48, vbH = 48;
                var vbAttr = root.Attribute("viewBox")?.Value;
                if (!string.IsNullOrWhiteSpace(vbAttr))
                {
                    var nums = ParseFloatList(vbAttr);
                    if (nums.Count >= 4) { vbX = nums[0]; vbY = nums[1]; vbW = nums[2]; vbH = nums[3]; }
                }

                var classStyles = ParseCssClasses(root);

                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float scale = Math.Min(targetSize / vbW, targetSize / vbH);
                g.TranslateTransform(-vbX * scale, -vbY * scale);
                g.ScaleTransform(scale, scale);

                foreach (var el in root.Descendants())
                {
                    string local = el.Name.LocalName.ToLowerInvariant();
                    if (local == "g" || local == "style" || local == "lineargradient" || local == "stop" || local == "defs")
                        continue;

                    RenderElement(g, el, local, classStyles, tintOverride);
                }
            }
            catch { }
            return bmp;
        }

        private static Dictionary<string, Dictionary<string, string>> ParseCssClasses(XElement root)
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var styleEl in root.Descendants().Where(e => e.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)))
            {
                string css = styleEl.Value ?? "";
                foreach (Match m in Regex.Matches(css, @"\.([A-Za-z0-9_-]+)\s*\{([^}]*)\}"))
                {
                    string cls = m.Groups[1].Value;
                    var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string decl in m.Groups[2].Value.Split(';'))
                    {
                        var kv = decl.Split(':');
                        if (kv.Length == 2) props[kv[0].Trim()] = kv[1].Trim();
                    }
                    map[cls] = props;
                }
            }
            return map;
        }

        private static string GetStyleProp(XElement el, string prop, Dictionary<string, Dictionary<string, string>> classes)
        {
            string attr = el.Attribute(prop)?.Value;
            if (!string.IsNullOrEmpty(attr)) return attr;
            string cls = el.Attribute("class")?.Value;
            if (!string.IsNullOrEmpty(cls) && classes.TryGetValue(cls.Trim(), out var props) && props.TryGetValue(prop, out string val))
                return val;
            return null;
        }

        private static Color? ResolveColor(string val, float opacity, Color? tintOverride, bool isSecondaryDark = false)
        {
            if (string.IsNullOrWhiteSpace(val) || val.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;
            if (val.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb((int)(120 * opacity), tintOverride ?? Color.FromArgb(217, 217, 217));

            Color baseCol;
            if (val.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                baseCol = tintOverride ?? Color.FromArgb(228, 228, 232);
            }
            else if (val.StartsWith("#"))
            {
                baseCol = ColorTranslator.FromHtml(val);
                if (tintOverride.HasValue && !isSecondaryDark &&
                    (baseCol.R > 160 && baseCol.G > 160 && baseCol.B > 160))
                {
                    baseCol = tintOverride.Value;
                }
            }
            else
            {
                baseCol = tintOverride ?? Color.FromArgb(228, 228, 232);
            }

            int a = Math.Max(0, Math.Min(255, (int)Math.Round(baseCol.A * opacity)));
            return Color.FromArgb(a, baseCol.R, baseCol.G, baseCol.B);
        }

        private static void RenderElement(Graphics g, XElement el, string local,
            Dictionary<string, Dictionary<string, string>> classes, Color? tintOverride)
        {
            string fillStr = GetStyleProp(el, "fill", classes);
            string strokeStr = GetStyleProp(el, "stroke", classes);
            string fillOpStr = GetStyleProp(el, "fill-opacity", classes);
            string strokeWStr = GetStyleProp(el, "stroke-width", classes);

            float fillOpacity = float.TryParse(fillOpStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fo) ? fo : 1f;
            float strokeWidth = float.TryParse(strokeWStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sw) ? sw : 1f;

            if (fillStr == null && strokeStr == null) fillStr = "#D9D9D9";
            if (fillStr == null && fillOpStr != null) fillStr = "#D9D9D9";

            bool isDarkCutout = fillStr != null && (fillStr.Equals("#262626", StringComparison.OrdinalIgnoreCase) || fillStr.Equals("#18181b", StringComparison.OrdinalIgnoreCase));
            Color? fillCol = ResolveColor(fillStr, fillOpacity, tintOverride, isDarkCutout);
            Color? strokeCol = ResolveColor(strokeStr, 1f, tintOverride, false);

            using var path = new GraphicsPath(FillMode.Winding);

            switch (local)
            {
                case "path":
                    string d = el.Attribute("d")?.Value;
                    if (!string.IsNullOrWhiteSpace(d)) BuildSvgPath(path, d);
                    break;
                case "rect":
                    float rx = ParseAttr(el, "x"), ry = ParseAttr(el, "y"), rw = ParseAttr(el, "width"), rh = ParseAttr(el, "height");
                    if (rw > 0 && rh > 0) path.AddRectangle(new RectangleF(rx, ry, rw, rh));
                    break;
                case "circle":
                    float cx = ParseAttr(el, "cx"), cy = ParseAttr(el, "cy"), cr = ParseAttr(el, "r");
                    if (cr > 0) path.AddEllipse(cx - cr, cy - cr, cr * 2, cr * 2);
                    break;
                case "ellipse":
                    float ex = ParseAttr(el, "cx"), ey = ParseAttr(el, "cy"), erx = ParseAttr(el, "rx"), ery = ParseAttr(el, "ry");
                    if (erx > 0 && ery > 0) path.AddEllipse(ex - erx, ey - ery, erx * 2, ery * 2);
                    break;
                case "line":
                    float x1 = ParseAttr(el, "x1"), y1 = ParseAttr(el, "y1"), x2 = ParseAttr(el, "x2"), y2 = ParseAttr(el, "y2");
                    path.AddLine(x1, y1, x2, y2);
                    break;
                case "polygon":
                    var pts = ParsePoints(el.Attribute("points")?.Value);
                    if (pts.Length >= 3) path.AddPolygon(pts);
                    break;
            }

            if (path.PointCount == 0) return;

            if (fillCol.HasValue && (fillStr == null || !fillStr.Equals("none", StringComparison.OrdinalIgnoreCase)))
            {
                using var br = new SolidBrush(fillCol.Value);
                g.FillPath(br, path);
            }
            if (strokeCol.HasValue)
            {
                using var pen = new Pen(strokeCol.Value, strokeWidth);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        private static float ParseAttr(XElement el, string name)
        {
            string v = el.Attribute(name)?.Value;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        }

        private static PointF[] ParsePoints(string ptsStr)
        {
            var nums = ParseFloatList(ptsStr ?? "");
            var pts = new List<PointF>();
            for (int i = 0; i + 1 < nums.Count; i += 2)
                pts.Add(new PointF(nums[i], nums[i + 1]));
            return pts.ToArray();
        }

        private static List<float> ParseFloatList(string text)
        {
            var list = new List<float>();
            foreach (Match m in Regex.Matches(text, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"))
            {
                if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    list.Add(v);
            }
            return list;
        }

        private static void BuildSvgPath(GraphicsPath gp, string d)
        {
            var tokens = Regex.Matches(d, @"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
            int idx = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty;
            PointF figStart = PointF.Empty;
            PointF lastCtrl = PointF.Empty;
            char prevCmd = '\0';

            bool HasNum() => idx < tokens.Count && !char.IsLetter(tokens[idx].Value[0]);
            float NextNum() => float.Parse(tokens[idx++].Value, CultureInfo.InvariantCulture);

            while (idx < tokens.Count)
            {
                if (char.IsLetter(tokens[idx].Value[0]))
                {
                    cmd = tokens[idx++].Value[0];
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        gp.CloseFigure();
                        cur = figStart;
                        prevCmd = cmd;
                        continue;
                    }
                }

                switch (cmd)
                {
                    case 'M':
                    case 'm':
                    {
                        bool rel = cmd == 'm';
                        bool first = true;
                        while (HasNum())
                        {
                            float x = NextNum(), y = NextNum();
                            var pt = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            if (first)
                            {
                                gp.StartFigure();
                                figStart = pt;
                                first = false;
                                cmd = rel ? 'l' : 'L';
                            }
                            else gp.AddLine(cur, pt);
                            cur = pt;
                        }
                        break;
                    }
                    case 'L':
                    case 'l':
                    {
                        bool rel = cmd == 'l';
                        while (HasNum())
                        {
                            float x = NextNum(), y = NextNum();
                            var pt = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            gp.AddLine(cur, pt);
                            cur = pt;
                        }
                        break;
                    }
                    case 'H':
                    case 'h':
                    {
                        bool rel = cmd == 'h';
                        while (HasNum())
                        {
                            float x = NextNum();
                            var pt = new PointF(rel ? cur.X + x : x, cur.Y);
                            gp.AddLine(cur, pt);
                            cur = pt;
                        }
                        break;
                    }
                    case 'V':
                    case 'v':
                    {
                        bool rel = cmd == 'v';
                        while (HasNum())
                        {
                            float y = NextNum();
                            var pt = new PointF(cur.X, rel ? cur.Y + y : y);
                            gp.AddLine(cur, pt);
                            cur = pt;
                        }
                        break;
                    }
                    case 'C':
                    case 'c':
                    {
                        bool rel = cmd == 'c';
                        while (HasNum())
                        {
                            float x1 = NextNum(), y1 = NextNum(), x2 = NextNum(), y2 = NextNum(), x = NextNum(), y = NextNum();
                            var p1 = rel ? new PointF(cur.X + x1, cur.Y + y1) : new PointF(x1, y1);
                            var p2 = rel ? new PointF(cur.X + x2, cur.Y + y2) : new PointF(x2, y2);
                            var p3 = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            gp.AddBezier(cur, p1, p2, p3);
                            lastCtrl = p2;
                            cur = p3;
                        }
                        break;
                    }
                    case 'S':
                    case 's':
                    {
                        bool rel = cmd == 's';
                        while (HasNum())
                        {
                            PointF p1 = (prevCmd == 'C' || prevCmd == 'c' || prevCmd == 'S' || prevCmd == 's')
                                ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y)
                                : cur;
                            float x2 = NextNum(), y2 = NextNum(), x = NextNum(), y = NextNum();
                            var p2 = rel ? new PointF(cur.X + x2, cur.Y + y2) : new PointF(x2, y2);
                            var p3 = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            gp.AddBezier(cur, p1, p2, p3);
                            lastCtrl = p2;
                            cur = p3;
                            prevCmd = cmd;
                        }
                        break;
                    }
                    case 'Q':
                    case 'q':
                    {
                        bool rel = cmd == 'q';
                        while (HasNum())
                        {
                            float x1 = NextNum(), y1 = NextNum(), x = NextNum(), y = NextNum();
                            var qp = rel ? new PointF(cur.X + x1, cur.Y + y1) : new PointF(x1, y1);
                            var end = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            var c1 = new PointF(cur.X + 2f / 3f * (qp.X - cur.X), cur.Y + 2f / 3f * (qp.Y - cur.Y));
                            var c2 = new PointF(end.X + 2f / 3f * (qp.X - end.X), end.Y + 2f / 3f * (qp.Y - end.Y));
                            gp.AddBezier(cur, c1, c2, end);
                            lastCtrl = qp;
                            cur = end;
                        }
                        break;
                    }
                    case 'A':
                    case 'a':
                    {
                        bool rel = cmd == 'a';
                        while (HasNum())
                        {
                            float rx = NextNum(), ry = NextNum(), rot = NextNum(), large = NextNum(), sweep = NextNum();
                            float x = NextNum(), y = NextNum();
                            var end = rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);
                            gp.AddLine(cur, end);
                            cur = end;
                        }
                        break;
                    }
                    default:
                        idx++;
                        break;
                }
                prevCmd = cmd;
            }
        }
    }
}
