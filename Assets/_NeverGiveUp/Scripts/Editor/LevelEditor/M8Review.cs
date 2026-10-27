using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace NeverGiveUp.Editor
{
    // Bounded, repeatable checks for the new authoring scene. Never saves source scenes.
    public static class M8Review
    {
        const string Folder="Logs/M8A";
        static void RequireMap(){LevelAuthoring.RequireEdit();if(SceneManager.GetActiveScene().path!=LevelAuthoring.ScenePath)throw new Exception("OnlyUpMain must be active.");Directory.CreateDirectory(Folder);}
        public static void Inspect()
        {
            Directory.CreateDirectory(Folder);var scene=SceneManager.GetActiveScene();var map=LevelAuthoring.Map;
            var s=new StringBuilder($"scene={scene.path} dirty={scene.isDirty} play={EditorApplication.isPlaying}\n");
            if(map){foreach(var piece in map.GetComponentsInChildren<LevelPiece>())s.AppendLine($"{piece.name}: {piece.transform.position} size={piece.transform.localScale} prefab={PrefabUtility.IsPartOfPrefabInstance(piece)}");}
            if(Selection.activeGameObject)s.AppendLine("selected="+Selection.activeGameObject.name+" position="+Selection.activeGameObject.transform.position+" scale="+Selection.activeGameObject.transform.localScale);
            File.WriteAllText(Folder+"/state.txt",s.ToString());
        }
        public static void Checks()
        {
            RequireMap();var map=LevelAuthoring.Map;var section=map.GetComponentInChildren<LevelSection>();var log=new StringBuilder();int failed=0;
            void Check(bool ok,string name){log.AppendLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;}
            int group=Undo.GetCurrentGroup();Undo.IncrementCurrentGroup();group=Undo.GetCurrentGroup();
            try
            {
                Check(map.GetComponentsInChildren<LevelPiece>().Count(p=>p.name.StartsWith("발판 "))>=10,"sample has ten climb platforms");
                var terrain=map.stage.transform.Find("Terrain");var floor=terrain.Find("NGU Safety Floor");
                int colliderCount=terrain.GetComponentsInChildren<Collider>().Length;
                Undo.RecordObject(floor.gameObject,"test authored floor name");floor.name="Authored floor";map.stage.EnsureM3Geometry();
                Check(terrain.GetComponentsInChildren<Collider>().Length==colliderCount && !terrain.Find("NGU Safety Floor"),"authored maps do not regenerate legacy floor or boundaries");
                floor.name="NGU Safety Floor";
                var host=new SerializedObject(map.actor.GetComponent<GameplayPolicyHost>());Check(host.FindProperty("correctPolicyActions").boolValue,"M7-D action correction enabled");
                var walkA=LevelAuthoring.Place(map,section,LevelPieceKind.Platform,new Vector2(-16,3),.25f);
                var walkB=LevelAuthoring.Place(map,section,LevelPieceKind.Platform,new Vector2(-13.4f,3),0);
                using(var preview=new LevelRoutePreview())
                {
                    preview.Begin(map,null);while(preview.Running)preview.Tick();
                    Check(preview.Edges.Any(e=>e.link.From.Collider.gameObject==walkA.gameObject && e.link.To.Collider.gameObject==walkB.gameObject && e.status==LevelRoutePreview.Status.Walk),"adjacent clear surfaces classified WALK");
                    Check(preview.Edges.Any(e=>e.status==LevelRoutePreview.Status.Short) && preview.Edges.Any(e=>e.status==LevelRoutePreview.Status.Long),"SHORT and LONG categories both evaluated");
                    var ceiling=LevelAuthoring.Place(map,section,LevelPieceKind.Wall,new Vector2(-14.7f,4),0);
                    LevelAuthoring.SetGeometry(ceiling,new Vector2(-14.7f,4),new Vector2(5,.2f));Physics.SyncTransforms();
                    preview.Begin(map,null);while(preview.Running)preview.Tick();
                    Check(!preview.Edges.Any(e=>e.link.From.Collider.gameObject==walkA.gameObject && e.link.To.Collider.gameObject==walkB.gameObject && e.status==LevelRoutePreview.Status.Walk),"starting capsule overlap rejects WALK");
                    LevelAuthoring.Delete(ceiling);
                }
                LevelAuthoring.Delete(walkA);LevelAuthoring.Delete(walkB);
                var p=LevelAuthoring.Place(map,section,LevelPieceKind.Platform,new Vector2(-14.13f,3.12f),.25f);
                Check(p.transform.position==new Vector3(-14.25f,3,0),"placement snaps XY and locks Z");
                LevelAuthoring.SetGeometry(p,new Vector2(-14,4),new Vector2(3,.3f));Undo.FlushUndoRecordObjects();
                Check(p.transform.position==new Vector3(-14,4,0) && Mathf.Approximately(p.transform.localScale.x,3),"position and size edit");
                var duplicate=LevelAuthoring.Duplicate(p,.25f);
                Check(PrefabUtility.IsPartOfPrefabInstance(duplicate) && duplicate.transform.localScale==p.transform.localScale,"duplicate retains prefab and overrides");
                Undo.IncrementCurrentGroup();LevelAuthoring.Delete(duplicate);Undo.FlushUndoRecordObjects();
                Check(!duplicate,"delete");Undo.PerformUndo();
                var restored=section.GetComponentsInChildren<LevelPiece>().FirstOrDefault(x=>x.name==p.name+" 복사");Check(restored,"undo restores deleted prefab");
                Undo.PerformRedo();Check(!restored,"redo deletes it again");
                LevelAuthoring.SetGeometry(p,new Vector2(-14,60),new Vector2(3,.3f));Physics.SyncTransforms();
                using(var preview=new LevelRoutePreview())
                {
                    preview.Begin(map,null);while(preview.Running)preview.Tick();
                    Check(preview.Warnings.Any(x=>x.Contains(p.name)),"unreachable elevated platform warned");
                    Undo.RecordObject(section,"test intentional barrier");section.requiresPlayerBox=true;
                    preview.Begin(map,null);while(preview.Running)preview.Tick();
                    Check(preview.Warnings.Any(x=>x.Contains(p.name)&&x.Contains("상자 도움 의도")),"intentional player-help distinguished");
                    preview.Invalidate();Check(preview.Stale,"edited geometry marks preview unverified");
                }
            }
            finally{Undo.RevertAllDownToGroup(group);Selection.activeObject=null;log.AppendLine("failures="+failed);File.WriteAllText(Folder+"/authoring-checks.txt",log.ToString());}
            Route();
        }
        public static void Route()
        {
            RequireMap();using(var preview=new LevelRoutePreview())
            {preview.Begin(LevelAuthoring.Map,null);while(preview.Running)preview.Tick();
             File.WriteAllText(Folder+"/route.txt",preview.Summary+"\n"+string.Join("\n",preview.Warnings)+"\n"+string.Join("\n",preview.Edges.Select(e=>$"{e.link.From.Collider.name} -> {e.link.To.Collider.name}: {e.status}")));}
        }
        public static void SaveReopen()
        {
            RequireMap();var before=LevelAuthoring.Map.GetComponentsInChildren<LevelPiece>().Select(p=>p.name+p.transform.position+p.transform.localScale).OrderBy(x=>x).ToArray();
            LevelAuthoring.Save();EditorSceneManager.OpenScene(LevelAuthoring.ScenePath);
            var after=LevelAuthoring.Map.GetComponentsInChildren<LevelPiece>().Select(p=>p.name+p.transform.position+p.transform.localScale).OrderBy(x=>x).ToArray();
            File.WriteAllText(Folder+"/save-reopen.txt",before.SequenceEqual(after)?"PASS scene objects and transforms persisted":"FAIL scene changed after reopening");
            LevelEditorWindow.Open();
        }
        public static void CleanupUIFixture()
        {
            RequireMap();var fixture=LevelAuthoring.Map.GetComponentsInChildren<LevelPiece>().SingleOrDefault(p=>p.name=="Platform" && p.transform.position==new Vector3(14.5f,20,0) && Mathf.Approximately(p.transform.localScale.x,3.5f));
            if(!fixture)throw new Exception("Expected UI test fixture not found; no objects deleted.");
            LevelAuthoring.Delete(fixture);LevelAuthoring.Save();
        }
        public static void DirtySceneCheck()
        {
            RequireMap();LevelAuthoring.RequireCleanScenes();var original=SceneManager.GetActiveScene();var extra=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try{EditorSceneManager.MarkSceneDirty(extra);SceneManager.SetActiveScene(original);bool protectedScene=false;try{LevelAuthoring.RequireCleanScenes();}catch(InvalidOperationException){protectedScene=true;}
            File.WriteAllText(Folder+"/dirty-scene-check.txt",protectedScene?"PASS dirty inactive additive scene blocks destructive scene switch":"FAIL dirty scene not protected");}
            finally{EditorSceneManager.CloseScene(extra,true);SceneManager.SetActiveScene(original);}
        }
        public static void Build()
        {
            RequireMap();Directory.CreateDirectory("Builds/M8A");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{LevelAuthoring.ScenePath},locationPathName="Builds/M8A/NeverGiveUp.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText(Folder+"/build.txt",report.summary.result+" errors="+report.summary.totalErrors);
        }
    }
}
