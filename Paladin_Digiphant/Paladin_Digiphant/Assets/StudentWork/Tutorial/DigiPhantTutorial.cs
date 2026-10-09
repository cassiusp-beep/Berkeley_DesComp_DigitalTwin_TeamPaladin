using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigiPhant
{
    // "How to move" pop-up: one card per role (P1 driver, P2 navigator, P3 trunk), a version for Full body and one
    // for Seated. Each move is a little stick figure that loops the motion, drawn from discs and rotated bars
    // (no imported art). Adds itself next to DigiPhantController in Play mode, so no scene needs editing.
    // Opens by itself the first time the input switches to Camera, and from the "How to move" button.
    // Captions follow the real controls: DigiPhantPoseActions, DigiPhantTrunkActions, DigiPhantLocomotion,
    // the scene's body-part mapping, and Tracking/bridge.py (seated: hands replace feet, shoulder tilt replaces lean).
    public class DigiPhantTutorial : MonoBehaviour
    {
        [Tooltip("Open the pop-up by itself the first time the input switches to Camera.")]
        public bool openOnFirstCamera = true;
        public bool showButton = true;

        DigiPhantController controller;
        bool open, seenCamera;
        int page;
        InputMode lastMode = InputMode.TestSliders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isBatchMode) return;
            var c = FindAnyObjectByType<DigiPhantController>();
            if (c && !c.GetComponent<DigiPhantTutorial>()) c.gameObject.AddComponent<DigiPhantTutorial>();
        }

        void OnEnable() { controller = GetComponent<DigiPhantController>(); }

        void Update()
        {
            if (!controller) return;
            if (controller.inputMode == InputMode.Camera && lastMode != InputMode.Camera && !seenCamera)
            {
                seenCamera = true;
                if (openOnFirstCamera) { open = true; page = 0; }
            }
            lastMode = controller.inputMode;
            var kb = Keyboard.current;
            if (!open || kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) open = false;
            if (kb.rightArrowKey.wasPressedThisFrame) page++;
            if (kb.leftArrowKey.wasPressedThisFrame) page--;
        }

        // ---------- The figure: a pose is a few angles (degrees from straight down, outward is positive) ----------

        struct Fig
        {
            public float lean, crouch, hop, aL, fL, aR, fR; // lean, squat depth, hop height; upper arm / forearm angles
            public Vector2 footL, footR;                    // L = the performer's left = the viewer's right
            public static Fig Lerp(Fig a, Fig b, float t) => new Fig
            {
                lean = Mathf.Lerp(a.lean, b.lean, t), crouch = Mathf.Lerp(a.crouch, b.crouch, t), hop = Mathf.Lerp(a.hop, b.hop, t),
                aL = Mathf.Lerp(a.aL, b.aL, t), fL = Mathf.Lerp(a.fL, b.fL, t), aR = Mathf.Lerp(a.aR, b.aR, t), fR = Mathf.Lerp(a.fR, b.fR, t),
                footL = Vector2.Lerp(a.footL, b.footL, t), footR = Vector2.Lerp(a.footR, b.footR, t)
            };
        }
        static readonly Fig Rest = new Fig { aL = 8, fL = 4, aR = 8, fR = 4, footL = new Vector2(.07f, 0), footR = new Vector2(-.07f, 0) };
        static Fig L(Fig f, float a, float b) { f.aL = a; f.fL = b; return f; }
        static Fig R(Fig f, float a, float b) { f.aR = a; f.fR = b; return f; }
        static Fig Lean(Fig f, float d) { f.lean = d; return f; }
        static Fig Squat(Fig f) { f.crouch = 1; f.footL = new Vector2(.17f, 0); f.footR = new Vector2(-.17f, 0); return f; }
        static Fig KneeUp(Fig f) { f.footR = new Vector2(-.1f, .22f); f.hop = .04f; return f; }
        static Vector2 V(float x, float y) => new Vector2(x, y);

        const int ArmL = 1, ArmR = 2, LegR = 8, Body = 16;
        class Move
        {
            public string title, sub;
            public Func<float, float, Fig> pose; // (eased 0..1, signed swing -1..1)
            public int hot;                      // parts drawn in the accent colour
            public float hold, period = 3f;      // seconds to hold (0 = no timer ring)
            public bool off;                     // not available in this mode
            public Vector2[] arrows;             // pairs: from, to
        }

        List<Move> Moves(int role, bool seated, int people)
        {
            var list = new List<Move>();
            Fig rest = role == 1 ? L(Rest, 25, -80) : role == 3 ? R(Rest, 25, -80) : Rest;
            Move Add(string title, string sub, Func<float, float, Fig> pose, int hot = 0, float hold = 0, float period = 3f, params Vector2[] arrows)
            {
                var m = new Move { title = title, sub = sub, pose = pose, hot = hot, hold = hold, period = period, arrows = arrows };
                list.Add(m);
                return m;
            }
            Func<float, float, Fig> To(Fig target) => (s, u) => Fig.Lerp(rest, target, s);

            Add("Start here", role == 1 ? "Left hand at your waist" : role == 2 ? "Arms down by your sides" : "Right hand at your waist", (s, u) => rest);
            if (role == 1)
            {
                Add("Walk", "Left hand up from waist to chest", To(L(rest, 20, -155)), ArmL, 0, 3f, V(.27f, .6f), V(.27f, .8f));
                Add("Run", "Left hand above your shoulder", To(L(rest, 110, 175)), ArmL, 0, 3f, V(.36f, .8f), V(.36f, 1f));
                Add("Back up", "Left hand down by your thigh", To(L(rest, 8, 4)), ArmL, 0, 3f, V(.27f, .62f), V(.27f, .4f));
                if (seated) Add("Jump", "Quick right hand raise", To(R(rest, 150, 175)), ArmR, 0, 2f, V(-.36f, .74f), V(-.36f, .96f));
                else Add("Jump", "Quick right knee lift", To(KneeUp(rest)), LegR, 0, 2f, V(-.3f, .12f), V(-.3f, .32f));
                if (people == 1) Add("Turn", seated ? "Tilt your shoulders left or right" : "Lean left or right", (s, u) => Lean(rest, u * 14f), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
            }
            else if (role == 2)
            {
                Add("Turn", seated ? "Tilt your shoulders left or right" : "Lean left or right", (s, u) => Lean(rest, u * 14f), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
                Add("About-turn", "T-pose, arms straight out", To(L(R(rest, 90, 90), 90, 90)), ArmL | ArmR, .6f);
                Add("Rear up", "Both arms straight overhead", To(L(R(rest, 165, 178), 165, 178)), ArmL | ArmR, .4f, 3f, V(.3f, .85f), V(.3f, 1.05f));
            }
            else
            {
                if (seated) Add("Reach, pick up, drop", "Standing only: it needs a squat", (s, u) => Squat(R(rest, 35, 15))).off = true;
                else
                {
                    Add("Reach", "Squat with your right hand low", To(Squat(R(rest, 35, 15))), ArmR | 4 | LegR, 0, 3f, V(.3f, .58f), V(.3f, .34f));
                    Add("Pick up or drop", "Hold the reach about 1 s, near the log", To(Squat(R(rest, 35, 15))), ArmR | 4 | LegR, 1f, 3.4f);
                }
                Add("Lean", seated ? "Tilt shoulders: head turns, trunk swings" : "Head turns, trunk swings", (s, u) => Lean(rest, u * 14f), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
                Add("Ears", "Hands apart flaps the ears", To(L(R(rest, 70, 80), 70, 80)), ArmL | ArmR, 0, 2f, V(.5f, .55f), V(.56f, .55f), V(-.5f, .55f), V(-.56f, .55f));
                Add("Trunk curl", "Right hand up from your waist", To(R(rest, 60, 170)), ArmR, 0, 3f, V(-.34f, .72f), V(-.34f, .94f));
            }
            return list;
        }

        // ---------- Drawing ----------

        static Texture2D disc, tileDisc;
        static Texture2D Disc(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(.5f - (Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) - (n / 2f - 1)))));
            t.Apply();
            return t;
        }

        static void Dot(Vector2 p, float r, Color c)
        {
            if (!disc) disc = Disc(64);
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - r, p.y - r, 2 * r, 2 * r), disc);
        }

        // A rounded bar: a rotated rectangle with a disc on each end.
        static void Seg(Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a, m = (a + b) / 2;
            if (d.magnitude > .5f)
            {
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, m);
                GUI.color = c;
                GUI.DrawTexture(new Rect(m.x - d.magnitude / 2, m.y - w / 2, d.magnitude, w), DigiPhantUi.White);
                GUI.matrix = saved;
            }
            Dot(a, w / 2, c); Dot(b, w / 2, c);
        }

        static Vector2 Dir(float deg, float side) => new Vector2(side * Mathf.Sin(deg * Mathf.Deg2Rad), -Mathf.Cos(deg * Mathf.Deg2Rad));

        // Two-bone leg (0.24 + 0.24): the knee bulges outward.
        static Vector2 Knee(Vector2 hip, ref Vector2 foot, float side)
        {
            Vector2 d = foot - hip;
            float dist = Mathf.Min(d.magnitude, .475f);
            if (dist < .001f) return hip;
            Vector2 dir = d.normalized, n = new Vector2(-dir.y, dir.x);
            if (n.x * side < 0) n = -n;
            foot = hip + dir * dist;
            return hip + dir * dist / 2 + n * Mathf.Sqrt(Mathf.Max(0, .0576f - dist * dist / 4));
        }

        static void Arrow(Vector2 a, Vector2 b, float w, Color c)
        {
            Seg(a, b, w, c);
            Vector2 back = (a - b).normalized * w * 4;
            Seg(b, b + (Vector2)(Quaternion.Euler(0, 0, 35) * back), w, c);
            Seg(b, b + (Vector2)(Quaternion.Euler(0, 0, -35) * back), w, c);
        }

        // box: where the figure lives. Ground is near the bottom; units are about one standing height.
        static void DrawFig(Rect box, Fig f, int hot, bool seated, float t, float alpha)
        {
            float k = Mathf.Min(box.height * .8f, box.width * .9f), ox = box.center.x, oy = box.yMax - box.height * .08f, lw = .03f * k;
            Vector2 P(Vector2 u) => new Vector2(ox + u.x * k, oy - u.y * k);
            Color Col(bool h) { var c = h ? DigiPhantUi.Ready : DigiPhantUi.Ink; c.a = alpha; return c; }
            bool bodyHot = (hot & Body) != 0;

            float bob = .008f * Mathf.Sin(t * 7.5f);
            float hipY = (seated ? .3f : .465f) - (seated ? 0 : f.crouch * .2f) + bob + f.hop;
            float lr = f.lean * Mathf.Deg2Rad;
            Vector2 up = new Vector2(Mathf.Sin(lr), Mathf.Cos(lr)), right = new Vector2(Mathf.Cos(lr), -Mathf.Sin(lr));
            Vector2 hip = V(0, hipY), neck = hip + up * .28f, head = neck + up * .095f;
            Vector2 sL = neck + right * .09f - up * .02f, sR = neck - right * .09f - up * .02f;

            // Soft ground shadow, smaller while hopping.
            GUI.color = new Color(0, 0, 0, .08f * alpha);
            if (!disc) disc = Disc(64);
            float sw = .36f * k * (1 - f.hop * 2.5f), sh = .05f * k;
            GUI.DrawTexture(new Rect(ox - sw / 2, oy - sh / 2, sw, sh), disc);

            if (seated)
            {
                var chair = new Color(.463f, .463f, .463f, alpha);
                Seg(P(V(-.17f, .28f)), P(V(.17f, .28f)), lw * .7f, chair);
                Seg(P(V(-.16f, .28f)), P(V(-.16f, 0)), lw * .5f, chair);
                Seg(P(V(.16f, .28f)), P(V(.16f, 0)), lw * .5f, chair);
                Seg(P(V(-.07f, .3f)), P(V(-.1f, .12f)), lw, Col(false));
                Seg(P(V(.07f, .3f)), P(V(.1f, .12f)), lw, Col(false));
            }
            else
            {
                Vector2 fL = f.footL + V(0, f.hop), fR = f.footR + V(0, f.hop);
                Vector2 hL = V(.05f, hipY), hR = V(-.05f, hipY);
                Vector2 kL = Knee(hL, ref fL, 1), kR = Knee(hR, ref fR, -1);
                Seg(P(hL), P(kL), lw, Col(false)); Seg(P(kL), P(fL), lw, Col(false)); Dot(P(fL), lw * .7f, Col(false));
                bool kneeHot = (hot & LegR) != 0;
                Seg(P(hR), P(kR), lw, Col(kneeHot)); Seg(P(kR), P(fR), lw, Col(kneeHot)); Dot(P(fR), lw * .7f, Col(kneeHot));
            }

            Seg(P(hip), P(neck), 1.9f * lw, Col(bodyHot));
            Seg(P(sR), P(sL), 1.2f * lw, Col(bodyHot));
            void Arm(Vector2 s, float side, float a, float b, bool h)
            {
                Vector2 e = s + Dir(a, side) * .17f, w = e + Dir(b, side) * .16f;
                Seg(P(s), P(e), lw, Col(h)); Seg(P(e), P(w), lw, Col(h)); Dot(P(w), lw * .65f, Col(h));
            }
            Arm(sL, 1, f.aL, f.fL, (hot & ArmL) != 0);
            Arm(sR, -1, f.aR, f.fR, (hot & ArmR) != 0);

            // Round head with eyes and a small smile that look the way the body leans.
            float hr = .075f * k * (1 + .03f * Mathf.Sin(t * 7.5f + 1f)), look = Mathf.Sign(f.lean) * Mathf.Min(1, Mathf.Abs(f.lean) / 10) * .012f * k;
            Vector2 hc = P(head);
            Dot(hc, hr, Col(bodyHot));
            var white = new Color(1, 1, 1, alpha);
            Dot(hc + new Vector2(-.028f * k + look, -.012f * k), .011f * k, white);
            Dot(hc + new Vector2(.028f * k + look, -.012f * k), .011f * k, white);
            Dot(hc + new Vector2(-.02f * k + look, .022f * k), .006f * k, white);
            Dot(hc + new Vector2(look, .029f * k), .006f * k, white);
            Dot(hc + new Vector2(.02f * k + look, .022f * k), .006f * k, white);
        }

        static float Shape(float p) => p < .12f ? 0 : p < .34f ? Mathf.SmoothStep(0, 1, (p - .12f) / .22f)
            : p < .76f ? 1 : p < .92f ? 1 - Mathf.SmoothStep(0, 1, (p - .76f) / .16f) : 0;

        void DrawTile(Rect r, Move m, bool seated, float time)
        {
            if (tileDisc == null) tileDisc = Disc(32);
            GUI.color = new Color(.95f, .95f, .95f);
            new GUIStyle { normal = { background = tileDisc }, border = new RectOffset(16, 16, 16, 16) }.Draw(r, false, false, false, false);
            float cap = DigiPhantUi.Px(50);
            var box = new Rect(r.x, r.y + DigiPhantUi.Px(4), r.width, r.height - cap - DigiPhantUi.Px(6));
            float p = Mathf.Repeat(time / m.period, 1), s = m.off ? 0 : Shape(p), u = Mathf.Sin(p * 2 * Mathf.PI);
            float alpha = m.off ? .3f : 1;
            DrawFig(box, m.pose(s, u), m.off ? 0 : m.hot, seated, m.off ? 0 : time, alpha);

            float k = Mathf.Min(box.height * .8f, box.width * .9f), ox = box.center.x, oy = box.yMax - box.height * .08f;
            var muted = DigiPhantUi.Muted; muted.a = Mathf.Clamp01(s * 2);
            if (m.arrows != null)
                for (int i = 0; i + 1 < m.arrows.Length; i += 2)
                    Arrow(new Vector2(ox + m.arrows[i].x * k, oy - m.arrows[i].y * k), new Vector2(ox + m.arrows[i + 1].x * k, oy - m.arrows[i + 1].y * k), .011f * k, muted);
            if (m.hold > 0)
            {
                // Timer ring: fills while the pose is held.
                float prog = p < .34f || p >= .92f ? 0 : Mathf.Min(1, (p - .34f) / .42f);
                Vector2 c = new Vector2(ox + .34f * k, oy - .93f * k);
                for (int i = 0; i < 12; i++)
                {
                    float a = i / 12f * 2 * Mathf.PI - Mathf.PI / 2;
                    Dot(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .07f * k, .012f * k, i < prog * 12 ? DigiPhantUi.Ready : new Color(.8f, .8f, .8f));
                }
                GUI.color = Color.white;
                GUI.Label(new Rect(c.x - .15f * k, c.y + .08f * k, .3f * k, .08f * k), "hold " + m.hold.ToString("0.#") + " s", DigiPhantUi.Text(DigiPhantUi.Heading, DigiPhantUi.Muted, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + DigiPhantUi.Px(6), box.yMax, r.width - DigiPhantUi.Px(12), DigiPhantUi.Px(18)), m.title,
                DigiPhantUi.Text(DigiPhantUi.Body, m.off ? DigiPhantUi.Muted : DigiPhantUi.Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(r.x + DigiPhantUi.Px(6), box.yMax + DigiPhantUi.Px(18), r.width - DigiPhantUi.Px(12), DigiPhantUi.Px(28)), m.sub,
                DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Normal, TextAnchor.UpperCenter));
        }

        // ---------- The pop-up ----------

        static readonly string[] RoleName = { "", "Driver", "Navigator", "Trunk" };

        string Note(int role, bool seated, int people)
        {
            string n = role == 1 ? "Jump goes forward if the elephant is already moving." :
                role == 2 ? "While the elephant rears or turns around, it ignores walking and leaning." :
                "Grabs only work at walking speed or slower, near the log. Stand up between a pick up and a drop.";
            if (role == 1 && people == 1) n += " With 1 person you steer too. T-pose and arms overhead (P2) and the trunk moves (P3) need more people.";
            if (role == 2 && people == 2) n += " With 2 people, P2's right hand curls the trunk too. P3's moves need a third person.";
            if (role == 3 && people > 3) n += " P4 has no moves yet.";
            if (seated) n += " Seated: hands stand in for feet, so a raised hand also lifts that leg.";
            return n;
        }

        void OnGUI()
        {
            if (!controller || Application.isBatchMode) return;
            GUI.depth = -30; // above the status bar and the guided-recording panel
            DigiPhantUi.ScaleOverride = controller.uiScale;
            float Px(float v) => DigiPhantUi.Px(v);
            var previousSkin = GUI.skin;
            GUI.skin = DigiPhantUi.Skin;
            if (!open) { DrawButton(Px); GUI.skin = previousSkin; return; }

            bool seated = controller.upperBodyOnly;
            int people = Mathf.Clamp(controller.performerCount, 1, 4), pages = Mathf.Min(people, 3);
            page = Mathf.Clamp(page, 0, pages - 1);
            int role = page + 1;
            var moves = Moves(role, seated, people);

            GUI.color = new Color(1, 1, 1, .72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), DigiPhantUi.White);
            GUI.color = Color.white;
            float w = Mathf.Min(Px(780), Screen.width - Px(24)), h = Mathf.Min(Px(570), Screen.height - Px(24)), pad = Px(18);
            var card = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            DigiPhantUi.Panel(card, Px(1.5f));

            // Header
            float x = card.x + pad, y = card.y + pad;
            GUI.Label(new Rect(x, y, w, Px(14)), "HOW TO MOVE", DigiPhantUi.Text(DigiPhantUi.Heading, DigiPhantUi.Muted, FontStyle.Bold));
            GUI.Label(new Rect(x, y + Px(14), w - Px(100), Px(26)), "P" + role + "  ·  " + RoleName[role], DigiPhantUi.Text(DigiPhantUi.Title + 4, DigiPhantUi.Ink, FontStyle.Bold));
            string zone = people == 1 ? "the whole view" : role == 1 ? "the left zone" : role == people ? "the right zone" : "the middle zone";
            GUI.Label(new Rect(x, y + Px(41), w - Px(100), Px(16)), "Stand in " + zone + "  ·  " + (seated ? "Seated" : "Full body") + "  ·  " + people + (people == 1 ? " person" : " people"),
                DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted));
            if (GUI.Button(new Rect(card.xMax - pad - Px(72), y, Px(72), Px(DigiPhantUi.ControlHeight)), "Close", DigiPhantUi.Button())) open = false;

            // Footer: note, then Back | P1 P2 P3 | Next
            float navH = Px(DigiPhantUi.ControlHeight + 4), noteH = Px(30);
            var nav = new Rect(x, card.yMax - pad - navH, w - 2 * pad, navH);
            GUI.Label(new Rect(x, nav.y - noteH - Px(4), w - 2 * pad, noteH), Note(role, seated, people), DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Normal, TextAnchor.LowerLeft));

            // Tiles
            var area = new Rect(x, y + Px(66), w - 2 * pad, nav.y - noteH - Px(4) - (y + Px(66)) - Px(6));
            int n = moves.Count, cols = n <= 4 ? n : 3, rows = (n + cols - 1) / cols;
            float gap = Px(10), tw = (area.width - gap * (cols - 1)) / cols, th = Mathf.Min((area.height - gap * (rows - 1)) / rows, tw * 1.35f);
            float top = area.y + (area.height - (rows * th + (rows - 1) * gap)) / 2, time = Time.unscaledTime;
            for (int i = 0; i < n; i++)
            {
                int c = i % cols, r = i / cols, inRow = Mathf.Min(cols, n - r * cols);
                float left = area.x + (area.width - (inRow * tw + (inRow - 1) * gap)) / 2;
                if (Event.current.type == EventType.Repaint)
                    DrawTile(new Rect(left + c * (tw + gap), top + r * (th + gap), tw, th), moves[i], seated, time);
            }
            GUI.color = Color.white;

            GUILayout.BeginArea(nav);
            GUILayout.BeginHorizontal();
            GUI.enabled = page > 0;
            if (GUILayout.Button("Back", GUILayout.Width(Px(72)))) page--;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            var labels = new string[pages];
            for (int i = 0; i < pages; i++) labels[i] = "P" + (i + 1);
            GUILayout.BeginHorizontal(GUILayout.Width(Px(48) * pages));
            page = DigiPhantUi.Segmented(null, page, labels);
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(page < pages - 1 ? "Next" : "Done", DigiPhantUi.Button(DigiPhantUi.Body, true), GUILayout.Width(Px(72))))
            { if (page < pages - 1) page++; else open = false; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Modal: nothing behind the card reacts to the mouse.
            var e = Event.current;
            if (e.isMouse || e.type == EventType.ScrollWheel) e.Use();
            GUI.skin = previousSkin;
        }

        // The "How to move" button: top left of the stage, next to the control panel. If the window is too narrow
        // for the pose-action bar (top right), it moves just below that bar's row instead of overlapping it.
        void DrawButton(Func<float, float> Px)
        {
            if (!showButton) return;
            var style = DigiPhantUi.Button();
            float bw = style.CalcSize(new GUIContent("How to move")).x + Px(8), bh = Px(DigiPhantUi.ControlHeight);
            float left = (controller.showControls ? DigiPhantUi.PanelWidth : 0) + Px(12), y = Px(12);
            var pose = GetComponent<DigiPhantPoseActions>();
            if (pose && pose.isActiveAndEnabled && pose.showSignals && left + bw + Px(12) > Screen.width - Px(720)) y = Px(60);
            if (GUI.Button(new Rect(left, y, bw, bh), "How to move", style)) { open = true; page = 0; }
        }
    }
}
