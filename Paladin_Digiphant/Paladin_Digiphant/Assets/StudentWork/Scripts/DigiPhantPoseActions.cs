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
        public float jumpHeight = 1.2f, jumpSeconds = .7f, jumpForwardDistance = 2.5f, jumpCooldown = 1.2f;

        [Header("About-turn (navigator T-pose)")]
        public float tPoseSpreadMin = .8f;
        public float tPoseHandMin = .6f, tPoseHandMax = 1.5f;
        public float tPoseHoldSeconds = .6f, turnSeconds = 1.4f, turnCooldown = 2f;

        [Header("Rear up (navigator arms overhead)")]
        public float rearHandOn = 1.5f, rearHandOff = 1.1f, rearSpreadMax = .4f;
        public float rearHoldSeconds = .4f, rearDegrees = 35f, rearBlendSpeed = 4f;

        [Header("Tuning aids")]
        public bool showSignals = true;

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
            float knee = Read(driver, Movement.RightFootLift, now);
            bool kneeUp = !float.IsNaN(knee) && knee > kneeLiftThreshold;
            if (kneeUp && !kneeWasUp && !rearing && t - lastJump > jumpCooldown && t - jumpStart > jumpSeconds)
            {
                jumpStart = lastJump = t;
                jumpForward = locomotion != null && locomotion.CurrentSpeed > .1f;
            }
            kneeWasUp = kneeUp;

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

        void DrawGuide(float now)
        {
            if (guide == GuideStage.Off) return;
            var takes = GuideTakes();
            float elapsed = now - guideStageStart;
            string step = "", title = "", detail = "", count = "";
            Color accent = Color.white;
            switch (guide)
            {
                case GuideStage.Calibrating:
                    step = "STEP 1: CALIBRATE";
                    title = "STAND IN YOUR ZONE";
                    detail = "P" + driver + " and P" + trunkPerformer + ": one hand at your waist.   P" + navigator + ": arms down.\nHold still.";
                    count = Mathf.CeilToInt(Mathf.Max(0, 10 - elapsed)).ToString();
                    accent = new Color(1f, .75f, .2f);
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
                    accent = hold ? new Color(.3f, 1f, .4f) : new Color(1f, .75f, .2f);
                    break;
                case GuideStage.Done:
                    title = "ALL DONE";
                    detail = $"{takes.Length} recordings saved in the Recordings folder.\nPress Stop and tell Claude \"done\".";
                    accent = new Color(.3f, 1f, .4f);
                    break;
                case GuideStage.Failed:
                    title = "STOPPED";
                    detail = guideMessage;
                    accent = new Color(1f, .4f, .35f);
                    break;
            }

            float w = Screen.width * .7f, h = Screen.height * .62f;
            var box = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            var previous = GUI.color;
            GUI.color = new Color(0, 0, 0, .82f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            int unit = Mathf.Max(12, Screen.height / 28);
            GUIStyle Style(int size, Color color, FontStyle font = FontStyle.Bold) => new GUIStyle(GUI.skin.label)
            { fontSize = size, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = font, normal = { textColor = color } };

            float y = box.y + unit;
            GUI.Label(new Rect(box.x, y, w, unit * 1.4f), step, Style(unit, new Color(.8f, .8f, .8f))); y += unit * 1.6f;
            GUI.Label(new Rect(box.x, y, w, unit * 3f), title, Style(unit * 2, accent)); y += unit * 3.2f;
            GUI.Label(new Rect(box.x + unit, y, w - unit * 2, unit * 4.5f), detail, Style(Mathf.RoundToInt(unit * 1.1f), Color.white, FontStyle.Normal)); y += unit * 4.8f;
            if (count.Length > 0) GUI.Label(new Rect(box.x, y, w, unit * 3.4f), count, Style(Mathf.RoundToInt(unit * 2.6f), accent));
            if (guide == GuideStage.Ready || guide == GuideStage.Hold || guide == GuideStage.Calibrating)
            {
                if (GUI.Button(new Rect(box.xMax - 110, box.y + 10, 100, 24), "Cancel")) { controller.CancelCalibrationCountdown(); StopGuide(); }
            }
            else if (GUI.Button(new Rect(box.xMax - 110, box.y + 10, 100, 24), "Close")) guide = GuideStage.Off;
        }

        // Live readout (the button uses IMGUI, so it works with either Unity input system) of each performer's calibrated values. Strike a pose, read the numbers, set thresholds.
        void OnGUI()
        {
            if (controller == null) return;
            GUI.depth = -10; // above the starter's panels, so the guide is never hidden
            if (showSignals) DrawSignals();
            DrawGuide(Time.realtimeSinceStartup); // last, so it sits on top of the readout
        }

        void DrawSignals()
        {
            float now = Time.realtimeSinceStartup, w = 360, x = Screen.width - w - 10, y = 10;
            GUI.Box(new Rect(x - 6, y - 4, w + 12, 26 + controller.performerCount * 112), GUIContent.none);
            GUI.Label(new Rect(x, y, w - 220, 20), $"<b>Pose action:</b> {CurrentPoseAction}", Rich());
            bool guiding = guide != GuideStage.Off;
            GUI.enabled = !guiding;
            if (GUI.Button(new Rect(x + w - 215, y, 105, 20), "Guided recording")) StartGuide(now);
            if (GUI.Button(new Rect(x + w - 105, y, 105, 20), log != null ? "Stop signal log" : "Log signals"))
            { if (log == null) StartLog(); else StopLog(); }
            GUI.enabled = true;
            y += 22;
            for (int p = 1; p <= controller.performerCount; p++)
            {
                GUI.Label(new Rect(x, y, w, 18), $"<b>P{p}</b>", Rich()); y += 16;
                foreach (Movement m in System.Enum.GetValues(typeof(Movement)))
                {
                    float v = Read(p, m, now);
                    GUI.Label(new Rect(x, y, 130, 16), m.ToString());
                    GUI.Label(new Rect(x + 130, y, 50, 16), float.IsNaN(v) ? "—" : v.ToString("+0.00;-0.00"));
                    if (!float.IsNaN(v))
                    {
                        float half = 55, mid = x + 185 + half, len = Mathf.Clamp(v / 2f, -1, 1) * half;
                        GUI.Box(new Rect(Mathf.Min(mid, mid + len), y + 4, Mathf.Abs(len), 8), GUIContent.none);
                    }
                    y += 15;
                }
                y += 6;
            }
        }

        static GUIStyle rich;
        static GUIStyle Rich() => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
