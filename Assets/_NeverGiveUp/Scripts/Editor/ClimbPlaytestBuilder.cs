using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.InferenceEngine;

namespace NeverGiveUp.Editor
{
    public static class ClimbPlaytestBuilder
    {
        public const string Path="Assets/_NeverGiveUp/Scenes/ClimbPlaytest.unity";
        public const string Model="Assets/_NeverGiveUp/AI/Models/NGUClimbVertical.onnx";
        public static void Create()
        {
            if(EditorApplication.isPlaying || File.Exists(Path))throw new System.InvalidOperationException("Only create a new playtest in Edit Mode.");
            var policy=AssetDatabase.LoadAssetAtPath<ModelAsset>(Model);
            if(!policy)throw new System.InvalidOperationException("Export and evaluate the vertical policy first.");
            if(!AssetDatabase.CopyAsset("Assets/_NeverGiveUp/Scenes/MainPrototype.unity",Path))throw new System.IO.IOException("Could not copy source scene.");
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(Path,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var roots=scene.GetRootGameObjects();
            var stage=roots.SelectMany(r=>r.GetComponentsInChildren<StageController>()).Single();
            var actor=roots.SelectMany(r=>r.GetComponentsInChildren<CharacterBrain>()).Single();
            var game=roots.SelectMany(r=>r.GetComponentsInChildren<GameManager>()).Single();
            var terrain=stage.transform.Find("Terrain");
            var material=terrain.GetComponentsInChildren<Renderer>().First().sharedMaterial;
            foreach(Transform child in terrain.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            Solid("NGU Safety Floor",terrain,new Vector3(0,-.25f,0),new Vector3(40,.5f,3),material);
            Solid("NGU Boundary Left",terrain,new Vector3(-20.4f,11,0),new Vector3(.8f,24,3),material);
            Solid("NGU Boundary Right",terrain,new Vector3(20.4f,11,0),new Vector3(.8f,24,3),material);
            float y=0;Vector3 final=default;
            for(int i=1;i<=16;i++)
            {
                int n=(i-1)%6,column=n<=3?n:6-n;
                float rise=i==1?.45f:i>3 && (i-1)%3==0?1.63f:(i-1)%3==1?1.19f:.47f;
                y+=rise;float x=column*3.15f+((n>=4 || n==0 && i>1)?-1.2f:0);
                final=new Vector3(x,y,0);
                Solid("Climb ledge "+i,terrain,final-Vector3.up*.12f,new Vector3(2.6f,.24f,3),material);
            }
            foreach(var waypoint in stage.GetComponentsInChildren<StageWaypoint>())Object.DestroyImmediate(waypoint.gameObject);
            var goal=new GameObject("Climb goal").AddComponent<StageWaypoint>();goal.transform.SetParent(stage.transform);goal.transform.position=final;goal.Configure(false);
            var origin=new GameObject("Climb start").transform;origin.SetParent(stage.transform);origin.position=new Vector3(-3.8f,0,0);
            stage.Configure(actor,game,origin,new[]{goal},-5);
            actor.transform.position=origin.position+Vector3.up*.97f;
            VerticalBuilder.Profile(actor.GetComponent<CharacterMotor>());
            var memory=new SerializedObject(actor);memory.FindProperty("persistLearning").boolValue=false;memory.ApplyModifiedPropertiesWithoutUndo();
            var host=actor.GetComponent<GameplayPolicyHost>();var settings=new SerializedObject(host);
            settings.FindProperty("startingMode").enumValueIndex=1;settings.FindProperty("useIntermediateTargets").boolValue=true;
            settings.FindProperty("stage").objectReferenceValue=stage;settings.FindProperty("model").objectReferenceValue=policy;
            settings.FindProperty("shortPreparation").floatValue=.22f;settings.FindProperty("longPreparation").floatValue=.45f;settings.ApplyModifiedPropertiesWithoutUndo();
            var boxPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Prefabs/Box.prefab");
            var box=(GameObject)PrefabUtility.InstantiatePrefab(boxPrefab,terrain);box.transform.position=new Vector3(-10,.6f,0);
            EditorSceneManager.SaveScene(scene,Path);EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);
        }
        static void Solid(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=position;go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
