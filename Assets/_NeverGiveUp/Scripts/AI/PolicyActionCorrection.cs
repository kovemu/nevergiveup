using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    // A veto/approach layer for requested actions, never an independent jump policy.
    public sealed class PolicyActionCorrection
    {
        public enum Phase { Walking, Approaching, Ready, Waiting }
        readonly CharacterMotor motor;
        readonly List<Failure> failures=new List<Failure>();
        struct Failure { public Collider from,to;public JumpMode mode;public float x,until;public int count; }
        Collider launchSupport,launchTarget;
        Collider evaluatedTarget;
        float evaluatedMinX,evaluatedMaxX,launchMinX,launchMaxX;
        bool pendingProbe;
        int probeCount;
        float lastProbeAt=-100;
        public int FailedAttempts {get;private set;}
        public int ProbeAttempts {get;private set;}
        public void RenewLocalAttempts(){probeCount=0;nextScan=0;hasApproach=false;}
        JumpMode launchMode;
        float launchX,launchAt,groundAt=-1,nextScan,approachX;
        int externalVersion,geometry=-1,routeRevision=-1,input;
        bool inAttempt,collided,hasApproach;
        JumpMode approachMode;
        float approachHeldUntil,approachStartX;
        int approachDirection;
        Collider approachSupport;
        public int StabilizeApproach(PolicyRoute route,int proposed,bool jumpRequested)
        {
            if(route!=null && route.StrategyActive)return proposed;
            // Finish a short, already model-selected approach instead of reacting
            // to a one-tick reversal. Never choose a new direction or an air input.
            if(jumpRequested && motor.Grounded && hasApproach && State==Phase.Approaching &&
                route!=null && route.Revision==routeRevision && motor.Support==approachSupport &&
                Time.time<approachHeldUntil && Mathf.Abs(motor.Feet.x-approachStartX)<.8f &&
                (approachX-motor.Feet.x)*approachDirection>.08f)
                return approachDirection;
            return proposed;
        }
        public Phase State {get;private set;}
        public string Reason {get;private set;}="walking";
        public int Blocked {get;private set;}
        public int UnnecessaryBlocked {get;private set;}
        public int Downgraded {get;private set;}
        public int Repeated {get;private set;}
        public PolicyActionCorrection(CharacterMotor value){motor=value;}
        public void Reset(){failures.Clear();inAttempt=hasApproach=false;nextScan=0;Blocked=UnnecessaryBlocked=Downgraded=Repeated=FailedAttempts=ProbeAttempts=probeCount=0;lastProbeAt=-100;State=Phase.Walking;Reason="walking";geometry=routeRevision=-1;approachHeldUntil=0;}
        public void Contact(){if(inAttempt)collided=true;}
        public void Observe()
        {
            if(!inAttempt)return;
            if(motor.ExternalVersion!=externalVersion){inAttempt=false;return;}
            if(!motor.Grounded){groundAt=-1;return;}
            if(Time.time-launchAt<.2f)return;
            if(groundAt<0)groundAt=Time.time;
            if(Time.time-groundAt<.15f)return;
            inAttempt=false;
            if(motor.Support==launchTarget && motor.Feet.x>=launchMinX && motor.Feet.x<=launchMaxX)return;
            FailedAttempts++;
            // A return to the launch surface or another support is a failed link,
            // irrespective of whether there was a side collision.
            int found=failures.FindIndex(f=>f.from==launchSupport && f.to==launchTarget && f.mode==launchMode && Mathf.Abs(f.x-launchX)<.4f);
            var item=found<0?new Failure{from=launchSupport,to=launchTarget,mode=launchMode,x=launchX}:failures[found];
            item.count++;item.until=Time.time+(item.count>=2?4f:0f);
            if(found<0){if(failures.Count>=64)failures.RemoveAt(0);failures.Add(item);}else failures[found]=item;
            nextScan=0;hasApproach=false;Reason=collided?"retry after contact":"retry after missed landing";
        }
        bool Excluded(Collider from,Collider to,JumpMode mode,float x)
        {
            foreach(var f in failures)if(f.from==from && f.to==to && f.mode==mode && Mathf.Abs(f.x-x)<.4f && Time.time<f.until)return true;
            return false;
        }
        public void Launched(PolicyRoute route,JumpMode mode,bool downgraded)
        {
            launchSupport=motor.Support;launchTarget=evaluatedTarget;launchMode=mode;launchX=motor.Feet.x;launchAt=Time.time;
            launchMinX=evaluatedMinX;launchMaxX=evaluatedMaxX;
            externalVersion=motor.ExternalVersion;inAttempt=true;collided=false;groundAt=-1;
            foreach(var f in failures)if(f.from==launchSupport && f.to==launchTarget && f.mode==mode && Mathf.Abs(f.x-launchX)<.4f){Repeated++;break;}
            if(downgraded)Downgraded++;
            if(pendingProbe){probeCount++;ProbeAttempts++;lastProbeAt=Time.time;}
            hasApproach=false;nextScan=0;
        }
        bool ResolveLocalTarget(PolicyRoute route,int horizontal,out Collider target,out Vector3 landing,out float minimumX,out float maximumX)
        {
            target=route.TargetCollider;landing=route.Target;minimumX=float.NegativeInfinity;maximumX=float.PositiveInfinity;
            var support=route.CurrentPlatform;
            RaycastHit obstacle=default;
            bool blockedAhead=horizontal!=0 && motor.ObstacleAhead(horizontal,out obstacle);
            if(blockedAhead && support!=null && obstacle.collider!=target &&
                obstacle.collider.bounds.max.y<=support.Bounds.max.y+motor.LongJumpHeight-.12f)
            {
                float edge=horizontal>0?obstacle.collider.bounds.max.x:obstacle.collider.bounds.min.x;
                float x=edge+horizontal*(motor.Capsule.radius+.15f);
                if(x>support.Bounds.min.x+motor.Capsule.radius && x<support.Bounds.max.x-motor.Capsule.radius)
                {
                    target=support.Collider;landing=support.Surface(x);
                    if(horizontal>0)minimumX=x;else maximumX=x;
                }
            }
            return blockedAhead;
        }
        public bool BlocksGroundInput(PolicyRoute route,int horizontal)
        {
            if(route==null || !route.HasRoute || horizontal==0)return false;
            bool obstacle=ResolveLocalTarget(route,horizontal,out var target,out var landing,out float min,out float max);
            if(motor.GroundAhead(horizontal) && !obstacle)return false;
            if(CanProbe(route,horizontal,JumpMode.Long,out _))return false;
            // Do not mask an edge/obstacle direction if a real jump can execute
            // from here. Otherwise let PPO pick another input instead of silently
            // vetoing an unexecutable walking/jump command forever after a fall.
            return !route.ArcForInput(motor.Feet,landing,target,JumpMode.Short,horizontal,min,max) &&
                !route.ArcForInput(motor.Feet,landing,target,JumpMode.Long,horizontal,min,max);
        }
        bool CanProbe(PolicyRoute route,int horizontal,JumpMode requested,out JumpMode selected)
        {
            selected=requested;
            if(route.StrategyAvoidUnsafeProbes)return false;
            if(!route.KeepApproachingBlockedRoute || probeCount>=3 || Time.time-lastProbeAt<.7f || !motor.Grounded || motor.ControlSuppressed)return false;
            float direction=Mathf.Sign(route.Target.x-motor.Feet.x);
            if(horizontal!=0 && horizontal!=direction)return false;
            // Only an actually nearby obstacle/edge permits exploratory contact.
            // A distant blocked graph link never schedules a speculative jump.
            bool obstacle=motor.ObstacleAhead(direction,out var hit);
            if(!obstacle && motor.GroundAhead(direction))return false;
            if(!motor.IsClear(motor.Body.position+Vector3.up*.04f))return false;
            if(obstacle && hit.collider.bounds.max.y-motor.Feet.y<motor.ShortJumpHeight-.12f)selected=JumpMode.Short;
            return true;
        }
        bool TryProbe(PolicyRoute route,int horizontal,JumpMode requested,out JumpMode selected)
        {
            if(!CanProbe(route,horizontal,requested,out selected))return false;
            pendingProbe=true;State=Phase.Ready;Reason="local obstacle retry probe";return true;
        }
        public bool Request(PolicyRoute route,int horizontal,JumpMode requested,out JumpMode selected)
        {
            pendingProbe=false;
            selected=requested;
            if(route==null || !route.HasRoute){State=Phase.Waiting;Reason="no feasible route; reevaluate";Blocked++;return false;}
            if(route.StrategyActive)
            {
                hasApproach=false;evaluatedTarget=route.TargetCollider;evaluatedMinX=float.NegativeInfinity;evaluatedMaxX=float.PositiveInfinity;
                if(route.StrategyApproaching || route.StrategyAction==StrategyAction.Walk)
                {State=Phase.Approaching;Reason="strategy approach goal; PPO walking";Blocked++;return false;}
                selected=route.StrategyAction==StrategyAction.Short?JumpMode.Short:JumpMode.Long;
                if(!route.ArcForInput(motor.Feet,route.Target,route.TargetCollider,selected,horizontal))
                {State=Phase.Waiting;Reason="strategy rejected for actual PPO launch input";Blocked++;return false;}
                State=Phase.Ready;Reason="strategy validated for actual PPO input";return true;
            }
            var support=route.CurrentPlatform;
            bool blockedAhead=ResolveLocalTarget(route,horizontal,out var target,out var landing,out float minimumX,out float maximumX);
            if(route.WalkableTarget && !blockedAhead)
            {State=Phase.Walking;Reason="walkable target";Blocked++;UnnecessaryBlocked++;hasApproach=false;return false;}
            if(support==null || !target){State=Phase.Waiting;Blocked++;return false;}
            evaluatedMinX=minimumX;evaluatedMaxX=maximumX;
            if(evaluatedTarget!=target){nextScan=0;hasApproach=false;evaluatedTarget=target;}
            if(geometry!=route.GeometryRevision){failures.Clear();geometry=route.GeometryRevision;nextScan=0;}
            if(routeRevision!=route.Revision || input!=horizontal){nextScan=0;hasApproach=false;routeRevision=route.Revision;input=horizontal;}
            if(Time.time>=nextScan)
            {
                nextScan=Time.time+.2f;hasApproach=false;
                // Nearest safe position first, SHORT before LONG. Sampling spans
                // only this support; no new waypoint or target is imposed.
                for(int m=0;m<2 && !hasApproach;m++)
                for(int step=0;step<=40 && !hasApproach;step++)
                for(int side=-1;side<=1;side+=2)
                {
                    float x=motor.Feet.x+side*step*.2f;
                    if(step>0 && (horizontal==0 || (x-motor.Feet.x)*horizontal<0 || !route.WalkApproachClear(x)))continue;
                    var mode=(JumpMode)m;
                    if(Excluded(motor.Support,target,mode,x))continue;
                    if(!route.ArcForInput(support.Surface(x),landing,target,mode,horizontal,minimumX,maximumX))continue;
                    if(State!=Phase.Approaching || approachDirection!=horizontal || approachSupport!=motor.Support)
                    {approachHeldUntil=Time.time+.6f;approachStartX=motor.Feet.x;approachDirection=horizontal;approachSupport=motor.Support;}
                    approachX=x;approachMode=mode;hasApproach=true;break;
                }
            }
            if(!hasApproach){if(TryProbe(route,horizontal,requested,out selected))return true;State=Phase.Waiting;Reason="no safe launch for current input";Blocked++;return false;}
            if(Mathf.Abs(motor.Feet.x-approachX)>.16f)
            {State=Phase.Approaching;Reason="approach safe "+approachMode+" launch";Blocked++;return false;}
            selected=approachMode;
            if(Excluded(motor.Support,target,selected,motor.Feet.x) ||
                !route.ArcForInput(motor.Feet,landing,target,selected,horizontal,minimumX,maximumX))
            {nextScan=0;State=Phase.Approaching;Blocked++;return false;}
            State=Phase.Ready;Reason="current pose permits "+selected;return true;
        }
        public float GroundSpeed(PolicyRoute route,int horizontal)
        {
            if(route==null || !route.HasRoute)return 0;
            float speed=horizontal*motor.MoveSpeed;
            if(route.StrategyActive && route.StrategyApproaching)
            {
                float delta=route.StrategyLaunchX-motor.Feet.x;
                if(Mathf.Sign(delta)==horizontal)speed=horizontal*Mathf.Min(motor.MoveSpeed,Mathf.Abs(delta)*5);
            }
            if(route.WalkableTarget)
            {
                float remaining=route.Target.x-motor.Feet.x;
                if(Mathf.Abs(remaining)<.2f)return 0;
                if(Mathf.Sign(remaining)==horizontal)speed=horizontal*Mathf.Min(motor.MoveSpeed,Mathf.Abs(remaining)*5);
            }
            if(!route.WalkableTarget && State==Phase.Approaching && hasApproach && route.Revision==routeRevision)
            {
                float delta=approachX-motor.Feet.x;
                if(Mathf.Sign(delta)==horizontal)speed=horizontal*Mathf.Min(motor.MoveSpeed,Mathf.Abs(delta)*5);
            }
            // Keep the existing cliff and obstacle probes. A request denied for
            // impossibility does not manufacture an alternative jump.
            if(horizontal!=0 && (!motor.GroundAhead(horizontal) || motor.ObstacleAhead(horizontal,out _)))return 0;
            return speed;
        }
    }
}
