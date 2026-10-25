using System.Collections;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using NeverGiveUp.Training;

namespace NeverGiveUp
{
    // Learned authority never falls back to the rule-based controller.
    [RequireComponent(typeof(CharacterBrain),typeof(CharacterMotor),typeof(CharacterRecovery))]
    public sealed class GameplayPolicyHost : MonoBehaviour
    {
        public enum ControlMode { Legacy, Learned, Stopped }
        [SerializeField] ControlMode startingMode=ControlMode.Legacy;
        ControlMode mode=ControlMode.Legacy;
        [SerializeField] ModelAsset model;
        [SerializeField] StageController stage;
        [SerializeField] bool useIntermediateTargets;
        [SerializeField, Min(.05f)] float shortPreparation=.4f,longPreparation=.4f;
        PolicyRoute route;
        public float PreparationSeconds(JumpMode jump)=>jump==JumpMode.Short?shortPreparation:longPreparation;
        // After recoil, a glancing airborne obstacle contact must still allow
        // policy air steering. Otherwise waiting for Grounded can wedge forever
        // between two ledges. Grounded sitting and physical knockdown stay locked.
        public bool LocomotionBlocked=>recovery.ExternalReaction || recovery.ObstacleReacting && motor.Grounded;
        CharacterBrain brain;
        CharacterMotor motor;
        CharacterRecovery recovery;
        CharacterAnimation presentation;
        ClimbAgent agent;
        DecisionRequester decisions;
        float activatedAt, contactAt=-100, progressAt;
        float bestGoalDistance;
        Vector3 previousGoal;
        float routeMissingSince=-1;
        public ControlMode Mode=>mode;
        public string LastFallback {get;private set;}="none";
        public Vector3 Goal=>useIntermediateTargets && route!=null?route.Target:stage?stage.GoalPosition:motor.Feet;
        public CharacterState? ReactionState=>recovery.Active?(recovery.GettingUp?CharacterState.GettingUp:CharacterState.KnockedDown):
            recovery.ObstacleReacting?recovery.ObstacleState:recovery.Stumbling?CharacterState.Stumbling:
            stage && stage.Completed?CharacterState.GoalReached:
            mode==ControlMode.Stopped?(motor.Grounded?CharacterState.Idle:CharacterState.Falling):(CharacterState?)null;
        void Awake()
        {
            brain=GetComponent<CharacterBrain>();motor=GetComponent<CharacterMotor>();
            recovery=GetComponent<CharacterRecovery>();presentation=GetComponent<CharacterAnimation>();
            motor.Contact+=Contact;
            if(startingMode==ControlMode.Learned){brain.enabled=false;mode=ControlMode.Stopped;}
        }
        IEnumerator Start()
        {
            yield return null; // StageController initializes the preserved Legacy memory first.
            if(startingMode==ControlMode.Learned && !agent) UseLearned(stage,model);
        }
        [ContextMenu("Use Learned (Play Mode)")]
        void SelectLearned(){if(Application.isPlaying)UseLearned(stage,model);}
        public bool UseLearned(StageController owner,ModelAsset policy)
        {
            stage=owner;model=policy;
            if(!stage || !stage.Graph || !model) {StopLearned("missing model or initialized stage");return false;}
            try
            {
                brain.RestartRoute();brain.enabled=false;
                route=useIntermediateTargets?new PolicyRoute(stage.Graph,motor):null;
                if(route!=null){motor.SampleGround();route.Refresh(stage.GoalPosition,true);}
                var behavior=GetComponent<BehaviorParameters>();
                if(!behavior)behavior=gameObject.AddComponent<BehaviorParameters>();
                behavior.BehaviorName="NGUClimb";behavior.BrainParameters.VectorObservationSize=42;
                behavior.BrainParameters.ActionSpec=ActionSpec.MakeDiscrete(3,3);
                behavior.DeterministicInference=true;behavior.Model=model;
                behavior.InferenceDevice=InferenceDevice.Burst;behavior.BehaviorType=BehaviorType.InferenceOnly;
                mode=ControlMode.Learned;
                if(!agent)agent=gameObject.AddComponent<ClimbAgent>();else agent.enabled=true;
                brain.BindExternalPresentation(agent);
                decisions=GetComponent<DecisionRequester>();
                if(!decisions)decisions=gameObject.AddComponent<DecisionRequester>();
                decisions.DecisionPeriod=5;decisions.TakeActionsBetweenDecisions=true;decisions.enabled=true;
                activatedAt=progressAt=Time.time;previousGoal=Goal;bestGoalDistance=Vector3.Distance(Goal,motor.Feet);
                LastFallback="none";
                routeMissingSince=-1;
                Debug.Log("[M7A] Learned authority active; Legacy movement disabled.",this);
                return true;
            }
            catch(System.Exception error) {StopLearned("inference initialization: "+error.Message);return false;}
        }
        void StopLearned(string reason)
        {
            if(decisions)decisions.enabled=false;
            if(agent){agent.CancelPendingActions();agent.enabled=false;}
            brain.enabled=false;mode=ControlMode.Stopped;
            if(motor.Grounded && !recovery.ExternalReaction && !recovery.ObstacleReacting)motor.Stop();
            LastFallback=reason;
            Debug.LogWarning("Learned control stopped (no Legacy fallback): "+reason,this);
        }
        public bool PreparePhysicsStep()
        {
            if(mode!=ControlMode.Learned)return false;
            if(brain.enabled){StopLearned("conflicting movement authority");return false;}
            recovery.Tick(Time.fixedDeltaTime);
            if(route!=null)route.Refresh(stage.GoalPosition);
            if(LocomotionBlocked) return false;
            if(stage.Completed){motor.Stop();return false;}
            return true;
        }
        void Update()
        {
            if(mode!=ControlMode.Learned || !agent)return;
            if(Time.time-activatedAt>2 && Time.time-agent.LastActionTime>1)
            {StopLearned("inference stopped returning actions");return;}
            if(route!=null && !route.HasRoute)
            {
                if(routeMissingSince<0)routeMissingSince=Time.time;
                if(Time.time-routeMissingSince>8)LastFallback="no reachable intermediate target; remaining Learned";
                return;
            }
            routeMissingSince=-1;
            if(Vector3.Distance(previousGoal,Goal)>.1f)
            {previousGoal=Goal;bestGoalDistance=Vector3.Distance(Goal,motor.Feet);progressAt=Time.time;}
            if(LocomotionBlocked)
            {progressAt=Time.time;bestGoalDistance=Vector3.Distance(Goal,motor.Feet);return;}
            float distance=Vector3.Distance(Goal,motor.Feet);
            if(motor.Grounded && distance<bestGoalDistance-.3f)
            {bestGoalDistance=distance;progressAt=Time.time;}
            if(!stage.Completed && Time.time-progressAt>8)
                LastFallback="no physical progress for 8 seconds; remaining Learned";
        }
        void Contact(Collision collision)
        {
            if(mode!=ControlMode.Learned || recovery.ExternalReaction || recovery.ObstacleReacting || Time.time-contactAt<1)return;
            if(!motor.TryObstacle(collision,out var contact))return;
            contactAt=Time.time;
            recovery.ReactToObstacle(contact,presentation?presentation.SitSeconds:.35f,presentation?presentation.StandSeconds:.5f);
        }
        void FixedUpdate()
        {
            if(mode!=ControlMode.Stopped)return;
            motor.SampleGround();recovery.Tick(Time.fixedDeltaTime);
            if(motor.Grounded && !recovery.ExternalReaction && !recovery.ObstacleReacting)motor.Stop();
        }
        void OnDisable() {if(brain && mode==ControlMode.Learned)StopLearned("policy host disabled");}
        void OnDestroy(){if(motor)motor.Contact-=Contact;}
    }
}
