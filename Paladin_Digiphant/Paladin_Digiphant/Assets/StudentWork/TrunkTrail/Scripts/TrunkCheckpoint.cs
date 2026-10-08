using UnityEngine;

namespace DigiPhant
{
    public enum CheckpointMode { Hurdle, Carry, Tunnel, Beam, Rolling, Deliver, Finish }
    public enum CheckpointState { Waiting, Active, Passed, Missed }

    // Feedback only: nothing blocks the elephant. The zone is a box on this object: local X = width, local Z = depth
    // (Z points along the course). The elephant is the travel root's position, so no colliders or rigidbodies are needed.
    //   Hurdle : pass if a "Jump" pose action happens while inside; miss when leaving without one.
    //   Carry  : "pick up" - pass when the trunk is carrying the prop while inside the zone; miss when leaving the far half without it.
    //   Deliver: pass when the prop has been dropped inside the drop zone (the finish stone); miss when leaving the far half without it.
    //   Tunnel / Beam : pass when going from the entry end to the exit end without leaving the lane width for longer than
    //                   laneGraceSeconds (tracking is noisy, so a brief step out does not count).
    //   Rolling : pass on reaching the exit; touching a rolling log counts as a miss ("try again") but play continues.
    //   Finish : not counted. Crossing it forwards ends the run (timer stops, "Finished n / total"), misses or not.
    // A miss can be retried by re-entering. A passed checkpoint stays passed.
    [DefaultExecutionOrder(300)]
    public class TrunkCheckpoint : MonoBehaviour
    {
        public CheckpointMode mode;
        public string label = "Checkpoint";
        public TrunkTrailProgress progress;
        [Tooltip("Width (X) and depth (Z) of the zone, in units. Tunnel, Beam and Rolling: lane width and lane length.")]
        public Vector2 zoneSize = new Vector2(6, 3);
        [Header("Deliver")]
        public Transform dropZone;
        public float dropRadius = 1.6f;
        [Header("Tunnel and Beam")]
        [Tooltip("How long the elephant may be outside the lane before it counts as leaving it.")]
        public float laneGraceSeconds = .4f;
        [Header("Rolling")]
        public RollingTrunk[] rollingTrunks;
        public float touchRadius = 1.2f;
        [Header("Marker colours")]
        public Renderer[] markers;
        public Material idleMaterial, passMaterial, missMaterial;
        [Tooltip("Passed markers rise and missed markers sink by this much (a cue besides colour).")]
        public float markerShift = .3f;

        public CheckpointState State { get; private set; }

        Transform elephant;
        DigiPhantPoseActions pose;
        DigiPhantTrunkActions trunk;
        bool inside, attempting, jumped, touched;
        float outSince = -1, lastZ = float.NaN, markerOffset;
        Vector3[] markerBase;

        // Re-enabling (the bonus branch switching on) clears what happened while away but keeps Passed and Missed.
        void OnEnable()
        {
            inside = attempting = jumped = touched = false;
            outSince = -1; lastZ = float.NaN;
            if (State == CheckpointState.Active) Set(CheckpointState.Waiting);
        }

        public void ResetCheckpoint()
        {
            inside = attempting = jumped = touched = false;
            outSince = -1; lastZ = float.NaN;
            Set(CheckpointState.Waiting);
        }

        // What this checkpoint asks for and who does it, for the HUD. Uses the real gesture settings.
        public string Hint
        {
            get
            {
                string hold = (trunk ? trunk.grabHoldSeconds : 1f).ToString("0.#");
                string p3 = "P" + (trunk ? trunk.trunkPerformer : 3), p1 = "P" + (pose ? pose.driver : 1);
                switch (mode)
                {
                    case CheckpointMode.Hurdle: return "Hurdle: " + p1 + " lift right knee to jump";
                    case CheckpointMode.Carry: return "Pick up: " + p3 + " squat, hand low, hold " + hold + " s";
                    case CheckpointMode.Deliver: return "Deliver: " + p3 + " drop the log on the finish stone (squat, hand low, hold " + hold + " s)";
                    case CheckpointMode.Tunnel: return "Tunnel: stay in the lane";
                    case CheckpointMode.Beam: return "Beam: stay in the lane";
                    case CheckpointMode.Rolling: return "Rolling: dodge the logs";
                    default: return "";
                }
            }
        }

        void Set(CheckpointState state)
        {
            bool changed = state != State;
            State = state;
            markerOffset = state == CheckpointState.Passed ? markerShift : state == CheckpointState.Missed ? -markerShift : 0;
            var material = state == CheckpointState.Passed ? passMaterial : state == CheckpointState.Missed ? missMaterial : idleMaterial;
            if (material && markers != null)
                foreach (var m in markers) if (m) m.sharedMaterial = material;
            if (changed && progress) progress.OnCheckpointChanged(this);
        }

        // Markers rise when passed and sink when missed, so the result does not depend on telling colours apart.
        void Update()
        {
            if (markers == null || markers.Length == 0) return;
            if (markerBase == null || markerBase.Length != markers.Length)
            {
                markerBase = new Vector3[markers.Length];
                for (int i = 0; i < markers.Length; i++) if (markers[i]) markerBase[i] = markers[i].transform.localPosition;
            }
            for (int i = 0; i < markers.Length; i++)
            {
                if (!markers[i]) continue;
                var t = markers[i].transform;
                Vector3 target = markerBase[i] + Vector3.up * markerOffset;
                t.localPosition = Vector3.MoveTowards(t.localPosition, target, 1.2f * Time.deltaTime);
            }
        }

        void Miss() { if (State != CheckpointState.Passed) Set(CheckpointState.Missed); }
        void Pass() { Set(CheckpointState.Passed); }

        float nextSearch;

        bool FindElephant()
        {
            if (elephant) return true;
            if (Time.unscaledTime < nextSearch) return false;
            nextSearch = Time.unscaledTime + 1; // a scene search every frame per checkpoint would be wasteful
            var locomotion = FindAnyObjectByType<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot) return false;
            elephant = locomotion.travelRoot;
            pose = locomotion.GetComponent<DigiPhantPoseActions>();
            trunk = locomotion.GetComponent<DigiPhantTrunkActions>();
            if (mode == CheckpointMode.Hurdle && !pose)
                Debug.LogWarning("Trunk Trail: \"" + label + "\" is a hurdle but there is no DigiPhantPoseActions next to the locomotion, so it can never pass.", this);
            if ((mode == CheckpointMode.Carry || mode == CheckpointMode.Deliver) && (!trunk || !trunk.carryProp))
                Debug.LogWarning("Trunk Trail: \"" + label + "\" needs DigiPhantTrunkActions with a carry prop, so it can never pass.", this);
            if (mode == CheckpointMode.Deliver && !dropZone)
                Debug.LogWarning("Trunk Trail: \"" + label + "\" has no drop zone, so it can never pass.", this);
            return true;
        }

        void LateUpdate()
        {
            if (!FindElephant()) return;
            Vector3 p = transform.InverseTransformPoint(elephant.position);
            float hw = zoneSize.x / 2, hd = zoneSize.y / 2;
            bool box = Mathf.Abs(p.x) <= hw && Mathf.Abs(p.z) <= hd;
            if (mode == CheckpointMode.Hurdle) UpdateHurdle(box);
            else if (mode == CheckpointMode.Carry) UpdatePickUp(box, p.z > 0);
            else if (mode == CheckpointMode.Deliver) UpdateDeliver(box, p.z > 0);
            else if (mode == CheckpointMode.Finish) UpdateFinish(p, hw, hd);
            else UpdateLane(p, hw, hd);
        }

        void Begin() { if (State != CheckpointState.Passed) Set(CheckpointState.Active); }

        void UpdateHurdle(bool box)
        {
            if (box && !inside) { inside = true; jumped = false; Begin(); }
            if (box && !jumped && pose && pose.CurrentPoseAction.Contains("Jump")) { jumped = true; Pass(); }
            if (!box && inside) { inside = false; if (!jumped) Miss(); }
        }

        void UpdatePickUp(bool box, bool pastMiddle)
        {
            if (box && !inside) { inside = true; Begin(); }
            if (box && trunk && trunk.Carrying) Pass();
            if (!box && inside) { inside = false; LeaveWithoutPassing(pastMiddle); }
        }

        void UpdateDeliver(bool box, bool pastMiddle)
        {
            if (box && !inside) { inside = true; Begin(); }
            if (box && trunk && trunk.carryProp && dropZone && !trunk.Carrying)
            {
                Vector3 d = trunk.carryProp.position - dropZone.position;
                d.y = 0;
                if (d.magnitude <= dropRadius) Pass();
            }
            if (!box && inside) { inside = false; LeaveWithoutPassing(pastMiddle); }
        }

        void LeaveWithoutPassing(bool pastMiddle)
        {
            if (State != CheckpointState.Active) return;
            if (pastMiddle) Miss(); else Set(CheckpointState.Waiting);
        }

        // Crossing forwards through the arch ends the run, whatever was missed.
        void UpdateFinish(Vector3 p, float hw, float hd)
        {
            bool inZone = Mathf.Abs(p.x) <= hw && Mathf.Abs(p.z) <= hd;
            if (inZone && !float.IsNaN(lastZ) && lastZ < 0 && p.z >= 0 && progress) progress.Finish();
            lastZ = inZone ? p.z : float.NaN;
        }

        void UpdateLane(Vector3 p, float hw, float hd)
        {
            bool inLane = Mathf.Abs(p.x) <= hw;
            if (!attempting)
            {
                if (inLane && p.z >= -hd && p.z <= -hd + 2) { attempting = true; touched = false; outSince = -1; Begin(); }
                return;
            }
            // Tunnel and Beam forgive a brief step out of the lane (noisy tracking); Rolling checks the exit position only.
            if (inLane) outSince = -1;
            else if (outSince < 0) outSince = Time.time;
            bool grace = mode != CheckpointMode.Rolling && outSince >= 0 && Time.time - outSince < laneGraceSeconds;
            if (p.z >= hd) { attempting = false; if ((inLane || grace) && !touched) Pass(); else Miss(); return; }
            if (p.z < -hd - 1) { attempting = false; if (State == CheckpointState.Active) Set(CheckpointState.Waiting); return; }
            if (mode == CheckpointMode.Rolling)
            {
                if (rollingTrunks != null && !touched)
                    foreach (var log in rollingTrunks)
                        if (log && log.Touches(elephant.position, touchRadius))
                        { touched = true; Miss(); if (progress) progress.Notify("Try again: " + label); break; }
            }
            else if (!inLane && !grace) { attempting = false; Miss(); }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.up * .5f, new Vector3(zoneSize.x, 1, zoneSize.y));
        }
    }
}
