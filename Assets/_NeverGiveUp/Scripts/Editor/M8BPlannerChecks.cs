using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    // Deterministic ledger/lifecycle regressions. Physical policy behavior is
    // tested separately by M8BReview; these inject only isolated test state.
    public static class M8BPlannerChecks
    {
        public static void Run()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Play Mode required");
            var root=new GameObject("M8B isolated ledger checks");
            var output=new System.Text.StringBuilder();int failed=0;
            void Check(bool ok,string name){output.AppendLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;}
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(object o,string name,object value)=>o.GetType().GetField(name,flags).SetValue(o,value);
            object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,flags).Invoke(o,args);
            try
            {
                Collider Floor(string name,float x)
                {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform);go.transform.position=new Vector3(x,-.25f,0);go.transform.localScale=new Vector3(4,.5f,3);return go.GetComponent<Collider>();}
                var from=Floor("from",-1000);var target=Floor("target",-996);
                var actor=new GameObject("isolated motor");actor.transform.SetParent(root.transform);
                var capsule=actor.AddComponent<CapsuleCollider>();capsule.height=1.9f;capsule.radius=.32f;
                var motor=actor.AddComponent<CharacterMotor>();motor.Body.isKinematic=true;
                var graph=root.AddComponent<PlatformGraph>();graph.Initialize(root.transform,motor);
                void Pose(float x,Collider support)
                {motor.Body.position=new Vector3(x,.95f,0);Set(motor,"<Support>k__BackingField",support);Set(motor,"<Grounded>k__BackingField",true);Physics.SyncTransforms();}
                AutonomousStrategyPlanner Planner(out PolicyRoute route)
                {route=new PolicyRoute(graph,motor);return new AutonomousStrategyPlanner(graph,motor,route);}
                StrategyCandidate Select(AutonomousStrategyPlanner p,Collider to,Vector3 landing,StrategyAction action)
                {var c=(StrategyCandidate)Call(p,"Make",from,to,motor.Feet,landing,action,1);Call(p,"Select",c);return c;}
                void Stable(AutonomousStrategyPlanner p,Collider support)
                {Set(p,"airborne",true);Set(p,"stableLanding",support);Set(p,"stableAt",Time.time-1);Set(p,"lastLocalCheck",Time.time+1);}

                Pose(-999,from);var p=Planner(out var route);
                Select(p,from,new Vector3(-1000,0,0),StrategyAction.Short);p.Launched(JumpMode.Short,-1);Stable(p,from);p.ObserveCompletedGoal();
                Check(p.Memory.Recent.Count==1 && p.Memory.Recent[0].outcome!=StrategyOutcome.Success,"unchanged same-support landing is not recovery success");

                Pose(-999,from);p=Planner(out route);Select(p,target,new Vector3(-997,0,0),StrategyAction.Long);
                p.Launched(JumpMode.Long,1);Pose(-997,target);Stable(p,target);p.ObserveCompletedGoal();
                Check(p.Memory.Recent.Last().outcome==StrategyOutcome.Success,"final jump finishes ledger after goal detection");
                p.ObserveCompletedGoal();Check(p.Memory.Recent.Count==1,"completed goal does not fabricate further attempts");

                Pose(-999,from);p=Planner(out route);var walk=Select(p,target,new Vector3(-996,0,0),StrategyAction.Walk);
                Set(p,"selectedAt",Time.time-1);Pose(-997.9f,target);p.Tick(new Vector3(-996,0,0),false,false,false);
                Check(p.Selected==walk,"WALK selection survives crossing target support boundary");
                Pose(-996,target);p.ObserveCompletedGoal();var e=p.Memory.Recent.Last();
                Check(e.outcome==StrategyOutcome.Success && e.distanceGain>2.9f && e.endedAt-e.startedAt>=1,"final WALK records original start, duration and progress");

                Pose(-999,from);p=Planner(out route);Select(p,target,new Vector3(-996,0,0),StrategyAction.Walk);
                Pose(-996,target);Set(motor,"<ExternalVersion>k__BackingField",motor.ExternalVersion+1);p.ObserveCompletedGoal();
                Check(p.Memory.Recent.Last().outcome==StrategyOutcome.PlayerInterference && p.Memory.Strategies.Last().attempts==0,"player-assisted final WALK is neutral");

                Pose(-999,from);p=Planner(out route);Select(p,target,new Vector3(-997,0,0),StrategyAction.Long);
                Set(p,"changed",true);p.Launched(JumpMode.Long,1);Pose(-997,target);Stable(p,target);p.ObserveCompletedGoal();
                p.Launched(JumpMode.Short,1);
                var pending=(StrategyExperience)p.GetType().GetField("attempt",flags).GetValue(p);
                Check(pending!=null && !pending.afterStrategyChange,"later unselected launch does not inherit strategy-change flag");

                Pose(-999,from);p=Planner(out route);
                for(int i=0;i<2;i++)
                {
                    Select(p,target,new Vector3(-997,0,0),StrategyAction.Long);p.Launched(JumpMode.Long,1);
                    Stable(p,from);p.ObserveCompletedGoal();Set(p,"failuresOnSupport",0);
                }
                Check((bool)p.GetType().GetField("adaptive",flags).GetValue(p),"session failure evidence survives local support-counter resets");
                Select(p,target,new Vector3(-997,0,0),StrategyAction.Long);p.Launched(JumpMode.Long,1);
                p.ClearMemory();Pose(-997,target);Stable(p,target);p.ObserveCompletedGoal();
                Check(p.Memory.Recent.Count==0 && p.Memory.Strategies.Count==0 && !p.NeedsRest && p.Changes==0,"clear during flight cannot reinsert pre-clear evidence");
            }
            finally{UnityEngine.Object.Destroy(root);}
            output.AppendLine("Failures="+failed);Directory.CreateDirectory("Logs/M8B");File.WriteAllText("Logs/M8B/planner-checks.txt",output.ToString());
            if(failed>0)throw new Exception(output.ToString());
        }
    }
}
