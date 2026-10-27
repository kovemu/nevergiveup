using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NeverGiveUp.Editor
{
    public static class LevelAuthoring
    {
        public const string ScenePath="Assets/_NeverGiveUp/Scenes/OnlyUpMain.unity";
        public const string PalettePath="Assets/_NeverGiveUp/Prefabs/LevelPalette";
        public static readonly string[] Labels={"발판","벽","기둥","물리 상자","장식물"};
        public static readonly Vector3[] Sizes={new Vector3(2.6f,.24f,3),new Vector3(.5f,3,3),new Vector3(.8f,2,3),new Vector3(1.1f,1.1f,1.1f),Vector3.one};
        public static LevelMap Map => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<LevelMap>(true)).FirstOrDefault();
        public static void RequireEdit()
        {if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("재생을 멈춘 뒤 편집하세요.");}
        public static void RequireCleanScenes()
        {
            RequireEdit();for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("저장하지 않은 씬 변경이 있습니다. 모든 열린 씬을 먼저 보존하세요.");
        }
        public static void Changed(Object target)
        {
            EditorUtility.SetDirty(target);
            if(PrefabUtility.IsPartOfPrefabInstance(target))PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
        public static void CreatePalette()
        {
            RequireEdit();Directory.CreateDirectory(PalettePath);AssetDatabase.Refresh();
            var original=SceneManager.GetActiveScene();var temporary=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            try{
            for(int i=0;i<5;i++)
            {
                string path=$"{PalettePath}/{(LevelPieceKind)i}.prefab";
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path))continue;
                GameObject go=i==3?(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Prefabs/Box.prefab")):GameObject.CreatePrimitive(i==4?PrimitiveType.Sphere:PrimitiveType.Cube);
                try
                {
                    go.name=((LevelPieceKind)i).ToString();go.transform.position=Vector3.zero;go.transform.localScale=Sizes[i];
                    var piece=go.AddComponent<LevelPiece>();piece.kind=(LevelPieceKind)i;
                    if(i==4)Object.DestroyImmediate(go.GetComponent<Collider>());
                    PrefabUtility.SaveAsPrefabAsset(go,path);
                }
                finally{Object.DestroyImmediate(go);}
            }
            AssetDatabase.SaveAssets();
            }finally{EditorSceneManager.CloseScene(temporary,true);SceneManager.SetActiveScene(original);}
        }
        public static LevelSection AddSection(LevelMap map,string name)
        {
            RequireEdit();var go=new GameObject(string.IsNullOrWhiteSpace(name)?"새 구역":name.Trim());
            Undo.RegisterCreatedObjectUndo(go,"구역 추가");go.transform.SetParent(map.stage.transform.Find("Terrain"),false);
            var section=Undo.AddComponent<LevelSection>(go);Selection.activeGameObject=go;return section;
        }
        public static LevelPiece Place(LevelMap map,LevelSection section,LevelPieceKind kind,Vector2 point,float snap)
        {
            RequireEdit();if(!map || !section || !section.transform.IsChildOf(map.stage.transform.Find("Terrain")))throw new InvalidOperationException("배치할 구역을 선택하세요.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"{PalettePath}/{kind}.prefab");
            if(!prefab)throw new InvalidOperationException("팔레트 초기화가 필요합니다.");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,section.transform);
            Undo.RegisterCreatedObjectUndo(go,"레벨 물체 배치");
            go.transform.position=new Vector3(Snap(point.x,snap),Snap(point.y,snap),0);Changed(go.transform);
            Selection.activeGameObject=go;return go.GetComponent<LevelPiece>();
        }
        public static float Snap(float value,float step)=>step>.001f?Mathf.Round(value/step)*step:value;
        public static void SetGeometry(LevelPiece piece,Vector2 point,Vector2 size)
        {
            RequireEdit();Undo.RecordObject(piece.transform,"위치·크기 수정");
            piece.transform.position=new Vector3(point.x,point.y,0);
            piece.transform.localScale=new Vector3(Mathf.Max(.1f,size.x),Mathf.Max(.1f,size.y),piece.transform.localScale.z);
            piece.transform.rotation=Quaternion.identity;Changed(piece.transform);
        }
        public static LevelPiece Duplicate(LevelPiece piece,float offset)
        {
            RequireEdit();var go=GameObjectUtility.DuplicateGameObjects(new[]{piece.gameObject},false)[0];go.name=piece.name+" 복사";
            Undo.RegisterCreatedObjectUndo(go,"레벨 물체 복제");
            go.transform.position=piece.transform.position+Vector3.right*Mathf.Max(.1f,offset);Changed(go.transform);
            Selection.activeGameObject=go;return go.GetComponent<LevelPiece>();
        }
        public static void Delete(LevelPiece piece){RequireEdit();Undo.DestroyObjectImmediate(piece.gameObject);}
        public static void SetMarker(LevelMap map,bool start,Vector2 position)
        {
            RequireEdit();var marker=start?map.start:map.goal.transform;
            Undo.RecordObject(marker,"시작·목표 이동");marker.position=new Vector3(position.x,position.y,0);Changed(marker);
            if(start){Undo.RecordObject(map.actor.transform,"캐릭터 시작 위치");map.actor.transform.position=marker.position+Vector3.up*(map.actor.GetComponent<CapsuleCollider>().height*.5f+.02f);Changed(map.actor.transform);}
        }
        public static void Save()
        {RequireEdit();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());}
        public static void CreateSample()
        {
            RequireEdit();
            if(File.Exists(ScenePath))throw new InvalidOperationException("OnlyUpMain이 이미 있습니다. 기존 맵을 열어 편집하세요.");
            RequireCleanScenes();
            CreatePalette();
            if(!AssetDatabase.CopyAsset(ClimbPlaytestBuilder.Path,ScenePath))throw new IOException("새 씬 복사 실패");
            var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var stage=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<StageController>()).Single();
                var actor=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CharacterMotor>()).Single();
                var game=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<GameManager>()).Single();
                var terrain=stage.transform.Find("Terrain");var material=terrain.GetComponentsInChildren<Renderer>().First().sharedMaterial;
                foreach(var child in terrain.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                var map=stage.gameObject.AddComponent<LevelMap>();map.stage=stage;map.actor=actor;
                var sectionA=new GameObject("01 · 출발").AddComponent<LevelSection>();sectionA.transform.SetParent(terrain,false);
                var sectionB=new GameObject("02 · 등반").AddComponent<LevelSection>();sectionB.transform.SetParent(terrain,false);
                void Solid(string name,Vector3 position,Vector3 size,LevelSection section)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"{PalettePath}/Platform.prefab");
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,section.transform);go.name=name;go.transform.position=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;Changed(go.transform);Changed(go.GetComponent<Renderer>());
                }
                Solid("NGU Safety Floor",new Vector3(0,-.25f,0),new Vector3(40,.5f,3),sectionA);
                Solid("NGU Boundary Left",new Vector3(-20.4f,11,0),new Vector3(.8f,24,3),sectionA);
                Solid("NGU Boundary Right",new Vector3(20.4f,11,0),new Vector3(.8f,24,3),sectionA);
                // Stage's existing migration sentinel must stay directly beneath Terrain.
                sectionA.transform.Find("NGU Safety Floor").SetParent(terrain,true);
                float y=0;Vector3 final=default;
                for(int i=1;i<=10;i++)
                {
                    int n=(i-1)%6,column=n<=3?n:6-n;
                    y+=i==1?.45f:i>3 && (i-1)%3==0?1.63f:(i-1)%3==1?1.19f:.47f;
                    final=new Vector3(column*3.15f+((n>=4 || n==0 && i>1)?-1.2f:0),y,0);
                    Solid("발판 "+i.ToString("00"),final-Vector3.up*.12f,new Vector3(2.6f,.24f,3),i<=5?sectionA:sectionB);
                }
                foreach(var waypoint in stage.GetComponentsInChildren<StageWaypoint>())Object.DestroyImmediate(waypoint.gameObject);
                map.goal=new GameObject("최종 목표 (발 위치)").AddComponent<StageWaypoint>();map.goal.transform.SetParent(stage.transform);map.goal.transform.position=final;map.goal.Configure(false);
                map.start=stage.transform.Find("Climb start");if(!map.start){map.start=new GameObject("시작점 (발 위치)").transform;map.start.SetParent(stage.transform);}
                map.start.position=new Vector3(-3.8f,0,0);actor.transform.position=map.start.position+Vector3.up*.97f;
                stage.Configure(actor.GetComponent<CharacterBrain>(),game,map.start,new[]{map.goal},-5);
                var host=new SerializedObject(actor.GetComponent<GameplayPolicyHost>());host.FindProperty("correctPolicyActions").boolValue=true;host.ApplyModifiedPropertiesWithoutUndo();
                var box=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PalettePath}/PhysicsBox.prefab"),sectionA.transform);box.transform.position=new Vector3(-10,.6f,0);Changed(box.transform);
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
            EditorSceneManager.OpenScene(ScenePath);LevelEditorWindow.Open();
        }
    }
}
