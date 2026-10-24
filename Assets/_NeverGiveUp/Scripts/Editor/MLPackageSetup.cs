using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    public static class MLPackageSetup
    {
        static AddRequest request;
        [MenuItem("NeverGiveUp/AI Training/Install ML Agents 4.1.0 %#F6")]
        public static void Install()
        {
            if (EditorApplication.isPlaying || request != null) return;
            request = Client.Add("com.unity.ml-agents@4.1.0");
            EditorApplication.update += Poll;
        }
        static void Poll()
        {
            if (request == null || !request.IsCompleted) return;
            Directory.CreateDirectory("Logs/M7");
            string result = request.Status == StatusCode.Success ?
                request.Result.name + "@" + request.Result.version : request.Error.message;
            File.WriteAllText("Logs/M7/package-install.txt", result);
            Debug.Log("M7 package installation: " + result);
            request = null;
            EditorApplication.update -= Poll;
        }
    }
}
