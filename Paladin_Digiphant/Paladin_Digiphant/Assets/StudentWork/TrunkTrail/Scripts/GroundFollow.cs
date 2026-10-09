using UnityEngine;

namespace DigiPhant
{
    // Puts the elephant on the Trunk Trail terrain without touching DigiPhantLocomotion, which moves on a flat plane
    // and resets the travel root's height to its start height every frame. Runs after locomotion (controller, order 0),
    // pose actions (100) and trunk actions (110), so jumps still move only the model under the travel root.
    // Height and camera height are absolute each frame (no accumulated drift); the body tilts smoothly to the slope.
    [DefaultExecutionOrder(200)]
    public class GroundFollow : MonoBehaviour
    {
        public DigiPhantLocomotion locomotion;
        public Terrain terrain;
        [Tooltip("Largest slope tilt of the body, in degrees.")]
        [Range(0, 20)] public float maxTiltDegrees = 12;
        [Tooltip("Higher = the body follows the slope faster.")]
        [Range(0, 20)] public float tiltSmoothing = 8;
        [Tooltip("Higher = the camera follows the ground height faster.")]
        [Range(0, 30)] public float cameraSmoothing = 10;
        [Tooltip("Half length and half width of the footprint sampled for the slope, in units.")]
        public Vector2 footprint = new Vector2(1.2f, .6f);

        Vector3 smoothNormal = Vector3.up;
        float cameraHeight, cameraOffset, propHalfHeight;
        bool ready, wasCarrying;
        DigiPhantTrunkActions trunk;
        Transform watchedProp;

        void OnEnable()
        {
            if (!locomotion) locomotion = GetComponent<DigiPhantLocomotion>();
            trunk = GetComponent<DigiPhantTrunkActions>(); // optional
            ready = false; smoothNormal = Vector3.up; watchedProp = null;
        }

        // The terrain, or the top of a WalkableSurface (the balance beam log) where one is higher.
        float Ground(float x, float z) => WalkableSurface.Raise(x, z, terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y);

        void LateUpdate()
        {
            if (!locomotion || !terrain || !terrain.terrainData || !locomotion.travelRoot) return;
            var root = locomotion.travelRoot;
            var cam = locomotion.followCamera;
            Vector3 p = root.position;
            float y = Ground(p.x, p.z);
            if (!ready)
            {
                // Same offset locomotion keeps: camera start minus travel root start.
                cameraOffset = cam ? cam.transform.position.y - p.y : 0;
                cameraHeight = y + cameraOffset;
                ready = true;
            }

            // Stateless heading: the current forward axis, flattened. The tilt below is a vertical projection of this heading
            // onto the slope plane, which leaves its compass direction exactly unchanged, so reading it back next frame
            // gives the same heading (no drift on a cross slope), and any rotation written by locomotion (steering) or
            // PoseActions (the about-turn's Euler yaw) or a teleport is simply picked up as the new heading.
            Vector3 heading = root.forward; heading.y = 0;
            heading = heading.sqrMagnitude < 1e-6f ? Vector3.forward : heading.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 front = p + heading * footprint.x, back = p - heading * footprint.x;
            Vector3 east = p + right * footprint.y, west = p - right * footprint.y;
            Vector3 along = new Vector3(front.x, Ground(front.x, front.z), front.z) - new Vector3(back.x, Ground(back.x, back.z), back.z);
            Vector3 across = new Vector3(east.x, Ground(east.x, east.z), east.z) - new Vector3(west.x, Ground(west.x, west.z), west.z);
            Vector3 normal = Vector3.Cross(along, across).normalized;
            if (normal.y < 0) normal = -normal;
            normal = Vector3.RotateTowards(Vector3.up, normal, maxTiltDegrees * Mathf.Deg2Rad, 0);
            float k = tiltSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-tiltSmoothing * Time.deltaTime);
            smoothNormal = Vector3.Slerp(smoothNormal, normal, k).normalized;

            // Forward = the heading lifted onto the slope plane (same x and z), up = the smoothed slope normal.
            Vector3 n = smoothNormal;
            Vector3 onSlope = new Vector3(heading.x, -(n.x * heading.x + n.z * heading.z) / n.y, heading.z);
            root.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.LookRotation(onSlope, n));

            // The root just moved after DigiPhantTrunkActions placed the carried prop, so put the prop back on the trunk tip
            // (otherwise it trails one frame behind on slopes and while the camera height eases).
            if (trunk && trunk.Carrying && trunk.carryProp && trunk.trunkTip)
                trunk.carryProp.position = trunk.trunkTip.position + trunk.carryOffset;

            if (cam && locomotion.followElephant)
            {
                float target = y + cameraOffset;
                cameraHeight = cameraSmoothing <= 0 ? target : Mathf.Lerp(cameraHeight, target, 1 - Mathf.Exp(-cameraSmoothing * Time.deltaTime));
                Vector3 c = cam.transform.position;
                cam.transform.position = new Vector3(c.x, cameraHeight, c.z);
            }

            SettleDroppedProp();
        }

        // DigiPhantTrunkActions drops the prop at "travel root height + height it had at start", which floats or sinks it
        // wherever the ground differs from the start level. Put it back on the terrain under it, resting as it did at start.
        void SettleDroppedProp()
        {
            var prop = trunk ? trunk.carryProp : null;
            if (!prop) { watchedProp = null; return; }
            if (prop != watchedProp)
            {
                watchedProp = prop;
                var r = prop.GetComponentInChildren<Renderer>();
                propHalfHeight = r ? r.bounds.extents.y : 0; // the prop's rotation is unchanged while carried
                wasCarrying = trunk.Carrying;
            }
            if (wasCarrying && !trunk.Carrying)
            {
                Vector3 q = prop.position;
                prop.position = new Vector3(q.x, Ground(q.x, q.z) + propHalfHeight, q.z);
            }
            wasCarrying = trunk.Carrying;
        }
    }
}
