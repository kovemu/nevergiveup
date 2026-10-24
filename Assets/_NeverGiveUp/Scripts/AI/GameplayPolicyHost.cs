using System.Collections;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using NeverGiveUp.Training;

namespace NeverGiveUp
{
    // Explicit authority switch. It never teleports, trains, or writes learning JSON.
    [RequireComponent(typeof(CharacterBrain),typeof(CharacterMotor),typeof(CharacterRecovery))]
    public sealed class GameplayPolicyHost : MonoBehaviour
    {
        public enum ControlMode { Legacy, Learned }
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
            stage && stage.Completed?CharacterState.GoalReached:(CharacterState?)null;
        void Awake()
        {
            brain=GetComponent<CharacterBrain>();motor=GetComponent<CharacterMotor>();
            recovery=GetComponent<CharacterRecovery>();presentation=GetComponent<CharacterAnimation>();
            motor.Contact+=Contact;
        }
        IEnumerator Start()
        {
            yield return null; // StageController initializes the preserved Legacy memory first.
            if(startingMode==ControlMode.Learned && !agent) UseLearned(stage,model);
        }
        [ContextMenu("Use Learned (Play Mode)")]
        void SelectLearned(){if(Application.isPlaying)UseLearned(stage,model);}
        [ContextMenu("Use Legacy (Play Mode)")]
        void SelectLegacy(){if(Application.isPlaying)UseLegacy();}
        public bool UseLearned(StageController owner,ModelAsset policy)
        {
            stage=owner;model=policy;
            if(!stage || !stage.Graph || !model) {UseLegacy("missing model or initialized stage");return false;}
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
            catch(System.Exception error) {UseLegacy("inference initialization: "+error.Message);return false;}
        }
        public void UseLegacy(string reason="user selected Legacy")
        {
            if(decisions)decisions.enabled=false;
            if(agent)agent.enabled=false;
            brain.BindExternalPresentation(null);mode=ControlMode.Legacy;
            brain.enabled=true;brain.RestartRoute();LastFallback=reason;
            Debug.Log("[M7A] Legacy authority restored: "+reason,this);
        }
        public bool PreparePhysicsStep()
        {
            if(mode!=ControlMode.Learned)return false;
            if(brain.enabled){UseLegacy("conflicting movement authority");return false;}
            recovery.Tick(Time.fixedDeltaTime);
            if(route!=null)route.Refresh(stage.GoalPosition);
            if(LocomotionBlocked) return false;
            if(stage.Completed){motor.Stop();return false;}
            return true;
        }
        void Update()
        {
            if(mode!=ControlMode.Learned || !agent)return;
            if(route!=null && !route.HasRoute)
            {
                if(routeMissingSince<0)routeMissingSince=Time.time;
                if(Time.time-routeMissingSince>8)UseLegacy("no reachable intermediate target for 8 seconds");
                return;
            }
            routeMissingSince=-1;
            if(Vector3.Distance(previousGoal,Goal)>.1f)
            {previousGoal=Goal;bestGoalDistance=Vector3.Distance(Goal,motor.Feet);progressAt=Time.time;}
            if(Time.time-activatedAt>2 && Time.time-agent.LastActionTime>1)
            {UseLegacy("inference stopped returning actions");return;}
            if(LocomotionBlocked)
            {progressAt=Time.time;bestGoalDistance=Vector3.Distance(Goal,motor.Feet);return;}
            float distance=Vector3.Distance(Goal,motor.Feet);
            if(motor.Grounded && distance<bestGoalDistance-.3f)
            {bestGoalDistance=distance;progressAt=Time.time;}
            if(!stage.Completed && Time.time-progressAt>8)
                UseLegacy("policy made no physical progress for 8 seconds");
        }
        void Contact(Collision collision)
        {
            if(mode!=ControlMode.Learned || recovery.ExternalReaction || recovery.ObstacleReacting || Time.time-contactAt<1)return;
            if(!motor.TryObstacle(collision,out var contact))return;
            contactAt=Time.time;
            recovery.ReactToObstacle(contact,presentation?presentation.SitSeconds:.35f,presentation?presentation.StandSeconds:.5f);
        }
        void OnDisable() {if(brain && mode==ControlMode.Learned)UseLegacy("policy host disabled");}
        void OnDestroy(){if(motor)motor.Contact-=Contact;}
    }
}
