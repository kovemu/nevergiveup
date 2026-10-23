using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    public struct ObstacleContact
    {
        public Collider Collider;
        public Vector3 Point, Normal;
        public float Speed, Approach;
        public bool Ceiling;
    }

    // Short-lived geometric evidence complements (does not replace) persisted jump learning.
    public sealed class ObstacleMemory
    {
        public sealed class Observation
        {
            public Collider Collider;
            public Bounds Bounds;
            public Vector3 Position, Point, Normal, Launch;
            public float Approach, Time;
            public bool Movable, Ceiling;
            public int Repeats;
        }
        readonly List<Observation> observations = new List<Observation>();
        public Observation Last { get; private set; }
        public Vector3 LastMovement { get; private set; }
        public void Remember(ObstacleContact c, Vector3 launch)
        {
            Observation old = observations.Find(o => o.Collider == c.Collider && o.Approach == c.Approach);
            int repeats = old != null && Time.time - old.Time < 30 ? old.Repeats + 1 : 1;
            if (old != null) observations.Remove(old);
            Last = new Observation { Collider = c.Collider, Bounds = c.Collider.bounds,
                Position = c.Collider.transform.position, Point = c.Point, Normal = c.Normal,
                Launch = launch, Approach = c.Approach, Ceiling = c.Ceiling,
                Movable = c.Collider.GetComponentInParent<DraggableBody>() != null || c.Collider.attachedRigidbody,
                Time = Time.time, Repeats = repeats };
            if (observations.Count == 16) observations.RemoveAt(0);
            observations.Add(Last);
        }
        public bool Refresh()
        {
            bool changed = false;
            for (int i = observations.Count - 1; i >= 0; i--)
            {
                Observation o = observations[i];
                if (!o.Collider || !o.Collider.enabled) { observations.RemoveAt(i); changed = true; continue; }
                Bounds now = o.Collider.bounds;
                if ((now.center - o.Bounds.center).sqrMagnitude > 0.04f ||
                    (now.size - o.Bounds.size).sqrMagnitude > 0.04f)
                {
                    LastMovement = now.center - o.Bounds.center;
                    observations.RemoveAt(i); changed = true;
                }
                else if (Time.time - o.Time > 20f) observations.RemoveAt(i);
            }
            return changed;
        }
        public bool RepeatsLaunch(Vector3 launch, float direction)
        {
            foreach (Observation o in observations)
                if (o.Collider && Time.time - o.Time < 20f && direction * o.Approach > 0f &&
                    Mathf.Abs(launch.x - o.Launch.x) < 0.7f && Mathf.Abs(launch.y - o.Launch.y) < 0.45f) return true;
            return false;
        }
        public float Penalty(PlatformGraph.Link link)
        {
            foreach (Observation o in observations)
                if (o.Collider && Time.time - o.Time < 20f && link.Direction * o.Approach > 0f &&
                    (o.Collider == link.To.Collider || Mathf.Abs(link.Launch.x - o.Launch.x) < 1f))
                    return Mathf.Min(18, 6 + o.Repeats * 3);
            return 0;
        }
    }
}
