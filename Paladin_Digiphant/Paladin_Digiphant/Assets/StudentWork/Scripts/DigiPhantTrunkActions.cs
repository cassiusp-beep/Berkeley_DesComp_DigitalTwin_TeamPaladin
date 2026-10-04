using UnityEngine;

namespace DigiPhant
{
    // P3's trunk actions from the Pose Framework (stages 1-2 of the staged plan), without a tracker change:
    // squat with the hand low = reach; hold the reach 1 s = pick up the nearby prop (or drop it if carrying).
    // Stand up between a pick up and a drop. Grabs only count at walk speed or slower.
    // Runs after DigiPhantController and DigiPhantPoseActions so the trunk bones are already posed.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(DigiPhantController))]
    public class DigiPhantTrunkActions : MonoBehaviour
    {
        [Header("Who")]
        [Range(1, 4)] public int trunkPerformer = 3;

        [Header("Reach (squat, hand low)")]
        [Tooltip("Both foot-lift signals must rise above this. A squat raises both; a knee lift raises only one. Session 1: Freddie's squat measured +0.6 to +0.7, standing about 0; the analyzer suggested 0.25.")]
        public float squatOn = .25f;
        [Tooltip("Reach ends when either foot-lift signal falls below this.")]
        public float squatOff = .12f;
        [Tooltip("Right hand must be below this (neutral-relative). Set to 3 to ignore the hand.")]
        public float reachHandMax = -.2f;
        public float reachHoldSeconds = .25f;

        [Header("Pick up / drop (fallback grab)")]
        [Tooltip("Seconds of reach before it counts as a pick up or drop.")]
        public float grabHoldSeconds = 1f;
        [Tooltip("Grabs are ignored above this speed (units/s). The walk speed is 1.8.")]
        public float maxGrabSpeed = 1.8f;
        [Tooltip("How close the trunk tip must be to the prop (ground distance) to pick it up.")]
        public float pickupRadius = 2.5f;

        [Header("Objects")]
        public Transform trunkTip;
        public Transform carryProp;
        [Tooltip("Prop offset from the trunk tip while carried, in world units.")]
        public Vector3 carryOffset = new Vector3(0, -.15f, 0);

        public bool showStatus = true;

        public string CurrentTrunkAction { get; private set; } = "None";
        public bool Carrying { get; private set; }

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        bool reaching, waitForStand;
        float squatSince = -1, reachStart = -1, flashUntil = -1, propRestHeight;
        string flash = "";
        Vector3 propStartPosition;
        Quaternion propStartRotation;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (carryProp)
            {
                propStartPosition = carryProp.position;
                propStartRotation = carryProp.rotation;
                propRestHeight = carryProp.position.y - GroundHeight();
            }
        }

        float GroundHeight() => locomotion && locomotion.travelRoot ? locomotion.travelRoot.position.y : 0;

        float Read(Movement movement, float now) =>
            controller.TryReadMovement(trunkPerformer, movement, now, out float v) ? v : float.NaN;

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup, t = Time.time;
            if (controller.inputMode == InputMode.Camera && controller.IsCalibrated) Detect(now, t);
            else { reaching = waitForStand = false; squatSince = -1; }

            if (Carrying && carryProp && trunkTip) carryProp.position = trunkTip.position + carryOffset;

            CurrentTrunkAction = t < flashUntil ? flash
                : reaching ? (waitForStand ? "Stand up to grab again" : ReachStatus())
                : Carrying ? "Carrying" : "None";
        }

        void Detect(float now, float t)
        {
            float left = Read(Movement.LeftFootLift, now), right = Read(Movement.RightFootLift, now);
            float hand = Read(Movement.RightHandHeight, now);
            if (float.IsNaN(left) || float.IsNaN(right))
            { reaching = waitForStand = false; squatSince = -1; return; } // lost feet = no reach, never a phantom grab

            bool handLow = reachHandMax >= 3 || (!float.IsNaN(hand) && hand < reachHandMax);
            bool squat = left > squatOn && right > squatOn && handLow;
            if (reaching)
            {
                if (Mathf.Min(left, right) < squatOff) { reaching = false; waitForStand = false; }
            }
            else if (squat)
            {
                if (squatSince < 0) squatSince = t;
                if (t - squatSince >= reachHoldSeconds) { reaching = true; reachStart = t; }
            }
            else squatSince = -1;

            if (!reaching || waitForStand || t - reachStart < grabHoldSeconds || !SlowEnough()) return;
            if (Carrying) { Drop(); Flash("Dropped", t); waitForStand = true; }
            else if (PropInRange()) { PickUp(); Flash("Picked up", t); waitForStand = true; }
        }

        string ReachStatus()
        {
            if (!SlowEnough()) return "Reach (too fast to grab)";
            if (!Carrying && !PropInRange()) return "Reach (nothing in range)";
            return Carrying ? "Reach (hold to drop)" : "Reach (hold to pick up)";
        }

        bool SlowEnough() => locomotion == null || Mathf.Abs(locomotion.CurrentSpeed) <= maxGrabSpeed;

        bool PropInRange()
        {
            if (!carryProp || !trunkTip) return false;
            Vector3 d = carryProp.position - trunkTip.position;
            d.y = 0;
            return d.magnitude <= pickupRadius;
        }

        void Flash(string text, float t) { flash = text; flashUntil = t + 1f; }

        void PickUp() { if (carryProp && trunkTip) Carrying = true; }

        void Drop()
        {
            Carrying = false;
            if (!carryProp) return;
            Vector3 p = carryProp.position;
            carryProp.position = new Vector3(p.x, GroundHeight() + propRestHeight, p.z);
            carryProp.rotation = propStartRotation;
        }

        // Inspector ⋮ menu helpers for testing without the camera.
        [ContextMenu("Test: pick up or drop")]
        void TestToggle() { if (Carrying) Drop(); else PickUp(); }

        [ContextMenu("Reset prop to its start")]
        void ResetProp()
        {
            Carrying = false;
            if (carryProp) carryProp.SetPositionAndRotation(propStartPosition, propStartRotation);
        }

        void OnGUI()
        {
            if (!showStatus || controller == null) return;
            var poseActions = GetComponent<DigiPhantPoseActions>();
            if (poseActions && poseActions.isActiveAndEnabled && poseActions.showSignals) return; // shown in its status bar
            float w = 300, h = 24;
            var r = new Rect(Screen.width - w - 10, Screen.height - h - 10, w, h);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 6, r.y + 3, w - 12, h), $"<b>P{trunkPerformer} trunk:</b> {CurrentTrunkAction}",
                new GUIStyle(GUI.skin.label) { richText = true });
        }
    }
}
