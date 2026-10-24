using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using Unity.InferenceEngine;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    public static class VerticalBuilder
    {
        public const string Path="Assets/_NeverGiveUp/Scenes/AIClimbTraining.unity";
        public static void CreateAndBuild(){if(Application.isBatchMode)EditorSceneManager.OpenScene("Assets/_NeverGiveUp/Scenes/AITraining.unity");Create();Build();}
        [MenuItem("NeverGiveUp/M7B/Create vertical training %&F5")]
        public static void Create()
        {
            if(EditorApplication.isPlaying || File.Exists(Path))return;
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var visual=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Art/Characters/M6/BlueClay_M6_Visual.prefab");
            for(int i=0;i<16;i++)
            {
                var root=new GameObject("Vertical arena "+i);root.transform.position=Vector3.right*i*80;
                var arena=root.AddComponent<VerticalArena>();arena.index=i;
                var actor=new GameObject("Learning character");actor.SetActive(false);actor.transform.SetParent(root.transform);actor.transform.localPosition=Vector3.up;
                var capsule=actor.AddComponent<CapsuleCollider>();capsule.height=1.9f;capsule.radius=.32f;
                actor.AddComponent<Rigidbody>();var motor=actor.AddComponent<CharacterMotor>();
                Profile(motor);
                var behavior=actor.AddComponent<BehaviorParameters>();behavior.BehaviorName="NGUClimb";
                behavior.BrainParameters.VectorObservationSize=42;behavior.BrainParameters.ActionSpec=ActionSpec.MakeDiscrete(3,3);
                behavior.BehaviorType=BehaviorType.Default;behavior.InferenceDevice=InferenceDevice.Burst;behavior.DeterministicInference=true;
                var agent=actor.AddComponent<ClimbAgent>();agent.vertical=arena;
                var decisions=actor.AddComponent<DecisionRequester>();decisions.DecisionPeriod=5;decisions.TakeActionsBetweenDecisions=true;
                if(i==0 && visual)
                {
                    var model=(GameObject)PrefabUtility.InstantiatePrefab(visual,actor.transform);
                    var clay=model.GetComponent<ClayAnimator>();if(clay)Object.DestroyImmediate(clay);
                    var presentation=model.AddComponent<TrainingPresentation>();presentation.agent=agent;presentation.animator=model.GetComponentInChildren<Animator>();
                }
                actor.SetActive(true);
            }
            var cam=new GameObject("Vertical camera").AddComponent<Camera>();cam.tag="MainCamera";
            cam.transform.position=new Vector3(4,8,-25);cam.orthographic=true;cam.orthographicSize=11;cam.backgroundColor=new Color(.12f,.18f,.24f);cam.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-30,0);
            EditorSceneManager.SaveScene(scene,Path);EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);AssetDatabase.SaveAssets();
        }
        public static void Profile(CharacterMotor motor)
        {
            var so=new SerializedObject(motor);so.FindProperty("shortJumpHeight").floatValue=.8f;so.FindProperty("longJumpHeight").floatValue=2;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        [MenuItem("NeverGiveUp/M7B/Open vertical training %&F7")]
        static void Open(){if(!EditorApplication.isPlaying && !SceneManager.GetActiveScene().isDirty)EditorSceneManager.OpenScene(Path);}
        [MenuItem("NeverGiveUp/M7B/Build vertical player %&F6")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)return;
            Directory.CreateDirectory("Builds/M7B");Directory.CreateDirectory("Logs/M7B");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Path},locationPathName="Builds/M7B/NGUVertical.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Logs/M7B/build.txt",report.summary.result+" errors="+report.summary.totalErrors);
        }
        [MenuItem("NeverGiveUp/M7B/Run physics probes %&F8")]
        static void Probe()
        {
            if(!EditorApplication.isPlaying)return;
            var go=new GameObject("M7B isolated physics probe");go.transform.position=new Vector3(-100,1,0);go.AddComponent<VerticalPhysicsProbe>();
        }
    }
    public sealed class VerticalPhysicsProbe:MonoBehaviour
    {
        System.Collections.IEnumerator Start()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(-100,-.25f,0);floor.transform.localScale=new Vector3(20,.5f,3);
            var capsule=gameObject.AddComponent<CapsuleCollider>();capsule.height=1.9f;capsule.radius=.32f;
            var m=gameObject.AddComponent<CharacterMotor>();VerticalBuilder.Profile(m);
            Directory.CreateDirectory("Logs/M7B");string path="Logs/M7B/physics.txt";File.WriteAllText(path,"");
            foreach(var mode in new[]{JumpMode.Short,JumpMode.Long})
            {
                m.Teleport(new Vector3(-100,.97f,0));yield return new WaitForSeconds(.3f);m.SampleGround();
                float start=m.Feet.y,peak=start;m.Jump(0,m.JumpVelocity(mode));
                for(int n=0;n<90;n++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,m.Feet.y);}
                float expected=mode==JumpMode.Short?.8f:2;
                string line=(Mathf.Abs(peak-start-expected)<.12f?"PASS ":"FAIL ")+mode+" actual apex="+(peak-start)+" expected="+expected;
                File.AppendAllText(path,line+"\n");Debug.Log(line);
            }
            Destroy(floor);yield return null;
            // Reproduce the playtest's narrow staggered ledge corner, then let the
            // body sleep. The center ground sphere misses this real support.
            var lower=GameObject.CreatePrimitive(PrimitiveType.Cube);
            lower.transform.position=new Vector3(-98.675f,-.12f,0);lower.transform.localScale=new Vector3(2.65f,.24f,3);
            var upper=GameObject.CreatePrimitive(PrimitiveType.Cube);
            upper.transform.position=new Vector3(-101.875f,.35f,0);upper.transform.localScale=new Vector3(2.65f,.24f,3);
            m.Teleport(new Vector3(-100.23f,.853f,0));yield return new WaitForSeconds(.5f);
            m.SampleGround();bool awakeSupport=m.Grounded;
            m.Body.Sleep();yield return new WaitForSeconds(.25f);m.SampleGround();
            string supportResult=(awakeSupport && m.Grounded?"PASS ":"FAIL ")+"corner contact support before/after sleep="+awakeSupport+"/"+m.Grounded;
            File.AppendAllText(path,supportResult+"\n");Debug.Log(supportResult);
            m.Jump(0,m.JumpVelocity(JumpMode.Short));m.SampleGround();
            string ignoreResult=(!m.Grounded?"PASS ":"FAIL ")+"jump ignores cached corner support";
            File.AppendAllText(path,ignoreResult+"\n");Debug.Log(ignoreResult);
            Destroy(lower);Destroy(upper);Destroy(gameObject);
        }
    }
}
