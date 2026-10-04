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

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (actionPivot) { pivotBasePosition = actionPivot.localPosition; pivotBaseRotation = actionPivot.localRotation; }
        }

        void OnDisable() { UnlockMovement(); StopLog(); }

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
        void StartLog()
        {
            string dir = Path.Combine(Application.dataPath, "..", "Recordings");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "signals_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
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

        // Live readout (the button uses IMGUI, so it works with either Unity input system) of each performer's calibrated values. Strike a pose, read the numbers, set thresholds.
        void OnGUI()
        {
            if (!showSignals || controller == null) return;
            float now = Time.realtimeSinceStartup, w = 300, x = Screen.width - w - 10, y = 10;
            GUI.Box(new Rect(x - 6, y - 4, w + 12, 26 + controller.performerCount * 112), GUIContent.none);
            GUI.Label(new Rect(x, y, w - 110, 20), $"<b>Pose action:</b> {CurrentPoseAction}", Rich());
            if (GUI.Button(new Rect(x + w - 105, y, 105, 20), log != null ? "Stop signal log" : "Log signals"))
            { if (log == null) StartLog(); else StopLog(); }
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
