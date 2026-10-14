using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NeverGiveUp.Editor
{
    /// <summary>
    /// M4.0 art-only preview. It NEVER replaces a gameplay prefab, scene,
    /// Animator, AI script or Rigidbody.
    /// </summary>
    public static class ClayModelPreviewBuilder
    {
        private const string DirectoryPath = "Assets/_NeverGiveUp/Art/Characters";
        private const string ModelPath = DirectoryPath + "/BlueClay_M4_Concept.obj";
        private const string MaterialPath = DirectoryPath + "/BlueClay_M4.mat";
        private const string PrefabPath = DirectoryPath + "/BlueClay_M4_Visual.prefab";
        private const string PreviewScenePath = DirectoryPath + "/BlueClay_M4_Preview.unity";

        [MenuItem("NeverGiveUp/M4/Create Clay Preview (No Gameplay Changes)")]
        public static void CreatePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stop Play Mode before building the M4 art preview.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!importedModel)
            {
                Debug.LogError("M4 OBJ asset not found: " + ModelPath);
                return;
            }

            if (File.Exists(PreviewScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild M4 character preview?",
                    "Only the separate M4 preview scene will be replaced. " +
                    "Prologue, MainPrototype, BlueClay prefab and all gameplay code stay unchanged.",
                    "Rebuild M4 Preview", "Cancel"))
                return;

            Material clay = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!clay)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader)
                {
                    Debug.LogError("URP Lit shader missing. Wait for URP package import.");
                    return;
                }
                clay = new Material(shader) { name = "BlueClay_M4" };
                clay.SetColor("_BaseColor", new Color(0.048f, 0.28f, 0.82f, 1f));
                clay.SetFloat("_Metallic", 0f);
                clay.SetFloat("_Smoothness", 0.16f);
                AssetDatabase.CreateAsset(clay, MaterialPath);
            }

            GameObject root = new GameObject("BlueClay M4 - Art Only");
            GameObject body = PrefabUtility.InstantiatePrefab(importedModel) as GameObject;
            if (!body)
            {
                Object.DestroyImmediate(root);
                Debug.LogError("Could not instantiate the imported M4 OBJ.");
                return;
            }
            body.name = "Seamless Clay Body - Static (Rig in M4.1)";
            body.transform.SetParent(root.transform, false);
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = renderer.sharedMaterials;
                for (int i = 0; i < assigned.Length; i++) assigned[i] = clay;
                renderer.sharedMaterials = assigned;
            }
            // This is intentionally a purely VISUAL prefab: no collider,
            // Rigidbody, CharacterMotor, CharacterBrain, HandInteraction or animations.
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance) instance.transform.position = new Vector3(0f, 1.03f, 0f);

            Shader floorShader = Shader.Find("Universal Render Pipeline/Lit");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview floor - not gameplay";
            floor.transform.position = new Vector3(0f, -0.07f, 0f);
            floor.transform.localScale = new Vector3(5f, 0.14f, 4f);
            const string floorMaterialPath = DirectoryPath + "/M4_PreviewFloor.mat";
            Material floorMat = AssetDatabase.LoadAssetAtPath<Material>(floorMaterialPath);
            if (!floorMat)
            {
                floorMat = new Material(floorShader) { name = "M4 Preview Floor" };
                floorMat.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.63f));
                AssetDatabase.CreateAsset(floorMat, floorMaterialPath);
            }
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            GameObject lightObject = new GameObject("Soft key light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.55f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.73f, 0.79f, 0.90f);

            GameObject cameraObject = new GameObject("Preview camera", typeof(Camera), typeof(AudioListener));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.84f, 0.87f, 0.91f);
            cameraObject.transform.position = new Vector3(2.2f, 2.0f, -6.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.02f, 0f));

            if (!EditorSceneManager.SaveScene(scene, PreviewScenePath))
                Debug.LogError("Unable to save isolated M4 preview scene.");
            else
            {
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = instance;
                SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(0f, 1f, 0f),
                    new Vector3(3.2f, 3.2f, 3.2f)), false);
                Debug.Log("M4.0 art preview opened. Original gameplay scenes and prefabs are unchanged.");
            }
        }
    }
}
