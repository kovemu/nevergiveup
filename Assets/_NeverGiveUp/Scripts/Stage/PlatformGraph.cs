using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace NeverGiveUp
{
    /// <summary>Small 2.5D support graph built from this stage's actual colliders.</summary>
    public sealed class PlatformGraph : MonoBehaviour
    {
        public sealed class Platform
        {
            public string Id;
            public Collider Collider;
            public DraggableBody Movable;
            public Bounds Bounds => Collider.bounds;
            public Vector3 Surface(float x)
            {
                Bounds b = Bounds;
                x = Mathf.Clamp(x, b.min.x + 0.05f, b.max.x - 0.05f);
                Ray ray = new Ray(new Vector3(x, b.max.y + 2f, 0f), Vector3.down);
                return Collider.Raycast(ray, out RaycastHit hit, b.size.y + 4f)
                    ? hit.point : new Vector3(x, b.max.y, 0f);
            }
        }
        public sealed class Link
        {
            public Platform From, To;
            public Vector3 Launch, Landing;
            public bool Walk;
            public string Tag = "";
            public float Direction => Mathf.Sign(Landing.x - Launch.x);
            public string Id => From.Id + ">" + To.Id + Tag;
        }
        private readonly List<Platform> platforms = new List<Platform>();
        private readonly List<Link> links = new List<Link>();
        private CharacterMotor motor;
        private Transform terrain;
        public IReadOnlyList<Platform> Platforms => platforms;
        public string MapSignature { get; private set; }

        public void Initialize(Transform terrainRoot, CharacterMotor actor)
        {
            terrain = terrainRoot; motor = actor;
            Rebuild();
            StringBuilder geometry = new StringBuilder("M32-twojump-3|");
            geometry.Append(gameObject.scene.name).Append('|');
            foreach (Platform p in platforms)
                if (!p.Movable) geometry.Append(p.Id).Append(Geometry(p.Collider));
            geometry.Append(motor.MoveSpeed.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(motor.JumpSpeed.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(Physics.gravity.y.ToString("R", CultureInfo.InvariantCulture));
            MapSignature = StableHash(geometry.ToString());
        }
        public void Rebuild()
        {
            if (!terrain || !motor) return;
            platforms.Clear(); links.Clear();
            foreach (Collider collider in terrain.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.name.StartsWith("NGU Boundary")) continue;
                Bounds b = collider.bounds;
                if (b.size.x < 0.6f || b.min.z > 0.05f || b.max.z < -0.05f) continue;
                platforms.Add(new Platform { Id = HierarchyId(collider.transform), Collider = collider,
                    Movable = collider.GetComponentInParent<DraggableBody>() });
            }
            platforms.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (Platform a in platforms)
            foreach (Platform b in platforms)
            {
                if (a == b) continue;
                Link link = Connect(a, b);
                if (link != null) links.Add(link);
            }
        }
        public Platform Support(Collider collider)
        {
            if (!collider) return null;
            foreach (Platform p in platforms) if (p.Collider == collider) return p;
            return null;
        }
        public Platform At(Vector3 feet)
        {
            Platform best = null;
            float error = 0.55f;
            foreach (Platform p in platforms)
            {
                Bounds b = p.Bounds;
                if (feet.x < b.min.x - 0.1f || feet.x > b.max.x + 0.1f) continue;
                float dy = Mathf.Abs(p.Surface(feet.x).y - feet.y);
                if (dy < error) { error = dy; best = p; }
            }
            return best;
        }
        private Link Connect(Platform a, Platform b)
        {
            float direction = b.Bounds.center.x >= a.Bounds.center.x ? 1f : -1f;
            float marginA = Mathf.Min(0.5f, a.Bounds.size.x * 0.3f);
            float marginB = Mathf.Min(0.7f, b.Bounds.size.x * 0.3f);
            float launch = direction > 0 ? a.Bounds.max.x - marginA : a.Bounds.min.x + marginA;
            float landing = direction > 0 ? b.Bounds.min.x + marginB : b.Bounds.max.x - marginB;
            // A wide lower floor must approach the upper platform from outside its footprint.
            if (a.Bounds.min.x < b.Bounds.min.x && a.Bounds.max.x > b.Bounds.max.x && b.Bounds.max.y > a.Bounds.max.y + 0.3f)
            {
                direction = b.Bounds.center.x < a.Bounds.center.x ? -1f : 1f;
                launch = direction > 0 ? b.Bounds.min.x - 2.1f : b.Bounds.max.x + 2.1f;
                landing = direction > 0 ? b.Bounds.min.x + marginB : b.Bounds.max.x - marginB;
            }
            Vector3 from = a.Surface(launch), to = b.Surface(landing);
            float gap = Mathf.Max(b.Bounds.min.x - a.Bounds.max.x, a.Bounds.min.x - b.Bounds.max.x);
            float boundary = direction > 0 ? a.Bounds.max.x : a.Bounds.min.x;
            bool walk = gap < 0.18f && gap > -0.3f &&
                Mathf.Abs(a.Surface(boundary).y - b.Surface(boundary).y) < 0.25f;
            // Only advertise links reachable by the actual two jump strengths.
            if (!walk && !motor.CanReach(from, to)) return null;
            return new Link { From = a, To = b, Launch = from, Landing = to, Walk = walk };
        }
        public Link Next(Platform start, Platform goal, Vector3 position, string avoidImmediateReturn = null)
        {
            if (start == null || goal == null || start == goal) return null;
            var distance = new Dictionary<Platform, float>();
            var first = new Dictionary<Platform, Link>();
            var visited = new HashSet<Platform>();
            distance[start] = 0f;
            for (int iteration = 0; iteration < platforms.Count; iteration++)
            {
                Platform current = null; float closest = float.PositiveInfinity;
                foreach (var pair in distance)
                    if (!visited.Contains(pair.Key) && pair.Value < closest) { current = pair.Key; closest = pair.Value; }
                if (current == null) break;
                if (current == goal) return first.TryGetValue(current, out Link found) ? found : null;
                visited.Add(current);
                foreach (Link link in links)
                {
                    if (link.From != current || link.To.Movable && link.To.Movable.Held) continue;
                    // Rising toward the goal is preferred. Descents remain possible when
                    // required, but arbitrary down-up detours no longer win on raw distance.
                    float drop = Mathf.Max(0f, link.From.Bounds.max.y - link.To.Bounds.max.y);
                    float fromGoalX = Mathf.Abs(link.From.Bounds.center.x - goal.Bounds.center.x);
                    float toGoalX = Mathf.Abs(link.To.Bounds.center.x - goal.Bounds.center.x);
                    float backward = Mathf.Max(0f, toGoalX - fromGoalX);
                    float cost = closest + (link.Walk ? 0.8f : 2f) +
                        Vector3.SqrMagnitude(link.Landing - link.Launch) * 0.12f +
                        drop * 5f + backward * 0.9f;
                    if (current == start)
                    {
                        cost += Mathf.Abs(position.x - link.Launch.x) * 0.2f;
                        // A newly reached ledge should not immediately send the character
                        // back to the previous ledge unless all better routes are exhausted.
                        if (!string.IsNullOrEmpty(avoidImmediateReturn) &&
                            link.To.Id == avoidImmediateReturn) cost += 12f;
                    }
                    if (distance.TryGetValue(link.To, out float previous) && previous <= cost) continue;
                    distance[link.To] = cost;
                    first[link.To] = current == start ? link : first[current];
                }
            }
            return null;
        }
        // On a small movable box a full path can be temporarily unavailable.
        // Prefer a reachable higher/forward support instead of standing still.
        // This is a local fallback, not a teleport or a replacement for graph search.
        public Link NextStepFrom(Platform start, Platform goal, string previousId)
        {
            if (start == null || goal == null) return null;
            Link selected = null;
            float best = float.PositiveInfinity;
            foreach (Link link in links)
            {
                if (link.From.Id != start.Id || link.To.Movable && link.To.Movable.Held) continue;
                if (link.To.Id == start.Id) continue;
                float rise = link.To.Bounds.max.y - start.Bounds.max.y;
                float goalDirection = Mathf.Sign(goal.Bounds.center.x - start.Bounds.center.x);
                float horizontalGain = (link.To.Bounds.center.x - start.Bounds.center.x) * goalDirection;
                if (rise < -0.1f && horizontalGain <= 0f) continue;
                float score = -rise * 5f - horizontalGain * 0.6f +
                    Vector3.Distance(link.Launch, link.Landing) * 0.1f;
                if (link.To.Id == previousId) score += 18f;
                if (score >= best) continue;
                best = score; selected = link;
            }
            return selected;
        }
        public string Context(Link link)
        {
            StringBuilder text = new StringBuilder(link.Id);
            text.Append(Geometry(link.From.Collider)).Append(Geometry(link.To.Collider));
            float low = Mathf.Min(link.Launch.x, link.Landing.x) - 2f;
            float high = Mathf.Max(link.Launch.x, link.Landing.x) + 2f;
            foreach (Platform p in platforms)
                if (p.Bounds.max.x >= low && p.Bounds.min.x <= high)
                    text.Append(p.Id).Append(Geometry(p.Collider));
            return StableHash(text.ToString());
        }
        public bool PlayerChanged(Link link, float since)
        {
            float low = Mathf.Min(link.Launch.x, link.Landing.x) - 3f;
            float high = Mathf.Max(link.Launch.x, link.Landing.x) + 3f;
            foreach (Platform p in platforms)
                if (p.Movable && p.Movable.LastManipulatedTime >= since &&
                    p.Bounds.max.x >= low && p.Bounds.min.x <= high) return true;
            return link.From.Movable && link.From.Movable.LastManipulatedTime >= since ||
                link.To.Movable && link.To.Movable.LastManipulatedTime >= since;
        }
        private string HierarchyId(Transform item)
        {
            string path = item.name + "#" + item.GetSiblingIndex();
            while (item.parent && item.parent != terrain) { item = item.parent; path = item.name + "#" + item.GetSiblingIndex() + "/" + path; }
            return path;
        }
        private static string Geometry(Collider collider)
        {
            Bounds b = collider.bounds;
            // Quantization ignores contact jitter, but notices moved supports/obstacles.
            return $"|{Mathf.RoundToInt(b.center.x * 10)}:{Mathf.RoundToInt(b.center.y * 10)}:" +
                $"{Mathf.RoundToInt(b.size.x * 10)}:{Mathf.RoundToInt(b.size.y * 10)}:" +
                $"{Mathf.RoundToInt(collider.transform.eulerAngles.z / 5)}";
        }
        public static string StableHash(string value)
        {
            uint hash = 2166136261;
            unchecked { foreach (char c in value) { hash ^= c; hash *= 16777619; } }
            return hash.ToString("x8");
        }
    }
}
