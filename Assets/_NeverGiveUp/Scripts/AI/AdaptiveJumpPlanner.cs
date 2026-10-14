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
        public static JumpPlan Plan(PlatformGraph graph, PlatformGraph.Link link, CharacterMotor motor, LearningMemory memory)
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
                launch = launch, landing = landing, context = context, reusedSuccess = reused };
            return ComputeVelocity(plan, launch, motor) ? plan : null;
        }
        public static bool ComputeVelocity(JumpPlan plan, Vector3 actualFeet, CharacterMotor motor)
        {
            float g = -Physics.gravity.y;
            if (g <= 0f) return false;
            float dy = plan.landing.y - actualFeet.y;
            float apex = Mathf.Max(dy, 0f) + plan.parameters.clearance;
            plan.vy = Mathf.Min(motor.JumpSpeed, Mathf.Sqrt(2f * g * apex));
            float d = plan.vy * plan.vy - 2f * g * dy;
            if (d <= 0f) return false;
            plan.flightTime = (plan.vy + Mathf.Sqrt(d)) / g;
            plan.vx = (plan.landing.x - actualFeet.x) / plan.flightTime * plan.parameters.horizontalScale;
            if (Mathf.Abs(plan.vx) > motor.MaxJumpHorizontal)
            {
                // A longer flight is allowed only within the same Inspector jump ceiling.
                plan.vy = motor.JumpSpeed;
                d = plan.vy * plan.vy - 2f * g * dy;
                if (d <= 0f) return false;
                plan.flightTime = (plan.vy + Mathf.Sqrt(d)) / g;
                plan.vx = Mathf.Clamp((plan.landing.x - actualFeet.x) / plan.flightTime * plan.parameters.horizontalScale,
                    -motor.MaxJumpHorizontal, motor.MaxJumpHorizontal);
            }
            return Feasible(actualFeet, plan.landing, motor.JumpSpeed, motor.MaxJumpHorizontal);
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
