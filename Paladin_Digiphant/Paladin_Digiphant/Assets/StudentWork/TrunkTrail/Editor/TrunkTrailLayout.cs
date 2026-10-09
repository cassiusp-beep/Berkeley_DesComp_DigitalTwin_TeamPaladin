using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant.Editor
{
    // Pure geometry for the Trunk Trail, no scene access. Course space: the elephant start is (0,0) and it faces +Z
    // (x = right). The route is a clockwise loop around the start, reached by a short lead-in, with a bonus branch on
    // the inside of the east side. Heights are relative to the start level. Deterministic from Seed.
    public sealed class TrunkTrailLayout
    {
        public const int Seed = 20261008;
        public const float CorridorHalf = 2.5f, CorridorBlend = 6f, RidgeHalf = 1.25f, PitDepth = 1.5f;

        public sealed class PathLine
        {
            public Vector2[] pts;
            public float[] s, elev, bank, half, blend, gate; // gate: 0..1, the balance-beam ridge and pit
            public float length;

            public Vector2 At(float distance, out Vector2 dir)
            {
                distance = Mathf.Clamp(distance, 0, length);
                int i = 0;
                while (i < pts.Length - 2 && s[i + 1] < distance) i++;
                float u = Mathf.InverseLerp(s[i], s[i + 1], distance);
                dir = (pts[i + 1] - pts[i]).normalized;
                return Vector2.Lerp(pts[i], pts[i + 1], u);
            }
        }

        public struct Hit { public float d, off, elev, bank, half, blend, gate; }

        public readonly PathLine leadIn, loop, branch;
        public readonly float loopLength, branchStart = .19f, branchEnd = .43f;
        readonly Vector2 hillCentre;
        readonly float hillTop;

        static readonly float[] ElevF = { 0, .04f, .22f, .38f, .56f, .70f, .83f, .92f, .98f, 1 };
        static readonly float[] ElevY = { 0, 0, 1.8f, 1.8f, 0, 0, -1.5f, -.8f, 0, 0 };
        static readonly float[] LoopRadius = { 17, 22, 24, 22, 23, 24, 22, 21, 19 }; // every 40 degrees clockwise from +Z

        // Height of the loop's profile at a fraction of its length: a rise, a plateau, a descent, a flat carry
        // station (0.56 to 0.70), a downhill, then back to start level.
        public static float Elev(float f)
        {
            f = Mathf.Repeat(f, 1);
            for (int i = 1; i < ElevF.Length; i++)
                if (f <= ElevF[i]) return Mathf.Lerp(ElevY[i - 1], ElevY[i], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ElevF[i - 1], ElevF[i], f)));
            return 0;
        }

        static Vector2 CatmullRom(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t) =>
            .5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);

        public TrunkTrailLayout()
        {
            var ring = new List<Vector2>();
            int n = LoopRadius.Length;
            Vector2 P(int i) { i = (i % n + n) % n; float a = i * 40f * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * LoopRadius[i]; }
            for (int i = 0; i < n; i++)
                for (int k = 0; k < 20; k++) ring.Add(CatmullRom(P(i - 1), P(i), P(i + 1), P(i + 2), k / 20f));
            ring.Add(ring[0]);
            loop = Build(ring.ToArray(), true, f => Elev(f), true);
            loopLength = loop.length;

            Vector2 m = loop.At(.05f * loopLength, out _);
            var lead = new List<Vector2>();
            Vector2[] c = { new Vector2(0, 0), new Vector2(0, 6), new Vector2(.4f, 12.4f), new Vector2(2.2f, 16.2f), m };
            for (int i = 0; i < c.Length - 1; i++)
                for (int k = 0; k < 10; k++)
                    lead.Add(CatmullRom(c[Mathf.Max(i - 1, 0)], c[i], c[i + 1], c[Mathf.Min(i + 2, c.Length - 1)], k / 10f));
            lead.Add(m);
            leadIn = Build(lead.ToArray(), false, f => 0, false);

            // Bonus branch: leaves the loop, runs 10 units inside it, and rejoins. The middle stretch narrows to a ridge.
            var br = new List<Vector2>();
            var gates = new List<float>();
            const int steps = 80;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 p = loop.At(Mathf.Lerp(branchStart, branchEnd, t) * loopLength, out Vector2 dir);
                float inset = 10 * Mathf.SmoothStep(0, 1, Mathf.Min(t / .3f, (1 - t) / .3f, 1));
                br.Add(p + new Vector2(dir.y, -dir.x) * inset);
                gates.Add(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.50f, .56f, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.76f, .82f, t))));
            }
            branch = Build(br.ToArray(), false, null, false); // no banking, so it never fights the loop's where the two overlap
            for (int i = 0; i < branch.pts.Length; i++)
            {
                float f = Mathf.Lerp(branchStart, branchEnd, i / (float)steps);
                branch.elev[i] = Elev(f);
                branch.gate[i] = gates[i];
                branch.half[i] = Mathf.Lerp(CorridorHalf, RidgeHalf, gates[i]);
                branch.blend[i] = Mathf.Lerp(CorridorBlend, .7f, gates[i]);
            }

            hillCentre = loop.At(.485f * loopLength, out _);
            hillTop = Elev(.485f) + 3.3f;
        }

        // Fills lengths, heights and banking. Banking leans the road about 3 degrees into each turn.
        static PathLine Build(Vector2[] pts, bool closed, System.Func<float, float> elevAt, bool bank)
        {
            var p = new PathLine { pts = pts };
            int n = pts.Length;
            p.s = new float[n]; p.elev = new float[n]; p.bank = new float[n]; p.half = new float[n]; p.blend = new float[n]; p.gate = new float[n];
            for (int i = 1; i < n; i++) p.s[i] = p.s[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            p.length = p.s[n - 1];
            var curvature = new float[n];
            for (int i = 0; i < n; i++)
            {
                p.half[i] = CorridorHalf; p.blend[i] = CorridorBlend;
                if (elevAt != null) p.elev[i] = elevAt(p.s[i] / p.length);
                int a = closed ? (i - 1 + n - 1) % (n - 1) : Mathf.Max(i - 1, 0), b = closed ? (i + 1) % (n - 1) : Mathf.Min(i + 1, n - 1);
                Vector2 d0 = pts[i] - pts[a], d1 = pts[b] - pts[i];
                if (d0.sqrMagnitude > 1e-6f && d1.sqrMagnitude > 1e-6f)
                    curvature[i] = -Vector2.SignedAngle(d0, d1) * Mathf.Deg2Rad / ((d0.magnitude + d1.magnitude) / 2); // right turn positive
            }
            if (bank)
                for (int i = 0; i < n; i++)
                {
                    float sum = 0;
                    for (int k = -4; k <= 4; k++) sum += curvature[closed ? ((i + k) % (n - 1) + n - 1) % (n - 1) : Mathf.Clamp(i + k, 0, n - 1)];
                    p.bank[i] = Mathf.Clamp(sum / 9 * 65, -3, 3);
                }
            return p;
        }

        static Hit Nearest(Vector2 q, params PathLine[] paths)
        {
            float best = float.MaxValue;
            PathLine bp = null; int bi = 0; float bu = 0; Vector2 bc = default;
            foreach (var path in paths)
            {
                var pts = path.pts;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 a = pts[i], ab = pts[i + 1] - a;
                    float u = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    Vector2 c = a + ab * u;
                    float d2 = (q - c).sqrMagnitude;
                    if (d2 < best) { best = d2; bp = path; bi = i; bu = u; bc = c; }
                }
            }
            Vector2 dir = (bp.pts[bi + 1] - bp.pts[bi]).normalized;
            float L(float[] v) => Mathf.Lerp(v[bi], v[bi + 1], bu);
            return new Hit
            {
                d = Mathf.Sqrt(best), off = Vector2.Dot(q - bc, new Vector2(dir.y, -dir.x)), elev = L(bp.elev), bank = L(bp.bank),
                half = L(bp.half), blend = L(bp.blend), gate = L(bp.gate),
            };
        }

        public float DistanceToPaths(Vector2 q) => Nearest(q, leadIn, loop, branch).d;

        // Distance to the main route only (lead-in and loop), which the bonus branch props must stay clear of.
        public float DistanceToMain(Vector2 q) => Nearest(q, leadIn, loop).d;

        // First fraction (0 to 1) of the branch where a log of the given half length, lying across the branch, clears the
        // main corridor by at least `margin` at its centre and at both ends. Returns -1 if the branch never gets that far.
        public float FirstClearBranchFraction(float halfLength, float margin)
        {
            float need = CorridorHalf + halfLength + margin;
            for (float f = 0; f <= 1; f += .005f)
            {
                Vector2 at = branch.At(f * branch.length, out Vector2 dir);
                Vector2 side = new Vector2(dir.y, -dir.x);
                if (DistanceToMain(at) >= need && DistanceToMain(at + side * halfLength) >= CorridorHalf + margin
                    && DistanceToMain(at - side * halfLength) >= CorridorHalf + margin) return f;
            }
            return -1;
        }

        // Fraction of straight walks along the rolling lane that no log touches. Mirrors RollingTrunk.Touches: the logs lie
        // along the lane and roll sideways. A walk is one start time (spread over 4 full cycles of the slowest log) and one
        // lateral line across the lane, entering at the near end and leaving at the far end at constant speed.
        public static float RollingPassFraction(float[] logZ, float[] period, float[] phase, float halfSpan, float halfLength,
            float radius, float reach, float laneHalfWidth, float laneHalfDepth, float speed)
        {
            float cycle = 0; foreach (float p in period) cycle = Mathf.Max(cycle, p);
            const float dt = .02f;
            int ok = 0, total = 0;
            for (int ti = 0; ti < 400; ti++)
                for (float x = -laneHalfWidth; x <= laneHalfWidth + 1e-4f; x += .1f)
                {
                    total++;
                    bool hit = false;
                    float t = ti / 400f * cycle * 4;
                    for (float z = -laneHalfDepth; z < laneHalfDepth && !hit; z += speed * dt, t += dt)
                        for (int k = 0; k < logZ.Length && !hit; k++)
                        {
                            float lx = halfSpan * Mathf.Sin(2 * Mathf.PI * (t / period[k] + phase[k]));
                            float dz = Mathf.Max(0, Mathf.Abs(z - logZ[k]) - halfLength);
                            hit = new Vector2(x - lx, dz).magnitude <= radius + reach;
                        }
                    if (!hit) ok++;
                }
            return total == 0 ? 0 : ok / (float)total;
        }

        static float Smooth(float x) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(x));

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)Seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }

        static float Fbm(Vector2 p, float scale, int salt) =>
            (ValueNoise(p.x * scale + salt, p.y * scale) + .5f * ValueNoise(p.x * scale * 2.1f - salt, p.y * scale * 2.1f + 7) + .25f * ValueNoise(p.x * scale * 4.3f, p.y * scale * 4.3f - salt)) / 1.75f;

        static float Ground(Hit h) => h.elev - Mathf.Clamp(h.off, -CorridorHalf, CorridorHalf) * Mathf.Tan(h.bank * Mathf.Deg2Rad);

        // Terrain height at a course-space point, plus the paint weights for dirt and sand.
        public float Sample(Vector2 p, out float dirt, out float sand)
        {
            Hit h = Nearest(p, leadIn, loop, branch);
            float off = 4.4f * Fbm(p, 1f / 18f, 3) - .6f; // rolling hills, -0.6 to 3.8
            off *= Smooth((p.magnitude - 5) / 11);        // flat around the start
            float dh = (p - hillCentre).magnitude;
            off = Mathf.Max(off, hillTop * Mathf.Exp(-dh * dh / (2 * 6.5f * 6.5f)));
            Hit hb = Nearest(p, branch);
            float pit = hb.gate * (1 - Smooth((hb.d - 3f) / 2.5f));
            off = Mathf.Lerp(off, Ground(hb) - PitDepth, pit);
            float w = 1 - Smooth((h.d - h.half) / h.blend);
            float y = Mathf.Lerp(off, Ground(h), w);

            dirt = 1 - Smooth((h.d - h.half + .2f) / Mathf.Min(h.blend, .9f));
            float s = Smooth((Fbm(p, 1f / 14f, 11) - .58f) / .14f);
            s = Mathf.Max(s, pit * .9f);
            if (y < -.2f) s = Mathf.Max(s, .5f);
            sand = s * (1 - dirt);
            return y;
        }
    }
}
