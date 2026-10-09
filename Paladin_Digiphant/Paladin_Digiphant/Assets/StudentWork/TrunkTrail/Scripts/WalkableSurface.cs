using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant
{
    // A raised walkable top (the balance beam log) that GroundFollow treats as ground while the elephant is over it.
    // A box on this object: local X = half width, local Z = half length, and the walking height is topLocalY above
    // this object. The edges slope down to the terrain over `ramp` (x: across, y: along), so stepping on and off is smooth.
    // Only registered while enabled, so a surface under the switched-off bonus branch does nothing.
    public class WalkableSurface : MonoBehaviour
    {
        public Vector2 halfSize = new Vector2(1.25f, 4);
        public float topLocalY = .5f;
        public Vector2 ramp = new Vector2(.4f, 1.5f);

        static readonly List<WalkableSurface> All = new List<WalkableSurface>();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        // The ground height at (x, z): the terrain, or a registered surface's top if that is higher.
        public static float Raise(float x, float z, float terrainY)
        {
            for (int i = 0; i < All.Count; i++) terrainY = All[i].RaiseOne(x, z, terrainY);
            return terrainY;
        }

        float RaiseOne(float x, float z, float y)
        {
            Vector3 p = transform.InverseTransformPoint(x, transform.position.y, z);
            float ex = halfSize.x - Mathf.Abs(p.x), ez = halfSize.y - Mathf.Abs(p.z);
            if (ex <= 0 || ez <= 0) return y;
            float k = Mathf.Min(Mathf.SmoothStep(0, 1, ex / Mathf.Max(.01f, ramp.x)), Mathf.SmoothStep(0, 1, ez / Mathf.Max(.01f, ramp.y)));
            float top = transform.position.y + topLocalY;
            return Mathf.Max(y, Mathf.Lerp(y, top, k));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(new Vector3(0, topLocalY, 0), new Vector3(halfSize.x * 2, .05f, halfSize.y * 2));
        }
    }
}
