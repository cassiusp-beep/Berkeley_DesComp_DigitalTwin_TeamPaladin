using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant
{
    [Serializable] public class HandGesture
    {
        public int slot;          // performer the hand belongs to, 0 = not near anyone's wrist
        public string gesture;    // MediaPipe canned gesture: Open_Palm, Victory, Thumb_Up, Thumb_Down, ...
        public float score;
        public float x, y;
        public string side;       // "L" or "R": the performer's own left/right hand, "" when unknown
        public float rx, ry;      // offset from that hand's own shoulder, in shoulder widths (+rx = the performer's right, +ry = up)
    }
    [Serializable] public class GestureFrame
    {
        public int version;
        public HandGesture[] hands;
    }

    // Hand gestures from the tracker's MediaPipe Gesture Recognizer (sent on port + 3, beside the unchanged pose data).
    // Open palm = neutral. Victory = flap ears and shake head. Thumbs up = walk a circle clockwise, thumbs down = anticlockwise.
    // After each gesture, show an open palm again to arm the next one. Added automatically on Play if missing.
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(DigiPhantController))]
    public class DigiPhantGestureActions : MonoBehaviour
    {
        const string Palm = "Open_Palm", Victory = "Victory", ThumbUp = "Thumb_Up", ThumbDown = "Thumb_Down";

        [Header("Who")]
        [Tooltip("Performer whose hands are read. 0 = anyone's hand, including hands not matched to a performer.")]
        [Range(0, 4)] public int gesturePerformer;

        [Header("Recognition")]
        [Range(0, 1)] public float minimumScore = .6f;
        [Tooltip("Seconds a gesture must be held before it triggers.")]
        public float holdSeconds = .35f;
        public float cooldownSeconds = 1f;
        [Tooltip("Countdown for Set neutral hand. An open palm must be showing when it ends.")]
        public float neutralCountdownSeconds = 5f;

        [Header("Victory: flap ears and shake head")]
        public float celebrateSeconds = 2.5f;
        public float earFlapDegrees = 35f, earFlapsPerSecond = 3f;
        public float headShakeDegrees = 20f, headShakesPerSecond = 2.5f;

        [Header("Thumbs up / down: walk a circle")]
        [Tooltip("Time for one full circle at walking pace. Longer = wider circle.")]
        public float circleSeconds = 10f;
        [Range(.1f, .65f)] public float circleThrottle = .5f;

        public bool showPanel = true;
        [NonSerialized] public bool adventureControls;

        // Only fresh, confidently assigned hands from the requested performer can drive a game.
        public string ReadAssignedGesture(int performer, bool steering = false)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPacket > controller.trackingTimeout || !controller.IsTracked(performer, now)) return "None";
            HandGesture best = null;
            foreach (var hand in hands)
            {
                if (hand == null || hand.slot != performer || hand.score < minimumScore
                    || float.IsNaN(hand.score) || float.IsInfinity(hand.score)) continue;
                bool action = steering ? hand.gesture == ThumbUp || hand.gesture == ThumbDown : hand.gesture == "Closed_Fist";
                if (!action && hand.gesture != Palm) continue;
                bool bestAction = best != null && best.gesture != Palm;
                if (best == null || (action && !bestAction) || (action == bestAction && hand.score > best.score)) best = hand;
            }
            return best == null ? "None" : best.gesture;
        }

        // Counts tracker frames, so a scene can require a shape for several frames in a row.
        public int PacketCount { get; private set; }

        // One of a performer's own hands ("L" or "R"), from fresh, confident, assigned detections: its shape and
        // where it is relative to its own shoulder, in shoulder widths (+right = the performer's right, +up = above).
        // False when that hand is not in view. The shape is "None" when the hand is seen but makes no known shape.
        public bool TryReadHand(int performer, string side, out string shape, out float right, out float up)
        {
            shape = "None"; right = up = 0;
            float now = Time.realtimeSinceStartup;
            if (now - lastPacket > controller.trackingTimeout || !controller.IsTracked(performer, now)) return false;
            HandGesture best = null;
            foreach (var hand in hands)
            {
                if (hand == null || hand.slot != performer || hand.side != side || string.IsNullOrEmpty(hand.gesture)
                    || float.IsNaN(hand.score) || float.IsInfinity(hand.score)
                    || float.IsNaN(hand.rx) || float.IsInfinity(hand.rx) || float.IsNaN(hand.ry) || float.IsInfinity(hand.ry)) continue;
                if (best == null || hand.score > best.score) best = hand;
            }
            if (best == null) return false;
            shape = best.score >= minimumScore ? best.gesture : "None"; // a low-confidence shape is no shape
            right = best.rx; up = best.ry;
            return true;
        }

        // For a scene that drives the elephant from hands (Adventure, solo seated): where the trunk points
        // (-1..1 each; the scene sets them every frame), and the ear-flap and head-shake celebration.
        [NonSerialized] public float trunkLift, trunkSwing;
        float trunkLiftNow, trunkSwingNow;
        BoneControl trunkCurlControl, trunkSwingControl;
        public void PlayCelebrate() => Celebrate();

        void ApplyFlourishes(float t)
        {
            float step = 4 * Time.deltaTime;
            trunkLiftNow = Mathf.MoveTowards(trunkLiftNow, trunkLift, step);
            trunkSwingNow = Mathf.MoveTowards(trunkSwingNow, trunkSwing, step);
            if (t - celebrateStart < celebrateSeconds) ApplyCelebrate(t - celebrateStart);
            if (trunkLiftNow != 0 && trunkCurlControl != null) Rotate(trunkCurlControl, trunkLiftNow * Mathf.Abs(trunkCurlControl.degrees));
            if (trunkSwingNow != 0 && trunkSwingControl != null) Rotate(trunkSwingControl, trunkSwingNow * Mathf.Abs(trunkSwingControl.degrees));
        }

        public string CurrentGestureAction { get; private set; } = "None";
        public string SeenGesture { get; private set; } = "None";
        public bool NeutralSet { get; private set; }

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        BoneControl leftEar, rightEar, head;
        UdpClient socket;
        HandGesture[] hands = Array.Empty<HandGesture>();
        float lastPacket = -99, palmSeen = -99, neutralDeadline = -1, bindRetry;
        string held = "None", message = "";
        float heldSince = -1, lastTrigger = -99, celebrateStart = -99;
        bool armed, circling;
        float circleDirection, circleTurned, lastYaw;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToScene()
        {
            // Runs once per launch; sceneLoaded also covers scenes opened later by the Mac app's scene button.
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
            Attach(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        static void Attach(Scene scene, LoadSceneMode mode)
        {
            var c = FindAnyObjectByType<DigiPhantController>();
            if (c && !c.GetComponent<DigiPhantGestureActions>()) c.gameObject.AddComponent<DigiPhantGestureActions>();
        }

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            leftEar = controller.controls.FirstOrDefault(c => c.label == "Left ear");
            rightEar = controller.controls.FirstOrDefault(c => c.label == "Right ear");
            head = controller.controls.FirstOrDefault(c => c.label == "Head turn");
            trunkCurlControl = controller.controls.FirstOrDefault(c => c.label == "Trunk curl");
            trunkSwingControl = controller.controls.FirstOrDefault(c => c.label == "Trunk swing");
        }

        void OnDisable() { StopReceiver(); StopCircle(); NeutralSet = armed = false; neutralDeadline = -1; }

        void StartReceiver()
        {
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, controller.port + 3));
                socket.Client.Blocking = false;
            }
            catch (SocketException e) { socket = null; message = "Gesture port busy: " + e.Message; }
        }

        void StopReceiver() { socket?.Close(); socket = null; hands = Array.Empty<HandGesture>(); }

        void Poll(float now)
        {
            if (socket == null) return;
            try
            {
                byte[] latest = null;
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref sender);
                if (latest == null || latest.Length > 16000) return;
                var frame = JsonUtility.FromJson<GestureFrame>(Encoding.UTF8.GetString(latest));
                if (frame == null || frame.version != 1) return;
                hands = frame.hands ?? Array.Empty<HandGesture>();
                lastPacket = now;
                PacketCount++;
            }
            catch (Exception e) when (e is SocketException || e is ArgumentException) { }
        }

        // The strongest action gesture wins over an open palm, so one hand can stay neutral while the other signs.
        string CurrentGesture(float now)
        {
            if (now - lastPacket > controller.trackingTimeout) return "None";
            string best = "None";
            float bestScore = 0;
            foreach (var h in hands)
            {
                if (h == null || h.score < minimumScore || (gesturePerformer != 0 && h.slot != gesturePerformer)) continue;
                bool action = h.gesture == Victory || h.gesture == ThumbUp || h.gesture == ThumbDown;
                bool bestIsAction = best == Victory || best == ThumbUp || best == ThumbDown;
                if (h.gesture == Palm && bestIsAction) continue;
                if ((action && !bestIsAction) || h.score > bestScore) { best = h.gesture; bestScore = h.score; }
            }
            return best;
        }

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup, t = Time.time;
            bool camera = controller.inputMode == InputMode.Camera;
            if (camera && socket == null && now >= bindRetry) { bindRetry = now + 2; StartReceiver(); }
            if (!camera && socket != null) { StopReceiver(); NeutralSet = armed = false; neutralDeadline = -1; }
            Poll(now);

            if (adventureControls) { StopCircle(); ApplyFlourishes(t); return; }
            SeenGesture = CurrentGesture(now);
            if (SeenGesture == Palm) palmSeen = now;
            if (SeenGesture != held) { held = SeenGesture; heldSince = t; }

            if (neutralDeadline >= 0 && now >= neutralDeadline)
            {
                neutralDeadline = -1;
                NeutralSet = armed = now - palmSeen < .5f;
                message = NeutralSet ? "Neutral hand saved" : "No open palm seen. Try again, palm facing the camera.";
            }
            if (NeutralSet && !armed && SeenGesture == Palm) armed = true;
            if (NeutralSet && armed && neutralDeadline < 0) Detect(t);

            bool celebrating = t - celebrateStart < celebrateSeconds;
            if (circling) UpdateCircle();
            if (celebrating) ApplyCelebrate(t - celebrateStart);
            CurrentGestureAction = circling ? (circleDirection > 0 ? "Circle clockwise" : "Circle anticlockwise")
                : celebrating ? "Ear flap + head shake" : "None";
        }

        void Detect(float t)
        {
            bool action = held == Victory || held == ThumbUp || held == ThumbDown;
            bool busy = circling || t - celebrateStart < celebrateSeconds;
            if (!action || busy || t - heldSince < holdSeconds || t - lastTrigger < cooldownSeconds) return;
            lastTrigger = t;
            armed = false; // open palm re-arms
            if (held == Victory) Celebrate();
            else StartCircle(held == ThumbUp ? 1 : -1);
        }

        // ---------- Victory: ears flap outward and back, head shakes side to side ----------

        void Celebrate() => celebrateStart = Time.time;

        void ApplyCelebrate(float time)
        {
            // Ease in and out over a quarter second so the bones never snap.
            float envelope = Mathf.Clamp01(Mathf.Min(time, celebrateSeconds - time) / .25f);
            float flap = (1 - Mathf.Cos(2 * Mathf.PI * earFlapsPerSecond * time)) * .5f * earFlapDegrees * envelope;
            float shake = Mathf.Sin(2 * Mathf.PI * headShakesPerSecond * time) * headShakeDegrees * envelope;
            Rotate(leftEar, flap);
            Rotate(rightEar, flap);
            Rotate(head, shake);
        }

        // Added on top of the controller's pose for this frame (it restores the rest pose before each frame).
        static void Rotate(BoneControl control, float degrees)
        {
            if (control == null || control.bones == null || control.localAxis.sqrMagnitude < .0001f) return;
            float sign = control.degrees < 0 ? -1 : 1; // same direction as the control's positive input
            foreach (var bone in control.bones)
                if (bone) bone.localRotation *= Quaternion.AngleAxis(sign * degrees, control.localAxis.normalized);
        }

        // ---------- Thumbs up / down: one full walking circle ----------

        void StartCircle(float direction)
        {
            if (!locomotion || !locomotion.isActiveAndEnabled || !locomotion.enableLocomotion || !locomotion.travelRoot)
            { message = "Turn Locomotion on to walk a circle."; return; }
            circling = true;
            circleDirection = direction; // +1 turns right = clockwise seen from above
            circleTurned = 0;
            lastYaw = locomotion.travelRoot.eulerAngles.y;
        }

        void UpdateCircle()
        {
            if (!locomotion || !locomotion.travelRoot || !locomotion.enableLocomotion) { StopCircle(); return; }
            float yaw = locomotion.travelRoot.eulerAngles.y;
            circleTurned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
            lastYaw = yaw;
            float rate = Mathf.Max(1, locomotion.turnDegreesPerSecond);
            float turn = Mathf.Clamp01(360f / (Mathf.Max(1, circleSeconds) * rate));
            // Stop a little early: input smoothing keeps turning briefly after release.
            float coast = locomotion.inputSmoothing > 0 ? turn * rate / locomotion.inputSmoothing : 0;
            if (circleTurned >= 360f - coast) { StopCircle(); return; }
            locomotion.scriptedInput = true;
            locomotion.scriptedForward = circleThrottle;
            locomotion.scriptedSteering = circleDirection * turn;
        }

        void StopCircle()
        {
            circling = false;
            if (locomotion) { locomotion.scriptedInput = false; locomotion.scriptedForward = locomotion.scriptedSteering = 0; }
        }

        void BeginNeutralCountdown(float now)
        {
            NeutralSet = armed = false;
            neutralDeadline = now + neutralCountdownSeconds;
            message = "";
        }

        // Inspector ⋮ menu helpers for testing without the camera.
        [ContextMenu("Test: victory (ears + head)")] void TestVictory() => Celebrate();
        [ContextMenu("Test: thumbs up (circle clockwise)")] void TestClockwise() => StartCircle(1);
        [ContextMenu("Test: thumbs down (circle anticlockwise)")] void TestAnticlockwise() => StartCircle(-1);

        // ---------- On-screen panel, bottom right: black on white like the other panels ----------

        static float Px(float points) => DigiPhantUi.Px(points);

        void OnGUI()
        {
            if (!showPanel || controller == null || controller.inputMode != InputMode.Camera) return;
            float now = Time.realtimeSinceStartup, pad = Px(10), gap = Px(4), margin = Px(12);
            // Fits the stage area right of the left control panel; height follows the text.
            float left = controller.showControls ? DigiPhantUi.PanelWidth : 0;
            float w = Mathf.Min(Px(280), Screen.width - left - 2 * margin), inner = w - 2 * pad;

            string prompt; Color color = DigiPhantUi.Ready;
            if (controller.CalibrationPending) prompt = "Setting neutral pose: hold still…";
            else if (!controller.IsCalibrated) prompt = "Step 1: everyone in their zone, then press Neutral pose.";
            else if (neutralDeadline >= 0)
                prompt = "Hold an open palm to the camera… " + Mathf.CeilToInt(Mathf.Max(0, neutralDeadline - now));
            else if (!NeutralSet) prompt = message.Length > 0 ? message : "Step 2: show an open palm, then press Neutral hand.";
            else if (!armed) prompt = "Show an open palm to arm the next gesture.";
            else { prompt = "Victory: ears + head · Thumb up / down: circle right / left"; color = DigiPhantUi.Muted; }

            var titleStyle = DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Bold);
            var statusStyle = DigiPhantUi.Text(DigiPhantUi.Body, DigiPhantUi.Ink, FontStyle.Bold);
            var promptStyle = DigiPhantUi.Text(DigiPhantUi.Small, color, FontStyle.Bold);
            string status = Pretty(SeenGesture) + "  →  " + CurrentGestureAction;
            float titleH = titleStyle.CalcHeight(new GUIContent("HAND GESTURES"), inner);
            float statusH = statusStyle.CalcHeight(new GUIContent(status), inner);
            float promptH = promptStyle.CalcHeight(new GUIContent(prompt), inner);
            float buttonH = Px(DigiPhantUi.ControlHeight);
            float h = pad + titleH + statusH + promptH + gap + buttonH + pad;

            var box = new Rect(Screen.width - w - margin, Screen.height - h - margin, w, h);
            DigiPhantUi.Panel(box);
            var previousSkin = GUI.skin;
            GUI.skin = DigiPhantUi.Skin;
            float x = box.x + pad, y = box.y + pad;
            GUI.Label(new Rect(x, y, inner, titleH), "HAND GESTURES", titleStyle); y += titleH;
            GUI.Label(new Rect(x, y, inner, statusH), status, statusStyle); y += statusH;
            GUI.Label(new Rect(x, y, inner, promptH), prompt, promptStyle); y += promptH + gap;

            float half = (inner - gap) / 2;
            var poseButton = new Rect(x, y, half, buttonH);
            var handButton = new Rect(x + half + gap, y, half, buttonH);
            if (controller.CalibrationPending) { if (GUI.Button(poseButton, "Cancel", DigiPhantUi.Button(DigiPhantUi.Small))) controller.CancelCalibrationCountdown(); }
            else if (GUI.Button(poseButton, "Neutral pose", DigiPhantUi.Button(DigiPhantUi.Small, !controller.IsCalibrated)))
                controller.BeginCalibrationCountdown(now);
            if (neutralDeadline >= 0) { if (GUI.Button(handButton, "Cancel", DigiPhantUi.Button(DigiPhantUi.Small))) neutralDeadline = -1; }
            else if (GUI.Button(handButton, "Neutral hand", DigiPhantUi.Button(DigiPhantUi.Small, controller.IsCalibrated && !NeutralSet)))
                BeginNeutralCountdown(now);
            GUI.skin = previousSkin;
        }

        static string Pretty(string gesture) => gesture switch
        {
            Palm => "Open palm", Victory => "Victory", ThumbUp => "Thumb up", ThumbDown => "Thumb down",
            "None" or "" or null => "—", _ => gesture.Replace('_', ' '),
        };
    }
}
