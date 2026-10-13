using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeverGiveUp
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private bool prologue;
        [SerializeField] private string nextScene = "MainPrototype";
        [SerializeField] private HandInteraction hand;
        [SerializeField] private CharacterBrain character;
        private bool complete;
        private float transitionAt;
        private bool loading;
        private string loadError;
        private GUIStyle title;
        public void Configure(bool isPrologue, HandInteraction input, CharacterBrain actor)
        { prologue = isPrologue; hand = input; character = actor; }
        public void CompleteStage()
        {
            if (complete) return;
            complete = true;
            transitionAt = Time.time + 1.25f;
        }
        private void Update()
        {
            if (!complete || !prologue || loading || Time.time < transitionAt) return;
            loading = true;
            if (!Application.CanStreamedLevelBeLoaded(nextScene))
            {
                loadError = "Run NeverGiveUp > Build Prototype Scenes to register both scenes.";
                Debug.LogError(loadError, this);
                return;
            }
            SceneManager.LoadSceneAsync(nextScene);
        }
        private void OnGUI()
        {
            if (title == null)
                title = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(20, 16, Screen.width - 40, 36),
                prologue ? "NEVER GIVE UP  /  PROLOGUE" : "NEVER GIVE UP", title);
            if (prologue && hand && hand.FlickCount == 0 && !complete)
                GUI.Label(new Rect(20, 60, Screen.width - 40, 36), "CLICK TO FLICK", title);
            if (complete)
                GUI.Label(new Rect(20, Screen.height * 0.42f, Screen.width - 40, 70),
                    prologue ? "KEEP GOING" : "PROTOTYPE COMPLETE", title);
            if (!string.IsNullOrEmpty(loadError))
                GUI.Label(new Rect(20, Screen.height - 70, Screen.width - 40, 50), loadError);
#if UNITY_EDITOR
            if (character)
                GUI.Label(new Rect(16, Screen.height - 28, 500, 24),
                    $"{character.State}  |  retries: {character.FailureCount}");
#endif
        }
    }
}
