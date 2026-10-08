using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant
{
    // Team Paladin's shared on-screen look: black on white, Helvetica Neue (Arial where it isn't installed).
    // Used by the left control panel and the pose-action status bar. Call only from OnGUI.
    //
    // Sizes are in points: Px() converts to screen pixels (2x on Retina), so text is rendered natively at
    // its final size and stays sharp instead of being scaled up.
    public static class DigiPhantUi
    {
        // Muted text #555 is ~7.5:1 on white (WCAG AAA); control outlines #767676 are 4.5:1 (above the 3:1 minimum).
        public static readonly Color Ink = Color.black, Muted = new Color(.333f, .333f, .333f), Outline = new Color(.463f, .463f, .463f);
        public static readonly Color Ready = new Color(.85f, .45f, 0f), Go = new Color(.1f, .55f, .2f), Stop = new Color(.8f, .15f, .1f);

        // Type scale in points.
        public const int Title = 13, Body = 11, Small = 10, Heading = 9;
        // Controls are 24 pt tall (48 px on Retina): above the WCAG 2.2 minimum target size.
        public const float ControlHeight = 24;

        // 0 = automatic (2x on high-density screens such as Retina, otherwise 1x), shrunk in quarter steps
        // so the 300 pt panel, the status bar and the gesture panel fit a small Game view. Set a value to force a scale.
        public static float ScaleOverride;
        public static float Scale => ScaleOverride > 0 ? ScaleOverride : AutoScale;
        static float AutoScale
        {
            get
            {
                float density = Screen.dpi >= 150 ? 2f : 1f;
                float fit = Mathf.Min(Screen.width / 1050f, Screen.height / 620f);
                return Mathf.Max(.75f, Mathf.Floor(Mathf.Min(density, fit) * 4) / 4);
            }
        }
        public static float Px(float points) => Mathf.Round(points * Scale);
        static int Pxi(float points) => Mathf.RoundToInt(points * Scale);

        // The left panel's width in pixels; the stage camera's viewport starts right after it.
        public static float PanelWidth => Mathf.Min(Px(300), Screen.width * .3f);

        static Font font;
        static GUISkin skin;
        static float skinScale;
        static Texture2D white, black, light, hover, line, go, track;

        public static Texture2D White { get { Ensure(); return white; } }
        public static Texture2D Black { get { Ensure(); return black; } }
        public static Texture2D Light { get { Ensure(); return light; } }

        static readonly Dictionary<string, Texture2D> rounded = new Dictionary<string, Texture2D>();

        // Rounded rectangle (fill + outline, anti-aliased) for nine-slicing: corners stay round at any size.
        static Texture2D Rounded(Color fill, Color edge, float radius, float edgeWidth)
        {
            int r = Mathf.Max(1, Mathf.CeilToInt(radius)), n = 2 * r + 3;
            string key = $"{fill}{edge}{r}{edgeWidth}";
            if (rounded.TryGetValue(key, out var cached) && cached) return cached;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float half = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float qx = Mathf.Abs(x + .5f - half) - (half - r), qy = Mathf.Abs(y + .5f - half) - (half - r);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                    var c = Color.Lerp(fill, edge, Mathf.Clamp01(d + edgeWidth + .5f)); // outline band at the edge
                    c.a *= Mathf.Clamp01(.5f - d);                                      // anti-aliased outer edge
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return rounded[key] = t;
        }

        static RectOffset Slice(Texture2D t) { int k = t.width / 2; return new RectOffset(k, k, k, k); }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static void Ensure()
        {
            if (white) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Helvetica", "Arial" }, 22);
            white = Solid(Color.white);
            black = Solid(Ink);
            light = Solid(new Color(.93f, .93f, .93f));
            hover = Solid(new Color(.84f, .84f, .84f));
            line = Solid(new Color(.82f, .82f, .82f));
            track = Solid(Outline);
            go = Solid(Go);
        }

        // Text style; size is in points.
        public static GUIStyle Text(float size, Color color, FontStyle weight = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Ensure();
            int p = Pxi(1);
            return new GUIStyle { font = font, fontSize = Pxi(size), fontStyle = weight, alignment = align, wordWrap = true,
                normal = { textColor = color }, padding = new RectOffset(p, p, p, p) };
        }

        // Rounded, outlined white button (selected: solid black, white text).
        public static GUIStyle Button(float size = Body, bool selected = false) =>
            ButtonWith(size, selected ? Ink : Color.white, selected ? Ink : new Color(.93f, .93f, .93f), selected ? Ink : Outline, selected ? Color.white : Ink);

        static GUIStyle ButtonWith(float size, Color fill, Color hoverFill, Color edge, Color text)
        {
            var s = Text(size, text, FontStyle.Bold, TextAnchor.MiddleCenter);
            s.wordWrap = false;
            s.padding = new RectOffset(Pxi(10), Pxi(10), Pxi(4), Pxi(4));
            s.margin = new RectOffset(Pxi(2), Pxi(2), Pxi(2), Pxi(2));
            float radius = Px(5), outline = Mathf.Max(1, Px(.75f));
            s.normal.background = Rounded(fill, edge, radius, outline);
            s.hover.background = Rounded(hoverFill, edge, radius, outline);
            s.active.background = Rounded(Color.Lerp(hoverFill, Ink, .08f), edge, radius, outline);
            s.border = Slice(s.normal.background);
            s.fixedHeight = Px(ControlHeight);
            s.hover.textColor = s.active.textColor = text;
            return s;
        }

        // Solid red record button with bold white text (~5.6:1 contrast).
        public static GUIStyle RecordButton(float size = Body) =>
            ButtonWith(size, Stop, new Color(.65f, .1f, .07f), Stop, Color.white);

        // A skin so every GUILayout label, button, slider and scrollbar inside a panel gets the same look.
        public static GUISkin Skin
        {
            get
            {
                Ensure();
                if (skin && Mathf.Approximately(skinScale, Scale)) return skin;
                if (skin) UnityEngine.Object.Destroy(skin);
                skinScale = Scale;
                skin = UnityEngine.Object.Instantiate(GUI.skin);
                skin.hideFlags = HideFlags.HideAndDontSave;
                skin.font = font;
                skin.label = Text(Body, Ink);
                skin.button = Button();
                skin.box = new GUIStyle { normal = { background = white } };
                skin.horizontalSlider = new GUIStyle { normal = { background = track }, fixedHeight = Px(2),
                    margin = new RectOffset(Pxi(6), Pxi(6), Pxi(10), Pxi(10)), border = new RectOffset() };
                skin.horizontalSliderThumb = new GUIStyle { normal = { background = black }, hover = { background = black }, active = { background = black },
                    fixedWidth = Px(12), fixedHeight = Px(12), margin = new RectOffset(0, 0, -Pxi(5), 0) };
                skin.verticalScrollbar = new GUIStyle { normal = { background = light }, fixedWidth = Px(4), margin = new RectOffset(Pxi(3), 0, 0, 0) };
                skin.verticalScrollbarThumb = new GUIStyle { normal = { background = line }, fixedWidth = Px(4) };
                skin.verticalScrollbarUpButton = GUIStyle.none;
                skin.verticalScrollbarDownButton = GUIStyle.none;
                skin.horizontalScrollbar = GUIStyle.none;
                skin.scrollView = GUIStyle.none;
                return skin;
            }
        }

        // ---------- Icons, drawn from simple shapes at the screen's resolution (no image files) ----------

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        // Shapes are signed distance functions over a 0..1 square (y up): negative inside, positive outside.
        static float Circle(Vector2 p, float cx, float cy, float r) => Vector2.Distance(p, new Vector2(cx, cy)) - r;
        static float Segment(Vector2 p, float ax, float ay, float bx, float by, float thickness)
        {
            Vector2 a = new Vector2(ax, ay), ab = new Vector2(bx, by) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t) - thickness;
        }

        static Texture2D Icon(string name, Func<Vector2, float> shape)
        {
            int size = Mathf.Max(12, Pxi(14));
            string key = name + size;
            if (icons.TryGetValue(key, out var cached) && cached) return cached;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = shape(new Vector2((x + .5f) / size, (y + .5f) / size)) * size;
                    var c = Muted; c.a = Mathf.Clamp01(.5f - d); // one-pixel anti-aliased edge
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return icons[key] = t;
        }

        // Head plus domed shoulders with a flat base.
        static float Person(Vector2 p, float cx, float headY, float headR, float bodyTop, float bodyR) =>
            Mathf.Min(Circle(p, cx, headY, headR), Mathf.Max(Circle(p, cx, bodyTop - bodyR, bodyR), .06f - p.y));

        // Two people: one in front, one behind with a small gap between them.
        public static Texture2D PeopleIcon => Icon("people", p =>
        {
            float front = Person(p, .63f, .66f, .14f, .46f, .27f);
            float back = Person(p, .35f, .74f, .12f, .56f, .23f);
            return Mathf.Min(front, Mathf.Max(back, -(front - .07f)));
        });

        // One standing figure.
        public static Texture2D BodyIcon => Icon("body", p => Mathf.Min(Mathf.Min(Circle(p, .5f, .84f, .12f),
            Segment(p, .5f, .64f, .5f, .36f, .08f)), Mathf.Min(Segment(p, .26f, .58f, .74f, .58f, .055f),
            Mathf.Min(Segment(p, .5f, .36f, .34f, .06f, .055f), Segment(p, .5f, .36f, .66f, .06f, .055f)))));

        // Input / output: an arrow in (right) above an arrow out (left).
        public static Texture2D InputIcon => Icon("input", p =>
        {
            const float w = .05f;
            float right = Mathf.Min(Segment(p, .12f, .7f, .84f, .7f, w), Mathf.Min(Segment(p, .84f, .7f, .66f, .86f, w), Segment(p, .84f, .7f, .66f, .54f, w)));
            float left = Mathf.Min(Segment(p, .88f, .3f, .16f, .3f, w), Mathf.Min(Segment(p, .16f, .3f, .34f, .46f, w), Segment(p, .16f, .3f, .34f, .14f, w)));
            return Mathf.Min(right, left);
        });

        // Rounded white panel with a thin dark outline (border in pixels).
        public static void Panel(Rect r, float border = 1)
        {
            Ensure();
            if (Event.current.type != EventType.Repaint) return;
            var t = Rounded(Color.white, Ink, Px(8), border);
            new GUIStyle { normal = { background = t }, border = Slice(t) }.Draw(r, false, false, false, false);
        }

        // Small grey capitals heading with a rule above it, for GUILayout panels.
        public static void Section(string title)
        {
            Ensure();
            GUILayout.Space(Px(12));
            var rule = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rule, line);
            GUILayout.Space(Px(4));
            GUILayout.Label(title, Text(Heading, Muted, FontStyle.Bold));
            GUILayout.Space(Px(1));
        }

        // Secondary explanatory text (width in pixels).
        public static void Note(string text, float width) => GUILayout.Label(text, Text(Small, Muted), GUILayout.Width(width));

        // One row of mutually exclusive choices; the selected one is filled black. Returns the chosen index.
        // Captions share one column so every row's buttons line up; rows are spaced so selections never merge.
        public static int Segmented(string caption, int selected, params string[] options) => Segmented(caption, null, selected, options);

        // Same, with an icon before the caption.
        public static int Segmented(string caption, Texture2D icon, int selected, params string[] options)
        {
            GUILayout.BeginHorizontal();
            if (caption != null)
            {
                float captionWidth = Px(icon ? 66 : 44), height = Px(ControlHeight + 4);
                var r = GUILayoutUtility.GetRect(captionWidth, height, GUILayout.Width(captionWidth), GUILayout.Height(height));
                float textX = r.x;
                if (icon)
                {
                    float size = Px(14);
                    GUI.DrawTexture(new Rect(r.x, r.y + (height - size) / 2, size, size), icon);
                    textX += size + Px(5);
                }
                GUI.Label(new Rect(textX, r.y, r.xMax - textX, height), caption, Text(Small, Muted, FontStyle.Bold));
            }
            for (int i = 0; i < options.Length; i++)
                if (GUILayout.Button(options[i], Button(Body, i == selected), GUILayout.MinWidth(10))) selected = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(Px(3));
            return selected;
        }

        // A small filled (on) or hollow (off) square with a label, e.g. performer visibility.
        public static void Dot(string label, bool on)
        {
            Ensure();
            // Filled vs hollow (not just colour) so it reads for colour-blind users too.
            float size = Px(9), row = Px(16);
            var r = GUILayoutUtility.GetRect(size + Px(2), row, GUILayout.Width(size + Px(2)));
            var square = new Rect(r.x, r.y + (row - size) / 2, size, size);
            GUI.DrawTexture(square, on ? go : black);
            float b = Mathf.Max(1, Px(1.5f));
            if (!on) GUI.DrawTexture(new Rect(square.x + b, square.y + b, size - 2 * b, size - 2 * b), white);
            GUILayout.Label(label + (on ? "" : " (not seen)"), Text(Small, on ? Ink : Muted, FontStyle.Bold), GUILayout.ExpandWidth(false));
        }
    }
}
