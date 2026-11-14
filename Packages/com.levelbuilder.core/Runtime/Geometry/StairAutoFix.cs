using System.Collections.Generic;
using UnityEngine;

namespace LevelBuilder.Geometry
{
    /// <summary>Proposes a new route for a stair whose arrival or departure zone is blocked. Never mutates the stair.</summary>
    public static class StairAutoFix
    {
        const float MatchDist = 0.02f;
        const float Eps = 1e-3f;

        /// <summary>
        /// Arrival: (a) turn the last flight ±90° around its start, (b) add a Corner where the stair ends now and
        /// continue ±90°. Departure: the same with the first flight. Within each step the side leaving more free
        /// distance to the walls is tried first. Returns false when the stair needs no fix or nothing works.
        /// </summary>
        public static bool TryFix(BuildingData data, StairData s, out List<StairPoint> fixedRoute)
        {
            fixedRoute = null;
            if (data == null || s == null || s.route == null) return false;
            var cur = StairValidator.Validate(data, s);
            if (!cur.layout.ok) return false;
            bool arrival = cur.issues.Contains(StairIssue.ArrivalBlocked);
            if (!arrival && !cur.issues.Contains(StairIssue.DepartureBlocked)) return false;

            var route = new List<StairPoint>(s.route);
            if (arrival && !Stage(data, s, true, ref route, ref cur)) return false;
            if (cur.issues.Contains(StairIssue.DepartureBlocked) && !Stage(data, s, false, ref route, ref cur)) return false;
            fixedRoute = route;
            return true;
        }

        static bool Stage(BuildingData data, StairData s, bool arrival, ref List<StairPoint> route, ref StairValidation cur)
        {
            var target = arrival ? StairIssue.ArrivalBlocked : StairIssue.DepartureBlocked;
            var floor = data.floors[data.FloorIndex(arrival ? s.toFloorId : s.fromFloorId)];
            var walls = StairValidator.WallFootprints(floor);
            var L = cur.layout;
            var pos = RoutePositions(route, L.centerline);
            var end = (L.topA + L.topB) * 0.5f;
            float h = Mathf.Max(s.width, 0.01f);

            for (int step = 0; step < 2; step++)
            {
                var cands = new List<(List<StairPoint> route, StairValidation v, float free)>();
                foreach (float sign in new[] { 1f, -1f })
                {
                    var r = arrival
                        ? (step == 0 ? TurnLastFlight(route, pos, L, end, sign) : CornerAtEnd(route, pos, L, end, sign, h))
                        : (step == 0 ? TurnFirstFlight(route, pos, L, end, sign) : CornerAtStart(route, L, sign, h));
                    var v = StairValidator.Validate(data, Clone(s, r));
                    float free = float.MinValue;
                    if (v.layout.ok)
                    {
                        free = float.MaxValue;
                        var zone = arrival ? v.layout.arrivalZone : v.layout.departureZone;
                        foreach (var w in walls) free = Mathf.Min(free, StairValidator.PolygonDistance(zone, w));
                    }
                    cands.Add((r, v, free));
                }
                if (cands[1].free > cands[0].free) cands.Reverse();
                foreach (var c in cands)
                {
                    if (!c.v.layout.ok || c.v.issues.Contains(target)) continue;
                    bool fresh = false;
                    foreach (var i in c.v.issues) if (!cur.issues.Contains(i)) { fresh = true; break; }
                    if (fresh) continue;
                    route = c.route;
                    cur = c.v;
                    return true;
                }
            }
            return false;
        }

        static StairData Clone(StairData s, List<StairPoint> route) => new StairData
        {
            id = s.id, fromFloorId = s.fromFloorId, toFloorId = s.toFloorId, route = route,
            width = s.width, riserTarget = s.riserTarget, tread = s.tread,
            railLeft = s.railLeft, railRight = s.railRight, collisionRamp = s.collisionRamp
        };

        static Vector2 Rot(Vector2 v, float sign) => sign > 0f ? new Vector2(-v.y, v.x) : new Vector2(v.y, -v.x);

        static StairPoint Pt(Vector2 p, StairPointKind k = StairPointKind.Corner) => new StairPoint { position = p, kind = k };

        /// <summary>Arc-length position of each route point on the centerline (it passes through all of them); NaN if not found.</summary>
        static float[] RoutePositions(List<StairPoint> route, List<Vector2> cl)
        {
            var pos = new float[route.Count];
            var cum = new float[cl.Count];
            for (int i = 1; i < cl.Count; i++) cum[i] = cum[i - 1] + (cl[i] - cl[i - 1]).magnitude;
            int j = 0;
            for (int i = 0; i < route.Count; i++)
            {
                pos[i] = float.NaN;
                for (int k = j; k < cl.Count; k++)
                    if ((cl[k] - route[i].position).magnitude < MatchDist) { pos[i] = cum[k]; j = k; break; }
            }
            return pos;
        }

        /// <summary>Last route index whose centerline position is before the stair end, or 0.</summary>
        static int LastFlightStart(float[] pos, float runLength)
        {
            for (int i = pos.Length - 1; i > 0; i--)
                if (!float.IsNaN(pos[i]) && pos[i] < runLength - Eps) return i;
            return 0;
        }

        static List<StairPoint> TurnLastFlight(List<StairPoint> route, float[] pos, StairLayoutResult L, Vector2 end, float sign)
        {
            int k = LastFlightStart(pos, L.runLength);
            var r = route.GetRange(0, k + 1);
            var pivot = route[k].position;
            r.Add(Pt(pivot + Rot(end - pivot, sign)));
            return r;
        }

        static List<StairPoint> CornerAtEnd(List<StairPoint> route, float[] pos, StairLayoutResult L, Vector2 end, float sign, float len)
        {
            int k = LastFlightStart(pos, L.runLength);
            var r = route.GetRange(0, k + 1);
            r.Add(Pt(end));
            r.Add(Pt(end + Rot(L.endDir, sign) * len));
            return r;
        }

        static List<StairPoint> TurnFirstFlight(List<StairPoint> route, float[] pos, StairLayoutResult L, Vector2 end, float sign)
        {
            bool second = route.Count > 1 && !float.IsNaN(pos[1]) && pos[1] < L.runLength - Eps;
            var pivot = second ? route[1].position : end;
            var r = new List<StairPoint> { Pt(pivot + Rot(route[0].position - pivot, sign), route[0].kind) };
            if (second) r.AddRange(route.GetRange(1, route.Count - 1));
            else r.Add(Pt(end));
            return r;
        }

        static List<StairPoint> CornerAtStart(List<StairPoint> route, StairLayoutResult L, float sign, float len)
        {
            var start = route[0].position;
            var r = new List<StairPoint> { Pt(start + Rot(L.startDir, sign) * len), Pt(start) };
            r.AddRange(route.GetRange(1, route.Count - 1));
            return r;
        }
    }
}
