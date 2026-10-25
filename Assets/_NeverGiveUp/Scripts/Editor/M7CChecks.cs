using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    // Inspection HUD only in the separate development validation build; saved
    // scenes and normal player builds retain their original settings.
    public sealed class M7CBuildDiagnostics:IProcessSceneWithReport
    {
        public int callbackOrder=>0;
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            if(report==null || !report.summary.outputPath.Replace('\\','/').Contains("Builds/M7C/"))return;
            foreach(var root in scene.GetRootGameObjects())foreach(var hand in root.GetComponentsInChildren<HandInteraction>(true))
            {
                var so=new SerializedObject(hand);so.FindProperty("showSlapDebug").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
    public static class M7CChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static int failed;
        static string path;
        static bool background;
        public static void CheckNoLegacy()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required");
            var host=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>();
            var stage=UnityEngine.Object.FindAnyObjectByType<StageController>();
            var model=(Unity.InferenceEngine.ModelAsset)typeof(GameplayPolicyHost).GetField("model",Private).GetValue(host);
            bool rejected=!host.UseLearned(stage,null);
            bool stopped=host.Mode==GameplayPolicyHost.ControlMode.Stopped && !host.GetComponent<CharacterBrain>().enabled && !host.GetComponent<NeverGiveUp.Training.ClimbAgent>().enabled;
            bool restored=host.UseLearned(stage,model) && host.Mode==GameplayPolicyHost.ControlMode.Learned && !host.GetComponent<CharacterBrain>().enabled;
            Directory.CreateDirectory("Logs/M7C");
            File.WriteAllText("Logs/M7C/no-legacy.txt",$"{(rejected && stopped?"PASS":"FAIL")} missing model stops both AI controllers, never enables Legacy\n{(restored?"PASS":"FAIL")} valid model resumes Learned exclusively\n");
        }
        public static void Run()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required");
            Directory.CreateDirectory("Logs/M7C");path="Logs/M7C/checks.txt";File.WriteAllText(path,"");failed=0;
            background=Application.runInBackground;Application.runInBackground=true;
            PalmSlapChecks.Run();
            Stroke("center",-2,0,0,true);
            Stroke("late release",-1,3,0,true);
            Stroke("early release",-3,-.95f,0,true);
            Stroke("right to left",2,0,0,true);
            Stroke("distant miss",-2,0,3,false);
            Stroke("stationary click",0,0,0,false);
            Stroke("stopped after crossing",-2,0,0,false,true);
            Stroke("reverse away before release",-3,-.8f,0,false,false,true);
            var go=new GameObject("M7C isolated reaction test");go.transform.position=new Vector3(-220,1,0);
            go.AddComponent<M7CReactionProbe>();
        }
        public static void Check(bool condition,string name)
        {if(!condition)failed++;File.AppendAllText(path,(condition?"PASS ":"FAIL ")+name+"\n");}
        public static void Finish(){File.AppendAllText(path,"Failures="+failed+"\n");Application.runInBackground=background;}
        static void Stroke(string name,float from,float to,float y,bool expect,bool stop=false,bool reverse=false)
        {
            var target=new GameObject("M7C palm target");target.transform.position=new Vector3(-200,1,0);
            var capsule=target.AddComponent<CapsuleCollider>();capsule.height=1.9f;capsule.radius=.32f;
            var recovery=target.AddComponent<CharacterRecovery>();
            // Compound colliders must still receive only one impulse per release.
            var duplicate=target.AddComponent<SphereCollider>();duplicate.radius=.35f;
            var cameraGO=new GameObject("M7C test camera");var camera=cameraGO.AddComponent<Camera>();camera.enabled=false;
            camera.transform.position=new Vector3(-200,1,-20);camera.orthographic=true;camera.orthographicSize=5;
            var handGO=new GameObject("M7C input test");var drag=handGO.AddComponent<PhysicsObjectInteraction>();
            var hand=handGO.AddComponent<HandInteraction>();hand.Configure(camera,drag);hand.enabled=false;
            var sampler=(HandDragSampler)typeof(HandInteraction).GetField("sampler",Private).GetValue(hand);
            float now=Time.unscaledTime;
            for(int i=0;i<=24;i++)
            {
                float phase=i/24f;
                float x=reverse?(phase<.5f?Mathf.Lerp(-3,-2,phase*2):phase<.8f?Mathf.Lerp(-2,-.7f,(phase-.5f)/.3f):Mathf.Lerp(-.7f,-.8f,(phase-.8f)/.2f)):Mathf.Lerp(from,to,phase);
                Vector3 world=new Vector3(-200+x,1+y,0);
                Vector3 screen=camera.WorldToScreenPoint(world);
                sampler.Add(((Vector2)screen-camera.pixelRect.position)/camera.pixelHeight,now-.2f+i/120f);
            }
            if(stop)for(int i=1;i<=30;i++)sampler.Add(sampler.Latest,now+i/120f);
            Physics.SyncTransforms();
            typeof(HandInteraction).GetMethod("ReleaseSlap",Private).Invoke(hand,new object[]{stop?now+.25f:now});
            Check(hand.SlapCount==(expect?1:0),name+" count="+hand.SlapCount);
            UnityEngine.Object.DestroyImmediate(handGO);UnityEngine.Object.DestroyImmediate(cameraGO);UnityEngine.Object.DestroyImmediate(target);
        }
    }
    public sealed class M7CReactionProbe:MonoBehaviour
    {
        System.Collections.IEnumerator Start()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(-220,-.25f,0);floor.transform.localScale=new Vector3(8,.5f,3);
            var motor=gameObject.AddComponent<CharacterMotor>();var recovery=gameObject.AddComponent<CharacterRecovery>();
            yield return new WaitForSeconds(.3f);motor.SampleGround();
            var contact=new ObstacleContact{Normal=Vector3.left,Approach=1,Speed=2};
            recovery.ReactToObstacle(contact,.4f,.5f);var impulse=recovery.ObstacleImpulse;
            recovery.ReactToObstacle(contact,.4f,.5f);
            M7CChecks.Check(recovery.ObstacleImpulse==impulse,"same-frame contact does not duplicate reaction");
            bool seated=false;
            for(int i=0;i<30;i++)
            {
                yield return new WaitForFixedUpdate();motor.SampleGround();recovery.Tick(Time.fixedDeltaTime);
                seated|=recovery.ObstacleState==CharacterState.SittingDown || recovery.ObstacleState==CharacterState.Resting || recovery.ObstacleState==CharacterState.GettingUpFromSit;
            }
            M7CChecks.Check(!seated,"ordinary contact never seats");
            M7CChecks.Check(!recovery.ObstacleReacting,"ordinary reaction completes within .6s");
            motor.VisualFacingAlignment=.5f;motor.Drive(motor.MoveSpeed);
            M7CChecks.Check(Mathf.Abs(motor.DesiredVelocity)<.001f,"walking waits for completed body turn");
            M7CChecks.Check(!motor.FacingReady(1),"jump readiness rejects incomplete visual turn");
            yield return new WaitForSeconds(.25f);
            M7CChecks.Check(!motor.FacingReady(1),"delayed renderer report cannot bypass turn gate");
            motor.RecoveryCrawl(-.35f,.15f);
            M7CChecks.Check(motor.DesiredVelocity<0,"physical recovery crawl does not deadlock on turn gate");
            motor.VisualFacingAlignment=1;
            M7CChecks.Check(!motor.FacingReady(-1),"jump readiness rejects opposite intent before facing changes");
            motor.Drive(motor.MoveSpeed);
            M7CChecks.Check(motor.DesiredVelocity>0,"walking resumes after turn completes");
            motor.Jump(0,motor.JumpVelocity(JumpMode.Long));
            float facing=motor.Facing;
            for(int i=0;i<20;i++)
            {yield return new WaitForFixedUpdate();motor.Drive(-motor.MoveSpeed);}
            M7CChecks.Check(motor.Facing==facing,"air steering preserves takeoff facing");
            M7CChecks.Finish();Destroy(floor);Destroy(gameObject);
        }
    }
}
