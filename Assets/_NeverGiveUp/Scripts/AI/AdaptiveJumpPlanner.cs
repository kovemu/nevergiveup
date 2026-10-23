using UnityEngine;

namespace NeverGiveUp
{
    public sealed class JumpPlan
    {
        public PlatformGraph.Link link;
        public JumpExperience experience;
        public LearnedAction action;
        public JumpParameters parameters;
        public Vector3 launch, landing;
        public float vx, vy, flightTime;
        public JumpMode mode;
        public bool exploratory;
        public string context;
        public bool reusedSuccess;
    }
    public static class AdaptiveJumpPlanner
    {
        private static readonly System.Random exploration = new System.Random();
        public static bool Feasible(Vector3 from, Vector3 to, float maximumY, float maximumX)
        {
            float g = -Physics.gravity.y;
            if (g <= 0f || maximumY <= 0f || maximumX <= 0f) return false;
            float d = maximumY * maximumY - 2f * g * (to.y - from.y);
            if (d <= 0f) return false;
            float time = (maximumY + Mathf.Sqrt(d)) / g;
            return Mathf.Abs(to.x - from.x) <= maximumX * time;
        }
        public static JumpPlan Plan(PlatformGraph graph, PlatformGraph.Link link, CharacterMotor motor, LearningMemory memory,
            ObstacleMemory obstacles = null)
        {
            string context = graph.Context(link);
            JumpExperience section = memory.Section(link.Id + ":" + context);
            LearnedAction action = memory.Select(section, out bool reused);
            JumpParameters p = action.parameters.Copy(); p.Bound(motor.MoveSpeed);
            float sign = link.Direction == 0f ? 1f : link.Direction;
            Bounds source = link.From.Bounds, target = link.To.Bounds;
            float sourceMargin = Mathf.Min(0.36f, source.size.x * 0.3f);
            float targetMargin = Mathf.Min(0.42f, target.size.x * 0.3f);
            Vector3 launch = link.From.Surface(Mathf.Clamp(link.Launch.x + p.launchOffset * sign,
                source.min.x + sourceMargin, source.max.x - sourceMargin));
            Vector3 landing = link.To.Surface(Mathf.Clamp(link.Landing.x + p.landingOffset * sign,
                target.min.x + targetMargin, target.max.x - targetMargin));
            var plan = new JumpPlan { link = link, experience = section, action = action, parameters = p,
                launch = launch, landing = landing, mode = action.mode, context = context, reusedSuccess = reused };
            if ((obstacles == null || !obstacles.RepeatsLaunch(launch, sign)) && ComputeVelocity(plan, launch, motor)) return plan;
            // A learned horizontal multiplier may become impossible after changing
            // to fixed-height jumping; re-normalize without raising jump strength.
            plan.parameters.horizontalScale = 1f;
            // Try a different takeoff on the SAME support before escalating jump power
            // or retrying a known ceiling/side impact at the same X.
            for (int n = 0; n <= 12; n++)
            {
                float offset = n == 0 ? 0 : ((n + 1) / 2) * 0.55f * (n % 2 == 1 ? -sign : sign);
                Vector3 alternative = link.From.Surface(Mathf.Clamp(launch.x + offset,
                    source.min.x + sourceMargin, source.max.x - sourceMargin));
                if (obstacles != null && obstacles.RepeatsLaunch(alternative, sign)) continue;
                if (!motor.IsClear(alternative + Vector3.up * (motor.HalfHeight + 0.05f))) continue;
                if (!ComputeVelocity(plan, alternative, motor)) continue;
                plan.launch = alternative;
                return plan;
            }
            return null;
        }
        // The Y impulse is always one of two fixed heights. Learning can adjust
        // launch X, target X and horizontal control but cannot invent a third height.
        public static bool ComputeVelocity(JumpPlan plan, Vector3 actualFeet, CharacterMotor motor)
        {
            // Respect the LEARNED mode. Only switch when the preferred arc is
            // physically infeasible; a failed SHORT can therefore become LONG.
            JumpMode preferred = plan.mode;
            if (TryVelocity(plan, actualFeet, motor, preferred)) return true;
            JumpMode alternative = preferred == JumpMode.Short ? JumpMode.Long : JumpMode.Short;
            return TryVelocity(plan, actualFeet, motor, alternative);
        }

        private static bool TryVelocity(JumpPlan plan, Vector3 from, CharacterMotor motor, JumpMode mode)
        {
            float g = -Physics.gravity.y;
            if (g <= 0f) return false;
            float height = mode == JumpMode.Short ? motor.ShortJumpHeight : motor.LongJumpHeight;
            // A small amount of headroom is needed to get the capsule over the ledge.
            // The learned clearance may select the LONG mode, not increase jump power.
            float requiredHeight = Mathf.Max(0f, plan.landing.y - from.y) +
                Mathf.Clamp(plan.parameters.clearance, 0.16f, 0.28f);
            if (height < requiredHeight) return false;
            float vertical = motor.JumpVelocity(mode);
            float dy = plan.landing.y - from.y;
            float discriminant = vertical * vertical - 2f * g * dy;
            if (discriminant <= 0.0001f) return false;
            float duration = (vertical + Mathf.Sqrt(discriminant)) / g;
            float horizontal = (plan.landing.x - from.x) / duration * plan.parameters.horizontalScale;
            if (Mathf.Abs(horizontal) > motor.MaxJumpHorizontal + 0.0001f) return false;
            plan.mode = mode;
            plan.vy = vertical;
            plan.vx = horizontal;
            plan.flightTime = duration;
            return motor.ArcClear(from, new Vector3(horizontal, vertical, 0), duration,
                plan.link?.To.Collider, out _);
        }
        // Probe even when the graph cannot find a full route. This must create
        // an observable JumpAttempt, not an unrecorded random hop.
        public static JumpPlan Probe(PlatformGraph graph, PlatformGraph.Platform support,
            PlatformGraph.Platform candidate, CharacterMotor motor, LearningMemory memory,
            float direction)
        {
            if (support == null) return null;
            direction = direction < 0f ? -1f : 1f;
            Vector3 from = motor.Feet;
            Vector3 destination = candidate == null
                ? from + new Vector3(direction * 2.5f, 0.5f, 0f)
                : candidate.Surface(candidate.Bounds.center.x);
            var link = new PlatformGraph.Link
            {
                From = support, To = candidate ?? support,
                Launch = from, Landing = destination,
                Tag = candidate == null ? ":probe:edge:" + (direction > 0f ? "R" : "L") :
                    ":probe:target:" + (direction > 0f ? "R" : "L")
            };
            string context = graph.Context(link);
            JumpExperience experience = memory.Section(link.Id + ":" + context);
            LearnedAction action = memory.Select(experience, out bool reused);
            JumpParameters p = action.parameters.Copy();
            p.Bound(motor.MoveSpeed);
            JumpMode mode = action.mode;
            float vertical = motor.JumpVelocity(mode);
            float gravity = -Physics.gravity.y;
            if (gravity <= 0f) return null;
            float flightTime = 2f * vertical / gravity;
            float desiredDx = destination.x - from.x;
            float vx = Mathf.Clamp(desiredDx / Mathf.Max(0.2f, flightTime) *
                p.horizontalScale, -motor.MaxJumpHorizontal, motor.MaxJumpHorizontal);
            // Keep heading when target lies vertically above or too close in X.
            if (Mathf.Abs(vx) < 0.6f) vx = direction * Mathf.Min(1f, motor.MaxJumpHorizontal);
            return new JumpPlan
            {
                link = link, experience = experience, action = action,
                parameters = p, launch = from, landing = destination,
                mode = mode, vx = vx, vy = vertical, flightTime = flightTime,
                context = context, reusedSuccess = reused, exploratory = true
            };
        }

        public static JumpParameters Improve(JumpParameters previous, JumpObservation result, float moveSpeed)
        {
            JumpParameters next = previous.Copy();
            float correction = Mathf.Clamp(Mathf.Abs(result.distanceError) * 0.035f, 0.035f, 0.09f);
            switch (result.outcome)
            {
                case JumpOutcome.Shortfall:
                    next.horizontalScale += correction; next.launchOffset += 0.1f;
                    next.approachSpeed += 0.2f; next.wait -= 0.025f; break;
                case JumpOutcome.Overshoot:
                    next.horizontalScale -= correction; next.airControl += 0.12f;
                    next.approachSpeed -= 0.25f; break;
                case JumpOutcome.SideCollision:
                    next.launchOffset -= 0.15f; next.clearance += 0.16f;
                    next.horizontalScale -= 0.025f; next.wait += 0.025f; break;
                case JumpOutcome.CeilingCollision:
                    next.clearance -= 0.12f; next.launchOffset += 0.12f;
                    next.wait -= 0.02f; break;
                case JumpOutcome.Slipped:
                    next.landingOffset += Mathf.Clamp(-result.distanceError * 0.12f, -0.2f, 0.2f);
                    next.airControl += 0.15f; next.horizontalScale -= 0.025f;
                    next.approachSpeed -= 0.2f; next.wait += 0.04f; break;
                case JumpOutcome.Blocked:
                    next.launchOffset -= 0.18f; next.clearance += 0.12f; next.wait += 0.05f; break;
                default:
                    // Unknown evidence never gets a fabricated directional diagnosis.
                    next.launchOffset += Noise(0.16f); next.horizontalScale += Noise(0.05f);
                    next.landingOffset += Noise(0.15f); next.wait += Noise(0.04f);
                    next.airControl += Noise(0.1f); break;
            }
            next.Bound(moveSpeed);
            // Saturated evidence-directed parameters still permit bounded exploration, not a failure-count cycle.
            if (next.Summary == previous.Summary)
            { next.landingOffset += Noise(0.2f); next.approachSpeed += Noise(0.25f); next.Bound(moveSpeed); }
            return next;
        }
        private static float Noise(float radius) => ((float)exploration.NextDouble() * 2f - 1f) * radius;
    }
}
