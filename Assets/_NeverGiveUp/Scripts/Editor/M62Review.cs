using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeverGiveUp.Editor
{
    // Opt-in runtime fixtures only. Stop Play Mode discards them; no asset/save writes.
    public sealed class M62Review : EditorWindow
    {
        static bool recording;
        static string path, lastState, lastObstacle;
        static float sampleAt, moveUntil;
        static int fixture;
        static PhysicsObjectInteraction drag;
        static HandInteraction hand;
        static Vector3 boxTarget;
        static GameObject fixtureObject;
        static CharacterMotor observedMotor;
        static float contactAt;
        static float captureAt = -1;
        static string captureLabel;
        static void Contact(Collision c)
        {
            if (!recording || Time.time < contactAt) return;
            for (int i = 0; i < c.contactCount; i++)
            {
                var p = c.GetContact(i);
                if (p.normal.y > .5f) continue;
                contactAt = Time.time + .1f;
                Write($"raw contact {c.collider.name} normal={p.normal} relative={c.relativeVelocity} point={p.point} command={observedMotor.DesiredVelocity} velocity={observedMotor.Body.linearVelocity}");
                break;
            }
        }
        [InitializeOnLoadMethod] static void Init() { EditorApplication.update -= Observe; EditorApplication.update += Observe; }
        [MenuItem("NeverGiveUp/M6.2/Collision Review %#F9")]
        static void Open() { GetWindow<M62Review>("M6.2 Collision Review"); }
        static void Record()
        {
            recording = true; lastState = lastObstacle = null; sampleAt = 0;
            Directory.CreateDirectory("Logs/M62");
            path = "Logs/M62/play-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            File.WriteAllText(path, "M6.2 real Play Mode trace\n");
        }
        static void Write(string text) { if (recording) File.AppendAllText(path, Time.time.ToString("F3") + " " + text + "\n"); }
        static void Observe()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            var brain = UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();
            if (!brain) return;
            var m = brain.GetComponent<CharacterMotor>(); var r = brain.GetComponent<CharacterRecovery>();
            if (observedMotor != m)
            {
                if (observedMotor) observedMotor.Contact -= Contact;
                observedMotor = m; observedMotor.Contact += Contact; contactAt = 0;
            }
            if (recording)
            {
                // Fixture evidence must never overwrite the player's persisted learning.
                var so = new SerializedObject(brain);
                if (so.FindProperty("persistLearning").boolValue)
                { so.FindProperty("persistLearning").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            if (fixture == 3 && m.Grounded)
            {
                fixture = 0;
                r.PalmSlap(Vector3.left * 4.5f, m.Body.worldCenterOfMass + Vector3.up * .55f);
                Write("left palm after reload; observe lower-platform recovery without teleport");
            }
            if (fixture == 1 && m.Grounded && Mathf.Abs(m.Body.linearVelocity.x) > 2.3f)
            {
                fixtureObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixtureObject.name = "M62 Runtime Wall";
                fixtureObject.transform.SetParent(UnityEngine.Object.FindAnyObjectByType<StageController>().transform.Find("Terrain"));
                fixtureObject.transform.position = m.Body.position + Vector3.right * m.Facing * (m.Capsule.radius + .155f);
                fixtureObject.transform.localScale = new Vector3(.3f, 2.3f, 3);
                Physics.SyncTransforms(); fixture = 0; Write("inserted runtime wall ahead of moving actor");
            }
            if (fixture == 2 && !m.Grounded && m.Body.linearVelocity.y > 1.5f)
            {
                fixtureObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixtureObject.name = "M62 Runtime Ceiling";
                fixtureObject.transform.SetParent(UnityEngine.Object.FindAnyObjectByType<StageController>().transform.Find("Terrain"));
                fixtureObject.transform.position = m.Body.position + Vector3.up * (m.HalfHeight + .45f);
                fixtureObject.transform.localScale = new Vector3(3, .3f, 3);
                Physics.SyncTransforms(); fixture = 0; Write("inserted runtime ceiling above rising actor");
            }
            if (drag)
            {
                if (Time.time < moveUntil) drag.SetTarget(boxTarget);
                else { drag.Release(); drag = null; if (hand) hand.enabled = true; Write("released box through existing drag API"); }
            }
            if (!recording || Time.time < sampleAt) return;
            sampleAt = Time.time + .1f;
            var v = brain.GetComponentInChildren<ClayAnimator>();
            string state = brain.State + "/" + v?.CurrentAction;
            Write($"sample {SceneManager.GetActiveScene().name} {state} pos={m.Body.position} vel={m.Body.linearVelocity} grounded={m.Grounded} physical={r.Active} obstacle={r.ObstacleReacting}");
            if (state != lastState)
            {
                Write("state " + brain.DebugSummary); lastState = state;
                if (r.ObstacleReacting && (brain.State == CharacterState.Resting || brain.State == CharacterState.GettingUpFromSit))
                { captureAt = Time.time + .35f; captureLabel = brain.State.ToString(); }
            }
            if (captureAt >= 0 && Time.time >= captureAt)
            {
                captureAt = -1;
                M6PlayReview.Snapshot(); Write("captured " + captureLabel + " to Logs/M6/Captures");
            }
            if (brain.ObstacleDebug != lastObstacle) { Write(brain.ObstacleDebug); lastObstacle = brain.ObstacleDebug; }
        }
        void OnGUI()
        {
            GUILayout.Label("Runtime-only collision verification", EditorStyles.boldLabel);
            GUILayout.Label(EditorApplication.isPlaying ? "PLAYING" : "EDIT MODE");
            GUI.enabled = EditorApplication.isPlaying;
            if (GUILayout.Button("Start trace")) Record();
            if (GUILayout.Button("Stop trace")) recording = false;
            if (GUILayout.Button("Reload MainPrototype")) SceneManager.LoadScene("MainPrototype");
            if (GUILayout.Button("Reload Prologue")) SceneManager.LoadScene("Prologue");
            if (GUILayout.Button("Arm wall on next ground run")) { if (!recording) Record(); fixture = 1; }
            if (GUILayout.Button("Arm ceiling on next rising jump")) { if (!recording) Record(); fixture = 2; }
            if (GUILayout.Button("Remove runtime fixture")) { if (fixtureObject) Destroy(fixtureObject); fixture = 0; }
            if (GUILayout.Button("Move box with existing physics drag API"))
            {
                var box = UnityEngine.Object.FindAnyObjectByType<DraggableBody>();
                drag = UnityEngine.Object.FindAnyObjectByType<PhysicsObjectInteraction>();
                hand = UnityEngine.Object.FindAnyObjectByType<HandInteraction>();
                if (box && drag) { if (hand) hand.enabled = false; drag.Grab(box, box.Body.position);
                    boxTarget = box.Body.position + new Vector3(-2, 1, 0); moveUntil = Time.time + 1.5f; Write("moving box via drag API"); }
            }
            if (GUILayout.Button("Show obstacle debug"))
            {
                var b = UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();
                if (b) { var so = new SerializedObject(b); so.FindProperty("showObstacleDebug").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            if (GUILayout.Button("Run arc checks")) ObstacleChecks.Run();
            if (GUILayout.Button("Run M5 regression checks")) PalmSlapChecks.Run();
            if (GUILayout.Button("Strong palm API impulse")) M6PlayReview.Hit(4.5f, false);
            if (GUILayout.Button("Capture game view")) M6PlayReview.Snapshot();
            if (GUILayout.Button("Diagnose 01 Start (1) arcs")) ObstacleChecks.DiagnoseStart();
            if (GUILayout.Button("Reload + left palm recovery test"))
            {
                if (!recording) Record();
                fixture = 3; SceneManager.LoadScene("MainPrototype");
            }
            GUI.enabled = true;
            EditorGUILayout.HelpBox("Fixtures only alter Play Mode. Box movement uses the real drag force API, not a native mouse test. Stop Play Mode to discard.", MessageType.Info);
        }
    }
}
