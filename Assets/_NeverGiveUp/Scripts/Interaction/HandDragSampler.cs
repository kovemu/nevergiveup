using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    /// <summary>Screen trajectory history, with independent recent velocity measurement.</summary>
    public sealed class HandDragSampler
    {
        private struct Sample { public Vector2 point; public float time; }
        private readonly List<Sample> samples = new List<Sample>(64);
        public float HistorySeconds { get; set; } = .2f;
        public int Count => samples.Count;
        public Vector2 Latest => Count == 0 ? Vector2.zero : samples[Count - 1].point;
        public Vector2 Point(int i) => samples[i].point;
        public void Clear() => samples.Clear();
        public void Add(Vector2 point, float time)
        {
            if (Count > 0)
            {
                Sample last = samples[Count - 1];
                float dt = time - last.time;
                if (dt <= 0f) return;
                // Reject impossible cursor warps and samples across a stalled frame.
                if (dt > 0.15f || Vector2.Distance(point, last.point) / dt > 12f) Clear();
            }
            samples.Add(new Sample { point = point, time = time });
            float cutoff = time - Mathf.Clamp(HistorySeconds,.1f,.25f);
            while (Count > 2 && samples[1].time <= cutoff) samples.RemoveAt(0);
            if (Count > 1 && samples[0].time < cutoff)
            {
                Sample a = samples[0], b = samples[1];
                samples[0] = new Sample { point = Vector2.Lerp(a.point, b.point,
                    Mathf.InverseLerp(a.time, b.time, cutoff)), time = cutoff };
            }
            if (Count > 256) Clear(); // Defensive bound, not frame-rate-dependent thinning.
        }
        public bool Velocity(float now, out Vector2 velocity)
        {
            velocity = Vector2.zero;
            if (Count < 2 || now - samples[Count - 1].time > 0.04f) return false;
            // Geometry may look back 200ms; impulse still uses the original 100ms
            // speed window. A stopped cursor cannot spend old crossing history.
            float end=samples[Count-1].time;
            float start=Mathf.Max(samples[0].time,end-.1f);
            float dt = end-start;
            Vector2 displacement = Latest - At(start);
            if (dt < 0.025f || displacement.magnitude < 0.008f) return false;
            if(Vector2.Distance(Latest,At(Mathf.Max(samples[0].time,end-.04f)))<.0015f)return false;
            velocity = displacement / dt;
            return true;
        }
        private Vector2 At(float time)
        {
            for(int i=1;i<Count;i++)if(samples[i].time>=time)
                return Vector2.Lerp(samples[i-1].point,samples[i].point,
                    Mathf.InverseLerp(samples[i-1].time,samples[i].time,time));
            return Latest;
        }
        public bool ContinuesAlong(Vector2 direction)
        {
            if(Count<2)return false;
            Vector2 recent=Latest-At(Mathf.Max(samples[0].time,samples[Count-1].time-.04f));
            return recent.sqrMagnitude>0 && Vector2.Dot(recent.normalized,direction.normalized)>.5f;
        }
    }
}
