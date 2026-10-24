using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    public static class TempoChecks
    {
        [MenuItem("NeverGiveUp/Validation/Check M7 Tempo %#F7")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("Tempo checks require Play Mode."); return; }
            var go = new GameObject("M7 isolated tempo check");
            go.transform.position = new Vector3(2000, 10, 0);
            var motor = go.AddComponent<CharacterMotor>();
            var report = new System.Text.StringBuilder();
            int failed = 0;
            void Check(bool ok, string label)
            { report.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; }
            try
            {
                Check(Mathf.Abs(motor.MoveSpeed - 2.8f) < .01f, "ground speed is 70% of original 4");
                Check(Mathf.Abs(motor.MaxJumpHorizontal - 6.4f) < .01f, "horizontal jump reach preserved");
                Check(Mathf.Abs(motor.ShortJumpHeight - 1.55f) < .01f && Mathf.Abs(motor.LongJumpHeight - 3f) < .01f,
                    "SHORT/LONG heights preserved");
                motor.Drive(-.1f);
                Check(motor.Facing == 1f, "small destination correction does not reverse facing");
                var graph = go.AddComponent<PlatformGraph>();
                graph.Initialize(go.transform, motor);
                string slowIdentity = graph.MapSignature;
                var config = new SerializedObject(motor);
                config.FindProperty("groundSpeedMultiplier").floatValue = 1f;
                config.ApplyModifiedPropertiesWithoutUndo();
                graph.Initialize(go.transform, motor);
                Check(graph.MapSignature == slowIdentity, "tempo tuning retains existing learning-map identity");
                var probe = new JumpPlan { exploratory = true, mode = JumpMode.Short,
                    landing = new Vector3(2004, 100, 0), parameters = new JumpParameters() };
                Check(AdaptiveJumpPlanner.ComputeVelocity(probe, motor.Feet, motor),
                    "prepared exploration may test an unreachable target with bounded velocity");
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
                Directory.CreateDirectory("Logs/M7");
                File.WriteAllText("Logs/M7/tempo-checks.txt", report.ToString());
            }
            if (failed > 0) Debug.LogWarning($"M7 tempo: {failed} checks failed; see Logs/M7/tempo-checks.txt");
            else Debug.Log("M7 tempo checks passed.");
            CheckFacingPause();
        }
        static void CheckFacingPause()
        {
            var go = new GameObject("M7 facing pulse check");
            go.transform.position = new Vector3(2100, 10, 0);
            var motor = go.AddComponent<CharacterMotor>();
            motor.LookToward(-1);
            float started = Time.time;
            void Tick()
            {
                if (!go || !EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (Time.time - started < .7f) { motor.Stop(); return; }
                motor.LookToward(-1);
                string result = (motor.Facing == 1f ? "PASS " : "FAIL ") + "separated opposite pulses do not accumulate facing intent\n";
                File.AppendAllText("Logs/M7/tempo-checks.txt", result);
                Debug.Log(result);
                EditorApplication.update -= Tick;
                UnityEngine.Object.Destroy(go);
            }
            EditorApplication.update += Tick;
        }
    }
}
