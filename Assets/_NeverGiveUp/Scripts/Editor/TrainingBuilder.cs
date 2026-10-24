using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using Unity.InferenceEngine;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    public static class TrainingBuilder
    {
        const string ScenePath="Assets/_NeverGiveUp/Scenes/AITraining.unity";
        [MenuItem("NeverGiveUp/AI Training/Use trained local model %#F2")]
        public static void UseModel()
        {
            if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=ScenePath)return;
            var model=AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/_NeverGiveUp/AI/Models/NGUClimb.onnx");
            if(!model) {Debug.LogError("No trained model found; no scene changes made.");return;}
            foreach(var behavior in Object.FindObjectsByType<BehaviorParameters>(FindObjectsSortMode.None))
            {
                if(behavior.gameObject.scene.path!=ScenePath)continue;
                behavior.Model=model;behavior.BehaviorType=BehaviorType.InferenceOnly;
                behavior.InferenceDevice=InferenceDevice.Burst;
                behavior.DeterministicInference=true;
                EditorUtility.SetDirty(behavior);
            }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("AITraining now uses the trained local model, with no Python connection.");
        }
        [MenuItem("NeverGiveUp/AI Training/Open training scene %#F3")]
        public static void Open()
        {
            if(EditorApplication.isPlaying)return;
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty) {Debug.LogWarning("Unsaved scene preserved; open canceled.");return;}
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("NeverGiveUp/AI Training/Create independent training scene %#F5")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)return;
            if(File.Exists(ScenePath)) {Debug.LogWarning("AITraining already exists; preserved.");return;}
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var visual=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Art/Characters/M6/BlueClay_M6_Visual.prefab");
            for(int i=0;i<16;i++)
            {
                var root=new GameObject("Training arena "+i);
                root.transform.position=new Vector3(i*50,0,0);
                var arena=root.AddComponent<TrainingArena>();arena.index=i;
                var actor=new GameObject("Learning character");actor.SetActive(false);
                actor.transform.SetParent(root.transform);actor.transform.localPosition=Vector3.up;
                var capsule=actor.AddComponent<CapsuleCollider>();capsule.height=1.9f;capsule.radius=.32f;
                var body=actor.AddComponent<Rigidbody>();body.mass=1;
                actor.AddComponent<CharacterMotor>();
                var behavior=actor.AddComponent<BehaviorParameters>();
                behavior.BehaviorName="NGUClimb";
                behavior.BrainParameters.VectorObservationSize=42;
                behavior.BrainParameters.NumStackedVectorObservations=1;
                behavior.BrainParameters.ActionSpec=ActionSpec.MakeDiscrete(3,3);
                behavior.BehaviorType=BehaviorType.Default;
                var agent=actor.AddComponent<ClimbAgent>();agent.arena=arena;
                var decisions=actor.AddComponent<DecisionRequester>();decisions.DecisionPeriod=5;decisions.TakeActionsBetweenDecisions=true;
                if(i==0 && visual)
                {
                    var model=(GameObject)PrefabUtility.InstantiatePrefab(visual,actor.transform);
                    var clay=model.GetComponent<ClayAnimator>();if(clay)Object.DestroyImmediate(clay);
                    var presentation=model.AddComponent<TrainingPresentation>();
                    presentation.agent=agent;presentation.animator=model.GetComponentInChildren<Animator>();
                }
                actor.SetActive(true);
            }
            var camera=new GameObject("Training camera").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,4,-18);camera.orthographic=true;camera.orthographicSize=7;
            camera.backgroundColor=new Color(.12f,.18f,.24f);camera.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Training light").AddComponent<Light>();light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(45,-30,0);
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorSceneManager.CloseScene(scene,true);
            SceneManager.SetActiveScene(original);
            AssetDatabase.SaveAssets();
            Debug.Log("Created independent AITraining with 16 arenas. Existing scenes unchanged.");
        }
        [MenuItem("NeverGiveUp/AI Training/Build training player %#F4")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)return;
            Directory.CreateDirectory("Builds/M7A");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ScenePath},locationPathName="Builds/M7A/NGUTraining.exe",
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            Directory.CreateDirectory("Logs/M7");
            File.WriteAllText("Logs/M7/training-build.txt",report.summary.result+" errors="+report.summary.totalErrors);
            Debug.Log("M7A build: "+report.summary.result);
        }
    }
}
