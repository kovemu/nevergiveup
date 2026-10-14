using UnityEngine;

namespace NeverGiveUp
{
    public enum CharacterState
    {
        Idle, Running, InspectingObstacle, Jumping, Falling, KnockedDown,
        GettingUp, Recovering, Retrying, GoalReached, Stumbling
    }
    [RequireComponent(typeof(CharacterMotor), typeof(CharacterRecovery))]
    public sealed class CharacterBrain : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float inspectDuration = 0.3f;
        [SerializeField, Min(0.1f)] private float stuckDuration = 3.5f;
        [SerializeField, Min(0.1f)] private float jumpPreparation = 0.18f;
        [SerializeField] private bool persistLearning = true;
        [SerializeField] private bool showLearningDebug = true;
        private CharacterMotor motor;
        private CharacterRecovery recovery;
        private StageController stage;
        private LearningMemory memory;
        private JumpPlan plan;
        private JumpAttempt attempt;
        private PlatformGraph.Link walkLink;
        private float waitTime, cooldown, stalledTime, bestDistance, saveAt;
        private bool runup, inspecting, recovering;
        private string lastOutcome = "none";
        private string lastParameters = "none";
        private string lastInterference = "none";
        public CharacterState State { get; private set; } = CharacterState.Idle;
        public int FailureCount { get; private set; }
        public bool PreparingJump => inspecting && plan != null && waitTime > inspectDuration;
        public bool SafeForGoal => !recovery.Active && attempt == null;
        public string DebugSummary
        {
            get
            {
                JumpExperience experience = plan?.experience;
                return $"{State} | target: {plan?.link.To.Id ?? walkLink?.To.Id ?? stage?.GoalPlatform?.Id ?? "none"}\n" +
                    $"attempts: {experience?.trials ?? 0}, successes: {experience?.successes ?? 0}, excluded: {experience?.excluded ?? 0} | last: {lastOutcome}\n" +
                    $"success memory: {plan?.reusedSuccess ?? false}, learned action: {(plan != null && plan.experience.trials > 0)} | interference: {lastInterference}\n" +
                    (plan != null ? plan.parameters.Summary : lastParameters);
            }
        }
        private void Awake()
        {
            motor = GetComponent<CharacterMotor>(); recovery = GetComponent<CharacterRecovery>();
            motor.Contact += OnContact;
        }
        private void OnContact(Collision collision) { if (attempt != null) attempt.Collision(collision); }
        public void Initialize(StageController owner)
        {
            stage = owner;
            memory = new LearningMemory(stage.Graph.MapSignature, persistLearning);
            RestartRoute();
        }
        public void RestartRoute()
        {
            if (attempt != null) { attempt.Finish(JumpOutcome.Cancelled, motor); RecordAttempt(); }
            plan = null; walkLink = null;
            runup = inspecting = false; waitTime = stalledTime = 0f;
            bestDistance = float.PositiveInfinity;
            cooldown = 0.2f;
            State = CharacterState.Recovering;
        }
        public void BeginRespawn() => RestartRoute(); // Compatibility: replan in place, never reset position.
        private void FixedUpdate()
        {
            if (!stage || memory == null) return;
            float dt = Time.fixedDeltaTime;
            cooldown -= dt;
            motor.SampleGround();
            recovery.Tick(dt);
            if (attempt != null && attempt.Tick(motor, recovery, stage.Graph)) RecordAttempt();
            if (Time.time >= saveAt) { if (persistLearning) memory.Save(); saveAt = Time.time + 10f; }
            if (recovery.Active)
            {
                recovering = true;
                State = recovery.GettingUp ? CharacterState.GettingUp : CharacterState.KnockedDown;
                return;
            }
            if (recovery.Stumbling) { State = CharacterState.Stumbling; return; }
            if (recovering)
            {
                recovering = false;
                if (attempt == null) RestartRoute();
            }
            if (!motor.Grounded)
            {
                State = motor.Body.linearVelocity.y > 0.1f ? CharacterState.Jumping : CharacterState.Falling;
                if (attempt != null) AirControl(attempt.Plan);
                // An unplanned fall keeps its real momentum. It is not steered toward a stale waypoint.
                return;
            }
            if (attempt != null)
            {
                State = CharacterState.Recovering;
                motor.Stop(); // Observe an actual stable landing before crediting success.
                return;
            }
            if (stage.Completed) { State = CharacterState.GoalReached; motor.Stop(); return; }
            if (cooldown > 0f) { motor.Stop(); return; }
            PlatformGraph.Platform support = stage.Graph.Support(motor.Support);
            if (support == null) { State = CharacterState.InspectingObstacle; motor.Stop(); return; }
            if (plan != null && (plan.link.From.Id != support.Id || stage.Graph.Context(plan.link) != plan.context))
            { RestartRoute(); return; }
            if (walkLink != null && walkLink.From.Id != support.Id) walkLink = null;
            if (plan == null && walkLink == null)
            {
                PlatformGraph.Platform goal = stage.GoalPlatform;
                if (goal == null) { motor.Stop(); return; }
                if (support.Id == goal.Id)
                { WalkToward(stage.GoalPosition.x, support, dt); return; }
                PlatformGraph.Link link = stage.Graph.Next(support, goal, motor.Feet);
                if (link == null)
                {
                    State = CharacterState.InspectingObstacle;
                    motor.Stop(); cooldown = 0.5f; stage.Graph.Rebuild(); return;
                }
                if (link.Walk) walkLink = link;
                else SetPlan(AdaptiveJumpPlanner.Plan(stage.Graph, link, motor, memory));
            }
            if (walkLink != null) { WalkToward(walkLink.Landing.x, support, dt); return; }
            if (plan == null) return;
            float direction = Mathf.Sign(plan.landing.x - plan.launch.x);
            float runupDistance = Mathf.Clamp(plan.parameters.approachSpeed * plan.parameters.approachSpeed / 48f + 0.22f, 0.3f, 0.65f);
            float runupX = Mathf.Clamp(plan.launch.x - direction * runupDistance,
                support.Bounds.min.x + 0.34f, support.Bounds.max.x - 0.34f);
            float target = runup ? plan.launch.x : runupX;
            if (!runup && Mathf.Abs(motor.Feet.x - runupX) < 0.12f)
            {
                inspecting = true;
                State = CharacterState.InspectingObstacle;
                motor.Stop(); waitTime += dt;
                if (waitTime >= inspectDuration + jumpPreparation + plan.parameters.wait)
                { runup = true; inspecting = false; }
                return;
            }
            State = runup ? CharacterState.Running : CharacterState.Retrying;
            float delta = target - motor.Feet.x;
            if (runup && delta * direction <= 0.07f)
            {
                if (!AdaptiveJumpPlanner.ComputeVelocity(plan, motor.Feet, motor))
                { BlockedAttempt(JumpOutcome.Unknown); return; }
                attempt = new JumpAttempt(plan, motor);
                motor.Jump(plan.vx, plan.vy);
                State = CharacterState.Jumping;
                return;
            }
            float speed = runup ? direction * plan.parameters.approachSpeed :
                Mathf.Clamp(delta * 5f, -motor.MoveSpeed, motor.MoveSpeed);
            motor.Drive(speed);
            if (ProgressStalled(Mathf.Abs(delta), dt))
            {
                if (motor.ObstacleAhead(Mathf.Sign(delta), out RaycastHit obstruction))
                {
                    BlockedAttempt(JumpOutcome.Blocked);
                    PlanObstacleHop(support, obstruction, Mathf.Sign(delta));
                }
                else BlockedAttempt(JumpOutcome.Unknown);
            }
        }
        private void SetPlan(JumpPlan next)
        {
            plan = next; walkLink = null;
            runup = inspecting = false; waitTime = stalledTime = 0f;
            bestDistance = float.PositiveInfinity;
            if (next == null) cooldown = 0.4f;
        }
        private bool ProgressStalled(float distance, float dt)
        {
            if (distance < bestDistance - 0.08f) { bestDistance = distance; stalledTime = 0f; }
            else stalledTime += dt;
            return stalledTime > stuckDuration;
        }
        private void WalkToward(float x, PlatformGraph.Platform support, float dt)
        {
            float delta = x - motor.Feet.x;
            State = CharacterState.Running;
            float direction = Mathf.Sign(delta);
            if (Mathf.Abs(delta) > 0.2f && motor.ObstacleAhead(direction, out RaycastHit obstruction))
            { PlanObstacleHop(support, obstruction, direction); return; }
            motor.Drive(Mathf.Clamp(delta * 4f, -motor.MoveSpeed, motor.MoveSpeed));
            if (ProgressStalled(Mathf.Abs(delta), dt))
            { walkLink = null; stalledTime = 0f; bestDistance = float.PositiveInfinity; cooldown = 0.3f; stage.Graph.Rebuild(); }
        }
        private void PlanObstacleHop(PlatformGraph.Platform support, RaycastHit obstruction, float direction)
        {
            Bounds b = obstruction.collider.bounds;
            float destinationX = direction > 0f ? b.max.x + 0.7f : b.min.x - 0.7f;
            PlatformGraph.Platform destination = support;
            if (destinationX < support.Bounds.min.x + 0.35f || destinationX > support.Bounds.max.x - 0.35f)
            {
                destination = stage.Graph.Support(obstruction.collider);
                if (destination == null) { RestartRoute(); return; }
                destinationX = destination.Bounds.center.x;
            }
            Vector3 landing = destination.Surface(destinationX);
            var local = new PlatformGraph.Link { From = support, To = destination,
                Launch = support.Surface(motor.Feet.x - direction * 0.2f), Landing = landing,
                Tag = ":obstacle:" + obstruction.collider.name };
            JumpPlan next = AdaptiveJumpPlanner.Plan(stage.Graph, local, motor, memory);
            if (next != null)
            {
                // The obstacle's observed height seeds the first physical estimate, not a forced success.
                next.parameters.clearance = Mathf.Max(next.parameters.clearance,
                    b.max.y - Mathf.Max(local.Launch.y, landing.y) + 0.25f);
                next.parameters.Bound(motor.MoveSpeed);
                AdaptiveJumpPlanner.ComputeVelocity(next, next.launch, motor);
            }
            SetPlan(next);
        }
        private void AirControl(JumpPlan current)
        {
            float g = -Physics.gravity.y;
            float dy = motor.Feet.y - current.landing.y;
            float vy = motor.Body.linearVelocity.y;
            float discriminant = vy * vy + 2f * g * dy;
            if (g <= 0f || discriminant <= 0f) return;
            float time = Mathf.Max(0.15f, (vy + Mathf.Sqrt(discriminant)) / g);
            float desired = Mathf.Clamp((current.landing.x - motor.Feet.x) / time,
                -motor.MaxJumpHorizontal, motor.MaxJumpHorizontal);
            motor.Drive(desired, current.parameters.airControl);
        }
        private void BlockedAttempt(JumpOutcome outcome)
        {
            if (plan == null) { RestartRoute(); return; }
            attempt = new JumpAttempt(plan, motor);
            attempt.Observation.launched = false;
            attempt.Observation.takeoffVelocity = motor.Body.linearVelocity;
            attempt.Finish(outcome, motor);
            RecordAttempt();
        }
        private void RecordAttempt()
        {
            JumpObservation observation = attempt.Observation;
            lastOutcome = observation.outcome.ToString(); lastParameters = observation.parameters.Summary;
            lastInterference = observation.interfered ? observation.interferenceSource : "none";
            memory.Observe(attempt.Plan.experience, attempt.Plan.action, observation, motor.MoveSpeed);
            if (observation.outcome != JumpOutcome.Success && !observation.interfered) FailureCount++;
            attempt = null; plan = null; walkLink = null;
            runup = inspecting = false; stalledTime = waitTime = 0f;
            bestDistance = float.PositiveInfinity; cooldown = 0.35f;
        }
        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showLearningDebug && stage)
                GUI.Box(new Rect(12, Screen.height - 116, Mathf.Min(950, Screen.width - 24), 104), DebugSummary);
#endif
        }
        private void OnApplicationPause(bool pause) { if (pause && persistLearning) memory?.Save(); }
        private void OnApplicationQuit() { if (persistLearning) memory?.Save(); }
        private void OnDisable() { if (persistLearning) memory?.Save(); }
        private void OnDestroy() { if (motor) motor.Contact -= OnContact; }
    }
}
