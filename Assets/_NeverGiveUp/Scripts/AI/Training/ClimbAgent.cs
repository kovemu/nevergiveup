using System;
using System.IO;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

namespace NeverGiveUp.Training
{
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class ClimbAgent : Agent, ICharacterActionState
    {
        public TrainingArena arena;
        public VerticalArena vertical;
        GameplayPolicyHost gameplay;
        public float LastActionTime {get;private set;}
        public int HorizontalDecision => horizontal;
        public int JumpDecision => previousJump;
        public int PendingJump => pendingJump;
        PolicyActionCorrection correction;
        public PolicyActionCorrection Correction => correction;
        public bool CorrectionEnabled => gameplay ? gameplay.CorrectPolicyActions : vertical && Academy.Instance.EnvironmentParameters.GetWithDefault("action_correction",0)>.5f;
        PolicyRoute CurrentRoute => vertical ? vertical.Route : gameplay ? gameplay.Route : null;
        bool pendingDowngrade;
        public float WalkSeconds {get;private set;}
        public float WalkDistance {get;private set;}
        public float RunSeconds {get;private set;}
        public float PrepareSeconds {get;private set;}
        public float LandingWalkDelay {get;private set;}
        public int LandingWalkSamples {get;private set;}
        Vector3 metricPosition;
        bool metricGrounded;
        float landedAt=-1;
        float leftWalkBlockedUntil,rightWalkBlockedUntil;
        ClayAnimator clay;
        TrainingPresentation trainingView;
        public bool HasAnimationMetrics => clay && clay.Ready || trainingView && trainingView.Ready;
        public void CancelPendingActions()
        {horizontal=previousJump=pendingJump=0;Preparation=0;}
        public int ShortJumps {get;private set;}
        public int LongJumps {get;private set;}
        public Vector3 ObservedGoal => vertical ? vertical.Target : arena ? arena.Goal : gameplay.Goal;
        public CharacterState DisplayState => gameplay && gameplay.ReactionState.HasValue ? gameplay.ReactionState.Value :
            !Motor.Grounded ? (Motor.Body.linearVelocity.y>.1f?CharacterState.Jumping:CharacterState.Falling) :
            Preparation>0?CharacterState.InspectingObstacle:Settling?CharacterState.Recovering:
            Mathf.Abs(Motor.Body.linearVelocity.x)>.12f?CharacterState.Running:CharacterState.Idle;
        public CharacterMotor Motor {get;private set;}
        public float Preparation {get;private set;}
        public JumpMode CurrentJump {get;private set;}
        public bool Settling => Time.time<settleUntil;
        const float PreparationSeconds=.4f, LandingSeconds=.25f;
        int horizontal, previousJump, pendingJump, ticks, collisions, jumps, unnecessary, reversals;
        float prepareAt, settleUntil, previousDistance, maxStableHeight, stableTime, collisionAt=-100, visualYaw, goalDwell;
        bool wasGrounded;
        Collider stableSupport;
        readonly RaycastHit[] hits=new RaycastHit[24];
        static readonly float[] groundOffsets={-4,-2,-1,-.5f,0,.5f,1,2,4};
        public override void Initialize()
        {
            Motor=GetComponent<CharacterMotor>();
            correction=new PolicyActionCorrection(Motor);
            clay=GetComponentInChildren<ClayAnimator>();trainingView=GetComponentInChildren<TrainingPresentation>();
            gameplay=GetComponent<GameplayPolicyHost>();
            var brain=GetComponent<CharacterBrain>();
            if(brain && brain.enabled) throw new InvalidOperationException("Two movement authorities are forbidden.");
            Motor.Contact+=OnContact;
        }
        protected override void OnDisable()
        {
            if(Motor) Motor.Contact-=OnContact;
            base.OnDisable();
        }
        public override void OnEpisodeBegin()
        {
            if(arena) Motor.Teleport(arena.ResetLayout());
            if(vertical) Motor.Teleport(vertical.ResetLayout(Motor));
            horizontal=previousJump=pendingJump=ticks=collisions=jumps=unnecessary=reversals=0;
            ShortJumps=LongJumps=0;
            Preparation=0;wasGrounded=false;stableSupport=null;stableTime=0;goalDwell=0;
            correction.Reset();WalkSeconds=WalkDistance=RunSeconds=PrepareSeconds=LandingWalkDelay=0;LandingWalkSamples=0;
            metricPosition=Motor.Feet;metricGrounded=false;landedAt=-1;
            leftWalkBlockedUntil=rightWalkBlockedUntil=0;
            settleUntil=Time.time+.2f;maxStableHeight=0;collisionAt=-100;
            visualYaw=Motor.Facing*90;
            previousDistance=Vector2.Distance(Motor.Feet,ObservedGoal);
            LastActionTime=Time.time;
        }
        bool Cast(Vector3 origin,Vector3 direction,float distance,out RaycastHit nearest)
        {
            nearest=default;float best=distance;
            int count=Physics.RaycastNonAlloc(origin,direction,hits,distance,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++) if(hits[i].rigidbody!=Motor.Body && hits[i].distance<best)
            {nearest=hits[i];best=hits[i].distance;}
            return nearest.collider;
        }
        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 v=Motor.Body.linearVelocity, d=ObservedGoal-Motor.Feet;
            sensor.AddObservation(Mathf.Clamp(v.x/6.4f,-1,1));sensor.AddObservation(Mathf.Clamp(v.y/8,-1,1));
            sensor.AddObservation(Motor.Grounded);sensor.AddObservation(Mathf.Clamp(d.x/12,-1,1));
            sensor.AddObservation(Mathf.Clamp(d.y/5,-1,1));sensor.AddObservation(Motor.Facing);
            sensor.AddObservation(horizontal);sensor.AddObservation(previousJump/2f);
            sensor.AddObservation(Preparation);sensor.AddObservation(Settling);
            sensor.AddObservation(Mathf.Clamp01(Time.time-collisionAt));
            foreach(float offset in groundOffsets)
            {
                bool found=Cast(Motor.Feet+new Vector3(offset,3.5f,0),Vector3.down,8,out var hit);
                sensor.AddObservation(found);sensor.AddObservation(found?Mathf.Clamp((hit.point.y-Motor.Feet.y)/4,-1,1):-1);
            }
            for(int side=-1;side<=1;side+=2)
            {
                for(int row=0;row<3;row++)
                {
                    bool found=Cast(Motor.Feet+Vector3.up*(.3f+row*.65f),Vector3.right*side,4,out var hit);
                    sensor.AddObservation(found?hit.distance/4:1);
                    sensor.AddObservation(found?Mathf.Clamp((hit.collider.bounds.max.y-Motor.Feet.y)/4,-1,1):0);
                }
            }
            bool ceiling=Cast(Motor.Feet+Vector3.up*1.95f,Vector3.up,3,out var overhead);
            sensor.AddObservation(ceiling?overhead.distance/3:1);
            // 42 scalar observations; all geometry is actor-relative.
        }
        public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
        {
            // An input blocked for both walking and current-pose jumping cannot
            // execute. Let PPO choose among the remaining actions instead of
            // silently rejecting the same input forever at the platform edge.
            if(CorrectionEnabled && Motor.Grounded && pendingJump==0 && CurrentRoute!=null)
            {
                // Hysteresis prevents alternating turns exactly at the probe
                // boundary. Stopping remains available; no direction is forced.
                if(correction.BlocksGroundInput(CurrentRoute,-1))leftWalkBlockedUntil=Time.time+.25f;
                if(correction.BlocksGroundInput(CurrentRoute,1))rightWalkBlockedUntil=Time.time+.25f;
                if(Time.time<leftWalkBlockedUntil)mask.SetActionEnabled(0,0,false);
                if(Time.time<rightWalkBlockedUntil)mask.SetActionEnabled(0,2,false);
            }
            if(!Motor.Grounded || pendingJump!=0 || Settling || gameplay && gameplay.ReactionState.HasValue)
            {mask.SetActionEnabled(1,1,false);mask.SetActionEnabled(1,2,false);}
        }
        public override void OnActionReceived(ActionBuffers actions)
        {
            LastActionTime=Time.time;
            if(gameplay && gameplay.LocomotionBlocked)
            {horizontal=previousJump=pendingJump=0;Preparation=0;return;}
            int next=actions.DiscreteActions[0]-1, jump=actions.DiscreteActions[1];
            // A validated jump is a committed action through its brief wind-up.
            // Changing launch direction mid-preparation invalidated the approach
            // and caused repeated turns without ever taking off.
            if(CorrectionEnabled && pendingJump!=0)next=horizontal;
            if(CorrectionEnabled && pendingJump==0 && !Settling)
                next=correction.StabilizeApproach(CurrentRoute,next,jump!=0);
            if(horizontal!=0 && next!=0 && next!=horizontal) {reversals++;Reward(-.005f);if(vertical)vertical.Reverse(this);}
            horizontal=next;
            if(jump!=0 && (previousJump==0 || CorrectionEnabled) && Motor.Grounded && !Settling && pendingJump==0)
            {
                var selected=jump==1?JumpMode.Short:JumpMode.Long;
                if(!CorrectionEnabled || correction.Request(CurrentRoute,horizontal,selected,out selected))
                {
                    pendingDowngrade=jump==2 && selected==JumpMode.Short;
                    pendingJump=(int)selected+1;prepareAt=Time.time;CurrentJump=selected;
                }
            }
            previousJump=jump;
        }
        void FixedUpdate()
        {
            if(!Motor || (!arena && !gameplay && !vertical)) return;
            Motor.SampleGround();if(arena) arena.Tick();ticks++;
            MeasureLocomotion();correction.Observe();
            if(vertical && vertical.Step(this))return;
            if(gameplay && !gameplay.PreparePhysicsStep())
            {pendingJump=0;Preparation=0;return;}
            // The same turn gate runs with and without rendered characters.
            if((!clay || !clay.Ready) && (!trainingView || !trainingView.Ready))
            {
                if(Motor.Grounded)visualYaw=Mathf.MoveTowardsAngle(visualYaw,Motor.Facing*90,150*Time.fixedDeltaTime);
                Motor.VisualFacingAlignment=Mathf.Clamp01(1-Mathf.Abs(Mathf.DeltaAngle(visualYaw,Motor.Facing*90))/90);
            }
            if(Motor.Grounded && !wasGrounded) settleUntil=Time.time+LandingSeconds;
            wasGrounded=Motor.Grounded;
            float distance=Vector2.Distance(Motor.Feet,ObservedGoal);
            if(!vertical)Reward((previousDistance-distance)*.08f-.0004f);
            previousDistance=distance;
            if(Motor.Grounded && Motor.Support==stableSupport) stableTime+=Time.fixedDeltaTime;
            else {stableSupport=Motor.Support;stableTime=0;}
            if(arena && stableTime>.15f && Motor.Feet.y-arena.transform.position.y>maxStableHeight+.1f)
            {
                float height=Motor.Feet.y-arena.transform.position.y;
                AddReward(Mathf.Min(height-maxStableHeight,3)*.08f);maxStableHeight=height;
            }
            bool atRestAtGoal=Motor.Grounded && Mathf.Abs(Motor.Feet.x-ObservedGoal.x)<.55f &&
                Mathf.Abs(Motor.Feet.y-ObservedGoal.y)<.2f && Mathf.Abs(Motor.Body.linearVelocity.x)<.4f &&
                Mathf.Abs(Motor.Body.linearVelocity.y)<.2f && pendingJump==0;
            goalDwell=atRestAtGoal ? goalDwell+Time.fixedDeltaTime : 0;
            if(arena && goalDwell>=.25f)
            {AddReward(2);Finish(true);return;}
            if(arena && (ticks>=900 || Motor.Feet.y<arena.transform.position.y-3 || Mathf.Abs(Motor.Feet.x-arena.transform.position.x)>12))
            {AddReward(-.5f);Finish(false);return;}
            if(pendingJump!=0)
            {
                if(!Motor.Grounded) {pendingJump=0;Preparation=0;}
                else
                {
                    float preparation=vertical?(CurrentJump==JumpMode.Short?vertical.shortPreparation:vertical.longPreparation):gameplay?gameplay.PreparationSeconds(CurrentJump):PreparationSeconds;
                    Motor.LookToward(horizontal);Motor.Stop();
                    if(!Motor.FacingReady(horizontal))
                    {prepareAt=Time.time;Preparation=0;return;}
                    Preparation=Mathf.Clamp01((Time.time-prepareAt)/preparation);
                    if(Preparation>=1 && !Settling)
                    {
                        if(CorrectionEnabled && (!correction.Request(CurrentRoute,horizontal,CurrentJump,out var selected) || selected!=CurrentJump))
                        {pendingJump=0;Preparation=0;return;}
                        if(CorrectionEnabled)correction.Launched(CurrentRoute,CurrentJump,pendingDowngrade);
                        if(vertical)vertical.Launch(this,CurrentJump);
                        Motor.Jump(horizontal*Motor.MaxJumpHorizontal*.65f,Motor.JumpVelocity(CurrentJump));
                        if(CurrentJump==JumpMode.Short)ShortJumps++;else LongJumps++;
                        jumps++;Reward(-.025f);
                        if(arena && arena.Level==1) {unnecessary++;AddReward(-.06f);}
                        pendingJump=0;Preparation=0;
                    }
                    return;
                }
            }
            if(Settling && Motor.Grounded) Motor.Stop();
            else Motor.Drive(CorrectionEnabled && Motor.Grounded ? correction.GroundSpeed(CurrentRoute,horizontal) : horizontal*Motor.MoveSpeed);
        }
        void MeasureLocomotion()
        {
            float dt=Time.fixedDeltaTime;
            if(Motor.Grounded && !metricGrounded && ticks>2)landedAt=Time.time;
            bool external=gameplay && gameplay.LocomotionBlocked;
            if(Motor.Grounded && metricGrounded && !external && Preparation<=0 && Mathf.Abs(Motor.DesiredVelocity)>.1f && Mathf.Abs(Motor.Body.linearVelocity.x)>.1f)
            {
                WalkSeconds+=dt;WalkDistance+=Mathf.Abs(Motor.Feet.x-metricPosition.x);
                if(landedAt>=0){LandingWalkDelay+=Time.time-landedAt;LandingWalkSamples++;landedAt=-1;}
            }
            if((clay && clay.CurrentAction=="NGU_Run") || (trainingView && trainingView.CurrentAction=="NGU_Run"))RunSeconds+=dt;
            if(Preparation>0)PrepareSeconds+=dt;
            metricPosition=Motor.Feet;metricGrounded=Motor.Grounded;
        }
        void OnContact(Collision collision)
        {
            if(Time.time-collisionAt<.25f)return;
            for(int i=0;i<collision.contactCount;i++) if(collision.GetContact(i).normal.y<.45f)
            {collisionAt=Time.time;collisions++;correction.Contact();Reward(-.025f);if(vertical)vertical.Contact(this);break;}
        }
        void Reward(float value) {if(arena)AddReward(value);}
        void Finish(bool success)
        {
            var stats=Academy.Instance.StatsRecorder;
            stats.Add("Climb/Success",success?1:0);stats.Add("Climb/Collisions",collisions);
            stats.Add("Climb/UnnecessaryJumps",unnecessary);stats.Add("Climb/Jumps",jumps);
            stats.Add("Climb/EpisodeSeconds",ticks*Time.fixedDeltaTime);
            stats.Add("Climb/Level",arena.Level);
            if(arena.index==0)
            {
                string path=Path.Combine(Application.persistentDataPath,"M7A-episodes-"+System.Diagnostics.Process.GetCurrentProcess().Id+".csv");
                if(!File.Exists(path))File.AppendAllText(path,"utc,seed,level,success,steps,reward,collisions,jumps,unnecessary,reversals\n");
                File.AppendAllText(path,FormattableString.Invariant($"{DateTime.UtcNow:o},{arena.EpisodeSeed},{arena.Level},{success},{ticks},{GetCumulativeReward():F5},{collisions},{jumps},{unnecessary},{reversals}\n"));
            }
            EndEpisode();
        }
        public override void Heuristic(in ActionBuffers actionsOut)
        {actionsOut.DiscreteActions.Array[0]=1;actionsOut.DiscreteActions.Array[1]=0;}
    }
}
