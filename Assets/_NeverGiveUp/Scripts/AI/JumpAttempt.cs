using System;
using UnityEngine;

namespace NeverGiveUp
{
    public enum JumpOutcome { Success, Shortfall, Overshoot, SideCollision, CeilingCollision, Slipped,
        ExternalInterference, EnvironmentChanged, Blocked, Unknown, Cancelled }
    [Serializable]
    public sealed class JumpObservation
    {
        public string from, to, context, interferenceSource, utc;
        public JumpParameters parameters;
        public JumpMode mode;
        public bool exploratory;
        public Vector3 takeoff, takeoffVelocity, commandedVelocity, landing;
        public JumpOutcome outcome, physicalOutcome;
        public bool interfered, touchedTarget, changedEnvironment;
        public bool launched = true;
        public float startTime, endTime, interferenceTime, progress, stability, distanceError;
    }
    public sealed class JumpAttempt
    {
        public readonly JumpPlan Plan;
        public readonly JumpObservation Observation;
        private readonly int externalVersion;
        private readonly Bounds targetBounds;
        private float stableTime;
        private bool leftGround, sideContact, ceilingContact, sampledVelocity;
        private float closestDistance;
        public bool TouchedTarget => Observation.touchedTarget;
        public JumpAttempt(JumpPlan plan, CharacterMotor motor)
        {
            Plan = plan; externalVersion = motor.ExternalVersion;
            targetBounds = plan.link.To.Bounds;
            Observation = new JumpObservation { from = plan.link.From.Id, to = plan.link.To.Id,
                context = plan.context, parameters = plan.parameters.Copy(),
                mode = plan.mode, exploratory = plan.exploratory, takeoff = motor.Feet,
                commandedVelocity = new Vector3(plan.vx, plan.vy, 0f), startTime = Time.time,
                utc = DateTime.UtcNow.ToString("O"), interferenceTime = -1f, interferenceSource = "none" };
            closestDistance = Mathf.Abs(plan.landing.x - motor.Feet.x);
            if (Time.time - motor.LastExternalTime < 0.8f) MarkExternal(motor.ExternalSource, motor.LastExternalTime);
        }
        public void Collision(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint point = collision.GetContact(i);
                if (point.normal.y < -0.5f) ceilingContact = true;
                if (collision.collider == Plan.link.To.Collider && Mathf.Abs(point.normal.x) > 0.6f) sideContact = true;
            }
        }
        private void MarkExternal(string source, float time)
        {
            if (!Observation.interfered) { Observation.interferenceTime = time; Observation.interferenceSource = source; }
            Observation.interfered = true;
        }
        public bool Tick(CharacterMotor motor, CharacterRecovery recovery, PlatformGraph graph)
        {
            float elapsed = Time.time - Observation.startTime;
            if (!sampledVelocity && elapsed > 0f)
            { Observation.takeoffVelocity = motor.Body.linearVelocity; sampledVelocity = true; }
            if (motor.ExternalVersion != externalVersion) MarkExternal(motor.ExternalSource, motor.LastExternalTime);
            if (graph.PlayerChanged(Plan.link, Observation.startTime - 0.2f)) MarkExternal("player-moved environment", Time.time);
            // Tiny passive PhysX movement of a landed-on box is not a new player
            // obstacle arrangement. Do not erase successful attempts for that.
            Observation.changedEnvironment |= graph.Context(Plan.link) != Plan.context &&
                graph.PlayerChanged(Plan.link, Observation.startTime - 0.2f);
            closestDistance = Mathf.Min(closestDistance, Mathf.Abs(Plan.landing.x - motor.Feet.x));
            if (!motor.Grounded) leftGround = true;
            // A probe that returns to its own floor is NOT progress. A deliberate
            // same-floor obstacle hop IS progress if it crosses its landing goal.
            bool sameSupport = Plan.link.To.Collider == Plan.link.From.Collider;
            bool crossedHop = !Plan.exploratory && sameSupport &&
                Mathf.Abs(motor.Feet.x - Plan.landing.x) <= 0.5f;
            bool onTarget = motor.Grounded && motor.Support == Plan.link.To.Collider &&
                (!sameSupport || crossedHop);
            // A safe landing anywhere on the intended platform counts, not only at
            // the planner's ideal X coordinate. Reject edges and unstable contacts.
            Bounds footprint = Plan.link.To.Bounds;
            float margin = Mathf.Min(0.16f, footprint.size.x * 0.2f);
            bool safelyOnPlatform = motor.Feet.x >= footprint.min.x + margin &&
                motor.Feet.x <= footprint.max.x - margin;
            if (onTarget && leftGround && elapsed > 0.12f)
            {
                Observation.touchedTarget = true;
                bool stable = safelyOnPlatform && !recovery.Active && Mathf.Abs(motor.Body.linearVelocity.x) < 1.1f &&
                    Mathf.Abs(motor.Body.linearVelocity.y) < 0.6f;
                stableTime = stable ? stableTime + Time.fixedDeltaTime : 0f;
                if (stableTime >= 0.3f) { Finish(JumpOutcome.Success, motor); return true; }
            }
            else if (Observation.touchedTarget)
            { Finish(JumpOutcome.Slipped, motor); return true; }
            if (leftGround && elapsed > 0.18f && motor.Grounded && !onTarget)
            { Finish(Diagnose(motor), motor); return true; }
            if (leftGround && motor.Body.linearVelocity.y < 0f && motor.Feet.y < targetBounds.min.y - 0.45f)
            { Finish(Diagnose(motor), motor); return true; }
            if (elapsed > Mathf.Max(3f, Plan.flightTime + 1.3f))
            { Finish(Diagnose(motor), motor); return true; }
            return false;
        }
        private JumpOutcome Diagnose(CharacterMotor motor)
        {
            if (ceilingContact) return JumpOutcome.CeilingCollision;
            if (sideContact) return JumpOutcome.SideCollision;
            if (Plan.exploratory && Plan.link.To.Collider == Plan.link.From.Collider &&
                motor.Grounded && motor.Support == Plan.link.From.Collider)
                return JumpOutcome.Blocked;
            float direction = Mathf.Sign(Plan.landing.x - Observation.takeoff.x);
            float near = direction > 0 ? targetBounds.min.x : targetBounds.max.x;
            float far = direction > 0 ? targetBounds.max.x : targetBounds.min.x;
            if ((motor.Feet.x - near) * direction < -0.15f) return JumpOutcome.Shortfall;
            if ((motor.Feet.x - far) * direction > 0.15f) return JumpOutcome.Overshoot;
            return JumpOutcome.Unknown;
        }
        public void Finish(JumpOutcome outcome, CharacterMotor motor)
        {
            // Finish can also be called by a route interruption before the next Tick.
            if (motor.ExternalVersion != externalVersion) MarkExternal(motor.ExternalSource, motor.LastExternalTime);
            Observation.landing = motor.Feet;
            Observation.endTime = Time.time;
            Observation.distanceError = (motor.Feet.x - Plan.landing.x) * Mathf.Sign(Plan.landing.x - Observation.takeoff.x);
            float distance = Mathf.Max(0.1f, Mathf.Abs(Plan.landing.x - Observation.takeoff.x));
            Observation.progress = Mathf.Clamp01(1f - closestDistance / distance);
            Observation.stability = outcome == JumpOutcome.Success ? Mathf.Clamp01(1f - Mathf.Abs(motor.Body.linearVelocity.x) / 1.1f) : 0f;
            Observation.physicalOutcome = outcome;
            Observation.outcome = Observation.interfered ? JumpOutcome.ExternalInterference :
                (Observation.changedEnvironment ? JumpOutcome.EnvironmentChanged : outcome);
        }
    }
}
