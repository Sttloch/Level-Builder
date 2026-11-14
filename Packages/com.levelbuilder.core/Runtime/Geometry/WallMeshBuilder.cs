using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Extrudes a floor's wall graph into a mesh. Plan (x, y) maps to world (x, height, y).
    /// Face keys: "{segmentId}/L", "/R" (sides looking start→end), "/start", "/end" (caps), "/top";
    /// per node: "node:{nodeId}/top" (junction top) and "node:{nodeId}/side" (steps where a junction
    /// falls back to square ends).
    /// <para>
    /// Walls of different heights meeting at a node are handled in height bands: in the band
    /// [H(k-1), H(k)] only the walls at least H(k) tall take part in the join, so a tall wall is
    /// mitred against a low neighbour below the neighbour's height and square (or mitred against the
    /// remaining walls) above it.
    /// </para>
    /// </summary>
    public static class WallMeshBuilder
    {
        const float HeightEps = 1e-4f;
        const float SameEps = 1e-5f;

        /// <summary>Corners of one wall end within one height band (segment frame, heights relative to baseY).</summary>
        class Band
        {
            public float lo, hi;
            public Vector2 left, right;
        }

        class EndInfo
        {
            /// <summary>Distance along the wall from the node beyond which no band's corner reaches.</summary>
            public float cut;
            public readonly List<Band> bands = new List<Band>();
        }

        public static Vector3 ToWorld(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        public static MeshData Build(FloorData f, float baseY)
        {
            var md = new MeshData();
            var byNode = WallJoins.CollectEnds(f, out var nodePos);
            var info = new Dictionary<(string, bool), EndInfo>();
            foreach (var kv in byNode) BuildNode(md, f, kv.Key, nodePos[kv.Key], kv.Value, baseY, info);
            foreach (var s in f.segments) BuildSegment(md, f, s, info, baseY);
            return md;
        }

        // ---------- nodes ----------

        static void BuildNode(MeshData md, FloorData f, string nodeId, Vector2 p, List<JoinEnd> all, float baseY,
            Dictionary<(string, bool), EndInfo> info)
        {
            var ends = all.FindAll(e => e.seg.Height(f) > HeightEps);
            if (ends.Count == 0) return;

            var heights = new List<float>();
            foreach (var e in ends) heights.Add(e.seg.Height(f));
            heights.Sort();
            var levels = new List<float>();
            foreach (var h in heights)
                if (levels.Count == 0 || h - levels[levels.Count - 1] > HeightEps) levels.Add(h);

            foreach (var e in ends) info[(e.seg.id, e.atStart)] = new EndInfo();

            var bandEnds = new List<List<JoinEnd>>();
            for (int k = 0; k < levels.Count; k++)
            {
                float lo = k == 0 ? 0f : levels[k - 1];
                float hi = levels[k];
                var present = new List<JoinEnd>();
                foreach (var e in ends)
                    if (e.seg.Height(f) >= hi - HeightEps) present.Add(e.Clone());
                WallJoins.SolveNode(p, present);
                bandEnds.Add(present);

                foreach (var e in present)
                {
                    var ei = info[(e.seg.id, e.atStart)];
                    var (l, r) = e.SegmentCorners;
                    ei.bands.Add(new Band { lo = lo, hi = hi, left = l, right = r });
                    ei.cut = Mathf.Max(ei.cut, Mathf.Max(Vector2.Dot(e.outLeft - p, e.dir), Vector2.Dot(e.outRight - p, e.dir)));
                }
                AddBandVerticals(md, nodeId, present, baseY, lo, hi);
            }

            // Tops around the node: at each level, the band's region minus the region of the band above.
            for (int k = 0; k < levels.Count; k++)
            {
                var outer = RegionPolygon(p, bandEnds[k], info);
                var inner = k + 1 < levels.Count ? RegionPolygon(p, bandEnds[k + 1], info) : null;
                AddTop(md, "node:" + nodeId + "/top", StarRegion.Difference(p, outer, inner), baseY + levels[k]);
            }
        }

        /// <summary>Faces closing a band where the walls do not meet flush (free ends, square fallbacks).</summary>
        static void AddBandVerticals(MeshData md, string nodeId, List<JoinEnd> present, float baseY, float lo, float hi)
        {
            int n = present.Count;
            if (n >= 3)
            {
                // The junction hub fills the space between the ends; only fallback gaps need a face.
                for (int i = 0; i < n; i++)
                {
                    var e = present[i];
                    var next = present[(i + 1) % n];
                    if (e.leftFallback && (e.outLeft - next.outRight).magnitude > SameEps)
                        AddVertical(md, "node:" + nodeId + "/side", e.outLeft, next.outRight, baseY, lo, hi);
                }
                return;
            }

            bool seamless = n == 2 && present[0].leftFallback && present[1].leftFallback
                && (present[0].outLeft - present[1].outRight).magnitude < SameEps
                && (present[1].outLeft - present[0].outRight).magnitude < SameEps;
            if (seamless) return;

            foreach (var e in present)
            {
                if (n == 1 || e.leftFallback || e.rightFallback)
                    AddVertical(md, e.seg.id + (e.atStart ? "/start" : "/end"), e.outLeft, e.outRight, baseY, lo, hi);
            }
        }

        /// <summary>
        /// Plan region covered by the band's walls near the node (each truncated at its cut distance),
        /// counter-clockwise; star-shaped about the node for ordinary joins.
        /// </summary>
        static List<Vector2> RegionPolygon(Vector2 p, List<JoinEnd> ends, Dictionary<(string, bool), EndInfo> info)
        {
            var poly = new List<Vector2>();
            int n = ends.Count;
            for (int i = 0; i < n; i++)
            {
                var e = ends[i];
                float cut = info[(e.seg.id, e.atStart)].cut;
                var side = WallJoins.LeftPerp(e.dir) * e.half;
                poly.Add(p + e.dir * cut - side);
                poly.Add(p + e.dir * cut + side);
                poly.Add(e.outLeft);
                if (e.leftFallback) poly.Add(ends[(i + 1) % n].outRight);
            }
            return poly;
        }

        // ---------- segments ----------

        static void BuildSegment(MeshData md, FloorData f, WallSegment s, Dictionary<(string, bool), EndInfo> info, float baseY)
        {
            if (!info.TryGetValue((s.id, true), out var si) || !info.TryGetValue((s.id, false), out var ei)) return;
            var A = f.GetNode(s.startNodeId).position;
            var d = f.GetNode(s.endNodeId).position - A;
            float len = d.magnitude;
            d /= len;
            var n = WallJoins.LeftPerp(d);
            float half = s.thickness * 0.5f;

            AddSide(md, s.id + "/L", SidePolygon(si, ei, A, d, true), A, d, n * half, baseY, n, len);
            AddSide(md, s.id + "/R", SidePolygon(si, ei, A, d, false), A, d, -n * half, baseY, -n, -1f);

            float u0 = si.cut, u1 = len - ei.cut;
            if (u1 - u0 > SameEps)
            {
                var q0 = A + d * u0 - n * half; var q1 = A + d * u1 - n * half;
                var q2 = A + d * u1 + n * half; var q3 = A + d * u0 + n * half;
                float top = si.bands[si.bands.Count - 1].hi;
                AddTop(md, s.id + "/top", new List<Vector2> { q0, q1, q2, q0, q2, q3 }, baseY + top);
            }
        }

        /// <summary>
        /// Side face outline in wall coords (u = metres along the wall from the start node, v = height):
        /// bottom edge, staircase up the end node's bands, top edge, staircase down the start node's bands.
        /// </summary>
        static List<Vector2> SidePolygon(EndInfo si, EndInfo ei, Vector2 A, Vector2 d, bool left)
        {
            float U(Band b) => Vector2.Dot((left ? b.left : b.right) - A, d);
            var raw = new List<Vector2> { new Vector2(U(si.bands[0]), 0f) };
            foreach (var b in ei.bands)
            {
                raw.Add(new Vector2(U(b), b.lo));
                raw.Add(new Vector2(U(b), b.hi));
            }
            for (int i = si.bands.Count - 1; i >= 0; i--)
            {
                raw.Add(new Vector2(U(si.bands[i]), si.bands[i].hi));
                raw.Add(new Vector2(U(si.bands[i]), si.bands[i].lo));
            }
            var poly = new List<Vector2>();
            foreach (var q in raw)
                if (poly.Count == 0 || (q - poly[poly.Count - 1]).magnitude > SameEps) poly.Add(q);
            while (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).magnitude <= SameEps) poly.RemoveAt(poly.Count - 1);
            return poly;
        }

        /// <summary>
        /// Triangulates a side polygon. UVs are metres, with u reading left-to-right as seen from outside:
        /// the right face uses u as is, the left face (mirrorLength &gt; 0) uses length - u.
        /// </summary>
        static void AddSide(MeshData md, string key, List<Vector2> poly, Vector2 A, Vector2 d, Vector2 offset,
            float baseY, Vector2 normal2D, float mirrorLength)
        {
            var tris = Triangulator.Triangulate(poly, null, out var verts);
            var pts = new List<Vector3>(verts.Count);
            var uvs = new List<Vector2>(verts.Count);
            foreach (var uv in verts)
            {
                pts.Add(ToWorld(A + d * uv.x + offset, baseY + uv.y));
                uvs.Add(mirrorLength > 0f ? new Vector2(mirrorLength - uv.x, uv.y) : uv);
            }
            md.AddFace(key, pts, uvs, tris, ToWorld(normal2D, 0f));
        }

        /// <summary>
        /// Vertical quad over plan edge a→b from lo to hi (relative to baseY). Seen from outside, a is on the
        /// left and b on the right, so the outward normal is the right perpendicular of a→b; u runs from a.
        /// </summary>
        static void AddVertical(MeshData md, string key, Vector2 a, Vector2 b, float baseY, float lo, float hi)
        {
            var e = b - a;
            float w = e.magnitude;
            if (w < SameEps || hi - lo < SameEps) return;
            var normal = new Vector3(e.y / w, 0f, -e.x / w);
            var pts = new List<Vector3>
            {
                ToWorld(a, baseY + lo), ToWorld(b, baseY + lo), ToWorld(b, baseY + hi), ToWorld(a, baseY + hi)
            };
            var uvs = new List<Vector2> { new Vector2(0f, lo), new Vector2(w, lo), new Vector2(w, hi), new Vector2(0f, hi) };
            md.AddFace(key, pts, uvs, new[] { 0, 1, 2, 0, 2, 3 }, normal);
        }

        /// <summary>Horizontal face from a flat list of plan triangles; UVs are world xz in metres.</summary>
        static void AddTop(MeshData md, string key, List<Vector2> triPoints, float y)
        {
            if (triPoints.Count < 3) return;
            var pts = new List<Vector3>(triPoints.Count);
            var idx = new List<int>(triPoints.Count);
            for (int i = 0; i < triPoints.Count; i++)
            {
                pts.Add(ToWorld(triPoints[i], y));
                idx.Add(i);
            }
            md.AddFace(key, pts, triPoints, idx, Vector3.up);
        }
    }
}
