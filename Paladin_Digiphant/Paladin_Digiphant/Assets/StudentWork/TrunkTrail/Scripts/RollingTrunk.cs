using UnityEngine;

namespace DigiPhant
{
    // A log that rolls back and forth across a lane on a timed loop. Put it under the lane object, with the log mesh
    // as a child lying along local Z. It only moves; touching it is detected by the Rolling TrunkCheckpoint (no physics).
    public class RollingTrunk : MonoBehaviour
    {
        [Tooltip("Sideways travel from the lane centre, in units.")]
        public float halfSpan = 2f;
        public float periodSeconds = 5;
        [Tooltip("Where in the loop this log starts, 0 to 1.")]
        public float phase;
        public float radius = .4f, halfLength = 1.1f;

        Vector3 basePosition;
        Quaternion baseRotation;

        void OnEnable() { basePosition = transform.localPosition; baseRotation = transform.localRotation; }
        void OnDisable() { transform.SetLocalPositionAndRotation(basePosition, baseRotation); }

        void Update()
        {
            float x = halfSpan * Mathf.Sin(2 * Mathf.PI * (Time.time / Mathf.Max(.1f, periodSeconds) + phase));
            transform.SetLocalPositionAndRotation(basePosition + Vector3.right * x,
                baseRotation * Quaternion.Euler(0, 0, -x / radius * Mathf.Rad2Deg)); // rolls without slipping
        }

        // True when a ground point is closer than `reach` to the log's body (horizontal distance to its axis).
        public bool Touches(Vector3 point, float reach)
        {
            Vector3 axis = transform.forward * halfLength, c = transform.position;
            Vector2 a = new Vector2(c.x - axis.x, c.z - axis.z), b = new Vector2(c.x + axis.x, c.z + axis.z), q = new Vector2(point.x, point.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            return (q - (a + ab * t)).magnitude <= radius + reach;
        }
    }
}
