using System.Collections.Generic;
using UnityEngine;

namespace NeverGiveUp
{
    // Supplies a destination only. No movement, takeoff position or timing commands.
    public sealed class PolicyRoute
    {
        readonly PlatformGraph graph;
        readonly CharacterMotor motor;
        readonly Dictionary<string,bool> clearance=new Dictionary<string,bool>();
        float refreshAt;
        int checkedRevision=-1;
        Collider support;
        Vector3 final;
        public Vector3 Target {get;private set;}
        public Collider TargetCollider {get;private set;}
        public bool HasRoute {get;private set;}
        public int Revision {get;private set;}
        public string LastBlock {get;private set;}
        public bool WalkableTarget {get;private set;}
        public bool KeepApproachingBlockedRoute {get;set;}
        public bool UsingBlockedRouteIntent {get;private set;}
        public bool StrategyActive {get;private set;}
        public bool StrategyAvoidUnsafeProbes {get;set;}
        public bool StrategyApproaching {get;set;}
        public float StrategyLaunchX {get;private set;}
        public StrategyAction StrategyAction {get;private set;}
        public void SetStrategy(Collider target,Vector3 landing,float launchX,StrategyAction action,bool approaching)
        {
            StrategyActive=true;StrategyApproaching=approaching;StrategyLaunchX=launchX;StrategyAction=action;
            TargetCollider=target;Target=landing;WalkableTarget=action==NeverGiveUp.StrategyAction.Walk;
            HasRoute=target;UsingBlockedRouteIntent=false;Revision++;
        }
        public void ClearStrategy(){if(!StrategyActive)return;StrategyActive=false;StrategyApproaching=false;refreshAt=0;Revision++;}
        public int GeometryRevision => graph.EnvironmentRevision;
        public PlatformGraph.Platform CurrentPlatform => graph.Support(motor.Support) ?? graph.At(motor.Feet);
        readonly RaycastHit[] sweepHits=new RaycastHit[24];
        readonly Collider[] startOverlaps=new Collider[24];
        public PolicyRoute(PlatformGraph graph,CharacterMotor motor){this.graph=graph;this.motor=motor;Target=motor.Feet;}
        public void Refresh(Vector3 destination,bool force=false)
        {
            if(StrategyActive && TargetCollider)return;
            if(!force && (!motor.Grounded || (Time.time<refreshAt && support==motor.Support && Vector3.Distance(final,destination)<.05f)))return;
            Collider previousSupport=support;
            refreshAt=Time.time+.6f;support=motor.Support;final=destination;
            graph.Rebuild();
            if(checkedRevision!=graph.EnvironmentRevision){clearance.Clear();checkedRevision=graph.EnvironmentRevision;}
            if(UsingBlockedRouteIntent)clearance.Clear(); // Reconsider blocked links on each retry refresh.
            var from=graph.Support(motor.Support)??graph.At(motor.Feet);var goal=graph.At(destination);
            var next=graph.Next(from,goal,motor.Feet,null,link=>Clear(link)?0:float.PositiveInfinity);
            UsingBlockedRouteIntent=false;
            if(next==null && KeepApproachingBlockedRoute && from!=null && goal!=null && from.Collider!=goal.Collider)
            {
                // A blocked future link must not stop an actor several platforms
                // away. Retain structural navigation intent; local motion and
                // retry checks deal with the obstacle once it is encountered.
                // Keep a still-clear current leg instead of changing local intent
                // merely because the obstruction is much farther up the map.
                if(previousSupport==motor.Support && TargetCollider && TargetCollider!=from.Collider)
                    next=graph.Next(from,graph.Support(TargetCollider),motor.Feet,null,link=>Clear(link)?0:float.PositiveInfinity);
                next=next??graph.Next(from,goal,motor.Feet)??graph.NextStepFrom(from,goal,null);
                UsingBlockedRouteIntent=true;
            }
            Collider selected=next?.To.Collider;
            Vector3 target=next!=null?next.Landing:from!=null && goal!=null && from.Collider==goal.Collider?destination:motor.Feet;
            bool reachable=next!=null || from!=null && goal!=null && from.Collider==goal.Collider;
            if(!reachable && KeepApproachingBlockedRoute && from!=null && goal!=null)
            {target=destination;selected=goal.Collider;reachable=true;UsingBlockedRouteIntent=true;}
            WalkableTarget=next!=null?next.Walk:from!=null && goal!=null && from.Collider==goal.Collider;
            if(selected!=TargetCollider || Vector3.Distance(target,Target)>.1f)Revision++;
            TargetCollider=selected;Target=target;HasRoute=reachable;
        }
        public bool Clear(PlatformGraph.Link link)
        {
            if(link.Walk)return true;
            if(clearance.TryGetValue(link.Id,out bool value))return value;
            value=false;
            // Graph launch is a Legacy planning sample, not the only legal place
            // a learned agent can jump. Search the support, without commanding it.
            foreach(float offset in new[]{0f,.3f,-.3f,.6f,-.6f,.9f,-.9f})
            {
                float x=link.Launch.x+offset;
                if(x<link.From.Bounds.min.x+.12f || x>link.From.Bounds.max.x-.12f)continue;
                var launch=link.From.Surface(x);
                if(Arc(launch,link.Landing,link.To.Collider,JumpMode.Short) || Arc(launch,link.Landing,link.To.Collider,JumpMode.Long)){value=true;break;}
            }
            clearance[link.Id]=value;return value;
        }
        public bool Arc(Vector3 from,Vector3 to,Collider target,JumpMode mode)
        {
            if(!motor.CanReach(from,to,mode))return false;
            var capsule=motor.Capsule;
            int overlapCount=Physics.OverlapCapsuleNonAlloc(from+Vector3.up*(capsule.radius+.035f),from+Vector3.up*(capsule.height-capsule.radius+.035f),capsule.radius*.94f,startOverlaps,~0,QueryTriggerInteraction.Ignore);
            if(overlapCount==startOverlaps.Length)return false;
            for(int i=0;i<overlapCount;i++)if(startOverlaps[i].attachedRigidbody!=motor.Body)return false;
            float dy=to.y-from.y,dir=Mathf.Sign(to.x-from.x);
            if((mode==JumpMode.Short?motor.ShortJumpHeight:motor.LongJumpHeight)<dy+.12f)return false;
            // Enumerate genuine discrete control choices, never the Motor's larger
            // Legacy launch envelope. Predict the same velocity-change air drive.
            foreach(int launch in new[]{1,0,-1})
            foreach(int air in new[]{1,0,-1})
                if(PolicyArc(from,to,target,mode,dir*launch,dir*air,false) && PolicyArc(from,to,target,mode,dir*launch,dir*air,true))return true;
            return false;
        }
        public bool WalkApproachClear(float x)
        {
            var p=CurrentPlatform;if(p==null)return false;
            // GroundSpeed's .65m forward probe must remain on this support.
            if(x<p.Bounds.min.x+.66f || x>p.Bounds.max.x-.66f)return false;
            Vector3 from=motor.Feet,to=p.Surface(x);var c=motor.Capsule;
            Vector3 delta=to-from;
            int count=Physics.CapsuleCastNonAlloc(from+Vector3.up*(c.radius+.035f),from+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,delta.normalized,sweepHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(count==sweepHits.Length)return false;
            for(int i=0;i<count;i++)if(sweepHits[i].rigidbody!=motor.Body && sweepHits[i].collider!=motor.Support)return false;
            // Match the Motor's existing forward obstacle scan at the destination.
            foreach(float height in new[]{.33f,.9f,1.43f})
            {
                count=Physics.SphereCastNonAlloc(to+Vector3.up*height,.12f,Vector3.right*Mathf.Sign(delta.x),sweepHits,.9f,~0,QueryTriggerInteraction.Ignore);
                if(count==sweepHits.Length)return false;
                for(int i=0;i<count;i++)if(sweepHits[i].rigidbody!=motor.Body && sweepHits[i].collider!=motor.Support && Mathf.Abs(sweepHits[i].normal.x)>=.45f)return false;
            }
            return true;
        }
        public bool WalkToClear(Vector3 to)
        {
            Vector3 from=motor.Feet,delta=to-from;var c=motor.Capsule;
            int count=Physics.OverlapCapsuleNonAlloc(from+Vector3.up*(c.radius+.035f),from+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,startOverlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==startOverlaps.Length)return false;
            for(int i=0;i<count;i++)if(startOverlaps[i].attachedRigidbody!=motor.Body)return false;
            count=Physics.CapsuleCastNonAlloc(from+Vector3.up*(c.radius+.035f),from+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,delta.normalized,sweepHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(count==sweepHits.Length)return false;
            for(int i=0;i<count;i++)if(sweepHits[i].rigidbody!=motor.Body)return false;
            int steps=Mathf.CeilToInt(delta.magnitude/.15f);
            for(int i=0;i<=steps;i++)
            {
                Vector3 p=Vector3.Lerp(from,to,steps==0?0:(float)i/steps);
                if(!Physics.Raycast(p+Vector3.up*.2f,Vector3.down,out var hit,.45f,~0,QueryTriggerInteraction.Ignore) || hit.rigidbody==motor.Body || hit.normal.y<.7f)return false;
            }
            return true;
        }
        // Feasibility for the model's actual launch input, not another launch
        // direction found by Arc(). Future discrete air control remains learned.
        public bool ArcForInput(Vector3 from,Vector3 to,Collider target,JumpMode mode,int horizontal,float minimumX=float.NegativeInfinity,float maximumX=float.PositiveInfinity)
        {
            if(!target || !motor.CanReach(from,to,mode))return false;
            var c=motor.Capsule;
            if((mode==JumpMode.Short?motor.ShortJumpHeight:motor.LongJumpHeight)<to.y-from.y+.12f)return false;
            int count=Physics.OverlapCapsuleNonAlloc(from+Vector3.up*(c.radius+.035f),from+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,startOverlaps,~0,QueryTriggerInteraction.Ignore);
            if(count==startOverlaps.Length)return false;
            for(int i=0;i<count;i++)if(startOverlaps[i].attachedRigidbody!=motor.Body)return false;
            for(int air=-1;air<=1;air++)
                if(PolicyArc(from,to,target,mode,horizontal,air,false,minimumX,maximumX) && PolicyArc(from,to,target,mode,horizontal,air,true,minimumX,maximumX))return true;
            return false;
        }
        bool PolicyArc(Vector3 start,Vector3 landing,Collider target,JumpMode mode,float launch,float air,bool sweep,float minimumX=float.NegativeInfinity,float maximumX=float.PositiveInfinity)
        {
            float dt=Time.fixedDeltaTime,vx=launch*motor.MaxJumpHorizontal*.65f,vy=motor.JumpVelocity(mode);
            Vector3 feet=start;var capsule=motor.Capsule;
            for(int n=0;n<150;n++)
            {
                if(n>0)vx=Mathf.MoveTowards(vx,air*motor.MoveSpeed,motor.AirAcceleration*dt);
                vy+=Physics.gravity.y*dt;
                Vector3 next=feet+new Vector3(vx,vy,0)*dt;
                bool arrived=vy<0 && next.y<=landing.y;
                if(arrived)next=Vector3.Lerp(feet,next,Mathf.InverseLerp(feet.y,next.y,landing.y));
                if(sweep)
                {
                    Vector3 delta=next-feet;
                    int count=Physics.CapsuleCastNonAlloc(feet+Vector3.up*(capsule.radius+.035f),feet+Vector3.up*(capsule.height-capsule.radius+.035f),capsule.radius*.94f,delta.normalized,sweepHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                    if(count==sweepHits.Length)return false;
                    for(int h=0;h<count;h++)
                    {
                        var hit=sweepHits[h];if(hit.rigidbody==motor.Body)continue;
                        if(hit.normal.y>.5f && (vy<0 && hit.collider==target || vy>=0 && n<2))continue;
                        LastBlock=$"{mode} {start}->{landing}: obstruction {hit.collider.name} at {hit.point}";return false;
                    }
                }
                feet=next;
                if(arrived)return feet.x>=Mathf.Max(minimumX,target.bounds.min.x+capsule.radius+.08f) && feet.x<=Mathf.Min(maximumX,target.bounds.max.x-capsule.radius-.08f);
            }
            return false;
        }
        public bool ShortSufficient
        {
            get
            {
                if(!TargetCollider || !motor.Grounded)return false;
                var current=graph.Support(motor.Support);
                if(current==null || Target.y-current.Bounds.max.y>motor.ShortJumpHeight-.12f)return false;
                // Ask whether this obstacle can use SHORT after approaching on the
                // current support; an unnecessarily early LONG is still optional.
                float dir=Mathf.Sign(Target.x-current.Bounds.center.x);
                float edge=dir>0?TargetCollider.bounds.min.x:TargetCollider.bounds.max.x;
                for(int i=0;i<11;i++)
                {
                    float x=edge-dir*(.45f+i*.3f);
                    if(x<current.Bounds.min.x+.12f || x>current.Bounds.max.x-.12f)continue;
                    if(Arc(current.Surface(x),Target,TargetCollider,JumpMode.Short))return true;
                }
                return false;
            }
        }
    }
}
