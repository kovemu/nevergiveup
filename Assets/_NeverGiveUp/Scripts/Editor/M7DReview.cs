using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    public static class M7DReview
    {
        static bool nextCorrection;
        static bool nextRetryFixture;
        static bool nextCube;
        static PhysicsObjectInteraction heldDrag;
        static DraggableBody heldCube;
        static HandInteraction heldHand;
        static Vector3 cubeHome,cubeTarget;
        static float cubeReleaseAt;
        static float cubeLogAt;
        static Vector3 cubeDestination;
        static int cubeTravelPhase;
        public static void RouteCube(bool clear)
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
            if(clear){cubeDestination=cubeHome;cubeTarget=new Vector3(heldCube.Body.position.x,8,0);cubeTravelPhase=0;cubeReleaseAt=Time.time+20;return;}
            heldCube=UnityEngine.Object.FindAnyObjectByType<DraggableBody>();heldDrag=UnityEngine.Object.FindAnyObjectByType<PhysicsObjectInteraction>();heldHand=UnityEngine.Object.FindAnyObjectByType<HandInteraction>();
            var stage=UnityEngine.Object.FindAnyObjectByType<StageController>();Collider ledge=null;
            foreach(var c in stage.GetComponentsInChildren<Collider>())if(c.name=="Climb ledge 4")ledge=c;
            if(!heldCube || !heldDrag || !ledge)throw new Exception("ClimbPlaytest cube and fourth ledge required");
            cubeHome=heldCube.Body.position;cubeTarget=new Vector3(ledge.bounds.min.x+.6f,ledge.bounds.max.y+.55f,0);cubeReleaseAt=-1;
            cubeDestination=cubeTarget;cubeTravelPhase=0;cubeTarget=new Vector3(cubeHome.x,8,0);
            heldDrag.Grab(heldCube,cubeHome);if(heldHand)heldHand.enabled=false;
            Directory.CreateDirectory("Logs/M7D");File.WriteAllText("Logs/M7D/held-cube.csv","time,x,y,targetX,targetY,held\n");cubeLogAt=0;
            EditorApplication.update-=CubeTick;EditorApplication.update+=CubeTick;
        }
        static void CubeTick()
        {
            if(!EditorApplication.isPlaying || !heldCube || !heldDrag){EditorApplication.update-=CubeTick;return;}
            // This is a deterministic drag-API test. Application focus changes
            // release native drags, so explicitly reacquire for this fixture.
            if(!heldDrag.IsDragging)heldDrag.Grab(heldCube,heldCube.Body.position);
            // Carry above the actor before crossing the course, so a distant
            // obstruction test does not accidentally slap the actor on the way.
            if(cubeTravelPhase<2 && Vector3.Distance(heldCube.Body.position,cubeTarget)<.3f)
            {cubeTravelPhase++;cubeTarget=cubeTravelPhase==1?new Vector3(cubeDestination.x,8,0):cubeDestination;}
            heldDrag.SetTarget(cubeTarget);
            if(Time.time>=cubeLogAt)
            {cubeLogAt=Time.time+.2f;File.AppendAllText("Logs/M7D/held-cube.csv",FormattableString.Invariant($"{Time.time:F3},{heldCube.Body.position.x:F3},{heldCube.Body.position.y:F3},{cubeTarget.x:F3},{cubeTarget.y:F3},{heldCube.Held}\n"));}
            if(cubeReleaseAt>=0 && (Time.time>=cubeReleaseAt || cubeTravelPhase==2 && Vector3.Distance(heldCube.Body.position,cubeDestination)<.3f))
            {heldDrag.Release();if(heldHand)heldHand.enabled=true;EditorApplication.update-=CubeTick;}
        }
        static GameplayPolicyHost host;
        static string path;
        static float sampleAt;
        static GameObject retryFixture;
        public static void RetryFixture(bool clear)
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
            if(retryFixture)UnityEngine.Object.Destroy(retryFixture);
            if(clear)return;
            var m=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>().GetComponent<CharacterMotor>();
            retryFixture=new GameObject("M7D temporary retry fixture");
            var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.transform.SetParent(retryFixture.transform);
            ceiling.transform.position=m.Feet+new Vector3(.3f,2.2f,0);ceiling.transform.localScale=new Vector3(5,.2f,3);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(retryFixture.transform);
            wall.transform.position=m.Feet+new Vector3(1.15f,1,0);wall.transform.localScale=new Vector3(.3f,2,3);
        }
        public static void Play(bool correction,bool retry=false,bool cube=false)
        {
            nextCorrection=correction;nextRetryFixture=retry;nextCube=cube;
            EditorApplication.playModeStateChanged-=Entered;EditorApplication.playModeStateChanged+=Entered;
            EditorApplication.isPlaying=true;
        }
        static void Entered(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode)return;
            EditorApplication.playModeStateChanged-=Entered;
            host=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>();
            var so=new SerializedObject(host);so.FindProperty("correctPolicyActions").boolValue=nextCorrection;so.ApplyModifiedPropertiesWithoutUndo();
            if(nextRetryFixture)RetryFixture(false);
            if(nextCube)RouteCube(false);
            Application.runInBackground=true;
            Directory.CreateDirectory("Logs/M7D");path="Logs/M7D/play-"+(nextCorrection?"on-":"off-")+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".csv";
            File.WriteAllText(path,"time,mode,x,y,grounded,state,action,horizontal,jump,pending,shorts,longs,walkSeconds,walkDistance,runSeconds,prepareSeconds,blockedChecks,downgraded,repeated,reason,alignment,unnecessaryChecks,completed,support,targetX,targetY,failedAttempts,probeAttempts,rests,blockedRouteIntent\n");
            sampleAt=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || !host){EditorApplication.update-=Tick;return;}
            if(Time.time<sampleAt)return;sampleAt=Time.time+.05f;
            var a=host.GetComponent<ClimbAgent>();if(!a || !a.Motor || a.Correction==null)return;
            var m=a.Motor;var v=host.GetComponentInChildren<ClayAnimator>();
            File.AppendAllText(path,FormattableString.Invariant($"{Time.time:F3},{host.Mode},{m.Feet.x:F3},{m.Feet.y:F3},{m.Grounded},{a.DisplayState},{v?.CurrentAction},{a.HorizontalDecision},{a.JumpDecision},{a.PendingJump},{a.ShortJumps},{a.LongJumps},{a.WalkSeconds:F3},{a.WalkDistance:F3},{a.RunSeconds:F3},{a.PrepareSeconds:F3},{a.Correction.Blocked},{a.Correction.Downgraded},{a.Correction.Repeated},{a.Correction.Reason},{m.VisualFacingAlignment:F4},{a.Correction.UnnecessaryBlocked},{host.ReactionState==CharacterState.GoalReached},{m.Support?.name},{host.Goal.x:F3},{host.Goal.y:F3},{a.Correction.FailedAttempts},{a.Correction.ProbeAttempts},{host.VoluntaryRestCount},{host.Route.UsingBlockedRouteIntent}\n"));
        }
        public static void Build(bool training)
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode first");
            Directory.CreateDirectory("Builds/M7D");Directory.CreateDirectory("Logs/M7D");
            var name=training?"Evaluation":"NeverGiveUp";
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{training?VerticalBuilder.Path:ClimbPlaytestBuilder.Path},locationPathName="Builds/M7D/"+name+".exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Logs/M7D/build-"+name+".txt",report.summary.result+" errors="+report.summary.totalErrors);
        }
        public static void Checks()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
            Application.runInBackground=true;
            var go=new GameObject("M7D isolated fixture");go.transform.position=new Vector3(-504,1,0);go.AddComponent<M7DProbe>();
        }
        public static void Diagnose()
        {
            var h=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>();var m=h.GetComponent<CharacterMotor>();var r=h.Route;
            var p=r.CurrentPlatform;var s=new System.Text.StringBuilder();
            s.AppendLine($"feet={m.Feet} support={m.Support?.name} bounds={p?.Bounds} target={r.Target} collider={r.TargetCollider?.name} grounded={m.Grounded}");
            s.AppendLine($"suppressed={m.ControlSuppressed} velocity={m.Body.linearVelocity} desired={m.DesiredVelocity} clear={m.IsClear(m.Body.position+Vector3.up*.04f)}");
            foreach(int direction in new[]{-1,1})s.AppendLine($"direction={direction} ground={m.GroundAhead(direction)} obstacle={m.ObstacleAhead(direction,out var hit)} collider={hit.collider?.name} normal={hit.normal} speed={h.GetComponent<ClimbAgent>().Correction.GroundSpeed(r,direction)} masked={h.GetComponent<ClimbAgent>().Correction.BlocksGroundInput(r,direction)}");
            if(p!=null && r.TargetCollider)foreach(var mode in new[]{JumpMode.Short,JumpMode.Long})for(int input=-1;input<=1;input++)
                for(float x=p.Bounds.min.x+.12f;x<=p.Bounds.max.x-.12f;x+=.1f)
                    if(r.ArcForInput(p.Surface(x),r.Target,r.TargetCollider,mode,input))s.AppendLine($"{mode} input={input} x={x:F3} leftMargin={x-p.Bounds.min.x:F3} rightMargin={p.Bounds.max.x-x:F3}");
            Directory.CreateDirectory("Logs/M7D");File.WriteAllText("Logs/M7D/geometry.txt",s.ToString());
        }
    }
    public sealed class M7DProbe:MonoBehaviour
    {
        string path="Logs/M7D/checks.txt";int failed;
        void Check(bool ok,string detail){if(!ok)failed++;File.AppendAllText(path,(ok?"PASS ":"FAIL ")+detail+"\n");}
        System.Collections.IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/M7D");File.WriteAllText(path,"");
            var terrain=new GameObject("M7D fixture terrain");
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(terrain.transform);floor.transform.position=new Vector3(-500,-.25f,0);floor.transform.localScale=new Vector3(18,.5f,3);
            var cap=gameObject.AddComponent<CapsuleCollider>();cap.height=1.9f;cap.radius=.32f;
            var motor=gameObject.AddComponent<CharacterMotor>();VerticalBuilder.Profile(motor);
            var graph=terrain.AddComponent<PlatformGraph>();
            yield return new WaitForSeconds(.3f);motor.SampleGround();graph.Initialize(terrain.transform,motor);
            var route=new PolicyRoute(graph,motor);route.Refresh(new Vector3(-496,0,0),true);
            var correction=new PolicyActionCorrection(motor);
            Check(route.WalkableTarget,"same floor recognized as walkable");
            Check(!correction.Request(route,1,JumpMode.Long,out _),"flat LONG request rejected");
            float start=motor.Feet.x;
            for(int i=0;i<60;i++){motor.SampleGround();motor.VisualFacingAlignment=1;motor.Drive(correction.GroundSpeed(route,1));yield return new WaitForFixedUpdate();}
            Check(motor.Feet.x-start>1.5f,"flat physical walking distance="+(motor.Feet.x-start));
            foreach(float rise in new[]{.5f,1.4f})
            {
                motor.Teleport(new Vector3(-504,.97f,0));
                var ledge=GameObject.CreatePrimitive(PrimitiveType.Cube);ledge.transform.SetParent(terrain.transform);ledge.transform.position=new Vector3(-500,rise-.12f,0);ledge.transform.localScale=new Vector3(2.65f,.24f,3);
                yield return new WaitForSeconds(.3f);motor.SampleGround();graph.Initialize(terrain.transform,motor);
                route=new PolicyRoute(graph,motor);route.Refresh(new Vector3(-500,rise,0),true);correction=new PolicyActionCorrection(motor);
                if(rise<.8f)Check(!correction.Request(route,1,JumpMode.Long,out _) && correction.State==PolicyActionCorrection.Phase.Approaching,"SHORT elsewhere requires approach before launch");
                bool jumped=false,landed=false;JumpMode selected=JumpMode.Long;float launchX=0;
                for(int i=0;i<220;i++)
                {
                    motor.SampleGround();motor.VisualFacingAlignment=1;
                    if(!jumped && motor.Grounded && correction.Request(route,1,JumpMode.Long,out selected))
                    {jumped=true;launchX=motor.Feet.x;motor.Jump(motor.MaxJumpHorizontal*.65f,motor.JumpVelocity(selected));}
                    else if(!jumped)motor.Drive(correction.GroundSpeed(route,1));
                    else if(motor.Grounded && motor.Body.linearVelocity.y<=.1f){landed=motor.Support==ledge.GetComponent<Collider>();break;}
                    else motor.Drive(motor.MoveSpeed);
                    yield return new WaitForFixedUpdate();
                }
                Check(jumped && selected==(rise<.8f?JumpMode.Short:JumpMode.Long),"rise="+rise+" selected="+selected+" launched="+jumped+" x="+launchX);
                Check(landed,"rise="+rise+" actual target landing");
                ledge.SetActive(false);Destroy(ledge);
            }
            // A grounded edge landing may already be a valid launch even though
            // walking farther toward that edge is forbidden.
            var edgeLedge=GameObject.CreatePrimitive(PrimitiveType.Cube);edgeLedge.transform.SetParent(terrain.transform);
            edgeLedge.transform.position=new Vector3(-489.3f,1.28f,0);edgeLedge.transform.localScale=new Vector3(2.65f,.24f,3);
            motor.Teleport(new Vector3(-491.18f,.97f,0));yield return new WaitForSeconds(.3f);motor.SampleGround();
            graph.Initialize(terrain.transform,motor);route=new PolicyRoute(graph,motor);route.Refresh(new Vector3(-489.3f,1.4f,0),true);
            correction=new PolicyActionCorrection(motor);
            Check(!route.WalkApproachClear(-491.18f),"edge position is not a walking approach candidate");
            bool edgeArc=route.ArcForInput(motor.Feet,route.Target,route.TargetCollider,JumpMode.Long,0);
            Check(edgeArc && correction.Request(route,0,JumpMode.Long,out var edgeMode) && edgeMode==JumpMode.Long,"supported edge current-pose LONG remains permitted");
            // Deliberately withhold the impulse: exercise observed failed launch
            // bookkeeping on a real grounded motor, independently of PPO luck.
            for(int attempt=0;attempt<2;attempt++)
            {
                correction.Launched(route,JumpMode.Long,false);
                yield return new WaitForSeconds(.3f);motor.SampleGround();correction.Observe();
                yield return new WaitForSeconds(.2f);motor.SampleGround();correction.Observe();
            }
            Check(correction.Repeated==1 && !correction.Request(route,0,JumpMode.Long,out _),"two same-place failed launches temporarily exclude that launch");
            yield return new WaitForSeconds(4.1f);
            Check(correction.Request(route,0,JumpMode.Long,out _),"failed launch is eligible again after bounded wait");
            correction=new PolicyActionCorrection(motor);correction.Request(route,0,JumpMode.Long,out _);
            correction.Launched(route,JumpMode.Long,false);motor.NotifyExternal("M7D test player impulse");correction.Observe();
            yield return new WaitForSeconds(.5f);correction.Observe();correction.Launched(route,JumpMode.Long,false);
            Check(correction.Repeated==0,"external intervention does not count as an autonomous failed attempt");
            edgeLedge.SetActive(false);Destroy(edgeLedge);
            // A box on the approach to a distant ledge needs a local floor hop.
            motor.Teleport(new Vector3(-504,.97f,0));
            var far=GameObject.CreatePrimitive(PrimitiveType.Cube);far.transform.SetParent(terrain.transform);far.transform.position=new Vector3(-495,.33f,0);far.transform.localScale=new Vector3(2.65f,.24f,3);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=new Vector3(-501.8f,.55f,0);blocker.transform.localScale=new Vector3(1.2f,1.1f,1.2f);
            yield return new WaitForSeconds(.3f);motor.SampleGround();graph.Initialize(terrain.transform,motor);
            route=new PolicyRoute(graph,motor);route.Refresh(new Vector3(-495,.45f,0),true);correction=new PolicyActionCorrection(motor);
            bool hopped=false,cleared=false;float hopX=0;JumpMode hopMode=JumpMode.Long;
            for(int i=0;i<240;i++)
            {
                motor.SampleGround();motor.VisualFacingAlignment=1;
                if(!hopped && motor.Grounded && correction.Request(route,1,JumpMode.Long,out var hop))
                {hopped=true;hopX=motor.Feet.x;hopMode=hop;motor.Jump(motor.MaxJumpHorizontal*.65f,motor.JumpVelocity(hop));}
                else if(!hopped)motor.Drive(correction.GroundSpeed(route,1));
                else if(motor.Grounded && motor.Body.linearVelocity.y<=.1f){cleared=motor.Feet.x>blocker.GetComponent<Collider>().bounds.max.x+motor.Capsule.radius;break;}
                else motor.Drive(motor.MoveSpeed);
                yield return new WaitForFixedUpdate();
            }
            Check(hopped && cleared,"local obstacle hop before distant next ledge; launch="+hopX+" mode="+hopMode+" landed="+motor.Feet+" support="+motor.Support?.name);
            motor.Teleport(new Vector3(-500.65f,.97f,0));yield return new WaitForSeconds(.3f);motor.SampleGround();
            Check(correction.BlocksGroundInput(route,-1) && !correction.BlocksGroundInput(route,1),"too-close blocker input is masked while safe retreat remains selectable");
            far.SetActive(false);blocker.SetActive(false);Destroy(far);Destroy(blocker);
            motor.Teleport(new Vector3(-504,.97f,0));yield return new WaitForSeconds(.3f);motor.SampleGround();
            var impossible=GameObject.CreatePrimitive(PrimitiveType.Cube);impossible.transform.SetParent(terrain.transform);impossible.transform.position=new Vector3(-500,3.5f,0);impossible.transform.localScale=new Vector3(2.65f,.24f,3);
            Physics.SyncTransforms();graph.Initialize(terrain.transform,motor);route=new PolicyRoute(graph,motor);route.Refresh(new Vector3(-500,3.62f,0),true);
            correction=new PolicyActionCorrection(motor);Check(!correction.Request(route,1,JumpMode.Long,out _),"unreachable target does not schedule jump");
            route.KeepApproachingBlockedRoute=true;route.Refresh(new Vector3(-500,3.62f,0),true);
            Check(route.HasRoute && route.UsingBlockedRouteIntent && !route.WalkableTarget && correction.GroundSpeed(route,1)>0 && !correction.Request(route,1,JumpMode.Long,out _),"distant blocked route retains walking intent without a premature jump");
            var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.transform.SetParent(terrain.transform);ceiling.transform.position=new Vector3(-504,1.7f,0);ceiling.transform.localScale=new Vector3(1,.2f,3);Physics.SyncTransforms();
            Check(!route.ArcForInput(motor.Feet,new Vector3(-500,.5f,0),impossible.GetComponent<Collider>(),JumpMode.Long,1),"capsule overlap blocks takeoff");
            ceiling.SetActive(false);impossible.SetActive(false);
            motor.Teleport(new Vector3(-504,.97f,0));yield return new WaitForSeconds(.3f);motor.SampleGround();
            var heldBox=GameObject.CreatePrimitive(PrimitiveType.Cube);heldBox.transform.position=new Vector3(-502.9f,.8f,0);heldBox.transform.localScale=new Vector3(1,1.6f,2);
            var boxBody=heldBox.AddComponent<Rigidbody>();boxBody.isKinematic=true;
            var draggable=heldBox.AddComponent<DraggableBody>();draggable.Held=true;
            int version=motor.ExternalVersion;
            for(int i=0;i<50;i++){motor.SampleGround();motor.VisualFacingAlignment=1;motor.Drive(motor.MoveSpeed);yield return new WaitForFixedUpdate();}
            Check(motor.ExternalVersion==version && motor.Feet.x>-504,"self-contact with stationary held box preserves autonomous retry history");
            motor.Stop();boxBody.isKinematic=false;boxBody.useGravity=false;boxBody.linearVelocity=Vector3.left*3;
            for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();
            Check(motor.ExternalVersion>version,"moving held box still registers genuine external intervention");
            boxBody.isKinematic=true;boxBody.position=new Vector3(-504,1,0);heldBox.transform.localScale=new Vector3(3,.5f,2);Physics.SyncTransforms();
            motor.Teleport(new Vector3(-504,2.22f,0));yield return new WaitForSeconds(.4f);motor.SampleGround();version=motor.ExternalVersion;
            yield return new WaitForSeconds(.2f);
            Check(motor.Support==heldBox.GetComponent<Collider>() && motor.ExternalVersion==version,"stationary held support does not reset retries");
            boxBody.isKinematic=false;boxBody.useGravity=false;boxBody.linearVelocity=Vector3.right*2;
            for(int i=0;i<10;i++){motor.SampleGround();yield return new WaitForFixedUpdate();}
            Check(motor.ExternalVersion>version,"sideways moving support remains external intervention");
            Destroy(heldBox);
            File.AppendAllText(path,"Failures="+failed+"\n");Destroy(terrain);Destroy(gameObject);
        }
    }
}
