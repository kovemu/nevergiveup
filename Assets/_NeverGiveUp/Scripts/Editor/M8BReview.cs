using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NeverGiveUp.Training;
using Object=UnityEngine.Object;

namespace NeverGiveUp.Editor
{
    // Dedicated copies only. No writes to the user's play maps.
    [InitializeOnLoad]
    public static class M8BReview
    {
        public const string Folder="Assets/_NeverGiveUp/Scenes/M8BTests";
        static GameplayPolicyHost host;
        static string log;
        static float nextSample;
        static PhysicsObjectInteraction rescueDrag;
        static DraggableBody rescueBox;
        static Vector3 rescueTarget;
        static bool autoRescue,autoFall,rescued,fell;
        public static void Rescue()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play required");
            rescueBox=Object.FindObjectsByType<DraggableBody>(FindObjectsSortMode.None).Single(x=>x.name=="Rescue Box");
            rescueDrag=Object.FindAnyObjectByType<PhysicsObjectInteraction>();
            rescueTarget=rescueBox.Body.position+new Vector3(5,7,0);rescueDrag.Grab(rescueBox,rescueBox.Body.position);
        }
        public static void Diagnose()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play required");
            var h=Object.FindAnyObjectByType<GameplayPolicyHost>();var m=h.GetComponent<CharacterMotor>();var r=h.Route;
            var text=new System.Text.StringBuilder($"feet={m.Feet} support={m.Support?.name}\n");
            var from=r.CurrentPlatform;
            foreach(var to in Object.FindAnyObjectByType<StageController>().Graph.Platforms)
            {
                if(to.Collider==m.Support || Mathf.Abs(to.Bounds.ClosestPoint(m.Feet).x-m.Feet.x)>6)continue;
                int possible=0,approachable=0;
                for(float x=m.Feet.x-4;x<=m.Feet.x+4;x+=.2f)
                {
                    if(x<from.Bounds.min.x+.12f || x>from.Bounds.max.x-.12f)continue;
                    var land=to.Surface(Mathf.Clamp(x,to.Bounds.min.x+.45f,to.Bounds.max.x-.45f));
                    for(int mode=0;mode<2;mode++)for(int dir=-1;dir<=1;dir++)
                    if(r.ArcForInput(from.Surface(x),land,to.Collider,(JumpMode)mode,dir))
                    {possible++;if(Mathf.Abs(x-m.Feet.x)<.2f || r.WalkApproachClear(x)){approachable++;text.AppendLine($"valid {to.Collider.name} x={x:F2} mode={mode} input={dir}");}}
                }
                text.AppendLine($"target={to.Collider.name} arcs={possible} approach={approachable}");
            }
            File.WriteAllText("Logs/M8B/geometry-"+SceneManager.GetActiveScene().name+".txt",text.ToString());
        }
        public static void Build(bool evaluation)
        {
            LevelAuthoring.RequireEdit();Directory.CreateDirectory("Builds/M8B");Directory.CreateDirectory("Logs/M8B");
            string name=evaluation?"Evaluation":"NeverGiveUp";
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{evaluation?VerticalBuilder.Path:LevelAuthoring.ScenePath},locationPathName="Builds/M8B/"+name+".exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Logs/M8B/build-"+name+".txt",report.summary.result+" errors="+report.summary.totalErrors);
        }
        static M8BReview(){EditorApplication.update+=Tick;}
        public static void Create()
        {
            LevelAuthoring.RequireCleanScenes();
            var original=SceneManager.GetActiveScene();
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(string name in new[]{"Blocked","Detour","Offset"})
            {
                string path=Folder+"/"+name+".unity";
                if(File.Exists(path))continue;
                if(!AssetDatabase.CopyAsset(ClimbPlaytestBuilder.Path,path))throw new IOException(path);
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                try
                {
                    var roots=scene.GetRootGameObjects();
                    var stage=roots.SelectMany(r=>r.GetComponentsInChildren<StageController>()).Single();
                    var actor=roots.SelectMany(r=>r.GetComponentsInChildren<CharacterBrain>()).Single();
                    var game=roots.SelectMany(r=>r.GetComponentsInChildren<GameManager>()).Single();
                    var terrain=stage.transform.Find("Terrain");
                    var mat=terrain.GetComponentsInChildren<Renderer>().First().sharedMaterial;
                    foreach(Transform child in terrain.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                    var map=stage.GetComponent<LevelMap>()??stage.gameObject.AddComponent<LevelMap>();
                    void Solid(string n,float x,float top,float width,float height=.24f)
                    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=n;go.transform.SetParent(terrain);go.transform.position=new Vector3(x,top-height*.5f,0);go.transform.localScale=new Vector3(width,height,3);go.GetComponent<Renderer>().sharedMaterial=mat;}
                    Solid("NGU Safety Floor",0,0,32,.5f);
                    Vector3 goalPos;
                    if(name=="Blocked")
                    {
                        Solid("Step 1",0,.45f,2.6f);Solid("Step 2",3.15f,1.64f,2.6f);
                        Solid("Step 3",6.3f,2.11f,2.6f);Solid("Step 4",9.45f,3.74f,2.6f);
                        var box=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Prefabs/Box.prefab"),terrain);
                        box.name="Rescue Box";box.transform.position=new Vector3(8.8f,5.25f,0);box.transform.localScale=new Vector3(1.5f,3,3);
                        goalPos=new Vector3(9.45f,3.74f,0);
                    }
                    else if(name=="Detour")
                    {
                        Solid("Front",0,1.4f,2.6f);Solid("Front ceiling",-2.25f,3.1f,1.0f,1.0f);
                        Solid("Back step",-6,1.1f,2.6f);Solid("Back high",-3.15f,2.7f,2.6f);
                        Solid("Goal",0,4.3f,2.6f);goalPos=new Vector3(0,4.3f,0);
                    }
                    else
                    {
                        Solid("Wide destination",0,1.4f,3.4f);Solid("Left overhang",-2.6f,3.2f,1.0f,.3f);
                        Solid("Next",3.7f,2.6f,2.6f);goalPos=new Vector3(3.7f,2.6f,0);
                    }
                    foreach(var p in stage.GetComponentsInChildren<StageWaypoint>())Object.DestroyImmediate(p.gameObject);
                    var goal=new GameObject("M8B goal").AddComponent<StageWaypoint>();goal.transform.SetParent(stage.transform);goal.transform.position=goalPos;goal.Configure(false);
                    var start=new GameObject("M8B start").transform;start.SetParent(stage.transform);start.position=new Vector3(-3.8f,0,0);
                    actor.transform.position=start.position+Vector3.up*.97f;
                    stage.Configure(actor,game,start,new[]{goal},-5);
                    map.stage=stage;map.actor=actor.GetComponent<CharacterMotor>();map.start=start;map.goal=goal;
                    EditorSceneManager.SaveScene(scene);
                }
                finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
            }
        }
        public static void Play(string scenario,bool strategy,bool rescue=false,bool fall=false)
        {
            LevelAuthoring.RequireCleanScenes();
            if(!new[]{"Blocked","Detour","Offset","ClimbPlaytest","OnlyUpMain","Fork"}.Contains(scenario))throw new ArgumentException(scenario);
            EditorSceneManager.OpenScene(scenario=="ClimbPlaytest"?ClimbPlaytestBuilder.Path:scenario=="OnlyUpMain"?LevelAuthoring.ScenePath:Folder+"/"+scenario+".unity");
            SessionState.SetString("M8B.run",scenario+"-"+(strategy?"on":"off")+(rescue?"-rescue":"")+(fall?"-fall":""));
            SessionState.SetBool("M8B.rescue",rescue);SessionState.SetBool("M8B.fall",fall);
            SessionState.SetBool("M8B.strategy",strategy);
            EditorApplication.playModeStateChanged-=Entered;EditorApplication.playModeStateChanged+=Entered;
            EditorApplication.isPlaying=true;
        }
        public static void MemoryChecks()
        {
            Directory.CreateDirectory("Logs/M8B");var output=new System.Text.StringBuilder();int failures=0;
            void Check(bool value,string title){output.AppendLine((value?"PASS ":"FAIL ")+title);if(!value)failures++;}
            var m=new StrategyExperienceMemory("isolated-test");float initial=m.Value("a");
            m.Record(new StrategyExperience{strategyId="a",outcome=StrategyOutcome.SideCollision});
            m.Record(new StrategyExperience{strategyId="a",outcome=StrategyOutcome.SideCollision});
            Check(m.Value("a")<initial && m.Find("a").failures==2,"two physical failures lower preference without expiry");
            Check(m.Value("b")>m.Value("a"),"new strategy outranks repeated failure");
            m.Record(new StrategyExperience{strategyId="b",outcome=StrategyOutcome.Success});
            Check(m.Value("b")>m.Value("untried"),"success improves preference");
            float value=m.Value("a");m.Record(new StrategyExperience{strategyId="a",outcome=StrategyOutcome.PlayerInterference,playerInterference=true});
            m.Record(new StrategyExperience{strategyId="a",outcome=StrategyOutcome.EnvironmentChanged});
            Check(m.Value("a")==value && m.Find("a").attempts==2,"interference and environment change do not punish AI");
            var copy=new StrategyExperienceMemory("isolated-test");Check(copy.Load(m.ToJson()) && copy.Find("a").failures==2,"JSON round trip");
            Check(!new StrategyExperienceMemory("other-map").Load(m.ToJson()),"other environment memory rejected");
            Check(!copy.Load("{broken") && copy.Find("a").failures==2,"malformed memory preserves current ledger");
            for(int i=0;i<600;i++)m.Record(new StrategyExperience{strategyId="fixture-"+i,outcome=StrategyOutcome.Unknown});
            Check(m.Strategies.Count==256 && m.Recent.Count==512,"bounded history");
            m.Clear();Check(m.Strategies.Count==0 && m.Recent.Count==0,"developer clear");
            output.AppendLine("failures="+failures);File.WriteAllText("Logs/M8B/memory-checks.txt",output.ToString());
            if(failures>0)throw new Exception(output.ToString());
        }
        static void Entered(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode)return;
            EditorApplication.playModeStateChanged-=Entered;
            host=Object.FindAnyObjectByType<GameplayPolicyHost>();
            autoRescue=SessionState.GetBool("M8B.rescue",false);autoFall=SessionState.GetBool("M8B.fall",false);rescued=fell=false;rescueBox=null;rescueDrag=null;
            var settings=new SerializedObject(host);
            var strategy=settings.FindProperty("autonomousStrategy");
            if(strategy!=null){strategy.boolValue=SessionState.GetBool("M8B.strategy",false);settings.ApplyModifiedPropertiesWithoutUndo();}
            Application.runInBackground=true;Directory.CreateDirectory("Logs/M8B");
            log="Logs/M8B/"+SessionState.GetString("M8B.run","unknown")+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv";
            File.WriteAllText(log,"time,x,y,vx,vy,support,state,horizontal,jump,shorts,longs,failures,repeated,rests,walkDistance,targetX,targetY,reason,rawHorizontal,rawJump,strategy,approachX,candidates,changes,changedSuccesses,outcome,status\n");nextSample=0;
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || !host || string.IsNullOrEmpty(log))return;
            if(rescueDrag && rescueBox)
            {if(!rescueDrag.IsDragging)rescueDrag.Grab(rescueBox,rescueBox.Body.position);rescueDrag.SetTarget(rescueTarget);}
            var a=host.GetComponent<ClimbAgent>();if(!a || !a.Motor || a.Correction==null || Time.time<nextSample)return;
            nextSample=Time.time+.1f;var m=a.Motor;
            if(autoRescue && !rescued && Time.time>32){Rescue();rescued=true;File.WriteAllText(log+".rescue.txt","start="+Time.time);}
            if(autoFall && !fell && m.Grounded && m.Feet.y>4){M6PlayReview.Hit(4.5f,false);fell=true;File.WriteAllText(log+".fall.txt","start="+Time.time+" position="+m.Feet);}
            if(rescueBox)File.AppendAllText(log+".box.csv",FormattableString.Invariant($"{Time.time:F3},{rescueBox.Body.position.x:F3},{rescueBox.Body.position.y:F3},{rescueBox.Held},{m.ExternalVersion}\n"));
            var s=host.Strategy;
            File.AppendAllText(log,FormattableString.Invariant($"{Time.time:F3},{m.Feet.x:F3},{m.Feet.y:F3},{m.Body.linearVelocity.x:F3},{m.Body.linearVelocity.y:F3},{m.Support?.name},{a.DisplayState},{a.HorizontalDecision},{a.JumpDecision},{a.ShortJumps},{a.LongJumps},{a.Correction.FailedAttempts},{a.Correction.Repeated},{host.VoluntaryRestCount},{a.WalkDistance:F3},{host.Goal.x:F3},{host.Goal.y:F3},{a.Correction.Reason},{a.RawHorizontal},{a.RawJump},{s?.Selected?.Id},{s?.Selected?.Launch.x:F3},{s?.Candidates.Count},{s?.Changes},{s?.SuccessfulChanges},{s?.LastOutcome},{s?.Status}\n"));
            if(Time.time>75){if(s!=null)File.WriteAllText(log+".memory.json",s.Memory.ToJson());EditorApplication.isPlaying=false;}
        }
    }
}
