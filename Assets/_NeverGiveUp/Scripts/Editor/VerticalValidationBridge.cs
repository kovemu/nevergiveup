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
