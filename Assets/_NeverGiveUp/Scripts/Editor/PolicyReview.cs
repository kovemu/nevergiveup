using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents.Sensors;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    // Opt-in Play Mode tools. Original stage assets are never saved by these checks.
    public static class PolicyReview
    {
        static GameplayPolicyHost host;
        static float nextSample;
        static string trace;
        static float recoveryCheckAt=-1;
        const string ModelPath="Assets/_NeverGiveUp/AI/Models/NGUClimb.onnx";
        [MenuItem("NeverGiveUp/AI Gameplay/Build Windows validation %&F4")]
        static void BuildGame()
        {
            if(EditorApplication.isPlaying)return;
            Directory.CreateDirectory("Builds/M7AGame");Directory.CreateDirectory("Logs/M7");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                locationPathName="Builds/M7AGame/NeverGiveUp.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Logs/M7/game-build.txt",report.summary.result+" errors="+report.summary.totalErrors+" warnings="+report.summary.totalWarnings);
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Open MainPrototype %&F3")]
        static void OpenMain()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)return;
            EditorSceneManager.OpenScene("Assets/_NeverGiveUp/Scenes/MainPrototype.unity");
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Install mode selector in current stage %&F2")]
        static void Install()
        {
            if(EditorApplication.isPlaying)return;
            var brain=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();var stage=UnityEngine.Object.FindAnyObjectByType<StageController>();
            if(!brain || !stage)return;
            var component=brain.GetComponent<GameplayPolicyHost>();if(!component)component=Undo.AddComponent<GameplayPolicyHost>(brain.gameObject);
            var so=new SerializedObject(component);so.FindProperty("stage").objectReferenceValue=stage;
            so.FindProperty("model").objectReferenceValue=AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);so.ApplyModifiedProperties();
            EditorSceneManager.SaveScene(brain.gameObject.scene);
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Open Prologue %#F10")]
        static void Open()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)return;
            EditorSceneManager.OpenScene("Assets/_NeverGiveUp/Scenes/Prologue.unity");
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Enable Learned and trace %#F11")]
        static void Learned()
        {
            if(!EditorApplication.isPlaying)return;
            var brain=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();
            var stage=UnityEngine.Object.FindAnyObjectByType<StageController>();
            if(!brain || !stage)return;
            // Runtime experiments must not overwrite the player's JSON experience.
            var so=new SerializedObject(brain);so.FindProperty("persistLearning").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
            host=brain.GetComponent<GameplayPolicyHost>();if(!host)host=brain.gameObject.AddComponent<GameplayPolicyHost>();
            Directory.CreateDirectory("Logs/M7");trace="Logs/M7/gameplay-"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt";
            File.WriteAllText(trace,"Actual Unity Play Mode; scene="+stage.gameObject.scene.name+"\n");
            bool ok=host.UseLearned(stage,AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath));
            Log("Learned activated="+ok);nextSample=0;
            EditorApplication.update-=Observe;EditorApplication.update+=Observe;
        }
        static void Log(string value){if(trace!=null)File.AppendAllText(trace,Time.time.ToString("F2")+" "+value+"\n");Debug.Log("[M7A check] "+value);}
        static float[] Values(VectorSensor sensor) => ((System.Collections.Generic.IEnumerable<float>)typeof(VectorSensor).GetMethod("GetObservations",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(sensor,null)).ToArray();
        static void Observe()
        {
            if(!EditorApplication.isPlaying || !host){EditorApplication.update-=Observe;return;}
            if(Time.time<nextSample)return;nextSample=Time.time+.5f;
            var m=host.GetComponent<CharacterMotor>();var a=host.GetComponent<ClimbAgent>();
            File.AppendAllText(trace,$"{Time.time:F2} mode={host.Mode} feet={m.Feet} velocity={m.Body.linearVelocity} grounded={m.Grounded} state={host.GetComponent<CharacterBrain>().State} age={Time.time-a.LastActionTime:F2} fallback={host.LastFallback}\n");
            if(recoveryCheckAt>0 && Time.time>=recoveryCheckAt)
            {
                recoveryCheckAt=-1;var recovery=host.GetComponent<CharacterRecovery>();
                Log((!recovery.Active && !recovery.GettingUp?"PASS ":"FAIL ")+"palm recovery completed; mode="+host.Mode);
                M6PlayReview.Snapshot();
            }
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Check moved box sensor")]
        static void Sensor()
        {
            if(!EditorApplication.isPlaying || !host)return;
            var a=host.GetComponent<ClimbAgent>();var m=host.GetComponent<CharacterMotor>();
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name="M7 temporary sensor box";
            box.transform.localScale=new Vector3(.6f,1,1);box.transform.position=m.Feet+new Vector3(1,.5f,0);Physics.SyncTransforms();
            var first=new VectorSensor(42);a.CollectObservations(first);var before=Values(first);
            box.transform.position+=Vector3.right*2;Physics.SyncTransforms();
            var second=new VectorSensor(42);a.CollectObservations(second);var after=Values(second);
            int changed=Enumerable.Range(11,31).Count(i=>Mathf.Abs(before[i]-after[i])>.001f);
            Log((changed>0?"PASS ":"FAIL ")+"moved box live geometry observation changes="+changed+" count="+after.Length);
            UnityEngine.Object.Destroy(box);
        }
        [MenuItem("NeverGiveUp/AI Gameplay/Palm recovery test")]
        static void Palm(){if(!EditorApplication.isPlaying || !host)return;var m=host.GetComponent<CharacterMotor>();host.GetComponent<CharacterRecovery>().PalmSlap(new Vector3(-2,1,0),m.Body.worldCenterOfMass+Vector3.up*.5f);Log("Applied existing PalmSlap; observe physics recovery without teleport");}
        [MenuItem("NeverGiveUp/AI Gameplay/Restore Legacy")]
        static void Legacy(){if(host)host.UseLegacy();}
        [MenuItem("NeverGiveUp/AI Gameplay/Check missing model fallback")]
        static void Missing(){if(!host)return;bool result=host.UseLearned(UnityEngine.Object.FindAnyObjectByType<StageController>(),null);Log((!result && host.Mode==GameplayPolicyHost.ControlMode.Legacy && host.GetComponent<CharacterBrain>().enabled && !host.GetComponent<ClimbAgent>().enabled?"PASS ":"FAIL ")+"missing model returns exclusive Legacy");}
        [MenuItem("NeverGiveUp/AI Gameplay/Run sensor and recovery checks %#F1")]
        static void Checks()
        {
            if(!EditorApplication.isPlaying || !host)return;
            Sensor();Missing();
            bool ok=host.UseLearned(UnityEngine.Object.FindAnyObjectByType<StageController>(),AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath));
            Log((ok && !host.GetComponent<CharacterBrain>().enabled && host.GetComponent<ClimbAgent>().enabled?"PASS ":"FAIL ")+"Learned reactivation has single authority");
            Palm();recoveryCheckAt=Time.time+9;M6PlayReview.Snapshot();
        }
    }
}
