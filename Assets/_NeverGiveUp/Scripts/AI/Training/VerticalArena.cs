using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

namespace NeverGiveUp.Training
{
    public sealed class VerticalArena : MonoBehaviour
    {
        public int index,initialStage=1,seed=4201;
        [Range(0,1)] public float simpleFraction=.15f;
        public float shortPreparation=.22f,longPreparation=.45f;
        [Range(0,.2f)] public float unnecessaryLongCost=.035f;
        public PolicyRoute Route {get;private set;}
        public AutonomousStrategyPlanner Strategy {get;private set;}
        public Vector3 Target=>Strategy?.ObservationGoal??Route?.Target??Final;
        public Vector3 Final {get;private set;}
        public float Height {get;private set;}
        public int Layers {get;private set;}
        public int Stage {get;private set;}
        readonly List<GameObject> surfaces=new List<GameObject>();
        readonly HashSet<Collider> visited=new HashSet<Collider>();
        readonly Dictionary<Collider,float> bestDistance=new Dictionary<Collider,float>();
        PlatformGraph graph;
        Transform terrain;
        Vector3 spawn;
        CharacterMotor motor;
        int episode,ticks,shorts,longs,unneeded,excessLong,collisions,reversals,shortLand,longLand,jumpFalls,jumpCollisions,reascents;
        float stableAt,maxHeight,previousStable,fallFrom,jumpFrom,goalDwell,collisionAt;
        Collider stableSupport;
        bool jumping,contactDuringJump,recoveryBonus;
        JumpMode lastJump;
        public Vector3 ResetLayout(CharacterMotor actor)
        {
            motor=actor;
            Stage=Mathf.Clamp(Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("stage",initialStage)),0,4);
            int baseSeed=Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("seed",seed));
            var random=new System.Random(baseSeed+index*100003+episode++);
            float mix=Academy.Instance.EnvironmentParameters.GetWithDefault("simple_fraction",simpleFraction);
            if(Stage>0 && random.NextDouble()<mix)Stage=Stage>1 && random.Next(2)==1?1:0;
            Layers=Stage==0?0:Stage==1?3:Stage==2?6:Stage==3?11:16;
            if(!graph)graph=gameObject.AddComponent<PlatformGraph>();
            if(!terrain){terrain=new GameObject("Terrain").transform;terrain.SetParent(transform,false);}
            for(int attempt=0;attempt<16;attempt++)
            {
                foreach(var surface in surfaces)surface.SetActive(false);
                float direction=random.Next(2)==0?-1:1,y=0;
                spawn=transform.position+new Vector3(-direction*3.8f,1.02f,0);
                Surface(0,-4.5f*direction,-.25f,40,.5f);
                for(int i=1;i<=Layers;i++)
                {
                    int n=(i-1)%6;int column=n<=3?n:6-n;
                    bool turn=i>3 && (i-1)%3==0;
                    float rise=i==1?.42f:turn?1.6f:(i-1)%3==1?1.16f:.44f;
                    rise+=(float)random.NextDouble()*.07f;
                    y+=rise;
                    // Offset the return flight so its underside leaves the outgoing
                    // takeoff edge clear for the full capsule, not just its feet.
                    float returnOffset=(n>=4 || n==0 && i>1)?-1.2f:0;
                    float x=(column*3.15f+returnOffset+(float)(random.NextDouble()-.5)*.1f)*direction;
                    Surface(i,x,y-.12f,2.55f+(float)random.NextDouble()*.15f,.24f);
                }
                Height=y;Final=Layers==0?transform.position+new Vector3(direction*4,0,0):surfaces[Layers].transform.position+Vector3.up*.12f;
                Physics.SyncTransforms();graph.Initialize(terrain,motor);
                bool valid=true;
                var check=new PolicyRoute(graph,motor);
                // Validate every intended adjacent edge, not just a single numeric height.
                for(int i=1;i<=Layers;i++)
                {
                    var a=graph.Support(surfaces[i-1].GetComponent<Collider>());var b=graph.Support(surfaces[i].GetComponent<Collider>());
                    var link=graph.Next(a,b,a.Bounds.center,null,l=>
                    {
                        bool clear=check.Clear(l);
                        if(attempt==15 && l.From.Collider==a.Collider && l.To.Collider==b.Collider && !clear)Debug.LogError($"M7B direct {i} {l.Launch}->{l.Landing}: {check.LastBlock}");
                        return clear?0:float.PositiveInfinity;
                    });
                    if(link==null){valid=false;if(attempt==15)Debug.LogError($"M7B rejected edge {i}: {a.Bounds} -> {b.Bounds}; {check.LastBlock}");break;}
                }
                if(valid)break;
                if(attempt==15)throw new InvalidOperationException("Generated vertical map has no collision-clear ascent path.");
            }
            Route=new PolicyRoute(graph,motor);visited.Clear();bestDistance.Clear();
            Strategy=Academy.Instance.EnvironmentParameters.GetWithDefault("autonomous_strategy",0)>.5f?new AutonomousStrategyPlanner(graph,motor,Route):null;
            ticks=shorts=longs=unneeded=excessLong=collisions=reversals=shortLand=longLand=jumpFalls=jumpCollisions=reascents=0;
            maxHeight=previousStable=fallFrom=goalDwell=0;collisionAt=-100;stableSupport=null;stableAt=Time.time;
            jumping=contactDuringJump=recoveryBonus=false;
            return spawn;
        }
        void Surface(int i,float x,float y,float width,float height)
        {
            while(surfaces.Count<=i){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Surface "+surfaces.Count;go.transform.SetParent(terrain,false);surfaces.Add(go);}
            var t=surfaces[i].transform;t.localPosition=new Vector3(x,y,0);t.localScale=new Vector3(width,height,3);t.gameObject.SetActive(true);
        }
        public bool Step(ClimbAgent agent)
        {
            if(Route==null)return true; // Academy has not started the first episode yet.
            ticks++;Route.KeepApproachingBlockedRoute=agent.CorrectionEnabled;Route.Refresh(Final);
            Strategy?.Tick(Final,false,false,agent.PendingJump!=0);
            agent.AddReward(-.00025f);
            if(motor.Grounded && stableSupport==motor.Support){}
            else {stableSupport=motor.Support;stableAt=Time.time;}
            if(motor.Grounded && Time.time-stableAt>.14f)
            {
                float height=motor.Feet.y-transform.position.y;
                if(jumping)
                {
                    if(height>=jumpFrom-.12f && !contactDuringJump){if(lastJump==JumpMode.Short)shortLand++;else longLand++;}
                    if(height<jumpFrom-.4f)jumpFalls++;
                    jumping=false;
                }
                if(height<previousStable-.65f)fallFrom=previousStable;
                if(fallFrom>0 && height>previousStable+.25f){reascents++;fallFrom=0;if(!recoveryBonus){agent.AddReward(.08f);recoveryBonus=true;}}
                previousStable=height;
                if(visited.Add(motor.Support) && height>.1f)agent.AddReward(.3f);
                if(height>maxHeight+.1f){agent.AddReward(Mathf.Min(height-maxHeight,2)*.12f);maxHeight=height;}
            }
            var target=Route.TargetCollider;
            if(target)
            {
                float d=Vector2.Distance(motor.Feet,Target);
                if(!bestDistance.TryGetValue(target,out float best))bestDistance[target]=d;
                else if(d<best){agent.AddReward(Mathf.Min(best-d,.5f)*.025f);bestDistance[target]=d;}
            }
            bool goal=motor.Grounded && Mathf.Abs(motor.Feet.y-Final.y)<.2f && Mathf.Abs(motor.Feet.x-Final.x)<.6f && Mathf.Abs(motor.Body.linearVelocity.x)<.5f;
            goalDwell=goal?goalDwell+Time.fixedDeltaTime:0;
            bool success=goalDwell>.25f;
            int limit=Stage<=1?1800:Stage==2?3500:6000;
            if(!success && ticks<limit && motor.Feet.y>transform.position.y-4)return false;
            if(success)agent.AddReward(2);
            Record(success);agent.EndEpisode();return true;
        }
        public void Launch(ClimbAgent agent,JumpMode mode)
        {
            lastJump=mode;jumping=true;contactDuringJump=false;jumpFrom=motor.Feet.y-transform.position.y;
            if(mode==JumpMode.Short)shorts++;else longs++;
            agent.AddReward(-.02f);
            if(Stage==0){unneeded++;agent.AddReward(-Academy.Instance.EnvironmentParameters.GetWithDefault("flat_jump_cost",.07f));}
            else if(mode==JumpMode.Long && Route.ShortSufficient){excessLong++;agent.AddReward(-Academy.Instance.EnvironmentParameters.GetWithDefault("long_cost",unnecessaryLongCost));}
        }
        public void Contact(ClimbAgent agent)
        {
            if(Time.time-collisionAt<.25f)return;collisionAt=Time.time;collisions++;agent.AddReward(-.015f);
            if(jumping && !contactDuringJump){jumpCollisions++;contactDuringJump=true;}
        }
        public void Reverse(ClimbAgent agent){reversals++;agent.AddReward(-.005f);}
        void Record(bool success)
        {
            var s=Academy.Instance.StatsRecorder;
            var agent=motor.GetComponent<ClimbAgent>();
            s.Add("Strategy/Changes",Strategy?.Changes??0);s.Add("Strategy/SuccessfulChanges",Strategy?.SuccessfulChanges??0);
            s.Add("Strategy/RepeatedAttempts",Strategy?.RepeatedAttempts??0);
            s.Add("Behavior/WalkSeconds",agent.WalkSeconds);s.Add("Behavior/WalkDistance",agent.WalkDistance);
            s.Add("Behavior/AnimationMetricsAvailable",agent.HasAnimationMetrics?1:0);
            if(agent.HasAnimationMetrics)s.Add("Behavior/RunAnimationSeconds",agent.RunSeconds);
            s.Add("Behavior/PreparationSeconds",agent.PrepareSeconds);
            if(agent.LandingWalkSamples>0)s.Add("Behavior/LandingWalkDelay",agent.LandingWalkDelay/agent.LandingWalkSamples);
            s.Add("Behavior/LandingWalkSamples",agent.LandingWalkSamples);
            s.Add("Behavior/BlockedJumpChecks",agent.Correction.Blocked);s.Add("Behavior/UnnecessaryJumpChecks",agent.Correction.UnnecessaryBlocked);s.Add("Behavior/LongToShort",agent.Correction.Downgraded);
            s.Add("Behavior/RepeatedAttempts",agent.Correction.Repeated);
            s.Add("Climb/Success",success?1:0);s.Add("Climb/Collisions",collisions);s.Add("Climb/Jumps",shorts+longs);
            s.Add("Climb/Short",shorts);s.Add("Climb/Long",longs);s.Add("Climb/UnnecessaryJumps",unneeded);s.Add("Climb/ExcessLong",excessLong);
            s.Add("Climb/ShortLandings",shortLand);s.Add("Climb/LongLandings",longLand);s.Add("Climb/JumpFalls",jumpFalls);s.Add("Climb/JumpCollisions",jumpCollisions);
            s.Add("Climb/MaxHeight",maxHeight);s.Add("Climb/Layers",Mathf.Max(0,visited.Count-1));s.Add("Climb/Reascents",reascents);s.Add("Climb/Reversals",reversals);
            s.Add("Climb/FinalHorizontalError",Mathf.Abs(motor.Feet.x-Final.x));
            s.Add("Climb/EpisodeSeconds",ticks*Time.fixedDeltaTime);s.Add("Climb/Level",Stage);s.Add("Climb/MapHeight",Height);
        }
    }
}
