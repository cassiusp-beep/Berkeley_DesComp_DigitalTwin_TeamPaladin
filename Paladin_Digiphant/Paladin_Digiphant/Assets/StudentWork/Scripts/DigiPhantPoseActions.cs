using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigiPhant
{
    // Recognises discrete poses (jump, about-turn, rear up) from the starter's calibrated signals
    // and plays them on the elephant. Add beside DigiPhantController and DigiPhantLocomotion.
    // Runs after the controller's LateUpdate so it sees this frame's values and can override motion.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(DigiPhantController))]
    public class DigiPhantPoseActions : MonoBehaviour
    {
        [Header("Who does what")]
        [Range(1, 4)] public int driver = 1;      // knee lift = jump
        [Range(1, 4)] public int navigator = 2;   // T-pose = about-turn, arms overhead = rear up

        [Header("Elephant")]
        [Tooltip("Empty child of the travel root that holds the elephant model. Jump and rear up move this.")]
        public Transform actionPivot;
        [Tooltip("Rear hips in the pivot's local space; rear up rotates around this point.")]
        public Vector3 rearPivot = new Vector3(0, 0.8f, -1.2f);

        [Header("Jump (driver knee lift)")]
        public float kneeLiftThreshold = .45f;
        [Tooltip("Seated mode reads the right HAND as the knee. Hands jitter far more than knees, so jumping needs a real raise (about chest height).")]
        public float seatedKneeLiftThreshold = 1f;
        public float jumpHeight = 1.2f, jumpSeconds = .7f, jumpForwardDistance = 2.5f, jumpCooldown = 1.2f;

        [Header("About-turn (navigator T-pose)")]
        public float tPoseSpreadMin = .8f;
        public float tPoseHandMin = .6f, tPoseHandMax = 1.5f;
        public float tPoseHoldSeconds = .6f, turnSeconds = 1.4f, turnCooldown = 2f;

        [Header("Rear up (navigator arms overhead)")]
        public float rearHandOn = 1.5f, rearHandOff = 1.1f, rearSpreadMax = .4f;
        public float rearHoldSeconds = .4f, rearDegrees = 35f, rearBlendSpeed = 4f;

        [Header("Tuning aids")]
        [Tooltip("Show the status bar (pose action, trunk, recording buttons) at the top right.")]
        public bool showSignals = true;
        [Tooltip("Expand the live numbers table under the status bar.")]
        public bool showNumbers;

        [Header("Guided recording (no clicker needed)")]
        [Tooltip("Seconds to get into each pose before recording starts.")]
        public float getReadySeconds = 5;
        [Tooltip("Seconds each pose is held and recorded.")]
        public float holdSeconds = 6;
        [Tooltip("Add a sixth take: the trunk performer squats with the right hand low.")]
        public bool includeSquatTake = true;

        public string CurrentPoseAction { get; private set; } = "None";

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        Vector3 pivotBasePosition;
        Quaternion pivotBaseRotation;
        float jumpStart = -99, lastJump = -99, lastTurn = -99, tPoseSince = -1, rearSince = -1;
        float turnStart = -99, turnFrom, rearAmount;
        bool rearing, kneeWasUp, jumpForward, lockingMovement;
        float savedForwardSensitivity, savedSteeringSensitivity;
        StreamWriter log;

        enum GuideStage { Off, Calibrating, Ready, Hold, Done, Failed }
        GuideStage guide = GuideStage.Off;
        int guideTake;
        float guideStageStart;
        string guideMessage = "";

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (actionPivot) { pivotBasePosition = actionPivot.localPosition; pivotBaseRotation = actionPivot.localRotation; }
        }

        void OnDisable() { UnlockMovement(); StopLog(); guide = GuideStage.Off; }

        float Read(int performer, Movement movement, float now) =>
            controller.TryReadMovement(performer, movement, now, out float v) ? v : float.NaN;

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup, t = Time.time;
            bool live = controller.inputMode == InputMode.Camera && controller.IsCalibrated;

            if (live) DetectPoses(now, t);
            else { tPoseSince = rearSince = -1; rearing = false; }

            bool jumping = t - jumpStart < jumpSeconds, turning = t - turnStart < turnSeconds;
            if (rearing || turning) LockMovement(); else UnlockMovement();
            CurrentPoseAction = rearing ? "Rear up" : turning ? "About-turn" : jumping ? (jumpForward ? "Jump forward" : "Jump") : "None";

            ApplyTurn(t, turning);
            ApplyBody(t, jumping);
            if (log != null) WriteLog(now);
            TickGuide(now);
        }

        void DetectPoses(float now, float t)
        {
            // Jump: rising edge of the driver's right knee, only from the ground and not while rearing.
            // A frame where the knee isn't tracked is unknown, not "down": treating it as down turned every
            // tracking flicker into a fresh lift (false jumps, signals_20261008_175503.csv), so keep the last state.
            float knee = Read(driver, Movement.RightFootLift, now);
            if (!float.IsNaN(knee))
            {
                bool kneeUp = knee > (controller.upperBodyOnly ? seatedKneeLiftThreshold : kneeLiftThreshold);
                if (kneeUp && !kneeWasUp && !rearing && t - lastJump > jumpCooldown && t - jumpStart > jumpSeconds)
                {
                    jumpStart = lastJump = t;
                    jumpForward = locomotion != null && locomotion.CurrentSpeed > .1f;
                }
                kneeWasUp = kneeUp;
            }

            float left = Read(navigator, Movement.LeftHandHeight, now);
            float right = Read(navigator, Movement.RightHandHeight, now);
            float spread = Read(navigator, Movement.ArmSpread, now);
            if (float.IsNaN(left) || float.IsNaN(right) || float.IsNaN(spread)) { tPoseSince = rearSince = -1; rearing = false; return; }

            // Rear up: both hands high and close together, held. Hysteresis keeps it up until arms drop.
            bool rearPose = left > rearHandOn && right > rearHandOn && spread < rearSpreadMax;
            if (rearing) rearing = left > rearHandOff && right > rearHandOff;
            else if (rearPose) { if (rearSince < 0) rearSince = t; if (t - rearSince >= rearHoldSeconds) rearing = true; }
            else rearSince = -1;

            // About-turn: arms out wide at shoulder height, held so arms swinging up sideways don't trigger it.
            bool tPose = spread > tPoseSpreadMin && left > tPoseHandMin && left < tPoseHandMax && right > tPoseHandMin && right < tPoseHandMax;
            if (tPose && !rearing)
            {
                if (tPoseSince < 0) tPoseSince = t;
                if (t - tPoseSince >= tPoseHoldSeconds && t - lastTurn > turnCooldown && t - turnStart > turnSeconds)
                { turnStart = lastTurn = t; turnFrom = locomotion && locomotion.travelRoot ? locomotion.travelRoot.eulerAngles.y : 0; tPoseSince = -1; }
            }
            else tPoseSince = -1;
        }

        // Inspector ⋮ menu / on-screen test button: the same jump the driver's knee lift starts, without the camera.
        [ContextMenu("Test: jump")]
        public void TestJump()
        {
            float t = Time.time;
            if (t - jumpStart <= jumpSeconds) return;
            jumpStart = lastJump = t;
            jumpForward = locomotion != null && locomotion.CurrentSpeed > .1f;
        }

        void ApplyTurn(float t, bool turning)
        {
            if (!turning || locomotion == null || locomotion.travelRoot == null) return;
            float k = Mathf.SmoothStep(0, 1, (t - turnStart) / turnSeconds);
            Vector3 e = locomotion.travelRoot.eulerAngles;
            locomotion.travelRoot.rotation = Quaternion.Euler(e.x, turnFrom + 180f * k, e.z);
        }

        void ApplyBody(float t, bool jumping)
        {
            if (!actionPivot) return;
            rearAmount = Mathf.MoveTowards(rearAmount, rearing ? 1 : 0, rearBlendSpeed * Time.deltaTime);
            Quaternion r = Quaternion.Euler(-rearDegrees * Mathf.SmoothStep(0, 1, rearAmount), 0, 0);
            Vector3 offset = rearPivot - r * rearPivot; // rotate around the rear hips, not the model origin
            if (jumping)
            {
                float p = (t - jumpStart) / jumpSeconds;
                offset.y += 4 * jumpHeight * p * (1 - p);
                if (jumpForward && locomotion && locomotion.travelRoot)
                    locomotion.travelRoot.position += locomotion.travelRoot.forward * (jumpForwardDistance / jumpSeconds) * Time.deltaTime;
            }
            actionPivot.localPosition = pivotBasePosition + offset;
            actionPivot.localRotation = pivotBaseRotation * r;
        }

        // Freeze travel and steering during scripted actions without editing the starter scripts.
        void LockMovement()
        {
            if (lockingMovement || locomotion == null) return;
            savedForwardSensitivity = locomotion.forward.sensitivity;
            savedSteeringSensitivity = locomotion.steering.sensitivity;
            locomotion.forward.sensitivity = locomotion.steering.sensitivity = 0;
            lockingMovement = true;
        }

        void UnlockMovement()
        {
            if (!lockingMovement || locomotion == null) return;
            locomotion.forward.sensitivity = savedForwardSensitivity;
            locomotion.steering.sensitivity = savedSteeringSensitivity;
            lockingMovement = false;
        }

        // CSV of every calibrated signal, for choosing thresholds and for the project's generated dataset.
        void StartLog(string label = null)
        {
            string dir = Path.Combine(Application.dataPath, "..", "Recordings");
            Directory.CreateDirectory(dir);
            string name = "signals_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + (label != null ? "_" + label : "");
            string path = Path.Combine(dir, name + ".csv");
            log = new StreamWriter(path);
            var header = new StringBuilder("time,action,speed,x,z,heading");
            for (int p = 1; p <= controller.performerCount; p++)
                foreach (Movement m in System.Enum.GetValues(typeof(Movement))) header.Append(",P").Append(p).Append('_').Append(m);
            log.WriteLine(header);
            Debug.Log("Logging signals to " + path);
        }

        void StopLog() { log?.Dispose(); log = null; }

        void WriteLog(float now)
        {
            var c = CultureInfo.InvariantCulture;
            var root = locomotion ? locomotion.travelRoot : null;
            string action = CurrentPoseAction != "None" ? CurrentPoseAction : locomotion ? locomotion.CurrentAction : "";
            var line = new StringBuilder();
            line.Append(now.ToString("F3", c)).Append(',').Append(action).Append(',')
                .Append((locomotion ? locomotion.CurrentSpeed : 0).ToString("F2", c)).Append(',')
                .Append((root ? root.position.x : 0).ToString("F2", c)).Append(',')
                .Append((root ? root.position.z : 0).ToString("F2", c)).Append(',')
                .Append((root ? root.eulerAngles.y : 0).ToString("F1", c));
            for (int p = 1; p <= controller.performerCount; p++)
                foreach (Movement m in System.Enum.GetValues(typeof(Movement)))
                {
                    float v = Read(p, m, now);
                    line.Append(',').Append(float.IsNaN(v) ? "" : v.ToString("F3", c));
                }
            log.WriteLine(line);
        }

        // Guided recording: on-screen instructions and countdowns run the whole tuning session,
        // so nobody has to stay at the laptop. Files are named by pose (signals_<time>_<pose>.csv).
        (string label, string title, string instruction)[] GuideTakes()
        {
            string d = "P" + driver, n = "P" + navigator, t = "P" + trunkPerformer;
            var takes = new System.Collections.Generic.List<(string, string, string)>
            {
                ("neutral", "NEUTRAL", "Everyone: hold your neutral pose."),
                ("tpose", "T-POSE", n + ": arms straight out to the sides.\nEveryone else: neutral."),
                ("overhead", "ARMS OVERHEAD", n + ": both arms straight up (lift through the front).\nEveryone else: neutral."),
                ("knee", "KNEE LIFT", d + ": right knee up, thigh level, hold it.\nEveryone else: neutral."),
                ("other", "OTHER MOVES", d + ": raise your left hand like walking.\n" + n + ": lean left and right, then raise one arm.\n" + t + ": neutral."),
            };
            if (includeSquatTake) takes.Add(("squat", "SQUAT", t + ": squat with your right hand low.\nEveryone else: neutral."));
            return takes.ToArray();
        }

        // The performer not used as driver or navigator (P3 in the three-person setup).
        int trunkPerformer
        {
            get
            {
                for (int p = 1; p <= 4; p++) if (p != driver && p != navigator) return p;
                return 3;
            }
        }

        void StartGuide(float now)
        {
            StopLog();
            guideTake = 0;
            guideStageStart = now;
            if (controller.inputMode != InputMode.Camera)
            { guide = GuideStage.Failed; guideMessage = "Click Camera first, then press Guided recording again."; return; }
            controller.BeginCalibrationCountdown(now);
            guide = GuideStage.Calibrating;
        }

        void StopGuide() { StopLog(); guide = GuideStage.Off; }

        void TickGuide(float now)
        {
            if (guide == GuideStage.Off) return;
            float elapsed = now - guideStageStart;
            var takes = GuideTakes();
            switch (guide)
            {
                case GuideStage.Calibrating:
                    if (controller.CalibrationPending) return;
                    if (!controller.IsCalibrated)
                    {
                        guide = GuideStage.Failed;
                        guideMessage = "Calibration failed: " + controller.Status + ".\nEveryone must be fully visible inside their own zone.";
                        return;
                    }
                    guide = GuideStage.Ready; guideStageStart = now;
                    break;
                case GuideStage.Ready:
                    if (!controller.IsCalibrated) { Fail("Calibration was reset. Press Guided recording to start again."); return; }
                    if (elapsed < getReadySeconds) return;
                    StartLog(takes[guideTake].label);
                    guide = GuideStage.Hold; guideStageStart = now;
                    break;
                case GuideStage.Hold:
                    if (elapsed < holdSeconds) return;
                    StopLog();
                    guideTake++;
                    guide = guideTake < takes.Length ? GuideStage.Ready : GuideStage.Done;
                    guideStageStart = now;
                    break;
                case GuideStage.Done:
                    if (elapsed > 15) guide = GuideStage.Off;
                    break;
            }
        }

        void Fail(string message) { StopLog(); guide = GuideStage.Failed; guideMessage = message; guideStageStart = Time.realtimeSinceStartup; }

        // ---------- On-screen UI: black on white, Helvetica Neue (Arial where it isn't installed) ----------

        static readonly Color Ink = DigiPhantUi.Ink, Muted = DigiPhantUi.Muted, Ready = DigiPhantUi.Ready, Go = DigiPhantUi.Go, Stop = DigiPhantUi.Stop;
        static float Px(float points) => DigiPhantUi.Px(points);
        static GUIStyle Text(float size, Color color, FontStyle weight = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft) =>
            DigiPhantUi.Text(size, color, weight, align);
        static GUIStyle Button(float size = DigiPhantUi.Body) => DigiPhantUi.Button(size);
        static void Panel(Rect r, float border = 1) => DigiPhantUi.Panel(r, border);

        void OnGUI()
        {
            if (controller == null) return;
            GUI.depth = -10; // above the starter's panels, so the guide is never hidden
            if (showSignals) DrawStatusBar();
            DrawGuide(Time.realtimeSinceStartup); // last, so it sits on top
        }

        // One slim bar: Pose action | Trunk | Guided recording | Log signals | Numbers. The 18 readouts open on demand.
        void DrawStatusBar()
        {
            float now = Time.realtimeSinceStartup, h = Px(40), pad = Px(14);
            var trunk = GetComponent<DigiPhantTrunkActions>();
            var label = Text(DigiPhantUi.Small, Muted, FontStyle.Bold);
            var value = Text(DigiPhantUi.Body, Ink, FontStyle.Bold);
            var button = Button();
            string guideText = "Guided recording", logText = log != null ? "Stop signal log" : "Log signals";
            string numbersText = showNumbers ? "Hide numbers" : "Show numbers";

            var parts = new System.Collections.Generic.List<(string label, string value)> { ("Pose action", CurrentPoseAction) };
            if (trunk) parts.Add(("Trunk", trunk.CurrentTrunkAction));
            float textWidth = 0;
            foreach (var (l, v) in parts)
                textWidth += label.CalcSize(new GUIContent(l + "  ")).x + Mathf.Max(Px(64), value.CalcSize(new GUIContent(v)).x) + Px(21);
            float bw(string s) => button.CalcSize(new GUIContent(s)).x + Px(8);
            float gap = Px(8);
            float w = pad + textWidth + bw(guideText) + bw(logText) + bw(numbersText) + 3 * gap + pad;
            // Right-aligned, but never over the left control panel.
            float left = controller.showControls ? DigiPhantUi.PanelWidth + Px(12) : Px(12);
            var bar = new Rect(Mathf.Max(left, Screen.width - w - Px(12)), Px(12), w, h);
            Panel(bar);

            float x = bar.x + pad;
            foreach (var (l, v) in parts)
            {
                float lw = label.CalcSize(new GUIContent(l + "  ")).x, vw = Mathf.Max(Px(64), value.CalcSize(new GUIContent(v)).x);
                GUI.Label(new Rect(x, bar.y, lw, h), l, label); x += lw;
                GUI.Label(new Rect(x, bar.y, vw, h), v, value); x += vw + Px(10);
                GUI.DrawTexture(new Rect(x, bar.y + Px(10), 1, h - Px(20)), DigiPhantUi.Light); x += Px(11);
            }
            bool guiding = guide != GuideStage.Off;
            GUI.enabled = !guiding;
            float bh = Px(DigiPhantUi.ControlHeight), by = bar.y + (h - bh) / 2;
            if (GUI.Button(new Rect(x, by, bw(guideText), bh), guideText, button)) StartGuide(now);
            x += bw(guideText) + gap;
            if (GUI.Button(new Rect(x, by, bw(logText), bh), logText, button)) { if (log == null) StartLog(); else StopLog(); }
            x += bw(logText) + gap;
            GUI.enabled = true;
            if (GUI.Button(new Rect(x, by, bw(numbersText), bh), numbersText, button)) showNumbers = !showNumbers;

            if (showNumbers) DrawNumbers(new Rect(bar.xMax - Px(290), bar.yMax + Px(8), Px(290), 0), now);
        }

        // Rows = the six signals, columns = performers. Values are measured from each person's neutral pose.
        void DrawNumbers(Rect area, float now)
        {
            var moves = (Movement[])System.Enum.GetValues(typeof(Movement));
            int people = controller.performerCount;
            float row = Px(17), nameW = Px(100), colW = (area.width - nameW - Px(14)) / Mathf.Max(1, people);
            area.height = Px(20) + row * (moves.Length + 1);
            Panel(area);
            var head = Text(DigiPhantUi.Small, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            var name = Text(DigiPhantUi.Small, Muted);
            var num = Text(DigiPhantUi.Small, Ink, FontStyle.Normal, TextAnchor.MiddleRight);
            float y = area.y + Px(10), x0 = area.x + Px(12);
            for (int p = 1; p <= people; p++)
                GUI.Label(new Rect(x0 + nameW + (p - 1) * colW, y, colW, row), "P" + p, head);
            y += row;
            foreach (Movement m in moves)
            {
                GUI.Label(new Rect(x0, y, nameW, row), m.ToString(), name);
                for (int p = 1; p <= people; p++)
                {
                    float v = Read(p, m, now), cx = x0 + nameW + (p - 1) * colW;
                    if (!float.IsNaN(v))
                    {
                        // Small black bar from the column centre: right = above neutral, left = below.
                        float half = colW * .22f, mid = cx + half + Px(3), len = Mathf.Clamp(v / 2f, -1, 1) * half;
                        GUI.DrawTexture(new Rect(Mathf.Min(mid, mid + len), y + row / 2 - Px(1.5f), Mathf.Max(1, Mathf.Abs(len)), Px(3)), DigiPhantUi.Black);
                    }
                    GUI.Label(new Rect(cx, y, colW - Px(4), row), float.IsNaN(v) ? "—" : v.ToString("+0.00;-0.00"), num);
                }
                y += row;
            }
        }

        void DrawGuide(float now)
        {
            if (guide == GuideStage.Off) return;
            var takes = GuideTakes();
            float elapsed = now - guideStageStart;
            string step = "", title = "", detail = "", count = "";
            Color accent = Ink;
            switch (guide)
            {
                case GuideStage.Calibrating:
                    step = "STEP 1: CALIBRATE";
                    title = "STAND IN YOUR ZONE";
                    detail = "P" + driver + " and P" + trunkPerformer + ": one hand at your waist.   P" + navigator + ": arms down.\nHold still.";
                    count = Mathf.CeilToInt(Mathf.Max(0, 10 - elapsed)).ToString();
                    accent = Ready;
                    break;
                case GuideStage.Ready:
                case GuideStage.Hold:
                    var take = takes[guideTake];
                    bool hold = guide == GuideStage.Hold;
                    step = $"RECORDING {guideTake + 1} OF {takes.Length}";
                    title = take.title;
                    detail = take.instruction;
                    float left = (hold ? holdSeconds : getReadySeconds) - elapsed;
                    count = (hold ? "HOLD  " : "GET READY  ") + Mathf.CeilToInt(Mathf.Max(0, left));
                    accent = hold ? Go : Ready;
                    break;
                case GuideStage.Done:
                    title = "ALL DONE";
                    detail = $"{takes.Length} recordings saved in the Recordings folder.\nPress Stop and tell Claude \"done\".";
                    accent = Go;
                    break;
                case GuideStage.Failed:
                    title = "STOPPED";
                    detail = guideMessage;
                    accent = Stop;
                    break;
            }

            float w = Screen.width * .7f, h = Screen.height * .62f;
            var box = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Panel(box, Px(1.5f));
            float unit = Mathf.Max(9, Screen.height / 28f / DigiPhantUi.Scale), u = Px(unit);
            float y = box.y + u;
            GUI.Label(new Rect(box.x, y, w, u * 1.4f), step, Text(unit, Muted, FontStyle.Bold, TextAnchor.MiddleCenter)); y += u * 1.6f;
            GUI.Label(new Rect(box.x, y, w, u * 3f), title, Text(unit * 2, Ink, FontStyle.Bold, TextAnchor.MiddleCenter)); y += u * 3.2f;
            GUI.Label(new Rect(box.x + u, y, w - u * 2, u * 4.5f), detail, Text(unit * 1.1f, Ink, FontStyle.Normal, TextAnchor.MiddleCenter)); y += u * 4.8f;
            if (count.Length > 0) GUI.Label(new Rect(box.x, y, w, u * 3.4f), count, Text(unit * 2.6f, accent, FontStyle.Bold, TextAnchor.MiddleCenter));
            var close = new Rect(box.xMax - Px(76), box.y + Px(8), Px(68), Px(DigiPhantUi.ControlHeight));
            if (guide == GuideStage.Ready || guide == GuideStage.Hold || guide == GuideStage.Calibrating)
            {
                if (GUI.Button(close, "Cancel", Button())) { controller.CancelCalibrationCountdown(); StopGuide(); }
            }
            else if (GUI.Button(close, "Close", Button())) guide = GuideStage.Off;
        }
    }
}
