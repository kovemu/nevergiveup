using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    public enum CharacterState
    {
        Idle, Running, InspectingObstacle, Jumping, Falling, KnockedDown,
        GettingUp, Recovering, Retrying, GoalReached
    }

    [RequireComponent(typeof(CharacterMotor), typeof(CharacterRecovery))]
    public sealed class CharacterBrain : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float inspectDuration = 0.4f;
        [SerializeField, Min(0.1f)] private float stuckDuration = 3.5f;
        [SerializeField, Min(0.1f)] private float jumpPreparation = 0.18f;
        private CharacterMotor motor;
        private CharacterRecovery recovery;
        private StageController stage;
        private readonly Dictionary<int, int> failures = new Dictionary<int, int>();
        private float stateTime;
        private float stalledTime;
        private float bestDistance;
        private float retryDirection;
        private float airSpeed;
        private float airTargetX;
        private bool routeJump;
        private bool plannedFlight;
        private bool wasRecovering;
        private float jumpCooldown;
        public CharacterState State { get; private set; } = CharacterState.Idle;
        public int FailureCount { get; private set; }
        public bool PreparingJump => State == CharacterState.InspectingObstacle && stateTime >= inspectDuration;

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            recovery = GetComponent<CharacterRecovery>();
        }
        public void Initialize(StageController owner) { stage = owner; RestartRoute(); }
        public void RestartRoute()
        {
            plannedFlight = wasRecovering = false;
            stalledTime = stateTime = 0f;
            bestDistance = float.PositiveInfinity;
            jumpCooldown = 0.3f;
            Change(CharacterState.Idle);
        }
        public void BeginRespawn()
        {
            RecordFailure();
            plannedFlight = false;
            Change(CharacterState.Recovering);
        }
        private void Change(CharacterState next)
        {
            if (State == next) return;
            State = next;
            stateTime = 0f;
        }
        private int Attempts => stage && failures.TryGetValue(stage.Index, out int count) ? count : 0;
        private void RecordFailure()
        {
            FailureCount++;
            if (stage) failures[stage.Index] = Mathf.Min(Attempts + 1, 1000);
        }
        private void FixedUpdate()
        {
            if (!stage) return;
            float dt = Time.fixedDeltaTime;
            stateTime += dt;
            jumpCooldown -= dt;
            motor.SampleGround();
            if (stage.IsRespawning) { Change(CharacterState.Recovering); return; }
            if (motor.Body.position.y < stage.FallThreshold)
            { stage.RequestRespawn(); return; }

            recovery.Tick(dt);
            if (recovery.NeedsRespawn) { stage.RequestRespawn(); return; }
            if (recovery.Active)
            {
                if (!wasRecovering) { RecordFailure(); plannedFlight = false; }
                wasRecovering = true;
                Change(recovery.GettingUp ? CharacterState.GettingUp : CharacterState.KnockedDown);
                return;
            }
            if (wasRecovering)
            {
                wasRecovering = false;
                stalledTime = 0f;
                bestDistance = float.PositiveInfinity;
                Change(CharacterState.Recovering);
            }
            if (stage.Completed)
            {
                Change(CharacterState.GoalReached);
                motor.Stop();
                return;
            }
            if (State == CharacterState.Recovering || State == CharacterState.Idle)
            {
                motor.Stop();
                if (stateTime < 0.4f) return;
                Change(CharacterState.Running);
            }

            StageWaypoint waypoint = stage.Current;
            if (!waypoint) return;
            Vector3 delta = waypoint.Position - motor.Feet;
            float direction = Mathf.Abs(delta.x) < 0.02f ? motor.Facing : Mathf.Sign(delta.x);
            if (!motor.Grounded)
            {
                Change(motor.Body.linearVelocity.y > 0.1f ? CharacterState.Jumping : CharacterState.Falling);
                if (plannedFlight)
                {
                    float remaining = airTargetX - motor.Body.position.x;
                    float speed = Mathf.Clamp(remaining * 6f, -Mathf.Abs(airSpeed), Mathf.Abs(airSpeed));
                    motor.Drive(speed);
                }
                else motor.Drive(direction * motor.MoveSpeed * 0.45f);
                if (stateTime > 5f) stage.RequestRespawn();
                return;
            }
            if (State == CharacterState.Jumping || State == CharacterState.Falling)
            {
                plannedFlight = false;
                jumpCooldown = 0.2f;
                Change(CharacterState.Running);
            }

            bool reached = Mathf.Abs(delta.x) <= waypoint.ArrivalRadius && Mathf.Abs(delta.y) <= 0.5f;
            if (reached && !waypoint.JumpToNext)
            {
                stage.Advance();
                bestDistance = float.PositiveInfinity;
                stalledTime = 0f;
                return;
            }

            // Progress is toward the current waypoint, not just movement back and forth.
            float distance = delta.magnitude;
            if (distance < bestDistance - 0.12f)
            { bestDistance = distance; stalledTime = 0f; }
            else stalledTime += dt;
            if (stalledTime > stuckDuration)
            {
                RecordFailure();
                stalledTime = 0f;
                bestDistance = float.PositiveInfinity;
                if (Attempts % 4 == 0) { stage.RequestRespawn(); return; }
                retryDirection = direction;
                Change(CharacterState.Retrying);
            }
            if (State == CharacterState.Retrying)
            {
                // Bounded run-up variation, never arbitrary/random navigation.
                if (stateTime < 0.22f + (Attempts % 3) * 0.06f)
                {
                    motor.Drive(motor.GroundAhead(-retryDirection) ? -retryDirection * 1.8f : 0f);
                    return;
                }
                routeJump = waypoint.JumpToNext;
                Change(CharacterState.InspectingObstacle);
            }
            if (State == CharacterState.InspectingObstacle)
            {
                motor.Stop();
                float wait = inspectDuration + jumpPreparation + (Attempts % 3) * 0.045f;
                if (stateTime < wait || jumpCooldown > 0f) return;
                Launch(waypoint, direction);
                return;
            }
            bool obstacle = motor.ObstacleAhead(direction, out _);
            bool gap = !motor.GroundAhead(direction);
            if (jumpCooldown <= 0f && (reached && waypoint.JumpToNext || obstacle || gap))
            {
                routeJump = waypoint.JumpToNext && (reached || Mathf.Abs(delta.x) < 1f);
                Change(CharacterState.InspectingObstacle);
                motor.Stop();
                return;
            }
            Change(CharacterState.Running);
            float desired = Mathf.Clamp(delta.x * 5f, -motor.MoveSpeed, motor.MoveSpeed);
            motor.Drive(desired);
        }

        private void Launch(StageWaypoint waypoint, float direction)
        {
            StageWaypoint destination = routeJump ? stage.Next : null;
            Vector3 target = destination ? destination.Position :
                motor.Feet + Vector3.right * direction * (1.9f + (Attempts % 3) * 0.2f);
            float gravity = -Physics.gravity.y;
            float height = target.y - motor.Feet.y;
            // Use a lower arc on short authored hops to avoid overhead platforms.
            float desiredApex = Mathf.Max(0f, height) + 0.65f + (Attempts % 3) * 0.08f;
            float vertical = destination ? Mathf.Min(motor.JumpSpeed,
                Mathf.Sqrt(2f * Mathf.Max(0f, gravity) * desiredApex)) : motor.JumpSpeed;
            float discriminant = vertical * vertical - 2f * gravity * height;
            if (gravity <= 0f || discriminant <= 0f)
            { RecordFailure(); stage.RequestRespawn(); return; }
            float flightTime = (vertical + Mathf.Sqrt(discriminant)) / gravity;
            float speed = (target.x - motor.Body.position.x) / flightTime;
            if (Mathf.Abs(speed) > motor.MoveSpeed * 1.6f)
            { RecordFailure(); stage.RequestRespawn(); return; }
            airTargetX = target.x;
            airSpeed = speed;
            plannedFlight = true;
            motor.Jump(speed, vertical);
            if (destination) stage.Advance();
            bestDistance = float.PositiveInfinity;
            stalledTime = 0f;
            Change(CharacterState.Jumping);
        }
    }
}
