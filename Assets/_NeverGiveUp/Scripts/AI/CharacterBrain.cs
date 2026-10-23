using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    public enum CharacterState
    {
        Idle, Running, InspectingObstacle, Jumping, Falling, KnockedDown,
        GettingUp, Recovering, Retrying, GoalReached, Stumbling, Resting, SittingDown, GettingUpFromSit
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
        private CharacterAnimation presentation;
        private StageController stage;
        private LearningMemory memory;
        private JumpPlan plan;
        private JumpAttempt attempt;
        private PlatformGraph.Link walkLink;
        private float waitTime, cooldown, stalledTime, bestDistance, saveAt;
        private float restUntil, noRouteUntil, sitStarted, standUntil;
        private int unsuccessfulAttempts;
        private string lastSupportId, previousSupportId;
        private readonly Dictionary<string, int> repeatedTransitions = new Dictionary<string, int>();
        private float bestReachedHeight = float.NegativeInfinity;
        [SerializeField, Min(1)] private int failuresBeforeRest = 4;
        [SerializeField, Min(0.2f)] private float restDuration = 2.5f;
        private bool runup, inspecting, recovering;
        private string lastOutcome = "none";
        private string lastParameters = "none";
        private string lastInterference = "none";
        private JumpMode lastJumpMode = JumpMode.Short;
        private JumpExperience lastExperience;
        readonly ObstacleMemory obstacles = new ObstacleMemory();
        [SerializeField] private bool showObstacleDebug;
        [SerializeField] private bool logObstacleEvents = true;
        private float collisionAt = -100, retreatUntil, retreatX;
        private bool retreatPending, obstacleWasReacting;
        private int environmentRevision;
        private string replanReason = "none";
        private readonly Dictionary<string, float> rejectedRoutes = new Dictionary<string, float>();
        public string ObstacleDebug => obstacles.Last == null ? "Obstacle: none | replan: " + replanReason :
            $"Obstacle: {(obstacles.Last.Collider ? obstacles.Last.Collider.name : "removed")} | {(obstacles.Last.Ceiling ? "underside" : "side")} normal {obstacles.Last.Normal}\n" +
            $"point {obstacles.Last.Point}, bounds {obstacles.Last.Bounds}, approach {obstacles.Last.Approach}, movable {obstacles.Last.Movable}\n" +
            $"state {State}, grounded {motor.Grounded}, support {motor.Support?.name ?? "none"}, target {plan?.link.To.Id ?? attempt?.Plan.link.To.Id ?? "none"}\n" +
            $"backward impulse {recovery.ObstacleImpulse}, repeated {obstacles.Last.Repeats}, moved {obstacles.LastMovement} | replan: {replanReason}";
        public CharacterState State { get; private set; } = CharacterState.Idle;
        public int FailureCount { get; private set; }
        public bool PreparingJump => inspecting && plan != null && waitTime > inspectDuration;
        public bool SafeForGoal => !recovery.ExternalReaction && !recovery.ObstacleReacting && attempt == null;
        public JumpMode CurrentJumpMode => attempt?.Plan.mode ?? plan?.mode ?? lastJumpMode;
        public string DebugSummary
        {
            get
            {
                JumpExperience experience = plan?.experience ?? attempt?.Plan.experience ?? lastExperience;
                return $"{State} | target: {plan?.link.To.Id ?? walkLink?.To.Id ?? stage?.GoalPlatform?.Id ?? "none"}\n" +
                    $"attempts: {experience?.trials ?? 0}, successes: {experience?.successes ?? 0}, excluded: {experience?.excluded ?? 0} | last: {lastOutcome}\n" +
                    $"success memory: {plan?.reusedSuccess ?? false}, learned action: {(plan != null && plan.experience.trials > 0)} | interference: {lastInterference}\n" +
                    (plan != null ? $"jump: {plan.mode} | " + plan.parameters.Summary :
                        $"last jump: {lastJumpMode} | " + lastParameters);
            }
        }
        private void Awake()
        {
            motor = GetComponent<CharacterMotor>(); recovery = GetComponent<CharacterRecovery>();
            presentation = GetComponent<CharacterAnimation>();
            motor.Contact += OnContact;
        }
        private void OnContact(Collision collision)
        {
            if (!enabled || !stage || memory == null) return;
            if (attempt != null) attempt.Collision(collision);
            if (stage.Completed || recovery.ObstacleReacting || recovery.ExternalReaction || Time.time - collisionAt < 0.75f) return;
            if (!motor.TryObstacle(collision, out ObstacleContact contact)) return;
            collisionAt = Time.time;
            Vector3 takeoff = attempt?.Observation.takeoff ?? motor.Feet;
            obstacles.Remember(contact, takeoff);
            string target = attempt?.Plan.link.To.Id ?? plan?.link.To.Id ?? "walking";
            if (attempt != null)
            {
                attempt.Finish(contact.Ceiling ? JumpOutcome.CeilingCollision : JumpOutcome.SideCollision, motor);
                RecordAttempt(); // Exactly once: clears attempt before the next contact/FixedUpdate.
            }
            RestartRoute(); restUntil = standUntil = 0;
            retreatPending = true; retreatUntil = 0;
            replanReason = $"contact {contact.Collider.name} ({(contact.Ceiling ? "underside" : "side")}); former target {target}";
            recovery.ReactToObstacle(contact, presentation ? presentation.SitSeconds : 0.35f,
                presentation ? presentation.StandSeconds : 0.5f);
            State = recovery.ObstacleState;
            obstacleWasReacting = true;
            if (logObstacleEvents) Debug.Log("[M62] collision " + ObstacleDebug, this);
        }
        public void Initialize(StageController owner)
        {
            stage = owner;
            memory = new LearningMemory(stage.Graph.MapSignature, persistLearning);
            environmentRevision = stage.Graph.EnvironmentRevision;
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
            bool changed = obstacles.Refresh();
            if (changed || environmentRevision != stage.Graph.EnvironmentRevision)
            {
                environmentRevision = stage.Graph.EnvironmentRevision;
                rejectedRoutes.Clear();
                if (plan != null || walkLink != null || changed)
                {
                    replanReason = "collider bounds/held state changed; discard stale route";
                    if (attempt == null) { RestartRoute(); retreatPending = false; }
                    if (logObstacleEvents) Debug.Log("[M62] environment " + replanReason, this);
                }
            }
            if (recovery.ObstacleReacting)
            { State = recovery.ObstacleState; obstacleWasReacting = true; return; }
            if (obstacleWasReacting && !recovery.Active)
            {
                obstacleWasReacting = false; stage.Graph.Rebuild();
                RestartRoute();
                if (logObstacleEvents) Debug.Log("[M62] reaction finished; " + ObstacleDebug, this);
            }
            if (Time.time >= saveAt) { if (persistLearning) memory.Save(); saveAt = Time.time + 10f; }
            if (recovery.ExternalReaction) { restUntil = standUntil = 0f; recovering = true; }
            if (recovery.Active)
            {
                recovering = true;
                State = recovery.GettingUp ? CharacterState.GettingUp : CharacterState.KnockedDown;
                return;
            }
            if (recovery.Stumbling) { State = CharacterState.Stumbling; return; }
            if (motor.ControlSuppressed)
            {
                State = motor.Grounded ? CharacterState.Stumbling : CharacterState.Falling;
                return;
            }
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
            if (stage.Completed)
            {
                unsuccessfulAttempts = 0;
                restUntil = 0f;
                State = CharacterState.GoalReached; motor.Stop(); return;
            }
            // Rest is an emotional pause, never a terminal failure state. External
            // physics still acts while the character is seated.
            if (Time.time < restUntil)
            {
                State = Time.time - sitStarted < (presentation ? presentation.SitSeconds : 0.35f) ? CharacterState.SittingDown : CharacterState.Resting;
                motor.Stop(); return;
            }
            if (State == CharacterState.Resting || State == CharacterState.SittingDown)
            { restUntil = 0f; standUntil = Time.time + (presentation ? presentation.StandSeconds : 0.5f); }
            if (Time.time < standUntil)
            { State = CharacterState.GettingUpFromSit; motor.Stop(); return; }
            if (cooldown > 0f) { motor.Stop(); return; }
            PlatformGraph.Platform support = stage.Graph.Support(motor.Support);
            // Moving supports sometimes change the best contact for a single frame.
            // The feet position is a reliable fallback when already grounded.
            if (support == null) support = stage.Graph.At(motor.Feet);
            if (support == null) { State = CharacterState.InspectingObstacle; motor.Stop(); return; }
            if (RetreatFromObstacle(support)) return;
            if (support.Bounds.max.y > bestReachedHeight + 0.45f)
            {
                // Reaching a genuinely new height resets recent loop suspicion.
                bestReachedHeight = support.Bounds.max.y;
                repeatedTransitions.Clear();
            }
            if (lastSupportId != support.Id)
            {
                if (!string.IsNullOrEmpty(lastSupportId))
                {
                    string transition = lastSupportId + ">" + support.Id;
                    repeatedTransitions.TryGetValue(transition, out int visits);
                    repeatedTransitions[transition] = Mathf.Min(12, visits + 1);
                }
                previousSupportId = lastSupportId;
                lastSupportId = support.Id;
            }
            if (plan != null &&
                (plan.link.From.Id != support.Id ||
                 plan.link.From.Movable != null && plan.link.From.Movable.Held ||
                 plan.link.To.Movable != null && plan.link.To.Movable.Held ||
                 stage.Graph.Context(plan.link) != plan.context &&
                 stage.Graph.PlayerChanged(plan.link, Time.time - 0.7f)))
            {
                // A resting box may move by a few millimetres from PhysX contact.
                // Re-plan for player manipulation, not passive support jitter.
                RestartRoute(); return;
            }
            if (walkLink != null && walkLink.From.Id != support.Id) walkLink = null;
            if (plan == null && walkLink == null)
            {
                PlatformGraph.Platform goal = stage.GoalPlatform;
                if (goal == null) { motor.Stop(); return; }
                if (support.Id == goal.Id)
                { WalkToward(stage.GoalPosition.x, support, dt); return; }
                PlatformGraph.Link link = stage.Graph.Next(support, goal, motor.Feet,
                    previousSupportId, LearnedRouteCost);
                if (link == null)
                    link = stage.Graph.NextStepFrom(support, goal, previousSupportId,
                        LearnedRouteCost);
                if (link == null)
                {
                    // Do not become an idle statue when no complete graph route exists.
                    // Probe the way forward; when physically blocked, retry after a
                    // short pause and occasionally sit before trying again.
                    float heading = Mathf.Sign(stage.GoalPosition.x - motor.Feet.x);
                    if (Mathf.Abs(heading) < 0.1f) heading = motor.Facing;
                    if (motor.ObstacleAhead(heading, out RaycastHit obstacle))
                        PlanObstacleHop(support, obstacle, heading);
                    else
                    {
                        float edge = heading > 0f ? support.Bounds.max.x - 0.55f :
                            support.Bounds.min.x + 0.55f;
                        if (Mathf.Abs(motor.Feet.x - edge) > 0.25f && motor.GroundAhead(heading))
                            WalkToward(edge, support, dt);
                        else if (Time.time >= noRouteUntil)
                        {
                            // All exploratory jumps must have an outcome and affect
                            // the next jump mode / trajectory selection.
                            StartProbe(support, stage.Graph.ProbeTarget(support, heading,
                                motor.Feet), heading);
                        }
                        else { State = CharacterState.InspectingObstacle; motor.Stop(); }
                    }
                    return;
                }
                if (link.Walk) walkLink = link;
                else
                {
                    JumpPlan routePlan = AdaptiveJumpPlanner.Plan(stage.Graph, link, motor, memory, obstacles);
                    if (routePlan != null) SetPlan(routePlan);
                    else
                    {
                        rejectedRoutes[link.Id] = Time.time + 6f;
                        replanReason = "no clear capsule arc: " + link.Id;
                        StartProbe(support, link.To, link.Direction);
                    }
                }
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
            // The player may have placed a box in the approach lane after the
            // route was chosen. Re-evaluate the obstruction, don't walk into it.
            if (Mathf.Abs(delta) > 0.25f && motor.ObstacleAhead(Mathf.Sign(delta),
                out RaycastHit approachObstacle) && approachObstacle.collider != plan.link.To.Collider)
            {
                PlanObstacleHop(support, approachObstacle, Mathf.Sign(delta));
                return;
            }
            if (runup && delta * direction <= 0.07f)
            {
                if (!AdaptiveJumpPlanner.ComputeVelocity(plan, motor.Feet, motor))
                {
                    rejectedRoutes[plan.link.Id] = Time.time + 6f;
                    replanReason = "takeoff arc changed; predicted blockage, no learning failure";
                    RestartRoute(); return;
                }
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
        private float LearnedRouteCost(PlatformGraph.Link link)
        {
            if (memory == null || stage == null) return 0f;
            repeatedTransitions.TryGetValue(link.From.Id + ">" + link.To.Id, out int visits);
            // Traversing the same small loop repeatedly without gaining height
            // adds temporary cost, even when each individual jump succeeded.
            return (rejectedRoutes.TryGetValue(link.Id, out float until) && Time.time < until ? 50f : 0f) +
                obstacles.Penalty(link) + memory.RoutePenalty(link.Id + ":" + stage.Graph.Context(link)) +
                Mathf.Max(0, visits - 1) * 3f;
        }
        private void StartProbe(PlatformGraph.Platform support,
            PlatformGraph.Platform candidate, float direction)
        {
            if (!motor.Grounded || Time.time < noRouteUntil)
            {
                State = CharacterState.InspectingObstacle;
                motor.Stop();
                return;
            }
            JumpPlan probing = AdaptiveJumpPlanner.Probe(stage.Graph, support, candidate,
                motor, memory, direction);
            plan = null; walkLink = null; runup = inspecting = false;
            if (probing == null)
            {
                NoteBlocked();
                return;
            }
            if (obstacles.RepeatsLaunch(motor.Feet, direction) ||
                !motor.ArcClear(motor.Feet, new Vector3(probing.vx, probing.vy, 0), probing.flightTime,
                    probing.link.To.Collider, out _))
            {
                // A prediction is not a failed physical jump and must not train the JSON.
                replanReason = "probe rejected: known collision or occupied arc";
                noRouteUntil = Time.time + 1.2f;
                NoteBlocked(); return;
            }
            plan = null;
            walkLink = null;
            attempt = new JumpAttempt(probing, motor);
            motor.Jump(probing.vx, probing.vy);
            noRouteUntil = Time.time + 1.2f;
            State = CharacterState.Jumping;
        }
        private void SetPlan(JumpPlan next)
        {
            plan = next; walkLink = null;
            runup = inspecting = false; waitTime = stalledTime = 0f;
            bestDistance = float.PositiveInfinity;
            if (next == null) cooldown = 0.4f;
        }
        private bool RetreatFromObstacle(PlatformGraph.Platform support)
        {
            if (!retreatPending || obstacles.Last == null) return false;
            if (retreatUntil == 0f)
            {
                var o = obstacles.Last;
                float desired = motor.Feet.x - o.Approach * (0.9f + Mathf.Min(o.Repeats, 3) * 0.25f);
                if (o.Ceiling && o.Collider)
                    desired = o.Approach > 0 ? o.Collider.bounds.min.x - 0.65f : o.Collider.bounds.max.x + 0.65f;
                retreatX = Mathf.Clamp(desired, support.Bounds.min.x + 0.45f, support.Bounds.max.x - 0.45f);
                retreatUntil = Time.time + 3f;
                replanReason = $"different approach X={retreatX:F2} after {o.Collider?.name}";
                if (logObstacleEvents) Debug.Log("[M62] replan " + replanReason, this);
            }
            float delta = retreatX - motor.Feet.x;
            float direction = Mathf.Sign(delta);
            if (Mathf.Abs(delta) < 0.15f || Time.time > retreatUntil || !motor.GroundAhead(direction) ||
                motor.ObstacleAhead(direction, out _))
            { retreatPending = false; retreatUntil = 0; motor.Stop(); return false; }
            State = CharacterState.Retrying;
            motor.Drive(Mathf.Clamp(delta * 3f, -1.3f, 1.3f));
            return true;
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
            if (Mathf.Abs(delta) > 0.35f && !motor.GroundAhead(direction))
            {
                // A missing floor is not an instruction to walk off a cliff.
                State = CharacterState.InspectingObstacle;
                motor.Stop(); NoteBlocked(); return;
            }
            motor.Drive(Mathf.Clamp(delta * 4f, -motor.MoveSpeed, motor.MoveSpeed));
            if (ProgressStalled(Mathf.Abs(delta), dt))
            {
                walkLink = null; stalledTime = 0f; bestDistance = float.PositiveInfinity;
                stage.Graph.Rebuild();
                NoteBlocked();
            }
        }
        private void PlanObstacleHop(PlatformGraph.Platform support, RaycastHit obstruction, float direction)
        {
            Bounds b = obstruction.collider.bounds;
            float destinationX = direction > 0f ? b.max.x + 0.7f : b.min.x - 0.7f;
            PlatformGraph.Platform destination = support;
            PlatformGraph.Platform obstacleTop = stage.Graph.Support(obstruction.collider);
            // Treat a player-placed box or low step as a potential stepping stone:
            // first land ON it, then replan from the newly gained support.
            if (obstacleTop != null && obstacleTop.Id != support.Id &&
                (obstacleTop.Movable == null || !obstacleTop.Movable.Held) &&
                b.max.y > motor.Feet.y + 0.16f &&
                motor.CanReach(support.Surface(motor.Feet.x),
                    obstacleTop.Surface(b.center.x)))
            {
                destination = obstacleTop;
                destinationX = b.center.x;
            }
            else if (destinationX < support.Bounds.min.x + 0.35f ||
                     destinationX > support.Bounds.max.x - 0.35f)
            {
                // When this cannot be planned, physically probe the blockage
                // and LEARN from the outcome rather than repeating blind retries.
                StartProbe(support, obstacleTop, direction); return;
            }
            Vector3 landing = destination.Surface(destinationX);
            var local = new PlatformGraph.Link { From = support, To = destination,
                Launch = support.Surface(motor.Feet.x - direction * 0.2f), Landing = landing,
                Tag = ":obstacle:" + obstruction.collider.name };
            JumpPlan next = AdaptiveJumpPlanner.Plan(stage.Graph, local, motor, memory, obstacles);
            if (next != null)
            {
                // The obstacle's observed height seeds the first physical estimate, not a forced success.
                next.parameters.clearance = Mathf.Max(next.parameters.clearance,
                    b.max.y - Mathf.Max(local.Launch.y, landing.y) + 0.25f);
                next.parameters.Bound(motor.MoveSpeed);
                if (!AdaptiveJumpPlanner.ComputeVelocity(next, next.launch, motor)) next = null;
            }
            if (next == null) { StartProbe(support, obstacleTop, direction); return; }
            SetPlan(next);
        }
        private void NoteBlocked()
        {
            // Never equate blocked geometry with successful passage.
            // Even an impossible obstacle is tried again after a brief rest.
            if (Time.time < restUntil) return;
            unsuccessfulAttempts++;
            cooldown = 0.65f;
            State = CharacterState.InspectingObstacle;
            motor.Stop();
            if (unsuccessfulAttempts >= failuresBeforeRest)
            {
                unsuccessfulAttempts = 0;
                sitStarted = Time.time; restUntil = Time.time + restDuration + (presentation ? presentation.SitSeconds : 0f);
                State = CharacterState.SittingDown;
            }
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
            lastJumpMode = observation.mode;
            lastExperience = attempt.Plan.experience;
            lastInterference = observation.interfered ? observation.interferenceSource : "none";
            memory.Observe(attempt.Plan.experience, attempt.Plan.action, observation, motor.MoveSpeed);
            if (observation.outcome == JumpOutcome.Success)
                unsuccessfulAttempts = 0;
            else if (!observation.interfered && observation.outcome != JumpOutcome.Cancelled &&
                     observation.outcome != JumpOutcome.EnvironmentChanged)
            {
                FailureCount++;
                unsuccessfulAttempts++;
                if (unsuccessfulAttempts >= failuresBeforeRest)
                {
                    unsuccessfulAttempts = 0;
                    sitStarted = Time.time; restUntil = Time.time + restDuration + (presentation ? presentation.SitSeconds : 0f);
                }
            }
            attempt = null; plan = null; walkLink = null;
            runup = inspecting = false; stalledTime = waitTime = 0f;
            bestDistance = float.PositiveInfinity; cooldown = 0.35f;
        }
        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showLearningDebug && stage)
                GUI.Box(new Rect(12, Screen.height - 116, Mathf.Min(950, Screen.width - 24), 104), DebugSummary);
            if (showObstacleDebug && stage)
                GUI.Box(new Rect(12, 54, Mathf.Min(1000, Screen.width - 24), 140), ObstacleDebug);
#endif
        }
        private void OnApplicationPause(bool pause) { if (pause && persistLearning) memory?.Save(); }
        private void OnApplicationQuit() { if (persistLearning) memory?.Save(); }
        private void OnDisable() { if (persistLearning) memory?.Save(); }
        private void OnDestroy() { if (motor) motor.Contact -= OnContact; }
    }
}
