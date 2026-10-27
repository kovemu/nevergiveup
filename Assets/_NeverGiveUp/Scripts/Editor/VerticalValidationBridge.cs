using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    // Narrow local validation queue for an Editor whose native window is unavailable.
    // No arbitrary code or scene overwrite command is accepted.
    [InitializeOnLoad]
    public static class VerticalValidationBridge
    {
        const string Request="Logs/M7B/validation-request.txt";
        static VerticalValidationBridge()
        {
            EditorApplication.update+=Poll;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="ClimbPlaytest")VerticalPlayReview.Start();
            };
        }
        static void Poll()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request))return;
            string command;
            try{command=File.ReadAllText(Request).Trim();File.Delete(Request);}catch(IOException){return;}
            try
            {
                switch(command)
                {
                    case "m8-create":LevelAuthoring.CreateSample();break;
                    case "m8-dock":foreach(var w in Resources.FindObjectsOfTypeAll<LevelEditorWindow>())w.Close();LevelEditorWindow.Open();break;
                    case "m8-window":LevelEditorWindow.Open();break;
                    case "m8-inspect":M8Review.Inspect();break;
                    case "m8-checks":M8Review.Checks();break;
                    case "m8-route":M8Review.Route();break;
                    case "m8-save-reopen":M8Review.SaveReopen();break;
                    case "m8-cleanup-ui":M8Review.CleanupUIFixture();break;
                    case "m8-dirty-check":M8Review.DirtySceneCheck();break;
                    case "m8-build":M8Review.Build();break;
                    case "m7d-on":M7DReview.Play(true);break;
                    case "m7d-on-retry":M7DReview.Play(true,true);break;
                    case "m7d-on-cube":M7DReview.Play(true,false,true);break;
                    case "m7d-clear-cube":M7DReview.RouteCube(true);break;
                    case "m7d-retry-fixture":M7DReview.RetryFixture(false);break;
                    case "m7d-clear-retry":M7DReview.RetryFixture(true);break;
                    case "m7d-off":M7DReview.Play(false);break;
                    case "m7d-checks":M7DReview.Checks();break;
                    case "m7d-diagnose":M7DReview.Diagnose();break;
                    case "m7d-build-eval":M7DReview.Build(true);break;
                    case "m7d-build":M7DReview.Build(false);break;
                    case "m7c-checks":M7CChecks.Run();break;
                    case "m7c-no-legacy":M7CChecks.CheckNoLegacy();break;
                    case "m7c-monitor":M7CPlaybackMonitor.StartReview();break;
                    case "m7c-wall":M62Review.ArmCollisionFixture(false);break;
                    case "m7c-ceiling":M62Review.ArmCollisionFixture(true);break;
                    case "m7c-strong":M6PlayReview.Hit(4.5f,false);break;
                    case "m7c-background":Application.runInBackground=true;break;
                    case "m7c-build":
                        if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode first.");
                        Directory.CreateDirectory("Builds/M7C");
                        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ClimbPlaytestBuilder.Path},locationPathName="Builds/M7C/NeverGiveUp.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                        Directory.CreateDirectory("Logs/M7C");File.WriteAllText("Logs/M7C/build.txt",report.summary.result+" errors="+report.summary.totalErrors);break;
                    case "create":VerticalBuilder.Create();break;
                    case "create-playtest":ClimbPlaytestBuilder.Create();break;
                    case "reset-test-box":
                        if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ClimbPlaytestBuilder.Path)throw new Exception("Only the saved new playtest may be edited.");
                        UnityEngine.Object.FindAnyObjectByType<DraggableBody>().transform.position=new Vector3(-10,.6f,0);
                        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());break;
                    case "trace":VerticalPlayReview.Start();break;
                    case "arm-fall":VerticalPlayReview.ArmFall();break;
                    case "box":VerticalPlayReview.MoveBox();break;
                    case "regression":PalmSlapChecks.Run();TempoChecks.Run();break;
                    case "open-playtest":
                        if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Unsaved scene/play state preserved.");
                        EditorSceneManager.OpenScene(ClimbPlaytestBuilder.Path);break;
                    case "open":
                        if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Unsaved scene/play state preserved.");
                        EditorSceneManager.OpenScene(VerticalBuilder.Path);break;
                    case "build":VerticalBuilder.Build();break;
                    case "play":EditorApplication.isPaused=false;EditorApplication.isPlaying=true;break;
                    case "stop":EditorApplication.isPlaying=false;break;
                    case "physics":EditorApplication.ExecuteMenuItem("NeverGiveUp/M7B/Run physics probes");break;
                    case "stage4":
                        foreach(var arena in UnityEngine.Object.FindObjectsByType<VerticalArena>()){arena.initialStage=4;arena.simpleFraction=0;}
                        foreach(var agent in UnityEngine.Object.FindObjectsByType<ClimbAgent>())if(agent.vertical)agent.EndEpisode();break;
                    case "capture":M6PlayReview.Snapshot();break;
                    default:throw new Exception("Unknown bounded validation command");
                }
                File.WriteAllText("Logs/M7B/validation-result.txt",DateTime.UtcNow.ToString("o")+" "+command+" dispatched");
            }
            catch(Exception error){File.WriteAllText("Logs/M7B/validation-result.txt",command+" FAILED "+error);Debug.LogException(error);}
        }
    }
}
