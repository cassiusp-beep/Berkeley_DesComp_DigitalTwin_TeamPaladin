using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigiPhant
{
    // "How to move" pop-up: Setup, one card per role (P1 driver, P2 navigator, P3 trunk), Hand gestures and, when the
    // scene has Trunk Trail checkpoints, the course challenges. Roles have a version for Full body and one for Seated. Each move is a little stick figure that loops the motion, drawn from discs and rotated bars
    // (no imported art). Adds itself next to DigiPhantController in Play mode, so no scene needs editing.
    // Opens by itself the first time the input switches to Camera, and from the "How to move" button.
    // Captions follow the real controls: DigiPhantPoseActions, DigiPhantTrunkActions, DigiPhantLocomotion,
    // DigiPhantGestureActions, TrunkCheckpoint (its Hint text), the scene's body-part mapping, and Tracking/bridge.py
    // (seated: hands replace feet, shoulder tilt replaces lean; the preview status lines).
    public class DigiPhantTutorial : MonoBehaviour
    {
        [Tooltip("Open the pop-up by itself the first time the input switches to Camera.")]
        public bool openOnFirstCamera = true;
        public bool showButton = true;

        DigiPhantController controller;
        bool open, seenCamera;
        int page;
        TrunkCheckpoint[] course = new TrunkCheckpoint[0];
        float nextScan;
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
            public float kneeLift;                          // 0..1: right thigh comes forward and up, toward the viewer
            public Vector2 footL, footR;                    // L = the performer's left = the viewer's right
            public static Fig Lerp(Fig a, Fig b, float t) => new Fig
            {
                lean = Mathf.Lerp(a.lean, b.lean, t), kneeLift = Mathf.Lerp(a.kneeLift, b.kneeLift, t), crouch = Mathf.Lerp(a.crouch, b.crouch, t), hop = Mathf.Lerp(a.hop, b.hop, t),
                aL = Mathf.Lerp(a.aL, b.aL, t), fL = Mathf.Lerp(a.fL, b.fL, t), aR = Mathf.Lerp(a.aR, b.aR, t), fR = Mathf.Lerp(a.fR, b.fR, t),
                footL = Vector2.Lerp(a.footL, b.footL, t), footR = Vector2.Lerp(a.footR, b.footR, t)
            };
        }
        static readonly Fig Rest = new Fig { aL = 8, fL = 4, aR = 8, fR = 4, footL = new Vector2(.07f, 0), footR = new Vector2(-.07f, 0) };
        static Fig L(Fig f, float a, float b) { f.aL = a; f.fL = b; return f; }
        static Fig R(Fig f, float a, float b) { f.aR = a; f.fR = b; return f; }
        static Fig Lean(Fig f, float d) { f.lean = d; return f; }
        static Fig Squat(Fig f) { f.crouch = 1; f.footL = new Vector2(.17f, 0); f.footR = new Vector2(-.17f, 0); return f; }
        static Fig KneeUp(Fig f) { f.footR = new Vector2(-.09f, .2f); f.kneeLift = 1; f.hop = .04f; return f; }
        static Vector2 V(float x, float y) => new Vector2(x, y);
        const float LeanDeg = 13f;

        const int ArmL = 1, ArmR = 2, LegR = 8, Body = 16;
        // Extra drawing on a tile: props and hand pictograms. The tile's figure (pose) is optional.
        enum Prop { None, Panel, Zones, Frame, Preview, Neutral, NeutralHand, Palm, Victory, ThumbUp, ThumbDown, Hurdle, Deliver, Lane, Rolling, Arch, Toggle }
        class Move
        {
            public Prop prop;
            public int people;                   // for props that show the performers
            public float num;                    // a number the prop shows (the neutral-hand countdown)
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

            // Seated, hands in the lap drop out of frame and the neutral pose silently fails to save.
            Add("Start here", seated ? "Both hands on the desk, in view"
                : role == 1 ? "Left hand at your waist" : role == 2 ? "Arms down by your sides" : "Right hand at your waist", (s, u) => rest);
            if (role == 1)
            {
                Add("Walk", "Left hand up from waist to chest", To(L(rest, 20, -155)), ArmL, 0, 3f, V(.27f, .6f), V(.27f, .8f));
                Add("Run", "Left hand above your shoulder", To(L(rest, 110, 175)), ArmL, 0, 3f, V(.36f, .8f), V(.36f, 1f));
                Add("Back up", "Left hand down by your thigh", To(L(rest, 8, 4)), ArmL, 0, 3f, V(.27f, .62f), V(.27f, .4f));
                if (seated) Add("Jump", "Quick right hand raise", To(R(rest, 150, 175)), ArmR, 0, 2f, V(-.36f, .74f), V(-.36f, .96f));
                else Add("Jump", "Quick right knee lift", To(KneeUp(rest)), LegR, 0, 2f, V(-.3f, .12f), V(-.3f, .32f));
                if (people == 1) Add("Turn", seated ? "Tilt your shoulders left or right" : "Lean left or right", (s, u) => Lean(rest, u * LeanDeg), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
            }
            else if (role == 2)
            {
                Add("Turn", seated ? "Tilt your shoulders left or right" : "Lean left or right", (s, u) => Lean(rest, u * LeanDeg), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
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
                Add("Lean", seated ? "Tilt shoulders: head turns, trunk swings" : "Head turns, trunk swings", (s, u) => Lean(rest, u * LeanDeg), Body, 0, 3.4f, V(.2f, .82f), V(.34f, .82f), V(-.2f, .82f), V(-.34f, .82f));
                Add("Ears", "Hands apart flaps the ears", To(L(R(rest, 70, 80), 70, 80)), ArmL | ArmR, 0, 2f, V(.5f, .55f), V(.56f, .55f), V(-.5f, .55f), V(-.56f, .55f));
                Add("Trunk curl", "Right hand up from your waist", To(R(rest, 60, 170)), ArmR, 0, 3f, V(-.34f, .72f), V(-.34f, .94f));
            }
            return list;
        }

        // ---------- The other pages: Setup, Hand gestures, Trunk Trail ----------

        static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        static Fig P1Rest => L(Rest, 25, -80);                // one hand resting at the waist
        static Fig P3Rest => R(Rest, 25, -80);
        static Fig OnDesk => L(R(Rest, 40, 80), 40, 80);       // seated: both hands on the desk, in view

        // What a tap on the left panel and the preview window show, in the order the user hits them.
        List<Move> SetupMoves(bool seated, int people)
        {
            string ids = "";
            for (int i = 1; i <= Mathf.Min(people, 4); i++) ids += (i > 1 ? " " : "") + "P" + i;
            return new List<Move>
            {
                new Move { title = "Pick People and Body", sub = "Left panel: set People and Body, then switch Input to Camera", prop = Prop.Panel, people = people, period = 6f },
                new Move { title = "Where to stand", prop = Prop.Zones, people = people, period = 4f,
                    sub = people == 1 ? "In front of the camera, 2.5 to 3 m back" : "One person per red strip, P1 on the left. Same distance, 2.5 to 3 m back" },
                new Move { title = "Stay in frame", prop = Prop.Frame, period = 3f,
                    sub = seated ? "Head, shoulders and both hands in frame" : "The whole body in frame, head to feet" },
                new Move { title = "Read the preview", prop = Prop.Preview, people = people, period = 10f,
                    sub = "\"Tracking " + ids + "\" is good. A grey skeleton is not assigned yet" },
                new Move { title = "Neutral pose", prop = Prop.Neutral, people = people, hold = 10f, period = 12f,
                    sub = "Click Set neutral pose (10 s), hold still until Neutral pose saved" },
            };
        }

        List<Move> HandMoves()
        {
            var g = GetComponent<DigiPhantGestureActions>();
            float hold = g ? g.holdSeconds : .35f, countdown = g ? g.neutralCountdownSeconds : 5f, celebrate = g ? g.celebrateSeconds : 2.5f;
            return new List<Move>
            {
                new Move { title = "Neutral hand", prop = Prop.NeutralHand, num = countdown, period = 7f,
                    sub = "After Neutral pose, press Neutral hand and show an open palm for " + N(countdown) + " s" },
                new Move { title = "Open palm", prop = Prop.Palm, period = 3f, sub = "Arms the next gesture. Show it again after every gesture" },
                new Move { title = "Victory", prop = Prop.Victory, hold = hold, period = 3f, sub = "Ears flap and the head shakes for " + N(celebrate) + " s" },
                new Move { title = "Thumb up", prop = Prop.ThumbUp, hold = hold, period = 4f, sub = "Walks a full circle clockwise. Body steering is ignored meanwhile" },
                new Move { title = "Thumb down", prop = Prop.ThumbDown, hold = hold, period = 4f, sub = "Walks a full circle anticlockwise. Body steering is ignored meanwhile" },
            };
        }

        // One tile per kind of checkpoint in the scene. The captions reuse each checkpoint's own Hint.
        List<Move> CourseMoves(bool seated)
        {
            var list = new List<Move>();
            var pa = GetComponent<DigiPhantPoseActions>();
            TrunkCheckpoint Find(params CheckpointMode[] modes)
            {
                foreach (var c in course) if (c && Array.IndexOf(modes, c.mode) >= 0) return c;
                return null;
            }
            string Body(TrunkCheckpoint c)
            {
                string h = c.Hint;
                int i = h.IndexOf(": ", StringComparison.Ordinal);
                if (i >= 0) h = h.Substring(i + 2);
                return h.Length > 0 ? char.ToUpper(h[0]) + h.Substring(1) : h;
            }
            bool Bonus(TrunkCheckpoint c) => c && c.progress && Array.IndexOf(c.progress.bonusCheckpoints, c) >= 0;

            var hurdle = Find(CheckpointMode.Hurdle);
            if (hurdle)
            {
                Fig rest = P1Rest;
                if (seated)
                    list.Add(new Move { title = "Hurdle", sub = "P" + (pa ? pa.driver : 1) + " raise the right hand quickly to jump", prop = Prop.Hurdle, hot = ArmR, period = 2f,
                        pose = (s, u) => Fig.Lerp(rest, R(rest, 150, 175), s) });
                else
                    list.Add(new Move { title = "Hurdle", sub = Body(hurdle), prop = Prop.Hurdle, hot = LegR, period = 2f,
                        pose = (s, u) => Fig.Lerp(rest, KneeUp(rest), s) });
            }
            var carry = Find(CheckpointMode.Carry);
            var deliver = Find(CheckpointMode.Deliver);
            if (carry || deliver)
            {
                Fig rest = P3Rest;
                string sub = seated ? "Standing only: it needs a squat"
                    : carry ? Body(carry) + ", then carry it to the finish stone" : Body(deliver);
                list.Add(new Move { title = "Pick up and deliver", sub = sub, prop = Prop.Deliver, off = seated, hot = ArmR | 4 | LegR, hold = 1f, period = 5f,
                    pose = (s, u) => Fig.Lerp(rest, Squat(R(rest, 35, 15)), s) });
            }
            var tunnel = Find(CheckpointMode.Tunnel);
            var beam = Find(CheckpointMode.Beam);
            if (tunnel || beam)
            {
                Fig rest = Rest;
                string sub = Body(tunnel ? tunnel : beam) + (beam && Bonus(beam) ? (tunnel ? ". The beam is in the optional level" : " (optional level)") : "");
                list.Add(new Move { title = tunnel && beam ? "Tunnel and Beam" : tunnel ? "Tunnel" : "Beam", sub = sub, prop = Prop.Lane, period = 3.4f,
                    pose = (s, u) => Lean(rest, u * 5f) });
            }
            var rolling = Find(CheckpointMode.Rolling);
            if (rolling)
            {
                Fig rest = Rest;
                list.Add(new Move { title = "Rolling logs", sub = Body(rolling) + (Bonus(rolling) ? " (optional level)" : ""), prop = Prop.Rolling, period = 4f,
                    pose = (s, u) => Lean(rest, -12f * s) }); // leans out of the way as the log passes
            }
            if (Find(CheckpointMode.Finish))
                list.Add(new Move { title = "Finish arch", sub = "Walk through it to stop the timer", prop = Prop.Arch, period = 3.5f });
            if (Bonus(tunnel) || Bonus(beam) || Bonus(rolling))
                list.Add(new Move { title = "Optional level", sub = "Bottom-left bar: switch it On to open the bonus lane", prop = Prop.Toggle, period = 4f });
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
            float hipX = -Mathf.Sin(lr) * .06f; // the hips sway the other way, so the feet stay planted
            Vector2 hip = V(hipX, hipY), neck = hip + up * .28f, head = neck + up * .095f;
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
                Seg(P(V(hipX - .07f, .3f)), P(V(-.1f, .12f)), lw, Col(false));
                Seg(P(V(hipX + .07f, .3f)), P(V(.1f, .12f)), lw, Col(false));
            }
            else
            {
                Vector2 fL = f.footL + V(0, f.hop), fR = f.footR + V(0, f.hop);
                Vector2 hL = V(hipX + .05f, hipY), hR = V(hipX - .05f, hipY);
                Vector2 kL = Knee(hL, ref fL, 1), kR = Knee(hR, ref fR, -1);
                if (f.kneeLift > 0) kR = Vector2.Lerp(kR, V(hR.x - .1f, hipY - .05f), f.kneeLift); // knee forward and up: a short, thick thigh
                Seg(P(hL), P(kL), lw, Col(false)); Seg(P(kL), P(fL), lw, Col(false)); Dot(P(fL), lw * .7f, Col(false));
                bool kneeHot = (hot & LegR) != 0;
                Seg(P(hR), P(kR), lw * (1 + .35f * f.kneeLift), Col(kneeHot)); Seg(P(kR), P(fR), lw, Col(kneeHot)); Dot(P(fR), lw * .7f, Col(kneeHot));
            }

            Seg(P(hip), P(neck), 1.9f * lw, Col(bodyHot));
            Seg(P(sR), P(sL), 1.2f * lw, Col(bodyHot));
            void Arm(Vector2 s, float side, float a, float b, bool h)
            {
                float follow = side * f.lean * .35f; // arms swing a little with the leaning trunk
                float Swing(float v) => Mathf.Max(v + follow, Mathf.Min(v, 14f)); // the arm on the leaning side stays clear of the trunk
                Vector2 e = s + Dir(Swing(a), side) * .17f, w = e + Dir(Swing(b), side) * .16f;
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

        // ---------- Props and hand pictograms ----------

        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, DigiPhantUi.White); }

        static void Rim(Rect r, Color c, float w)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        static void Txt(Rect r, string text, float size, Color c, FontStyle style = FontStyle.Bold, TextAnchor align = TextAnchor.MiddleCenter)
        {
            GUI.color = Color.white;
            GUI.Label(r, text, DigiPhantUi.Text(size, c, style, align));
        }

        // A hand drawn from rounded bars, about 0.6 tall by 0.4 wide at scale s (c = the middle of the palm).
        static void Hand(Prop kind, Vector2 c, float s, Color col)
        {
            Vector2 H(float x, float y) => new Vector2(c.x + x * s, c.y - y * s);
            var white = new Color(1, 1, 1, col.a);
            switch (kind)
            {
                case Prop.Palm:
                case Prop.NeutralHand:
                {
                    Seg(H(-.06f, -.12f), H(.06f, -.12f), .26f * s, col);   // palm
                    float[] len = { .27f, .35f, .37f, .29f };
                    for (int i = 0; i < 4; i++) Seg(H(-.075f + .05f * i, -.04f), H(-.075f + .05f * i + (i - 1.5f) * .035f, -.04f + len[i]), .095f * s, col);
                    Seg(H(-.15f, -.14f), H(-.28f, .04f), .105f * s, col);   // thumb
                    break;
                }
                case Prop.Victory:
                {
                    Seg(H(-.06f, -.14f), H(.06f, -.14f), .28f * s, col);   // fist
                    Seg(H(-.05f, -.05f), H(-.15f, .34f), .1f * s, col);     // index
                    Seg(H(.05f, -.05f), H(.15f, .34f), .1f * s, col);       // middle
                    Seg(H(-.01f, -.2f), H(.17f, -.2f), .018f * s, white);   // curled fingers
                    Seg(H(-.01f, -.13f), H(.17f, -.13f), .018f * s, white);
                    break;
                }
                case Prop.ThumbUp:
                case Prop.ThumbDown:
                {
                    float d = kind == Prop.ThumbUp ? 1 : -1;
                    Vector2 T(float x, float y) => H(x, d * y);
                    Seg(T(-.06f, -.08f), T(.06f, -.08f), .3f * s, col);     // fist
                    Seg(T(-.04f, 0f), T(-.04f, .3f), .115f * s, col);       // thumb
                    for (int i = 0; i < 3; i++) Seg(T(.02f, -.14f + .06f * i), T(.19f, -.14f + .06f * i), .016f * s, white);
                    break;
                }
            }
        }

        // Desk for the seated figure, with the hands (OnDesk) resting on it.
        static void Desk(System.Func<float, float, Vector2> P, float lw, float alpha)
        {
            var c = new Color(.463f, .463f, .463f, alpha);
            Seg(P(-.46f, .385f), P(.46f, .385f), lw * 1.1f, c);
            Seg(P(-.44f, .385f), P(-.44f, 0), lw * .6f, c);
            Seg(P(.44f, .385f), P(.44f, 0), lw * .6f, c);
        }

        // Several small figures side by side (the zones and the neutral pose).
        void Row(Rect box, int n, Func<int, Fig> pose, bool seated, float time, bool strips, out float ground)
        {
            float sw = box.width * .94f / n, x0 = box.center.x - box.width * .94f / 2;
            var fb = new Rect(0, box.y + box.height * .3f, sw * 1.45f, box.height * .7f);
            float kf = Mathf.Min(fb.height * .8f, fb.width * .9f);
            ground = fb.yMax - fb.height * .08f;
            if (strips)
            {
                // Red zone strips under the feet, numbered.
                var red = DigiPhantUi.Stop; red.a = .6f + .15f * Mathf.Sin(time * 3);
                for (int i = 0; i < n; i++)
                {
                    var strip = new Rect(x0 + sw * i + 3, ground - .03f * kf, sw - 6, .15f * kf);
                    Fill(strip, red);
                    Txt(new Rect(strip.x, strip.y + strip.height * .35f, strip.width, strip.height * .65f), "P" + (i + 1), DigiPhantUi.Small, Color.white);
                }
            }
            for (int i = 0; i < n; i++)
                DrawFig(new Rect(x0 + sw * (i + .5f) - fb.width / 2, fb.y, fb.width, fb.height), pose(i), 0, seated, time + i, 1);
        }

        void DrawProp(Move m, Rect box, float p, float s, float time, bool seated, bool front)
        {
            float k = Mathf.Min(box.height * .8f, box.width * .9f), ox = box.center.x, oy = box.yMax - box.height * .08f, lw = .03f * k;
            Vector2 P(float x, float y) => new Vector2(ox + x * k, oy - y * k);
            var grey = new Color(.463f, .463f, .463f);
            int people = Mathf.Clamp(m.people, 1, 4);
            Fig Who(int i) => i == 0 ? P1Rest : i == 2 ? P3Rest : Rest; // each performer's rest pose
            float a = m.off ? .3f : 1;
            Color Tint(Color c) { c.a = a; return c; }

            switch (m.prop)
            {
                case Prop.Panel:
                    if (front) break;
                    {
                        // A copy of the left control panel: the options picked one row at a time.
                        var pr = new Rect(box.x + box.width * .05f, box.y + box.height * .03f, box.width * .9f, box.height * .9f);
                        DigiPhantUi.Panel(pr, 1);
                        string[] caption = { "People", "Body", "Input" };
                        string[][] options = { new[] { "1", "2", "3", "4" }, new[] { "Full body", "Seated" }, new[] { "Sliders", "Camera" } };
                        int[] target = { people - 1, seated ? 1 : 0, 1 };
                        int stage = Mathf.Min(2, Mathf.FloorToInt(p * 3));
                        float rowH = (pr.height - 6) / 3;
                        for (int r = 0; r < 3; r++)
                        {
                            var row = new Rect(pr.x + 4, pr.y + 3 + r * rowH, pr.width - 8, rowH - 2);
                            Txt(new Rect(row.x + 2, row.y, row.width * .27f, row.height), caption[r], DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
                            float cx = row.x + row.width * .3f, cw = row.width * .7f / options[r].Length;
                            int picked = stage >= r ? target[r] : 0;
                            for (int i = 0; i < options[r].Length; i++)
                            {
                                var cell = new Rect(cx + i * cw, row.y + row.height * .12f, cw - 2, row.height * .76f);
                                bool on = i == picked;
                                Fill(cell, on ? DigiPhantUi.Ink : Color.white);
                                Rim(cell, DigiPhantUi.Outline, 1);
                                Txt(cell, options[r][i], DigiPhantUi.Small, on ? Color.white : DigiPhantUi.Ink);
                            }
                            if (stage == r) Rim(new Rect(row.x, row.y, row.width, row.height), DigiPhantUi.Ready, 2);
                        }
                    }
                    break;

                case Prop.Zones:
                    if (front) break;
                    {
                        Row(box, people, Who, seated, time, true, out float ground);
                        float sw = box.width * .94f / people, x0 = box.center.x - box.width * .94f / 2;
                        var cam = new Vector2(box.center.x, box.y + box.height * .1f);
                        // The camera and the lines of its view.
                        var thin = new Color(.463f, .463f, .463f, .5f);
                        Seg(cam + new Vector2(-.05f * k, .06f * k), new Vector2(x0 + 4, ground), .008f * k, thin);
                        Seg(cam + new Vector2(.05f * k, .06f * k), new Vector2(x0 + sw * people - 4, ground), .008f * k, thin);
                        Seg(cam + new Vector2(-.09f * k, 0), cam + new Vector2(.09f * k, 0), .13f * k, DigiPhantUi.Ink);
                        Dot(cam, .042f * k, Color.white);
                        Dot(cam, .02f * k, DigiPhantUi.Ink);
                        Txt(new Rect(cam.x + .17f * k, cam.y - .06f * k, .7f * k, .12f * k), "2.5 to 3 m", DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
                    }
                    break;

                case Prop.Frame:
                    if (front) break;
                    {
                        if (seated) Desk(P, lw, 1);
                        DrawFig(box, seated ? OnDesk : Rest, 0, seated, time, 1);
                        float bottom = seated ? .15f : -.03f, top = seated ? .86f : 1f, half = seated ? .45f : .4f, grow = .012f * k * Mathf.Sin(time * 3), len = .12f * k;
                        var gold = DigiPhantUi.Ready;
                        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                        {
                            var at = P(corner.x * half, corner.y < 0 ? bottom : top) + new Vector2(corner.x * grow, -corner.y * grow);
                            Seg(at, at + new Vector2(-corner.x * len, 0), lw * .7f, gold);
                            Seg(at, at + new Vector2(0, corner.y * len), lw * .7f, gold);
                        }
                    }
                    break;

                case Prop.Preview:
                    if (front) break;
                    {
                        // The status lines of the tracker window: each one says what to fix.
                        string ids = "";
                        for (int i = 1; i <= people; i++) ids += (i > 1 ? " " : "") + "P" + i;
                        string who = people >= 2 ? "P2" : "P1";
                        string[] line =
                        {
                            "Tracking " + ids, "Waiting: nobody in " + who + " zone", "Waiting: " + who + " too close to camera",
                            "Waiting: " + who + " too far from camera", "1 person ignored: show " + (seated ? "shoulders" : "shoulders + hips"),
                        };
                        string[] fix = { "Good. Go on to the neutral pose", "Step into that strip", "Step back", "Step closer", "Get more of you in the picture" };
                        int stage = Mathf.Min(4, Mathf.FloorToInt(p * 5));
                        var pv = new Rect(box.x + box.width * .05f, box.y + box.height * .04f, box.width * .9f, box.height * .5f);
                        Fill(pv, new Color(.13f, .13f, .13f));
                        Txt(new Rect(pv.x + 6, pv.y, pv.width * .62f, pv.height), line[stage], DigiPhantUi.Small, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
                        // A grey skeleton: seen, but not assigned to anyone.
                        float h = pv.height, sx = pv.xMax - pv.width * .14f, g = .6f;
                        var sk = new Color(g, g, g);
                        Dot(new Vector2(sx, pv.y + .24f * h), .07f * h, sk);
                        Seg(new Vector2(sx, pv.y + .32f * h), new Vector2(sx, pv.y + .62f * h), .03f * h, sk);
                        Seg(new Vector2(sx - .12f * h, pv.y + .45f * h), new Vector2(sx + .12f * h, pv.y + .45f * h), .03f * h, sk);
                        Seg(new Vector2(sx, pv.y + .62f * h), new Vector2(sx - .08f * h, pv.y + .9f * h), .03f * h, sk);
                        Seg(new Vector2(sx, pv.y + .62f * h), new Vector2(sx + .08f * h, pv.y + .9f * h), .03f * h, sk);
                        Txt(new Rect(pv.x, pv.yMax + 2, pv.width, box.height * .3f), fix[stage], DigiPhantUi.Body, stage == 0 ? DigiPhantUi.Go : DigiPhantUi.Ready, FontStyle.Bold, TextAnchor.UpperCenter);
                    }
                    break;

                case Prop.Neutral:
                    if (front) break;
                    {
                        if (seated)
                        {
                            Desk(P, lw, 1);
                            DrawFig(box, OnDesk, 0, true, time, 1);
                        }
                        else Row(box, Mathf.Min(people, 3), Who, false, time, false, out _);
                        float saved = Mathf.Clamp01((p - .76f) * 8) * (p < .94f ? 1 : 0);
                        if (saved > 0) Txt(new Rect(box.x + 4, box.y + 2, box.width * .55f, box.height * .14f), "Neutral pose saved", DigiPhantUi.Small, new Color(DigiPhantUi.Go.r, DigiPhantUi.Go.g, DigiPhantUi.Go.b, saved));
                    }
                    break;

                case Prop.NeutralHand:
                    if (front) break;
                    {
                        float countdownEnd = .76f, startAt = .12f;
                        bool counting = p >= startAt && p < countdownEnd;
                        bool pressed = p > .04f && p < startAt;
                        var button = new Rect(box.x + box.width * .08f, box.y + box.height * .03f, box.width * .5f, box.height * .15f);
                        Fill(button, pressed ? DigiPhantUi.Ink : Color.white);
                        Rim(button, DigiPhantUi.Ink, 1.5f);
                        Txt(button, "Neutral hand", DigiPhantUi.Small, pressed ? Color.white : DigiPhantUi.Ink);
                        var right = new Rect(button.xMax + 6, button.y, box.xMax - button.xMax - 10, button.height);
                        if (counting) Txt(right, Mathf.CeilToInt(m.num * (1 - (p - startAt) / (countdownEnd - startAt))).ToString(), DigiPhantUi.Title + 4, DigiPhantUi.Ready);
                        else if (p >= countdownEnd && p < .94f) Txt(right, "Saved", DigiPhantUi.Body, DigiPhantUi.Go);
                        if (p > .2f) Hand(Prop.NeutralHand, P(0, .26f), .95f * k, Tint(DigiPhantUi.Ink));
                    }
                    break;

                case Prop.Palm:
                    if (front) break;
                    {
                        var saved = GUI.matrix;
                        GUIUtility.RotateAroundPivot(10 * Mathf.Sin(p * 4 * Mathf.PI), P(0, 0));
                        Hand(Prop.Palm, P(0, .3f), 1.2f * k, DigiPhantUi.Ink);
                        GUI.matrix = saved;
                    }
                    break;

                case Prop.Victory:
                    if (front) break;
                    {
                        var saved = GUI.matrix;
                        GUIUtility.RotateAroundPivot(14 * s * Mathf.Sin(p * 12 * Mathf.PI), P(-.2f, 0));
                        Hand(Prop.Victory, P(-.2f, .3f), 1f * k, DigiPhantUi.Ink);
                        GUI.matrix = saved;
                        // The head shakes and the ears flap while the sign is held.
                        float shake = Mathf.Sin(p * 10 * Mathf.PI) * .03f * k * s, flap = (1 - Mathf.Cos(p * 12 * Mathf.PI)) / 2 * .06f * k * s;
                        Vector2 hc = P(.3f, .38f) + new Vector2(shake, 0);
                        var inner = new Color(.8f, .8f, .8f);
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Dot(hc + new Vector2(side * (.15f * k + flap), -.05f * k), .1f * k, DigiPhantUi.Ink);
                            Dot(hc + new Vector2(side * (.15f * k + flap), -.05f * k), .06f * k, inner);
                        }
                        Dot(hc, .13f * k, DigiPhantUi.Ink);
                        Dot(hc + new Vector2(-.05f * k, -.02f * k), .02f * k, Color.white);
                        Dot(hc + new Vector2(.05f * k, -.02f * k), .02f * k, Color.white);
                    }
                    break;

                case Prop.ThumbUp:
                case Prop.ThumbDown:
                    if (front) break;
                    {
                        // The hand in a ring: a dot walks round it clockwise (thumb up) or anticlockwise (thumb down).
                        float dir = m.prop == Prop.ThumbUp ? 1 : -1, radius = .42f * k;
                        Vector2 mid = P(0, .44f);
                        Hand(m.prop, mid + new Vector2(0, dir > 0 ? .05f * k : -.05f * k), .85f * k, DigiPhantUi.Ink);
                        Vector2 At(float turn) { float t = (-.25f + dir * turn) * 2 * Mathf.PI; return mid + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius; }
                        for (int i = 0; i < 28; i++) Dot(At(i / 28f), .008f * k, new Color(.7f, .7f, .7f));
                        Arrow(At(-.05f), At(.03f), .011f * k, DigiPhantUi.Muted);
                        float walk = Mathf.Repeat(p, 1);
                        Dot(At(walk), .035f * k, DigiPhantUi.Ready);
                    }
                    break;

                case Prop.Hurdle:
                    if (front) break;
                    {
                        Seg(P(.26f, 0), P(.26f, .2f), lw * .8f, grey);
                        Seg(P(.5f, 0), P(.5f, .2f), lw * .8f, grey);
                        Seg(P(.24f, .2f), P(.52f, .2f), lw * 1.1f, DigiPhantUi.Ink);
                    }
                    break;

                case Prop.Deliver:
                {
                    // The log sits by the right hand (the viewer's left), is held, then travels to the finish stone.
                    float travel = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .92f, p));
                    if (!front)
                    {
                        GUI.color = Tint(new Color(.8f, .8f, .8f));
                        if (!disc) disc = Disc(64);
                        GUI.DrawTexture(new Rect(P(.4f, 0).x - .15f * k, oy - .035f * k, .3f * k, .07f * k), disc);
                        Dot(P(.4f, .005f), .02f * k, Tint(DigiPhantUi.Ready));
                    }
                    else
                    {
                        float lx = Mathf.Lerp(-.3f, .4f, travel), ly = .05f + .3f * Mathf.Sin(Mathf.PI * travel);
                        bool held = p >= .34f && p < .76f;
                        Seg(P(lx - .07f, ly), P(lx + .07f, ly), .055f * k, Tint(held ? DigiPhantUi.Ready : DigiPhantUi.Muted));
                    }
                    break;
                }

                case Prop.Lane:
                    if (front) break;
                    Seg(P(-.42f, 0), P(-.17f, .72f), lw * .7f, grey);
                    Seg(P(.42f, 0), P(.17f, .72f), lw * .7f, grey);
                    for (int i = 0; i < 4; i++) Seg(P(0, .08f + .2f * i), P(0, .15f + .2f * i), lw * .35f, new Color(.7f, .7f, .7f));
                    break;

                case Prop.Rolling:
                    if (!front) break;
                    {
                        float lx = Mathf.Lerp(.6f, -.6f, p), turn = p * 22f;
                        Vector2 c = P(lx, .09f);
                        Dot(c, .09f * k, grey);
                        Dot(c, .065f * k, new Color(.95f, .95f, .95f));
                        Seg(c, c + new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * .06f * k, lw * .5f, grey);
                    }
                    break;

                case Prop.Arch:
                    if (front) break;
                    {
                        Seg(P(-.34f, 0), P(-.34f, .85f), lw * 1.4f, grey);
                        Seg(P(.34f, 0), P(.34f, .85f), lw * 1.4f, grey);
                        Seg(P(-.37f, .88f), P(.37f, .88f), lw * 2.2f, DigiPhantUi.Ink);
                        Txt(new Rect(ox - .4f * k, oy - 1.04f * k, .8f * k, .12f * k), "FINISH", DigiPhantUi.Small, DigiPhantUi.Muted);
                        // Walks away from the viewer and through the arch.
                        float q = Mathf.Repeat(p, 1), f = Mathf.Lerp(1, .62f, q), fade = Mathf.Clamp01(Mathf.Min(q, 1 - q) * 6);
                        float h = box.height * f, yMax = box.yMax - (1 - f) * box.height * .24f;
                        Fig w = Rest;
                        w.aL = 12 + 14 * Mathf.Sin(time * 6); w.aR = 12 - 14 * Mathf.Sin(time * 6);
                        DrawFig(new Rect(box.x, yMax - h, box.width, h), w, 0, false, time, fade);
                    }
                    break;

                case Prop.Toggle:
                    if (front) break;
                    {
                        bool on = p > .3f && p < .9f;
                        var button = new Rect(box.x + box.width * .1f, box.y + box.height * .12f, box.width * .8f, box.height * .2f);
                        Fill(button, on ? DigiPhantUi.Ink : Color.white);
                        Rim(button, DigiPhantUi.Ink, 1.5f);
                        Txt(button, "Optional level: " + (on ? "On" : "Off"), DigiPhantUi.Body, on ? Color.white : DigiPhantUi.Ink);
                        if (on)
                        {
                            Txt(new Rect(box.x, button.yMax + 6, box.width, box.height * .14f), "Bonus lane opens", DigiPhantUi.Small, DigiPhantUi.Muted);
                            Vector2 c = P(0, .26f);
                            Dot(c + new Vector2(-.2f * k, 0), .09f * k, grey);
                            Dot(c + new Vector2(-.2f * k, 0), .065f * k, new Color(.95f, .95f, .95f));
                            Seg(c + new Vector2(.05f * k, .02f * k), c + new Vector2(.4f * k, .02f * k), lw * 1.2f, grey);
                        }
                        else Txt(new Rect(box.x, button.yMax + 6, box.width, box.height * .14f), "Barrier closed", DigiPhantUi.Small, DigiPhantUi.Muted);
                    }
                    break;
            }
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
            if (m.prop != Prop.None) DrawProp(m, box, p, s, m.off ? 0 : time, seated, false);
            if (m.pose != null) DrawFig(box, m.pose(s, u), m.off ? 0 : m.hot, seated, m.off ? 0 : time, alpha);
            if (m.prop != Prop.None) DrawProp(m, box, p, s, m.off ? 0 : time, seated, true);

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
                GUI.Label(new Rect(c.x - .15f * k, c.y + .08f * k, .3f * k, .08f * k), "hold " + N(m.hold) + " s", DigiPhantUi.Text(DigiPhantUi.Heading, DigiPhantUi.Muted, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + DigiPhantUi.Px(6), box.yMax, r.width - DigiPhantUi.Px(12), DigiPhantUi.Px(18)), m.title,
                DigiPhantUi.Text(DigiPhantUi.Body, m.off ? DigiPhantUi.Muted : DigiPhantUi.Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(r.x + DigiPhantUi.Px(6), box.yMax + DigiPhantUi.Px(18), r.width - DigiPhantUi.Px(12), DigiPhantUi.Px(28)), m.sub,
                DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Normal, TextAnchor.UpperCenter));
        }

        // ---------- The pop-up ----------

        static readonly string[] RoleName = { "", "Driver", "Navigator", "Trunk" };

        // The line above the buttons: what else to know about the page.
        string Note(int kind, bool seated, int people)
        {
            if (kind == 0)
                return (seated ? "Seated: both hands on the desk, in view. Hands in the lap drop out of frame and the save silently fails. "
                    : "Full body: P1 and P3 rest one hand at the waist, P2 has arms down. ")
                    + "Redo the neutral pose after changing People or Body. P1 is on the left of the unmirrored preview.";
            if (kind == 4)
            {
                var g = GetComponent<DigiPhantGestureActions>();
                int who = g ? g.gesturePerformer : 0;
                return "Hold a gesture " + N(g ? g.holdSeconds : .35f) + " s; " + N(g ? g.cooldownSeconds : 1f) + " s cooldown after each. Then show an open palm to arm the next one. Reads "
                    + (who == 0 ? "anyone's hands." : "P" + who + "'s hands only.");
            }
            if (kind == 5)
                return "In Test sliders mode, the Test jump and Test pick up/drop buttons (bottom left) stand in for the moves. A missed checkpoint can be retried by walking back into it.";
            return RoleNote(kind, seated, people);
        }

        string RoleNote(int role, bool seated, int people)
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
            int people = Mathf.Clamp(controller.performerCount, 1, 4);
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1; // the bonus checkpoints are inactive while the optional level is off
                course = FindObjectsByType<TrunkCheckpoint>(FindObjectsInactive.Include);
            }
            // Pages: Setup, P1 .. P3 (as many as there are people), Hands, and Course when the scene has checkpoints.
            // A page's kind is 0 = Setup, 1..3 = that performer, 4 = Hands, 5 = Course.
            var kinds = new List<int> { 0 };
            for (int r = 1; r <= Mathf.Min(people, 3); r++) kinds.Add(r);
            kinds.Add(4);
            if (course.Length > 0) kinds.Add(5);
            int pages = kinds.Count;
            page = Mathf.Clamp(page, 0, pages - 1);
            int kind = kinds[page], role = kind;
            var moves = kind == 0 ? SetupMoves(seated, people) : kind <= 3 ? Moves(role, seated, people) : kind == 4 ? HandMoves() : CourseMoves(seated);

            GUI.color = new Color(1, 1, 1, .72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), DigiPhantUi.White);
            GUI.color = Color.white;
            float w = Mathf.Min(Px(780), Screen.width - Px(24)), h = Mathf.Min(Px(570), Screen.height - Px(24)), pad = Px(18);
            var card = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            DigiPhantUi.Panel(card, Px(1.5f));

            // Header
            float x = card.x + pad, y = card.y + pad;
            GUI.Label(new Rect(x, y, w, Px(14)), "HOW TO MOVE", DigiPhantUi.Text(DigiPhantUi.Heading, DigiPhantUi.Muted, FontStyle.Bold));
            string title = kind == 0 ? "Setup" : kind <= 3 ? "P" + role + "  ·  " + RoleName[role] : kind == 4 ? "Hand gestures" : "Trunk Trail";
            GUI.Label(new Rect(x, y + Px(14), w - Px(100), Px(26)), title, DigiPhantUi.Text(DigiPhantUi.Title + 4, DigiPhantUi.Ink, FontStyle.Bold));
            string zone = people == 1 ? "the whole view" : role == 1 ? "the left zone" : role == people ? "the right zone" : "the middle zone";
            string where = kind == 0 ? "Do these first" : kind <= 3 ? "Stand in " + zone : kind == 4 ? "Show a hand to the camera" : "Challenges on the course";
            GUI.Label(new Rect(x, y + Px(41), w - Px(100), Px(16)), where + "  ·  " + (seated ? "Seated" : "Full body") + "  ·  " + people + (people == 1 ? " person" : " people"),
                DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted));
            if (GUI.Button(new Rect(card.xMax - pad - Px(72), y, Px(72), Px(DigiPhantUi.ControlHeight)), "Close", DigiPhantUi.Button())) open = false;

            // Footer: note, then Back | Setup P1 P2 P3 Hands Course | Next
            float navH = Px(DigiPhantUi.ControlHeight + 4), noteH = Px(kind >= 4 || kind == 0 ? 42 : 30);
            var nav = new Rect(x, card.yMax - pad - navH, w - 2 * pad, navH);
            GUI.Label(new Rect(x, nav.y - noteH - Px(4), w - 2 * pad, noteH), Note(kind, seated, people), DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Normal, TextAnchor.LowerLeft));

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
            for (int i = 0; i < pages; i++) labels[i] = kinds[i] == 0 ? "Setup" : kinds[i] <= 3 ? "P" + kinds[i] : kinds[i] == 4 ? "Hands" : "Course";
            float tab = Mathf.Min(Px(62), (nav.width - 2 * Px(72) - Px(16)) / pages); // narrower tabs in a small window
            GUILayout.BeginHorizontal(GUILayout.Width(tab * pages));
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
