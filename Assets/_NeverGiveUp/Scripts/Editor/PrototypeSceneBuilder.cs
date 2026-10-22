using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NeverGiveUp.Editor
{
    public static class PrototypeSceneBuilder
    {
        private const string Root = "Assets/_NeverGiveUp";
        private const string ProloguePath = Root + "/Scenes/Prologue.unity";
        private const string MainPath = Root + "/Scenes/MainPrototype.unity";
        private static Material clay;
        private static Material ground;
        private static Material boxMaterial;
        private static Material goalMaterial;
        private static PhysicsMaterial characterFriction;

        [MenuItem("NeverGiveUp/Build Prototype Scenes")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Exit Play Mode before generating prototype scenes."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if ((File.Exists(ProloguePath) || File.Exists(MainPath) || File.Exists(Root + "/Prefabs/BlueClay.prefab") ||
                File.Exists(Root + "/Prefabs/Box.prefab")) &&
                !EditorUtility.DisplayDialog("Rebuild NeverGiveUp prototype?",
                    "Replace the generated Prologue/MainPrototype scenes and BlueClay/Box prefabs? " +
                    "Manual changes to those generated assets will be replaced. Other assets are preserved.",
                    "Rebuild", "Cancel")) return;
            Generate();
        }

        // Can also be called via Unity -batchmode -executeMethod NeverGiveUp.Editor.PrototypeSceneBuilder.BuildBatch.
        public static void BuildBatch()
        {
            if (File.Exists(ProloguePath) || File.Exists(MainPath) ||
                File.Exists(Root + "/Prefabs/BlueClay.prefab") || File.Exists(Root + "/Prefabs/Box.prefab"))
                throw new System.InvalidOperationException("Batch generation only accepts missing prototype scenes.");
            Generate();
        }

        [MenuItem("NeverGiveUp/Upgrade Open Scenes to M3 (Preserve Layout)")]
        public static void UpgradeOpenScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade NeverGiveUp scene to M3");
            int upgraded = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                List<StageController> stages = new List<StageController>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    stages.AddRange(root.GetComponentsInChildren<StageController>(true));
                if (stages.Count == 0) continue;
                string backupFolder = Path.GetDirectoryName(scene.path).Replace('\\', '/') + "/M3Backups";
                Directory.CreateDirectory(backupFolder);
                AssetDatabase.Refresh();
                string backup = AssetDatabase.GenerateUniqueAssetPath(backupFolder + "/" + scene.name + "-before-M3.unity");
                if (!EditorSceneManager.SaveScene(scene, backup, true))
                    throw new System.IO.IOException("Could not back up " + scene.path);
                foreach (StageController stage in stages)
                {
                    var existing = new HashSet<GameObject>();
                    foreach (Transform t in stage.GetComponentsInChildren<Transform>(true)) existing.Add(t.gameObject);
                    stage.EnsureM3Geometry();
                    foreach (Transform t in stage.GetComponentsInChildren<Transform>(true))
                        if (!existing.Contains(t.gameObject)) Undo.RegisterCreatedObjectUndo(t.gameObject, "Add physical safety boundary");
                }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (HandInteraction hand in root.GetComponentsInChildren<HandInteraction>(true))
                    {
                        Undo.RecordObject(hand, "M3 hand defaults"); hand.ApplyM3Defaults();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(hand);
                    }
                    foreach (CharacterRecovery recovery in root.GetComponentsInChildren<CharacterRecovery>(true))
                    {
                        Undo.RecordObject(recovery, "M3 recovery defaults"); recovery.ApplyM3Defaults();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(recovery);
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                upgraded++;
            }
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"M3 upgrade prepared in {upgraded} open scene(s). Layout and prefab assets preserved. " +
                "Pre-upgrade scene copies are in M3Backups. Save the scene to keep changes; Undo is available.");
        }

        private static void Generate()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new System.InvalidOperationException("URP Lit shader was not found. Allow package import to finish.");
            clay = MaterialAsset("BlueClay", new Color(0.035f, 0.32f, 0.88f), shader);
            ground = MaterialAsset("WarmStone", new Color(0.65f, 0.58f, 0.47f), shader);
            boxMaterial = MaterialAsset("Terracotta", new Color(0.85f, 0.36f, 0.16f), shader);
            goalMaterial = MaterialAsset("Goal", new Color(0.2f, 0.72f, 0.57f), shader);
            string frictionPath = Root + "/Materials/CharacterSlide.physicMaterial";
            characterFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(frictionPath);
            if (!characterFriction)
            {
                characterFriction = new PhysicsMaterial("CharacterSlide")
                { dynamicFriction = 0.08f, staticFriction = 0.12f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                AssetDatabase.CreateAsset(characterFriction, frictionPath);
            }
            CreateScene(true);
            CreateScene(false);
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
            // Prologue is the default player entry point. Keep every other existing build scene.
            scenes.Add(new EditorBuildSettingsScene(ProloguePath, true));
            scenes.Add(new EditorBuildSettingsScene(MainPath, true));
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.path != ProloguePath && scene.path != MainPath) scenes.Add(scene);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ProloguePath, OpenSceneMode.Single);
            Debug.Log("NeverGiveUp M3 scenes generated. Press Play in Prologue. Drag and release: palm slap; hold box: drag.");
        }

        private static Material MaterialAsset(string name, Color color, Shader shader)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            Material material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.06f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateScene(bool prologue)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.72f, 0.77f, 0.84f);
            GameObject lightObject = new GameObject("Sun", typeof(Light));
            Light sun = lightObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraFollow));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.19f, 0.25f);
            cameraObject.transform.position = new Vector3(0f, 2f, -20f);

            CharacterBrain character = CreateCharacter(new Vector3(-3f, 1.02f, 0f));
            CameraFollow follow = cameraObject.GetComponent<CameraFollow>();
            follow.Configure(character.GetComponent<CharacterMotor>());
            GameObject input = new GameObject("Hand Interaction", typeof(PhysicsObjectInteraction), typeof(HandInteraction));
            HandInteraction hand = input.GetComponent<HandInteraction>();
            hand.Configure(camera, input.GetComponent<PhysicsObjectInteraction>());
            GameManager game = new GameObject("Game Manager", typeof(GameManager)).GetComponent<GameManager>();
            game.Configure(prologue, hand, character);
            StageController stage = new GameObject("Stage", typeof(StageController)).GetComponent<StageController>();
            Transform terrain = new GameObject("Terrain").transform;
            terrain.SetParent(stage.transform);
            Transform routeRoot = new GameObject("Waypoints - feet positions").transform;
            routeRoot.SetParent(stage.transform);
            Transform origin = new GameObject("Safe Start - feet position").transform;
            origin.SetParent(stage.transform);
            origin.position = new Vector3(-3f, 0f, 0f);
            List<StageWaypoint> route = new List<StageWaypoint>();
            if (prologue)
            {
                Platform(terrain, "Start", 0f, 0f, 10f);
                GameObject ramp = Cube("Gentle Ramp", terrain, new Vector3(6.5f, 0.25f, 0f), new Vector3(3.3f, 0.5f, 2.6f), ground);
                ramp.transform.rotation = Quaternion.Euler(0f, 0f, 18.435f);
                Platform(terrain, "Upper Path", 11f, 1f, 6f);
                Cube("Low Obstacle", terrain, new Vector3(10.5f, 1.3f, 0f), new Vector3(0.65f, 0.6f, 2.4f), ground);
                Platform(terrain, "After Small Pit", 19f, 1f, 6f);
                AddWaypoint(route, routeRoot, 4f, 0f, false);
                AddWaypoint(route, routeRoot, 8.7f, 1f, false);
                AddWaypoint(route, routeRoot, 13.1f, 1f, true);
                AddWaypoint(route, routeRoot, 16.9f, 1f, false);
                AddWaypoint(route, routeRoot, 20.7f, 1f, false);
                Goal(terrain, new Vector3(20.7f, 1f, 0f));
            }
            else
            {
                Platform(terrain, "01 Start", -1f, 0f, 8f);
                Platform(terrain, "02", 6f, 1.2f, 4f);
                Platform(terrain, "03", 11f, 2.4f, 4f);
                Platform(terrain, "04", 16f, 3.6f, 4f);
                Platform(terrain, "05 Turnaround", 21f, 4.8f, 4f);
                Platform(terrain, "06 Summit", 16f, 7.4f, 3f);
                AddWaypoint(route, routeRoot, 2.1f, 0f, true);
                AddWaypoint(route, routeRoot, 5.3f, 1.2f, false);
                AddWaypoint(route, routeRoot, 7.1f, 1.2f, true);
                AddWaypoint(route, routeRoot, 10.3f, 2.4f, false);
                AddWaypoint(route, routeRoot, 12.1f, 2.4f, true);
                AddWaypoint(route, routeRoot, 15.3f, 3.6f, false);
                AddWaypoint(route, routeRoot, 17.1f, 3.6f, true);
                AddWaypoint(route, routeRoot, 20.3f, 4.8f, false);
                AddWaypoint(route, routeRoot, 19.8f, 4.8f, true);
                AddWaypoint(route, routeRoot, 16.9f, 7.4f, false);
                AddWaypoint(route, routeRoot, 16f, 7.4f, false);
                CreateBox(terrain, new Vector3(-0.6f, 0.65f, 0f));
                Goal(terrain, new Vector3(16f, 7.4f, 0f));
            }
            stage.Configure(character, game, origin, route.ToArray(), -5f);
            stage.EnsureM3Geometry();
            EditorSceneManager.SaveScene(scene, prologue ? ProloguePath : MainPath);
        }

        private static CharacterBrain CreateCharacter(Vector3 center)
        {
            GameObject actor = new GameObject("Blue Clay");
            actor.transform.position = center;
            Rigidbody body = actor.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.linearDamping = 0f;
            body.angularDamping = 0.7f;
            body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            CapsuleCollider capsule = actor.AddComponent<CapsuleCollider>();
            capsule.height = 1.9f;
            capsule.radius = 0.32f;
            capsule.sharedMaterial = characterFriction;
            CharacterMotor motor = actor.AddComponent<CharacterMotor>();
            actor.AddComponent<CharacterRecovery>();
            CharacterBrain brain = actor.AddComponent<CharacterBrain>();
            Transform visual = new GameObject("Clay Silhouette").transform;
            visual.SetParent(actor.transform, false);
            Part("Round Head - no face", visual, PrimitiveType.Sphere, new Vector3(0f, 0.56f, 0f), new Vector3(0.84f, 0.84f, 0.8f));
            Part("Soft Body", visual, PrimitiveType.Capsule, new Vector3(0f, -0.12f, 0f), new Vector3(0.65f, 0.39f, 0.48f));
            Transform leftArm = Limb("Left Arm", visual, new Vector3(-0.29f, 0.04f, 0f), new Vector3(0.25f, 0.3f, 0.25f));
            Transform rightArm = Limb("Right Arm", visual, new Vector3(0.29f, 0.04f, 0f), new Vector3(0.25f, 0.3f, 0.25f));
            leftArm.localRotation = Quaternion.Euler(0f, 0f, -65f);
            rightArm.localRotation = Quaternion.Euler(0f, 0f, 65f);
            Transform leftLeg = Limb("Left Leg", visual, new Vector3(-0.17f, -0.43f, 0f), new Vector3(0.29f, 0.24f, 0.3f));
            Transform rightLeg = Limb("Right Leg", visual, new Vector3(0.17f, -0.43f, 0f), new Vector3(0.29f, 0.24f, 0.3f));
            CharacterAnimation animation = actor.AddComponent<CharacterAnimation>();
            animation.Configure(brain, motor, visual, leftArm, rightArm, leftLeg, rightLeg);
            M6Integration.AttachToActor(actor);
            PrefabUtility.SaveAsPrefabAssetAndConnect(actor, Root + "/Prefabs/BlueClay.prefab", InteractionMode.AutomatedAction);
            return brain;
        }
        private static Transform Limb(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            Transform pivot = new GameObject(name + " Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            Part(name, pivot, PrimitiveType.Capsule, Vector3.down * (scale.y - 0.03f), scale);
            return pivot;
        }
        private static void Part(string name, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = clay;
        }
        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        private static void Platform(Transform parent, string name, float x, float top, float width)
        { Cube(name, parent, new Vector3(x, top - 0.3f, 0f), new Vector3(width, 0.6f, 2.6f), ground); }
        private static void AddWaypoint(List<StageWaypoint> route, Transform parent, float x, float y, bool launch)
        {
            GameObject go = new GameObject($"{route.Count:00} {(launch ? "Launch" : "Land / Walk")}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            StageWaypoint point = go.AddComponent<StageWaypoint>();
            point.Configure(launch);
            route.Add(point);
        }
        private static void CreateBox(Transform parent, Vector3 position)
        {
            GameObject go = Cube("Movable Box", parent, position, Vector3.one * 1.1f, boxMaterial);
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.angularDamping = 0.8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            go.AddComponent<DraggableBody>();
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, Root + "/Prefabs/Box.prefab", InteractionMode.AutomatedAction);
        }
        private static void Goal(Transform parent, Vector3 feet)
        {
            GameObject marker = Cube("Goal Marker", parent, feet + new Vector3(0f, 0.04f, 0f), new Vector3(1.4f, 0.08f, 2.7f), goalMaterial);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            GameObject pole = Cube("Goal Post", parent, feet + new Vector3(0f, 1.2f, 0.8f), new Vector3(0.12f, 2.4f, 0.12f), goalMaterial);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
        }
    }
}
