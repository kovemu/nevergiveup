using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using Unity.MLAgents.Policies;

namespace NeverGiveUp.Editor
{
    // The saved scene previews local inference; the dedicated training player must
    // accept the trainer connection. Modify the build copy, never the source scene.
    public sealed class TrainingBuildMode : IProcessSceneWithReport
    {
        public int callbackOrder=>0;
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            if(report==null || scene.path!="Assets/_NeverGiveUp/Scenes/AITraining.unity")return;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var behavior in root.GetComponentsInChildren<BehaviorParameters>(true))
                    behavior.BehaviorType=BehaviorType.Default;
        }
    }
}
