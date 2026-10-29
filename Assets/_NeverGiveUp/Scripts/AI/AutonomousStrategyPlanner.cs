using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NeverGiveUp
{
    public sealed class StrategyCandidate
    {
        public string Id, Context, FromId, TargetId;
        public Collider From, Target;
        public Vector3 Launch, Landing;
        public StrategyAction Action;
        public int Direction;
        public float Score;
    }

    // Chooses goals and constraints only. Never drives, jumps, turns or teleports.
    public sealed class AutonomousStrategyPlanner
    {
        readonly PlatformGraph graph;
        readonly CharacterMotor motor;
        readonly PolicyRoute route;
        readonly Collider[] nearby=new Collider[96];
        readonly List<string> geometry=new List<string>();
        readonly List<StrategyCandidate> candidates=new List<StrategyCandidate>();
        readonly Dictionary<string,Bounds> observedMovables=new Dictionary<string,Bounds>();
        readonly Queue<string> recentSupports=new Queue<string>();
        readonly HashSet<string> exploredSupports=new HashSet<string>();
        IEnumerator scan;
        StrategyCandidate selected, attempted;
        StrategyExperience attempt;
        int externalVersion, selectedExternalVersion, failuresOnSupport, retreats, attemptsSinceRest;
        Collider lastSupport, stableLanding;
        float nextPlan, selectedAt, stableAt, progressAt, bestDistance, lastLocalCheck;
        Vector3 progressPosition, selectedStart;
        float explorationAt;
        bool adaptive, airborne, approach, wasResting, changed, afterRest;
        string lastSelected="";
        public StrategyExperienceMemory Memory {get;}
        public IReadOnlyList<StrategyCandidate> Candidates=>candidates;
        public StrategyCandidate Selected=>selected;
        public bool Approaching=>selected!=null && approach;
        public Vector3 ObservationGoal=>Approaching?selected.Launch:route.Target;
        public int Changes {get;private set;}
        public int SuccessfulChanges {get;private set;}
        public int RepeatedAttempts {get;private set;}
        public int EnvironmentChanges {get;private set;}
        public bool NeedsRest {get;private set;}
        public string LastOutcome {get;private set;}="none";
        public string Status {get;private set;}="observing PPO";
        public bool PlayerEnvironmentChanged {get;private set;}
        public AutonomousStrategyPlanner(PlatformGraph graph,CharacterMotor motor,PolicyRoute route)
        {this.graph=graph;this.motor=motor;this.route=route;Memory=new StrategyExperienceMemory(graph.MapSignature+"|strategy-v1|"+motor.ShortJumpHeight+"|"+motor.LongJumpHeight);progressAt=Time.time;progressPosition=motor.Feet;}
        public void ClearMemory()
        {
            // Clearing during a flight must not reinsert the pre-clear attempt
            // on landing, or retain discouragement derived from old evidence.
            Suspend();Memory.Clear();recentSupports.Clear();exploredSupports.Clear();observedMovables.Clear();
            failuresOnSupport=retreats=attemptsSinceRest=0;Changes=SuccessfulChanges=RepeatedAttempts=EnvironmentChanges=0;
            adaptive=afterRest=wasResting=NeedsRest=PlayerEnvironmentChanged=false;lastSelected="";
            progressAt=explorationAt=Time.time;progressPosition=motor.Feet;bestDistance=float.PositiveInfinity;
            LastOutcome="none";Status="memory cleared";
        }
        public void Suspend(){scan=null;attempt=null;attempted=null;ClearSelection();route.StrategyAvoidUnsafeProbes=false;}
        // Goal detection can precede the stable-landing interval. Finish the
        // actual flight ledger without starting another plan after completion.
        public void ObserveCompletedGoal()
        {
            scan=null;route.StrategyAvoidUnsafeProbes=false;ObserveAttempt();
            if(attempt==null && selected!=null && selected.Action==StrategyAction.Walk)
            {
                string context=Context(selected.Launch,selected.Landing,out bool moved);
                if(motor.ExternalVersion!=selectedExternalVersion || context!=selected.Context)
                {
                    var e=SelectedExperience();e.playerInterference=moved || motor.ExternalVersion!=selectedExternalVersion;
                    e.outcome=e.playerInterference?StrategyOutcome.PlayerInterference:StrategyOutcome.EnvironmentChanged;Complete(e);
                }
                else if(motor.Grounded && motor.Support==selected.Target && Vector3.Distance(motor.Feet,selected.Landing)<.3f)RecordWalkSuccess();
            }
            if(attempt==null)ClearSelection();
            Status="goal reached; finishing outcome observation";
        }
        void ClearSelection(){selected=null;approach=false;changed=false;route.ClearStrategy();scan=null;nextPlan=0;}
        string Id(Collider c)
        {
            if(!c)return "missing";
            var p=graph.Support(c);if(p!=null)return p.Id;
            string path=c.name;var t=c.transform.parent;
            while(t && t!=graph.transform){path=t.name+"/"+path;t=t.parent;}return path;
        }
        // Context covers the candidate flight corridor, not the whole map or held state.
        string Context(Vector3 launch,Vector3 landing,out bool moved)
        {
            moved=false;geometry.Clear();
            Vector3 lo=Vector3.Min(launch,landing)-new Vector3(.8f,.3f,1.5f);
            Vector3 hi=Vector3.Max(launch,landing)+new Vector3(.8f,motor.LongJumpHeight+motor.Capsule.height,1.5f);
            int n=Physics.OverlapBoxNonAlloc((lo+hi)*.5f,(hi-lo)*.5f,nearby,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            if(n==nearby.Length)return "overflow";
            for(int i=0;i<n;i++)
            {
                var c=nearby[i];if(c.attachedRigidbody==motor.Body)continue;
                Bounds b=c.bounds;string id=Id(c);
                var drag=c.GetComponentInParent<DraggableBody>();
                if(drag)
                {
                    if(observedMovables.TryGetValue(id,out var old) && (old.center-b.center).sqrMagnitude>.04f && drag.LastManipulatedTime>Time.time-3)moved=true;
                    if(!observedMovables.ContainsKey(id) || (observedMovables[id].center-b.center).sqrMagnitude>.04f)observedMovables[id]=b;
                }
                geometry.Add(id+":"+Mathf.RoundToInt(b.center.x*4)+":"+Mathf.RoundToInt(b.center.y*4)+":"+Mathf.RoundToInt(b.size.x*4)+":"+Mathf.RoundToInt(b.size.y*4));
            }
            geometry.Sort(StringComparer.Ordinal);
            return PlatformGraph.StableHash(string.Join("|",geometry));
        }
        StrategyCandidate Make(Collider from,Collider target,Vector3 launch,Vector3 landing,StrategyAction action,int direction)
        {
            string context=Context(launch,landing,out _),fromId=Id(from),targetId=Id(target);
            return new StrategyCandidate{From=from,Target=target,FromId=fromId,TargetId=targetId,Launch=launch,Landing=landing,Action=action,Direction=direction,Context=context,
                Id=fromId+">"+targetId+"|"+context+"|"+action+"|"+direction+"|"+Mathf.RoundToInt(launch.x/.4f)};
        }
        public void Tick(Vector3 final,bool resting,bool blocked,bool pending)
        {
            ObserveAttempt();
            route.StrategyAvoidUnsafeProbes=adaptive;
            if(resting)
            {
                if(!wasResting){afterRest=true;adaptive=true;ClearSelection();NeedsRest=false;retreats=0;attemptsSinceRest=0;explorationAt=Time.time;}
                wasResting=true;progressAt=Time.time;Status="resting; keep experience";return;
            }
            if(wasResting){wasResting=false;ClearSelection();nextPlan=0;Status="reconsider after rest";}
            if(attempt==null && selected!=null && motor.ExternalVersion!=selectedExternalVersion)
            {
                var e=SelectedExperience();e.outcome=StrategyOutcome.PlayerInterference;e.playerInterference=true;Complete(e);ClearSelection();
            }
            if(attempt==null && selected!=null && Time.time>=lastLocalCheck)
            {
                lastLocalCheck=Time.time+.5f;
                string context=Context(selected.Launch,selected.Landing,out bool moved);
                if(!selected.Target || context!=selected.Context)
                {
                    EnvironmentChanges++;PlayerEnvironmentChanged=moved;
                    var e=SelectedExperience();e.outcome=moved?StrategyOutcome.PlayerInterference:StrategyOutcome.EnvironmentChanged;e.playerInterference=moved;Complete(e);
                    ClearSelection();Status="local environment changed";
                }
            }
            if(blocked){progressAt=Time.time;return;}
            if(!motor.Grounded || pending || attempt!=null)return;
            if(selected!=null && selected.Action==StrategyAction.Walk && motor.Support==selected.Target && Vector3.Distance(motor.Feet,selected.Landing)<.3f)
            {RecordWalkSuccess();retreats++;ClearSelection();}
            if(motor.Support!=lastSupport)
            {lastSupport=motor.Support;failuresOnSupport=0;retreats=0;progressAt=Time.time;progressPosition=motor.Feet;bestDistance=Vector3.Distance(motor.Feet,route.Target);if(exploredSupports.Add(Id(motor.Support)))explorationAt=Time.time;
                // Crossing onto an adjacent collider is progress toward a WALK
                // landing, not a reason to discard that unfinished strategy.
                if(selected==null || selected.Action!=StrategyAction.Walk || selected.Target!=motor.Support)ClearSelection();}
            if(adaptive && Time.time-explorationAt>15 && attemptsSinceRest>=3)NeedsRest=true;
            float distance=Vector3.Distance(motor.Feet,selected!=null?selected.Landing:route.Target);
            if(distance<bestDistance-.3f){progressAt=Time.time;progressPosition=motor.Feet;bestDistance=distance;}
            if(Time.time-progressAt>8)
            {
                if(selected!=null)RecordNoProgress(selected);
                else if(route.TargetCollider)RecordNoProgress(Make(motor.Support,route.TargetCollider,motor.Feet,route.Target,StrategyAction.Walk,(int)Mathf.Sign(route.Target.x-motor.Feet.x)));
                adaptive=true;failuresOnSupport++;ClearSelection();progressAt=Time.time;
                NeedsRest=failuresOnSupport>=3;
            }
            if(selected!=null)
            {
                if(approach && Mathf.Abs(motor.Feet.x-selected.Launch.x)<.18f){approach=false;route.StrategyApproaching=false;Status="PPO launch choice";}
                return;
            }
            if(!adaptive)return;
            if(scan==null && Time.time>=nextPlan){candidates.Clear();scan=Scan(final);Status="evaluating local candidates";}
            if(scan==null)return;
            var clock=System.Diagnostics.Stopwatch.StartNew();int work=0;
            while(work++<6 && clock.Elapsed.TotalMilliseconds<2.5)
            {
                if(scan.MoveNext())continue;
                scan=null;nextPlan=Time.time+1;
                if(candidates.Count==0){Status="no validated local candidate; observing";return;}
                candidates.Sort((a,b)=>b.Score.CompareTo(a.Score));Select(candidates[0]);return;
            }
        }
        IEnumerator Scan(Vector3 final)
        {
            var from=graph.Support(motor.Support);if(from==null)yield break;
            // Recover a real corner contact by proposing a jump back onto the
            // same surface. Walking cannot climb a vertical collider side.
            if(motor.Feet.x<from.Bounds.min.x+motor.Capsule.radius || motor.Feet.x>from.Bounds.max.x-motor.Capsule.radius)
            {
                float x=Mathf.Clamp(motor.Feet.x,from.Bounds.min.x+.85f,from.Bounds.max.x-.85f);
                var land=from.Surface(x);int direction=(int)Mathf.Sign(x-motor.Feet.x);
                for(int m=0;m<2;m++)
                {
                    if(route.ArcForInput(motor.Feet,land,from.Collider,(JumpMode)m,direction))
                    {var c=Make(from.Collider,from.Collider,motor.Feet,land,(StrategyAction)(m+1),direction);Score(c,final);c.Score+=1;candidates.Add(c);}
                    yield return null;
                }
            }
            var supports=new List<PlatformGraph.Platform>(graph.Platforms);
            supports.Sort((a,b)=>Vector3.Distance(a.Bounds.ClosestPoint(motor.Feet),motor.Feet).CompareTo(Vector3.Distance(b.Bounds.ClosestPoint(motor.Feet),motor.Feet)));
            int tested=0,targets=0;
            foreach(var to in supports)
            {
                if(to.Collider==from.Collider || !to.Collider || to.Movable && to.Movable.Held)continue;
                if(Mathf.Abs(to.Bounds.ClosestPoint(motor.Feet).x-motor.Feet.x)>4 || to.Bounds.max.y-motor.Feet.y>3.5f || motor.Feet.y-to.Bounds.max.y>4)continue;
                if(++targets>8)break;
                float landingX=Mathf.Clamp(motor.Feet.x,to.Bounds.min.x+motor.Capsule.radius+.12f,to.Bounds.max.x-motor.Capsule.radius-.12f);
                var landing=to.Surface(landingX);
                if(Mathf.Abs(landing.y-motor.Feet.y)<.2f && route.WalkToClear(landing))
                {var c=Make(from.Collider,to.Collider,motor.Feet,landing,StrategyAction.Walk,(int)Mathf.Sign(landing.x-motor.Feet.x));Score(c,final);candidates.Add(c);}
                yield return null;
                // A corner contact can ground the capsule outside the support's
                // centre bounds. Evaluate its real feet, never a clamped Surface.
                for(int m=0;m<2;m++)
                {
                    int direction=(int)Mathf.Sign(landing.x-motor.Feet.x);
                    if(route.ArcForInput(motor.Feet,landing,to.Collider,(JumpMode)m,direction))
                    {var c=Make(from.Collider,to.Collider,motor.Feet,landing,(StrategyAction)(m+1),direction);Score(c,final);candidates.Add(c);}
                    yield return null;
                }
                foreach(float offset in new[]{0f,-.4f,.4f,-.8f,.8f,-1.2f,1.2f,-1.8f,1.8f,-2.4f,2.4f,-3.2f,3.2f})
                {
                    float x=motor.Feet.x+offset;
                    if(x<from.Bounds.min.x+.12f || x>from.Bounds.max.x-.12f || Mathf.Abs(offset)>.15f && (!route.WalkApproachClear(x) || !route.WalkToClear(from.Surface(x))))continue;
                    Vector3 launch=from.Surface(x);int direction=(int)Mathf.Sign(landing.x-x);
                    for(int m=0;m<2;m++)
                    {
                        if(++tested>160)break;
                        bool clear=route.ArcForInput(launch,landing,to.Collider,(JumpMode)m,direction);
                        if(clear){var c=Make(from.Collider,to.Collider,launch,landing,(StrategyAction)(m+1),direction);Score(c,final);candidates.Add(c);}
                        yield return null;
                    }
                }
            }
            // Bounded same-support retreat exposes a different local view. PPO
            // must still choose and execute walking; this is not a scripted input.
            if(retreats<2)foreach(int direction in new[]{-1,1})
            {
                var landing=from.Surface(motor.Feet.x+direction*1.2f);
                if(Mathf.Abs(landing.x-motor.Feet.x)<.8f || !route.WalkApproachClear(landing.x) || !route.WalkToClear(landing))continue;
                var c=Make(from.Collider,from.Collider,motor.Feet,landing,StrategyAction.Walk,direction);
                Score(c,final);c.Score-=2;candidates.Add(c);yield return null;
            }
        }
        void Score(StrategyCandidate c,Vector3 final)
        {
            float gain=Vector3.Distance(motor.Feet,final)-Vector3.Distance(c.Landing,final);
            c.Score=Memory.Value(c.Id)*3+gain*.6f+Mathf.Clamp(c.Landing.y-motor.Feet.y,-2,2)*.6f
                -Mathf.Abs(c.Launch.x-motor.Feet.x)*.25f+(c.Action==StrategyAction.Short?.3f:0)
                +Mathf.Min(3,c.Target.bounds.size.x)*.1f;
            if(c.Id==lastSelected)c.Score-=.5f;
            foreach(string visited in recentSupports)if(visited==c.TargetId)c.Score-=.8f;
        }
        void Select(StrategyCandidate c)
        {
            selected=c;selectedAt=Time.time;selectedStart=motor.Feet;selectedExternalVersion=motor.ExternalVersion;approach=c.Action!=StrategyAction.Walk && Mathf.Abs(c.Launch.x-motor.Feet.x)>.18f;
            changed=!string.IsNullOrEmpty(lastSelected) && lastSelected!=c.Id;
            if(changed)Changes++;
            lastSelected=c.Id;
            bestDistance=Vector3.Distance(motor.Feet,c.Landing);progressAt=Time.time;
            route.SetStrategy(c.Target,c.Landing,c.Launch.x,c.Action,approach);
            Status=approach?"PPO approach goal":"PPO launch choice";
        }
        public void Launched(JumpMode mode,int direction)
        {
            if(attempt!=null || !route.TargetCollider)return;
            attempted=selected??Make(motor.Support,route.TargetCollider,motor.Feet,route.Target,(StrategyAction)((int)mode+1),direction);
            // Attribute the executed launch, not an unexecuted proposal.
            attempted=Make(attempted.From,attempted.Target,motor.Feet,attempted.Landing,(StrategyAction)((int)mode+1),direction);
            attempt=Experience(attempted);attempt.launched=true;attempt.actualLaunch=motor.Feet;
            attempt.afterStrategyChange=selected!=null && changed;
            if(selected!=null){attempt.plannedLaunch=selected.Launch;attempt.start=selectedStart;}
            externalVersion=motor.ExternalVersion;airborne=false;stableLanding=null;stableAt=-1;
            if((Memory.Find(attempt.strategyId)?.failures??0)>0)RepeatedAttempts++;
            Status="observing physical flight";
        }
        StrategyExperience Experience(StrategyCandidate c)=>new StrategyExperience{strategyId=c.Id,fromId=c.FromId,targetId=c.TargetId,environment=c.Context,environmentVersion=graph.EnvironmentRevision,direction=c.Direction,action=c.Action,start=motor.Feet,plannedLaunch=c.Launch,intendedLanding=c.Landing,startedAt=Time.time,afterStrategyChange=changed,afterRest=afterRest};
        public void Contact(Collision collision)
        {
            if(attempt==null || !motor.TryObstacle(collision,out var contact))return;
            attempt.collided=true;attempt.collisionTarget=Id(contact.Collider);
            attempt.outcome=contact.Ceiling?StrategyOutcome.CeilingCollision:StrategyOutcome.SideCollision;
        }
        void ObserveAttempt()
        {
            if(attempt==null)return;
            if(motor.ExternalVersion!=externalVersion){Finish(StrategyOutcome.PlayerInterference,true);return;}
            if(Time.time>=lastLocalCheck)
            {
                lastLocalCheck=Time.time+.5f;
                string context=Context(attempt.actualLaunch,attempt.intendedLanding,out bool moved);
                if(context!=attempt.environment){EnvironmentChanges++;PlayerEnvironmentChanged=moved;Finish(moved?StrategyOutcome.PlayerInterference:StrategyOutcome.EnvironmentChanged,moved);return;}
            }
            if(!motor.Grounded){airborne=true;stableLanding=null;stableAt=-1;if(Time.time-attempt.startedAt>10)Finish(StrategyOutcome.UnsafeLanding,false);return;}
            if(!airborne && Time.time-attempt.startedAt<.3f)return;
            if(stableLanding!=motor.Support){stableLanding=motor.Support;stableAt=Time.time;return;}
            if(Time.time-stableAt<.2f)return;
            bool success=airborne && attempted.Target && motor.Support==attempted.Target && motor.Feet.x>=attempted.Target.bounds.min.x+motor.Capsule.radius && motor.Feet.x<=attempted.Target.bounds.max.x-motor.Capsule.radius;
            if(attempted.From==attempted.Target)success&=Mathf.Abs(motor.Feet.x-attempt.intendedLanding.x)<.4f && Mathf.Abs(motor.Feet.x-attempt.actualLaunch.x)>.4f;
            Finish(success?StrategyOutcome.Success:attempt.collided?attempt.outcome:StrategyOutcome.Unknown,false);
        }
        void Finish(StrategyOutcome result,bool player)
        {
            attempt.outcome=result;attempt.playerInterference=player;Complete(attempt);
            if(result==StrategyOutcome.Success){if(attempt.afterStrategyChange)SuccessfulChanges++;failuresOnSupport=0;recentSupports.Enqueue(attempt.targetId);if(recentSupports.Count>24)recentSupports.Dequeue();}
            else if(!player && result!=StrategyOutcome.EnvironmentChanged)
            {
                failuresOnSupport++;
                // Briefly landing on another collider must not erase repeated
                // evidence for the very same launch strategy in this session.
                if(failuresOnSupport>=2 || (Memory.Find(attempt.strategyId)?.failures??0)>=2)adaptive=true;
            }
            lastSelected=attempt.strategyId;attempt=null;attempted=null;afterRest=false;ClearSelection();
            if(!player && result!=StrategyOutcome.EnvironmentChanged)attemptsSinceRest++;
        }
        void Complete(StrategyExperience e)
        {e.actualLanding=motor.Feet;e.endedAt=Time.time;e.heightGain=motor.Feet.y-e.start.y;e.distanceGain=Vector3.Distance(e.start,e.intendedLanding)-Vector3.Distance(motor.Feet,e.intendedLanding);Memory.Record(e);LastOutcome=e.outcome.ToString();}
        void RecordNoProgress(StrategyCandidate c)
        {var e=c==selected?SelectedExperience():Experience(c);e.outcome=StrategyOutcome.RepeatedNoProgress;if(c!=selected){e.startedAt=progressAt;e.start=progressPosition;}Complete(e);lastSelected=c.Id;attemptsSinceRest++;}
        StrategyExperience SelectedExperience(){var e=Experience(selected);e.startedAt=selectedAt;e.start=selectedStart;return e;}
        void RecordWalkSuccess(){var e=SelectedExperience();e.outcome=StrategyOutcome.Success;Complete(e);if(e.afterStrategyChange)SuccessfulChanges++;attemptsSinceRest++;}
    }
}
