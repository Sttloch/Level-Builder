using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>
    /// Meshes of a stair from its <see cref="StairLayoutResult"/>. Plan (x, y) maps to (x, height, y) with
    /// heights measured from <c>baseY</c> (the floor N surface). UVs in metres: horizontal faces = world xz,
    /// vertical faces = (metres along the edge, world height). Face keys "stair:{id}/tread", "/landing",
    /// "/riser", "/side", "/rail"; the collision ramp uses "stair:{id}/ramp" (never rendered).
    /// An invalid layout (ok == false) yields an empty mesh.
    /// </summary>
    public static class StairMeshBuilder
    {
        const float MatchTol = 1e-3f;
        const float MinLen = 1e-6f;

        // ---------------------------------------------------------------- Steps

        /// <summary>
        /// Every piece is a solid prism [baseY, baseY + height]: top face "/tread" or "/landing", the front
        /// edge only over [previous height, height] ("/riser"), every other edge full height ("/side").
        /// Plus the last riser on topA–topB up to H = risers × riser.
        /// </summary>
        public static MeshData Steps(StairData s, StairLayoutResult l, float baseY)
        {
            var md = new MeshData();
            if (s == null || l == null || !l.ok) return md;
            string k = "stair:" + s.id;
            float prev = 0f;
            foreach (var p in l.pieces)
            {
                float top = baseY + p.height;
                bool riserDone = false;
                if (p.polygon != null && p.polygon.Count >= 3)
                {
                    var poly = Ccw(p.polygon);
                    AddPolygon(md, k + (p.kind == StairPieceKind.Landing ? "/landing" : "/tread"), poly, top, Vector3.up);
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                        if (!riserDone && SameEdge(a, b, p.frontA, p.frontB))
                        {
                            AddWall(md, k + "/riser", a, b, baseY + prev, top);
                            riserDone = true;
                        }
                        else
                            AddWall(md, k + "/side", a, b, baseY, top);
                    }
                }
                // Polygon without a matching front edge (layout fell back to a hull): riser on frontA–frontB anyway.
                if (!riserDone) AddWall(md, k + "/riser", p.frontA, p.frontB, baseY + prev, top);
                prev = p.height;
            }
            // topA→topB is left→right looking up: its right-hand normal faces down the stair.
            AddWall(md, k + "/riser", l.topA, l.topB, baseY + prev, baseY + l.risers * l.riser);
            return md;
        }

        /// <summary>
        /// Only the "/side" faces of <see cref="Steps"/>: the collider that keeps a character beside the stair
        /// out of the solid steps while the collision ramp (no side walls) is the walking surface.
        /// </summary>
        public static MeshData StepSides(StairData s, StairLayoutResult l, float baseY)
        {
            var all = Steps(s, l, baseY);
            var md = new MeshData();
            var g = s != null ? all.groups.Find(x => x.key == "stair:" + s.id + "/side") : null;
            if (g == null) return md;
            var tri = new[] { 0, 1, 2 };
            for (int i = 0; i + 2 < g.triangles.Count; i += 3)
            {
                int a = g.triangles[i], b = g.triangles[i + 1], c = g.triangles[i + 2];
                md.AddFace(g.key, new[] { all.vertices[a], all.vertices[b], all.vertices[c] },
                    new[] { all.uvs[a], all.uvs[b], all.uvs[c] }, tri, all.normals[a]);
            }
            return md;
        }

        // ---------------------------------------------------------------- Ramp

        /// <summary>
        /// Collision ramp. Per flight (the treads after the start or after a landing, possibly none) a ruled
        /// slab whose top starts one tread before the flight's lower riser edge at the lower height (0, or the
        /// landing height), passes through every tread nosing and ends at the upper riser edge (topA–topB at H,
        /// or the next landing's front edge at the landing height), so it never has a vertical step. Each landing
        /// gets a flat slab. The underside is the top moved down by <paramref name="thickness"/>.
        /// </summary>
        public static MeshData Ramp(StairLayoutResult l, float baseY, float thickness = 0.05f, string id = null)
        {
            var md = new MeshData();
            if (l == null || !l.ok) return md;
            string key = "stair:" + id + "/ramp";
            foreach (var flight in FlightSections(l))
            {
                var secs = new List<Section>(flight.Count);
                foreach (var f in flight) secs.Add(new Section(f.a, f.b, baseY + f.y));
                AddRuledSlab(md, key, secs, thickness);
            }
            foreach (var p in l.pieces)
                if (p.kind == StairPieceKind.Landing && p.polygon != null && p.polygon.Count >= 3)
                    AddFlatSlab(md, key, Ccw(p.polygon), baseY + p.height, thickness);
            return md;
        }

        /// <summary>Tread depth used for the ramp run-in when a layout carries no tread to measure.</summary>
        const float DefaultTread = 0.28f;

        /// <summary>
        /// Ramp top cross-sections per flight, heights relative to floor N: run-in point one tread before the
        /// lower riser edge, every nosing, then the upper riser edge.
        /// </summary>
        internal static List<List<Section>> FlightSections(StairLayoutResult l)
        {
            var res = new List<List<Section>>();
            if (l == null || !l.ok) return res;
            var pieces = l.pieces;
            float H = l.risers * l.riser;
            float T = EstimateTread(l);
            int start = 0;
            float lower = 0f;
            while (true)
            {
                int j = start;
                while (j < pieces.Count && pieces[j].kind == StairPieceKind.Tread) j++;
                Vector2 ea, eb;
                if (start < pieces.Count) { ea = pieces[start].frontA; eb = pieces[start].frontB; }
                else { ea = l.topA; eb = l.topB; }
                var e = eb - ea;
                var dir = e.sqrMagnitude > 1e-12f ? new Vector2(-e.y, e.x).normalized : l.startDir; // up the stair
                var secs = new List<Section> { new Section(ea - dir * T, eb - dir * T, lower) };
                for (int t = start; t < j; t++) secs.Add(new Section(pieces[t].frontA, pieces[t].frontB, pieces[t].height));
                if (j < pieces.Count) secs.Add(new Section(pieces[j].frontA, pieces[j].frontB, pieces[j].height));
                else secs.Add(new Section(l.topA, l.topB, H));
                res.Add(secs);
                if (j >= pieces.Count) break;
                lower = pieces[j].height;
                start = j + 1;
            }
            return res;
        }

        static float EstimateTread(StairLayoutResult l)
        {
            var pieces = l.pieces;
            for (int k = 0; k < pieces.Count; k++)
            {
                if (pieces[k].kind != StairPieceKind.Tread) continue;
                Vector2 na = k + 1 < pieces.Count ? pieces[k + 1].frontA : l.topA;
                Vector2 nb = k + 1 < pieces.Count ? pieces[k + 1].frontB : l.topB;
                float d = ((na + nb) - (pieces[k].frontA + pieces[k].frontB)).magnitude * 0.5f;
                if (d > 1e-4f) return d;
            }
            return DefaultTread;
        }

        internal struct Section
        {
            public Vector2 a, b;
            public float y;
            public Section(Vector2 a, Vector2 b, float y) { this.a = a; this.b = b; this.y = y; }
        }

        static void AddRuledSlab(MeshData md, string key, List<Section> secs, float th)
        {
            // Backward reference: right-hand normal of a→b (left→right looking up) faces down the stair.
            var e0 = secs[0].b - secs[0].a;
            var back = e0.sqrMagnitude > 1e-12f ? new Vector3(e0.y, 0f, -e0.x).normalized : Vector3.zero;
            var upRef = Vector3.up + back * 0.5f;
            for (int k = 0; k + 1 < secs.Count; k++)
            {
                Section s0 = secs[k], s1 = secs[k + 1];
                Vector3 a0 = V(s0.a, s0.y), b0 = V(s0.b, s0.y), a1 = V(s1.a, s1.y), b1 = V(s1.b, s1.y);
                var d = new Vector3(0f, -th, 0f);
                // Top and bottom.
                AddTriXZ(md, key, a0, b0, b1, upRef);
                AddTriXZ(md, key, a0, b1, a1, upRef);
                AddTriXZ(md, key, a0 + d, b0 + d, b1 + d, -upRef);
                AddTriXZ(md, key, a0 + d, b1 + d, a1 + d, -upRef);
                // Sides (vertical quads along the left and right edges).
                var mid0 = (s0.a + s0.b) * 0.5f;
                AddSideQuad(md, key, s0.a, s1.a, s0.y, s1.y, th, s0.a - mid0);
                AddSideQuad(md, key, s0.b, s1.b, s0.y, s1.y, th, s0.b - mid0);
            }
            // End caps.
            var first = secs[0]; var last = secs[secs.Count - 1];
            AddCap(md, key, first, th, true);
            AddCap(md, key, last, th, false);
        }

        static void AddSideQuad(MeshData md, string key, Vector2 p0, Vector2 p1, float y0, float y1, float th, Vector2 outward)
        {
            var e = p1 - p0;
            float len = e.magnitude;
            if (len < MinLen) return;
            var n = new Vector3(e.y, 0f, -e.x) / len;
            if (Vector3.Dot(n, new Vector3(outward.x, 0f, outward.y)) < 0f) n = -n;
            var pts = new[] { V(p0, y0 - th), V(p1, y1 - th), V(p1, y1), V(p0, y0) };
            var uvs = new[] { new Vector2(0f, y0 - th), new Vector2(len, y1 - th), new Vector2(len, y1), new Vector2(0f, y0) };
            md.AddFace(key, pts, uvs, Quad, n);
        }

        static void AddCap(MeshData md, string key, Section s, float th, bool start)
        {
            // a→b right-hand normal faces down the stair: right for the start cap, reversed for the end cap.
            if (start) AddWall(md, key, s.a, s.b, s.y - th, s.y);
            else AddWall(md, key, s.b, s.a, s.y - th, s.y);
        }

        static void AddFlatSlab(MeshData md, string key, List<Vector2> poly, float y, float th)
        {
            AddPolygon(md, key, poly, y, Vector3.up);
            AddPolygon(md, key, poly, y - th, Vector3.down);
            for (int i = 0; i < poly.Count; i++)
                AddWall(md, key, poly[i], poly[(i + 1) % poly.Count], y - th, y);
        }

        // ---------------------------------------------------------------- Rails

        /// <summary>
        /// For each enabled side (left = frontA side, right = frontB side): a bar × bar handrail whose top runs
        /// <paramref name="height"/> above the pieces, inset bar/2 from the stair edge (sloping between nosings
        /// on flights, flat on landings), from the first riser edge to the last one; plus posts at the first
        /// and last piece and at each landing's corners on that side.
        /// </summary>
        public static MeshData Rails(StairData s, StairLayoutResult l, float baseY, float height = 0.9f, float bar = 0.05f)
        {
            var md = new MeshData();
            if (s == null || l == null || !l.ok || l.pieces.Count == 0 || !(bar > 0f)) return md;
            string key = "stair:" + s.id + "/rail";
            if (s.railLeft) BuildRail(md, key, l, baseY, height, bar, true);
            if (s.railRight) BuildRail(md, key, l, baseY, height, bar, false);
            return md;
        }

        /// <summary>
        /// Rail collision: per enabled side and per rail path segment, a closed thin box just OUTSIDE the stair
        /// footprint (from the stair edge outwards by <paramref name="thickness"/>, mitred at the corners), from the
        /// pieces' height up to the rail top. Keeping it outside leaves the full stair width walkable.
        /// </summary>
        public static MeshData RailColliders(StairData s, StairLayoutResult l, float baseY, float height = 0.9f, float thickness = 0.03f)
        {
            var md = new MeshData();
            if (s == null || l == null || !l.ok || l.pieces.Count == 0 || !(thickness > 0f)) return md;
            string key = "stair:" + s.id + "/railCollider";
            if (s.railLeft) BuildRailCollider(md, key, l, baseY, height, thickness, true);
            if (s.railRight) BuildRailCollider(md, key, l, baseY, height, thickness, false);
            return md;
        }

        static void BuildRailCollider(MeshData md, string key, StairLayoutResult l, float baseY, float height, float th, bool left)
        {
            var verts = RailPath(l, baseY, height, left);
            if (verts.Count < 2) return;
            var inward = RailInward(verts, l, left);
            for (int i = 0; i + 1 < verts.Count; i++)
            {
                RailVertex v0 = verts[i], v1 = verts[i + 1];
                Vector2 i0 = v0.p, i1 = v1.p, o0 = v0.p - inward[i] * th, o1 = v1.p - inward[i + 1] * th;
                var dirIn = new Vector3(inward[i].x + inward[i + 1].x, 0f, inward[i].y + inward[i + 1].y);
                var along = new Vector3(v1.p.x - v0.p.x, 0f, v1.p.y - v0.p.y);
                // Inner, outer, top, bottom.
                AddTriXZ(md, key, V(i0, v0.floorY), V(i1, v1.floorY), V(i1, v1.railY), dirIn);
                AddTriXZ(md, key, V(i0, v0.floorY), V(i1, v1.railY), V(i0, v0.railY), dirIn);
                AddTriXZ(md, key, V(o0, v0.floorY), V(o1, v1.floorY), V(o1, v1.railY), -dirIn);
                AddTriXZ(md, key, V(o0, v0.floorY), V(o1, v1.railY), V(o0, v0.railY), -dirIn);
                AddTriXZ(md, key, V(i0, v0.railY), V(i1, v1.railY), V(o1, v1.railY), Vector3.up);
                AddTriXZ(md, key, V(i0, v0.railY), V(o1, v1.railY), V(o0, v0.railY), Vector3.up);
                AddTriXZ(md, key, V(i0, v0.floorY), V(i1, v1.floorY), V(o1, v1.floorY), Vector3.down);
                AddTriXZ(md, key, V(i0, v0.floorY), V(o1, v1.floorY), V(o0, v0.floorY), Vector3.down);
                // End caps.
                AddTriXZ(md, key, V(i0, v0.floorY), V(o0, v0.floorY), V(o0, v0.railY), -along);
                AddTriXZ(md, key, V(i0, v0.floorY), V(o0, v0.railY), V(i0, v0.railY), -along);
                AddTriXZ(md, key, V(i1, v1.floorY), V(o1, v1.floorY), V(o1, v1.railY), along);
                AddTriXZ(md, key, V(i1, v1.floorY), V(o1, v1.railY), V(i1, v1.railY), along);
            }
        }

        struct RailVertex
        {
            public Vector2 p;       // on the stair edge
            public float floorY;    // top of the piece under this vertex
            public float railY;     // top of the rail
            public bool post;
        }

        /// <summary>
        /// Rail path along one side: a vertex on the stair edge at each piece's front corner (plus a landing's
        /// intermediate side corners) and at the last riser's corner, with the floor and rail-top heights there.
        /// </summary>
        static List<RailVertex> RailPath(StairLayoutResult l, float baseY, float height, bool left)
        {
            float H = l.risers * l.riser;
            var verts = new List<RailVertex>();
            var pieces = l.pieces;
            for (int k = 0; k < pieces.Count; k++)
            {
                var pc = pieces[k];
                float y = baseY + pc.height;
                bool landing = pc.kind == StairPieceKind.Landing;
                Push(verts, new RailVertex
                {
                    p = left ? pc.frontA : pc.frontB, floorY = y, railY = y + height, post = k == 0 || landing,
                });
                if (!landing) continue;
                Vector2 nextA = k + 1 < pieces.Count ? pieces[k + 1].frontA : l.topA;
                Vector2 nextB = k + 1 < pieces.Count ? pieces[k + 1].frontB : l.topB;
                foreach (var c in SideChain(pc, left ? nextA : nextB, left))
                    Push(verts, new RailVertex { p = c, floorY = y, railY = y + height, post = true });
            }
            var lastPiece = pieces[pieces.Count - 1];
            Push(verts, new RailVertex
            {
                p = left ? l.topA : l.topB, floorY = baseY + lastPiece.height, railY = baseY + H + height, post = true,
            });
            return verts;
        }

        /// <summary>Inward vectors (towards the other side) at each rail vertex, mitred at interior vertices.</summary>
        static Vector2[] RailInward(List<RailVertex> verts, StairLayoutResult l, bool left)
        {
            int n = verts.Count;
            var inward = new Vector2[n];
            var startEdge = (left ? l.bottomB - l.bottomA : l.bottomA - l.bottomB).normalized;
            var endEdge = (left ? l.topB - l.topA : l.topA - l.topB).normalized;
            for (int i = 0; i < n; i++)
            {
                if (i == 0) { inward[i] = startEdge; continue; }
                if (i == n - 1) { inward[i] = endEdge; continue; }
                var n1 = Inward((verts[i].p - verts[i - 1].p).normalized, left);
                var n2 = Inward((verts[i + 1].p - verts[i].p).normalized, left);
                float c = 1f + Vector2.Dot(n1, n2);
                inward[i] = c > 1e-3f ? (n1 + n2) / c : n1;
            }
            return inward;
        }

        static void BuildRail(MeshData md, string key, StairLayoutResult l, float baseY, float height, float bar, bool left)
        {
            var verts = RailPath(l, baseY, height, left);
            if (verts.Count < 2) return;
            int n = verts.Count;
            var inward = RailInward(verts, l, left);

            float hb = bar * 0.5f;
            var center = new Vector2[n];
            for (int i = 0; i < n; i++) center[i] = verts[i].p + inward[i] * hb;

            // Swept bar: section i has corners center ± inward·hb, at [railY - bar, railY].
            float along = 0f;
            for (int i = 0; i + 1 < n; i++)
            {
                float segLen = (center[i + 1] - center[i]).magnitude;
                Vector2 i0 = center[i] + inward[i] * hb, o0 = center[i] - inward[i] * hb;
                Vector2 i1 = center[i + 1] + inward[i + 1] * hb, o1 = center[i + 1] - inward[i + 1] * hb;
                float t0 = verts[i].railY, t1 = verts[i + 1].railY;
                var inRef = new Vector3(inward[i].x + inward[i + 1].x, 0f, inward[i].y + inward[i + 1].y);
                // Top / bottom.
                AddTriXZ(md, key, V(i0, t0), V(o0, t0), V(o1, t1), Vector3.up);
                AddTriXZ(md, key, V(i0, t0), V(o1, t1), V(i1, t1), Vector3.up);
                AddTriXZ(md, key, V(i0, t0 - bar), V(o0, t0 - bar), V(o1, t1 - bar), Vector3.down);
                AddTriXZ(md, key, V(i0, t0 - bar), V(o1, t1 - bar), V(i1, t1 - bar), Vector3.down);
                // Inner and outer faces (vertical UVs: metres along, world height).
                AddVerticalStrip(md, key, i0, i1, t0, t1, bar, along, segLen, inRef);
                AddVerticalStrip(md, key, o0, o1, t0, t1, bar, along, segLen, -inRef);
                along += segLen;
            }
            // End caps.
            AddRailCap(md, key, center[0], inward[0], hb, verts[0].railY, bar, center[0] - center[1]);
            AddRailCap(md, key, center[n - 1], inward[n - 1], hb, verts[n - 1].railY, bar, center[n - 1] - center[n - 2]);

            // Posts.
            for (int i = 0; i < n; i++)
            {
                if (!verts[i].post) continue;
                Vector2 a, b, c;
                if (i == 0)
                {
                    a = inward[0];
                    b = Perp(a, center[1] - center[0]);
                    c = center[0] + b * hb; // starts at the riser edge, extends into the stair
                }
                else if (i == n - 1)
                {
                    a = inward[n - 1];
                    b = Perp(a, center[n - 2] - center[n - 1]);
                    c = center[n - 1] + b * hb;
                }
                else
                {
                    var d1 = (verts[i].p - verts[i - 1].p).normalized;
                    a = Inward(d1, left);
                    b = d1;
                    c = center[i];
                }
                AddPost(md, key, c, a, b, hb, verts[i].floorY, verts[i].railY);
            }
        }

        static void Push(List<RailVertex> l, RailVertex v)
        {
            if (l.Count > 0 && (l[l.Count - 1].p - v.p).sqrMagnitude < 1e-8f) return;
            l.Add(v);
        }

        /// <summary>
        /// Intermediate polygon vertices of a piece's side between its front corner and its back corner
        /// (the next riser's corner). The CCW polygon runs the right side forward and the left side backward.
        /// </summary>
        static List<Vector2> SideChain(StairPiece p, Vector2 back, bool left)
        {
            var res = new List<Vector2>();
            var poly = Ccw(p.polygon);
            int m = poly.Count;
            int start = Find(poly, left ? p.frontA : p.frontB);
            if (start < 0) return res;
            int step = left ? -1 : 1;
            for (int c = 1; c < m; c++)
            {
                var v = poly[((start + step * c) % m + m) % m];
                if ((v - back).magnitude < MatchTol) return res;
                res.Add(v);
            }
            res.Clear(); // back corner not found: no intermediates
            return res;
        }

        static int Find(List<Vector2> poly, Vector2 p)
        {
            for (int i = 0; i < poly.Count; i++)
                if ((poly[i] - p).magnitude < MatchTol) return i;
            return -1;
        }

        static Vector2 Inward(Vector2 d, bool left) => left ? new Vector2(d.y, -d.x) : new Vector2(-d.y, d.x);

        /// <summary>Unit vector perpendicular to a, on the side of towards.</summary>
        static Vector2 Perp(Vector2 a, Vector2 towards)
        {
            var b = new Vector2(-a.y, a.x);
            return Vector2.Dot(b, towards) < 0f ? -b : b;
        }

        static void AddVerticalStrip(MeshData md, string key, Vector2 p0, Vector2 p1, float t0, float t1, float bar,
            float u0, float len, Vector3 outward)
        {
            var a = V(p0, t0 - bar); var b = V(p1, t1 - bar); var c = V(p1, t1); var d = V(p0, t0);
            var ua = new Vector2(u0, t0 - bar); var ub = new Vector2(u0 + len, t1 - bar);
            var uc = new Vector2(u0 + len, t1); var ud = new Vector2(u0, t0);
            AddTri(md, key, a, b, c, ua, ub, uc, outward);
            AddTri(md, key, a, c, d, ua, uc, ud, outward);
        }

        static void AddRailCap(MeshData md, string key, Vector2 c, Vector2 inward, float hb, float top, float bar, Vector2 outward)
        {
            var i = c + inward * hb; var o = c - inward * hb;
            var n = new Vector3(outward.x, 0f, outward.y);
            float w = 2f * hb;
            AddTri(md, key, V(i, top - bar), V(o, top - bar), V(o, top),
                new Vector2(0f, top - bar), new Vector2(w, top - bar), new Vector2(w, top), n);
            AddTri(md, key, V(i, top - bar), V(o, top), V(i, top),
                new Vector2(0f, top - bar), new Vector2(w, top), new Vector2(0f, top), n);
        }

        /// <summary>Square post centred at c with unit axes a, b (half-size hb), from y0 to y1: 4 sides and top.</summary>
        static void AddPost(MeshData md, string key, Vector2 c, Vector2 a, Vector2 b, float hb, float y0, float y1)
        {
            if (!(y1 - y0 > MinLen)) return;
            var corners = new[] { c - a * hb - b * hb, c + a * hb - b * hb, c + a * hb + b * hb, c - a * hb + b * hb };
            if (Triangulator.SignedArea(corners) < 0f) System.Array.Reverse(corners);
            var poly = new List<Vector2>(corners);
            for (int i = 0; i < 4; i++) AddWall(md, key, poly[i], poly[(i + 1) % 4], y0, y1);
            AddPolygon(md, key, poly, y1, Vector3.up);
        }

        // ---------------------------------------------------------------- Helpers

        static readonly int[] Quad = { 0, 1, 2, 0, 2, 3 };

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static List<Vector2> Ccw(List<Vector2> poly)
        {
            var l = new List<Vector2>(poly);
            if (Triangulator.SignedArea(l) < 0f) l.Reverse();
            return l;
        }

        static bool SameEdge(Vector2 a, Vector2 b, Vector2 fa, Vector2 fb) =>
            ((a - fa).magnitude < MatchTol && (b - fb).magnitude < MatchTol) ||
            ((a - fb).magnitude < MatchTol && (b - fa).magnitude < MatchTol);

        /// <summary>Horizontal polygon (may be non-convex), UVs = world xz.</summary>
        static void AddPolygon(MeshData md, string key, List<Vector2> poly, float y, Vector3 normal)
        {
            var tris = Triangulator.Triangulate(poly, null, out var verts);
            if (tris.Count == 0) return;
            var pts = new List<Vector3>(verts.Count);
            foreach (var v in verts) pts.Add(V(v, y));
            md.AddFace(key, pts, verts, tris, normal);
        }

        /// <summary>Vertical quad on a→b over [y0, y1], normal = right-hand side of a→b (outward for CCW contours).</summary>
        static void AddWall(MeshData md, string key, Vector2 a, Vector2 b, float y0, float y1)
        {
            var e = b - a;
            float len = e.magnitude;
            if (len < MinLen || !(y1 - y0 > MinLen)) return;
            var n = new Vector3(e.y, 0f, -e.x) / len;
            var pts = new[] { V(a, y0), V(b, y0), V(b, y1), V(a, y1) };
            var uvs = new[] { new Vector2(0f, y0), new Vector2(len, y0), new Vector2(len, y1), new Vector2(0f, y1) };
            md.AddFace(key, pts, uvs, Quad, n);
        }

        /// <summary>Single triangle with its own flat normal, oriented to agree with <paramref name="reference"/>; UVs = world xz.</summary>
        static void AddTriXZ(MeshData md, string key, Vector3 a, Vector3 b, Vector3 c, Vector3 reference)
        {
            AddTri(md, key, a, b, c, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), reference);
        }

        static void AddTri(MeshData md, string key, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 reference)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            if (Vector3.Dot(n, reference) < 0f) n = -n;
            md.AddFace(key, new[] { a, b, c }, new[] { ua, ub, uc }, new[] { 0, 1, 2 }, n);
        }
    }
}
