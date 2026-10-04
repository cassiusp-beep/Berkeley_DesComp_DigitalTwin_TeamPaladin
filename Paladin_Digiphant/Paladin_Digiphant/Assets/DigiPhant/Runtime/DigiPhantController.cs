using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DigiPhant
{
    public enum Movement { LeftHandHeight, RightHandHeight, LeftFootLift, RightFootLift, Lean, ArmSpread }
    public enum InputMode { TestSliders, Camera }

    [Serializable] public class PerformerFrame
    {
        public int slot;
        public float[] values;
        public float[] confidence;
    }
    [Serializable] public class PoseFrame
    {
        public int version;
        public int performerCount;
        public bool upperBodyOnly;
        public PerformerFrame[] people;
    }
    [Serializable] public class BoneControl
    {
        public string label;
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        public Transform[] bones;
        [Tooltip("Rotation axis in each bone's local coordinates.")]
        public Vector3 localAxis = Vector3.forward;
        [Tooltip("Maximum rotation per bone for an input of 1. Negative values invert direction.")]
        public float degrees = 20;
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, 20)] public float smoothing = 8;
        [Range(-1, 1)] public float testValue;
        [Tooltip("How much of this gesture is added over locomotion animation. Zero lets the animation own this joint.")]
        [Range(0, 1)] public float locomotionWeight = 1;
        [NonSerialized] public float current;
    }

    public class DigiPhantController : MonoBehaviour
    {
        public InputMode inputMode = InputMode.TestSliders;
        [HideInInspector] public int performerCount = 3;
        public bool upperBodyOnly;
        public BoneControl[] controls = Array.Empty<BoneControl>();
        [Tooltip("Local camera bridge port. Change outside Play mode.")]
        public int port = 5055;
        [Range(0, 1)] public float minimumConfidence = .5f;
        [Min(.1f)] public float trackingTimeout = .5f;
        public bool showControls = true;
        public bool showCameraPreview = true;
        [Tooltip("On-screen UI scale. 0 = automatic (2x on Retina). Set 1, 1.5, 2… if the text looks too small or large.")]
        [Range(0, 3)] public float uiScale;
        public string Status { get; private set; } = "Test sliders ready";
        public int ReceivedFrames { get; private set; }
        public bool IsCalibrated { get; private set; }
        float calibrationDeadline = -1;
        public bool CalibrationPending => calibrationDeadline >= 0;
        readonly float[,] values = new float[4, 6];
        readonly float[,] confidence = new float[4, 6];
        readonly float[,] neutral = new float[4, 6];
        readonly float[] lastSeen = { -1000, -1000, -1000, -1000 };
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        UdpClient socket;
        Vector2 scroll;
        bool showBodySliders;
        float nextCountRequest;
        int previousCount;
        InputMode previousMode;
        bool previousUpperBodyOnly;

        void OnEnable()
        {
            if (Application.isPlaying && !Application.isBatchMode && showCameraPreview && GetComponent<DigiPhantCameraPreview>() == null)
                gameObject.AddComponent<DigiPhantCameraPreview>();
            if (Application.isPlaying && !Application.isBatchMode && GetComponent<DigiPhantRecording>() == null)
                gameObject.AddComponent<DigiPhantRecording>();
            previousCount = performerCount;
            previousUpperBodyOnly = upperBodyOnly;
            ClearTracking();
            nextCountRequest = 0;
            ReceivedFrames = 0;
            Status = "Test sliders ready";
            CaptureRestPose();
            previousMode = inputMode;
            if (inputMode == InputMode.Camera) StartReceiver();
        }
        public void SetPerformerCount(int count)
        {
            performerCount = previousCount = Mathf.Clamp(count, 1, 4);
            foreach (var c in controls)
            {
                switch (c.label)
                {
                    case "Front left leg": case "Front right leg": c.performer = 1; break;
                    case "Rear left leg": case "Rear right leg": case "Tail sway":
                        c.performer = Mathf.Min(2, performerCount); break;
                    case "Head turn": case "Left ear": case "Right ear":
                        c.performer = Mathf.Min(3, performerCount); break;
                    case "Trunk curl": c.performer = performerCount; break;
                    default: c.performer = Mathf.Clamp(c.performer, 1, performerCount); break;
                }
            }
            GetComponent<DigiPhantLocomotion>()?.ClampPerformers(performerCount);
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Group size changed. Set neutral pose again in Camera mode.";
        }
        public void SetUpperBodyOnly(bool enabled)
        {
            upperBodyOnly = previousUpperBodyOnly = enabled;
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Movement mode changed. Wait for camera, then set neutral pose.";
        }
        public void CaptureRestPose()
        {
            rest.Clear();
            foreach (var c in controls)
                if (c.bones != null)
                    foreach (var bone in c.bones)
                        if (bone != null && !rest.ContainsKey(bone)) rest.Add(bone, bone.localRotation);
        }
        void StartReceiver()
        {
            StopReceiver();
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                socket.Client.Blocking = false;
                Status = "Waiting for local camera bridge";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { StopReceiver(); Status = "Cannot open camera port: " + e.Message; }
        }
        void StopReceiver() { socket?.Close(); socket = null; }
        public void SetInputMode(InputMode mode)
        {
            inputMode = previousMode = mode;
            ClearTracking();
            if (mode == InputMode.Camera) StartReceiver();
            else { StopReceiver(); Status = "Test sliders ready"; }
        }
        void ClearTracking()
        {
            calibrationDeadline = -1;
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
            IsCalibrated = false;
            Array.Clear(confidence, 0, confidence.Length);
            Array.Clear(neutral, 0, neutral.Length);
            for (int i = 0; i < 4; i++) lastSeen[i] = -1000;
        }
        public bool AcceptPacket(string json, float now)
        {
            PoseFrame frame;
            try { frame = JsonUtility.FromJson<PoseFrame>(json); }
            catch (ArgumentException) { return false; }
            if (frame == null || frame.version != 1 || frame.people == null || frame.people.Length > 4) return false;
            if (frame.performerCount != 0 && frame.performerCount != performerCount)
            {
                Array.Clear(confidence, 0, confidence.Length);
                Status = "Updating camera group size...";
                return false;
            }
            if (frame.upperBodyOnly != upperBodyOnly)
            {
                Array.Clear(confidence, 0, confidence.Length);
                Status = "Updating camera movement mode...";
                return false;
            }
            var slots = new HashSet<int>();
            foreach (var p in frame.people)
            {
                if (p == null || p.slot < 1 || p.slot > 4 || !slots.Add(p.slot) ||
                    p.values == null || p.confidence == null || p.values.Length != 6 || p.confidence.Length != 6) return false;
                for (int k = 0; k < 6; k++)
                    if (!Finite(p.values[k]) || !Finite(p.confidence[k]) || p.confidence[k] < 0 || p.confidence[k] > 1) return false;
            }
            Array.Clear(confidence, 0, confidence.Length);
            foreach (var p in frame.people)
            {
                int i = p.slot - 1;
                lastSeen[i] = now;
                for (int k = 0; k < 6; k++)
                { values[i, k] = Mathf.Clamp(p.values[k], -3, 3); confidence[i, k] = p.confidence[k]; }
            }
            ReceivedFrames++;
            return true;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public void PollCamera(float now)
        {
            if (socket == null) return;
            try
            {
                if (now >= nextCountRequest)
                {
                    var request = Encoding.UTF8.GetBytes("{\"version\":1,\"performerCount\":" + performerCount + ",\"upperBodyOnly\":" + (upperBodyOnly ? "true" : "false") + "}");
                    socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                    nextCountRequest = now + .5f;
                }
                // Bound per-frame work; use the newest datagram from this batch.
                byte[] latest = null;
                IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref sender);
                if (latest != null && latest.Length < 16000 && !AcceptPacket(Encoding.UTF8.GetString(latest), now))
                    Status = "Ignored invalid camera packet";
            }
            catch (SocketException e) { Status = "Camera connection: " + e.Message; }
        }
        public bool IsTracked(int slot, float now)
        {
            int i = slot - 1;
            if (i < 0 || i >= 4 || now - lastSeen[i] > trackingTimeout) return false;
            for (int k = 0; k < 6; k++) if (confidence[i, k] >= minimumConfidence) return true;
            return false;
        }
        public void ReassignPeople()
        {
            ClearTracking();
            if (socket == null) return;
            try
            {
                var request = Encoding.UTF8.GetBytes("{\"version\":1,\"performerCount\":" + performerCount + ",\"upperBodyOnly\":" + (upperBodyOnly ? "true" : "false") + ",\"reset\":true}");
                socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                Status = "Reassigning people. Set neutral pose when visible.";
            }
            catch (SocketException e) { Status = e.Message; }
        }
        public void InvalidateCalibration()
        {
            calibrationDeadline = -1;
            IsCalibrated = false;
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
        }
        public bool IsMovementVisible(int performer, Movement movement, float now)
        {
            int i = performer - 1, k = (int)movement;
            return i >= 0 && i < performerCount && k >= 0 && k < 6 &&
                IsTracked(performer, now) && confidence[i, k] >= minimumConfidence;
        }
        public bool TryReadMovement(int performer, Movement movement, float now, out float value)
        {
            value = 0;
            if (!IsCalibrated || !IsMovementVisible(performer, movement, now)) return false;
            value = values[performer - 1, (int)movement] - neutral[performer - 1, (int)movement];
            return true;
        }
        public void BeginCalibrationCountdown(float now)
        {
            if (inputMode != InputMode.Camera) return;
            InvalidateCalibration();
            ResetControls();
            calibrationDeadline = now + 10;
            Status = "Get into your neutral pose and hold still.";
        }
        public void CancelCalibrationCountdown()
        {
            calibrationDeadline = -1;
            Status = "Calibration cancelled. Set neutral pose when ready.";
        }
        public void UpdateCalibrationCountdown(float now)
        {
            if (!CalibrationPending || now < calibrationDeadline) return;
            calibrationDeadline = -1;
            CalibrateAt(now);
        }
        public void Calibrate() => CalibrateAt(Time.realtimeSinceStartup);
        public bool CalibrateAt(float now)
        {
            calibrationDeadline = -1;
            // Require every mapped input, so occluded landmarks cannot silently become neutral.
            foreach (var c in controls)
            {
                int i = c.performer - 1, k = (int)c.movement;
                if (i < 0 || i >= performerCount || k < 0 || k >= 6 ||
                    !IsTracked(c.performer, now) || confidence[i, k] < minimumConfidence)
                { Status = "Calibration needs all assigned movements visible"; return false; }
            }
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion != null && locomotion.isActiveAndEnabled && !locomotion.InputsVisible(now))
            { Status = "Calibration needs speed and steering inputs visible"; return false; }
            Array.Copy(values, neutral, values.Length);
            IsCalibrated = true;
            Status = "Neutral pose saved";
            return true;
        }
        public void ResetControls()
        {
            foreach (var c in controls) { c.testValue = 0; c.current = 0; }
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
        }
        public void ApplyControls(float now, float dt)
        {
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            var locomotion = GetComponent<DigiPhantLocomotion>();
            bool animated = locomotion != null && locomotion.isActiveAndEnabled && locomotion.Evaluate(now, dt);
            foreach (var c in controls)
            {
                float target = 0;
                int i = c.performer - 1, k = (int)c.movement;
                if (i >= 0 && i < performerCount && k >= 0 && k < 6)
                {
                    if (inputMode == InputMode.TestSliders) target = c.testValue;
                    else if (IsCalibrated && IsTracked(c.performer, now) && confidence[i, k] >= minimumConfidence)
                        target = values[i, k] - neutral[i, k];
                }
                target = Mathf.Clamp(target * c.sensitivity, -1, 1);
                c.current = c.smoothing <= 0 ? target : Mathf.Lerp(c.current, target, 1 - Mathf.Exp(-c.smoothing * dt));
                if (c.bones == null || c.localAxis.sqrMagnitude < .0001f) continue;
                foreach (var bone in c.bones)
                    if (bone != null && rest.ContainsKey(bone))
                        bone.localRotation *= Quaternion.AngleAxis(c.current * c.degrees * (animated ? c.locomotionWeight : 1), c.localAxis.normalized);
            }
        }
        void LateUpdate()
        {
            if (previousUpperBodyOnly != upperBodyOnly) SetUpperBodyOnly(upperBodyOnly);
            if (previousCount != performerCount) SetPerformerCount(performerCount);
            if (previousMode != inputMode) SetInputMode(inputMode);
            if (inputMode == InputMode.Camera) PollCamera(Time.realtimeSinceStartup);
            UpdateCalibrationCountdown(Time.realtimeSinceStartup);
            ApplyControls(Time.realtimeSinceStartup, Time.deltaTime);
            var stageCamera = Camera.main;
            if (stageCamera != null)
            {
                DigiPhantUi.ScaleOverride = uiScale;
                float left = showControls ? DigiPhantUi.PanelWidth / Mathf.Max(1, Screen.width) : 0;
                stageCamera.rect = new Rect(left, 0, 1 - left, 1);
            }
        }
        void OnDisable() { calibrationDeadline = -1; StopReceiver(); GetComponent<DigiPhantLocomotion>()?.StopAnimation(); ResetControls(); }
        void OnGUI()
        {
            if (!showControls) return;
            DigiPhantUi.ScaleOverride = uiScale;
            float panel = DigiPhantUi.PanelWidth, edge = DigiPhantUi.Px(10);
            float width = panel - 2 * edge, inner = width - DigiPhantUi.Px(10), now = Time.realtimeSinceStartup;
            var previousSkin = GUI.skin;
            GUI.skin = DigiPhantUi.Skin;
            // White side panel with a thin rule on its right edge.
            GUI.DrawTexture(new Rect(0, 0, panel, Screen.height), DigiPhantUi.White);
            GUI.DrawTexture(new Rect(panel - 1, 0, 1, Screen.height), DigiPhantUi.Light);
            var note = DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted);
            void Note(string text) => DigiPhantUi.Note(text, inner);

            GUILayout.BeginArea(new Rect(edge, edge, width, Screen.height - 2 * edge));
            GUILayout.Label("DIGIPHANT", DigiPhantUi.Text(DigiPhantUi.Title, DigiPhantUi.Ink, FontStyle.Bold));
            GUILayout.Space(DigiPhantUi.Px(4));
            GetComponent<DigiPhantCameraPreview>()?.DrawInline(width);
            scroll = GUILayout.BeginScrollView(scroll);

            DigiPhantUi.Section("SETUP");
            int count = DigiPhantUi.Segmented("People", DigiPhantUi.PeopleIcon, performerCount - 1, "1", "2", "3", "4") + 1;
            if (count != performerCount) SetPerformerCount(count);
            bool upper = DigiPhantUi.Segmented("Body", DigiPhantUi.BodyIcon, upperBodyOnly ? 1 : 0, "Full body", "Seated") == 1;
            if (upper != upperBodyOnly) SetUpperBodyOnly(upper);
            int mode = DigiPhantUi.Segmented("Input", DigiPhantUi.InputIcon, (int)inputMode, "Test sliders", "Camera");
            if (mode != (int)inputMode) SetInputMode((InputMode)mode);
            if (upperBodyOnly) Note("Seated: keep shoulders and hands visible. Hands replace feet for the legs.");
            GUILayout.Space(DigiPhantUi.Px(2));
            GUILayout.Label(Status, DigiPhantUi.Text(DigiPhantUi.Body, DigiPhantUi.Ink, FontStyle.Bold), GUILayout.Width(inner));

            if (inputMode == InputMode.Camera)
            {
                DigiPhantUi.Section("CAMERA");
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= performerCount; i++) { DigiPhantUi.Dot("P" + i, IsTracked(i, now)); GUILayout.Space(DigiPhantUi.Px(8)); }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (CalibrationPending)
                {
                    int seconds = Mathf.Max(0, Mathf.CeilToInt(calibrationDeadline - now));
                    GUILayout.Label("Hold your neutral pose… " + seconds, DigiPhantUi.Text(DigiPhantUi.Body, DigiPhantUi.Ready, FontStyle.Bold), GUILayout.Width(inner));
                }
                else if (!IsCalibrated) Note("Everyone in their zone, then set neutral pose.");
                GUILayout.BeginHorizontal();
                if (CalibrationPending) { if (GUILayout.Button("Cancel countdown")) CancelCalibrationCountdown(); }
                else if (GUILayout.Button("Set neutral pose (10 s)", DigiPhantUi.Button(DigiPhantUi.Body, !IsCalibrated))) BeginCalibrationCountdown(now);
                if (GUILayout.Button("Reassign people")) ReassignPeople();
                GUILayout.EndHorizontal();
            }

            var recording = GetComponent<DigiPhantRecording>();
            if (recording) { DigiPhantUi.Section("RECORD"); recording.DrawControls(inner); }

            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion) { DigiPhantUi.Section("MOVEMENT"); locomotion.DrawControls(inner); }

            if (inputMode == InputMode.TestSliders)
            {
                DigiPhantUi.Section("BODY PART SLIDERS");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(showBodySliders ? "Hide" : "Show " + controls.Length + " sliders")) showBodySliders = !showBodySliders;
                if (showBodySliders && GUILayout.Button("Reset")) ResetControls();
                GUILayout.EndHorizontal();
                if (showBodySliders)
                    foreach (var c in controls)
                    {
                        GUILayout.Label(c.label + "  ·  P" + c.performer, note, GUILayout.Width(inner));
                        c.testValue = GUILayout.HorizontalSlider(c.testValue, -1, 1);
                    }
            }
            GUILayout.Space(DigiPhantUi.Px(10));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.skin = previousSkin;
        }
    }
}
