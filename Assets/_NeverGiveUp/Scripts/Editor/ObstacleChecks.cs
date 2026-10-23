using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    // Isolated runtime geometry, well outside the authored level. Never saves a scene or learning file.
    public static class ObstacleChecks
    {
        [MenuItem("NeverGiveUp/Validation/Check Obstacle Arcs %#F8")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("Obstacle checks require Play Mode."); return; }
            var actor = new GameObject("M62 test actor");
            actor.transform.position = new Vector3(1000, 1, 0);
            var motor = actor.AddComponent<CharacterMotor>();
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "M62 test ceiling";
            ceiling.transform.position = new Vector3(1001, 2.5f, 0);
            ceiling.transform.localScale = new Vector3(10, .4f, 4);
            var plan = new JumpPlan { landing = new Vector3(1002, 0, 0),
                parameters = new JumpParameters(), mode = JumpMode.Short };
            try
            {
                Physics.SyncTransforms();
                bool throughCeiling = AdaptiveJumpPlanner.ComputeVelocity(plan, new Vector3(1000, 0, 0), motor);
                Require(!throughCeiling, "reject arc through a ceiling");
                ceiling.SetActive(false); Physics.SyncTransforms();
                Require(AdaptiveJumpPlanner.ComputeVelocity(plan, new Vector3(1000, 0, 0), motor), "open arc remains reachable");
                ceiling.SetActive(true);
                var memory = new ObstacleMemory();
                var contact = new ObstacleContact { Collider = ceiling.GetComponent<Collider>(),
                    Normal = Vector3.down, Ceiling = true, Approach = 1 };
                memory.Remember(contact, new Vector3(1000, 0, 0));
                Require(memory.RepeatsLaunch(new Vector3(1000.3f, 0, 0), 1), "block repeated takeoff");
                Require(!memory.RepeatsLaunch(new Vector3(998, 0, 0), 1), "allow different takeoff");
                Require(!memory.RepeatsLaunch(new Vector3(1000, 0, 0), -1), "allow opposite approach");
                ceiling.transform.position += Vector3.right; Physics.SyncTransforms();
                Require(memory.Refresh(), "detect moved obstacle");
                Require(!memory.RepeatsLaunch(new Vector3(1000, 0, 0), 1), "reconsider opened route");
                Directory.CreateDirectory("Logs/M62");
                File.WriteAllText("Logs/M62/arc-checks.txt", "PASS: ceiling rejected; open arc accepted; repeated takeoff blocked; different/opposite approach allowed; moved obstacle reconsidered\n");
                Debug.Log("M62 arc checks passed.");
            }
            catch (Exception e)
            {
                Directory.CreateDirectory("Logs/M62");
                File.WriteAllText("Logs/M62/arc-checks.txt", "FAIL: " + e.Message + "\n");
                Debug.LogError(e.Message);
            }
            finally { UnityEngine.Object.Destroy(actor); UnityEngine.Object.Destroy(ceiling); }
        }
        [MenuItem("NeverGiveUp/Validation/Diagnose Start Platform Arcs")]
        public static void DiagnoseStart()
        {
            var stage = UnityEngine.Object.FindAnyObjectByType<StageController>();
            var motor = UnityEngine.Object.FindAnyObjectByType<CharacterMotor>();
            if (!EditorApplication.isPlaying || !stage || !motor) return;
            PlatformGraph.Platform floor = null, target = null;
            foreach (var p in stage.Graph.Platforms)
            { if (p.Collider.name == "NGU Safety Floor") floor = p; if (p.Collider.name == "01 Start (1)") target = p; }
            if (floor == null || target == null) return;
            var report = new System.Text.StringBuilder();
            var route = stage.Graph.Next(floor, target, motor.Feet);
            var planned = route == null ? null : AdaptiveJumpPlanner.Plan(stage.Graph, route, motor, new LearningMemory("M62-isolated", false));
            report.AppendLine($"GRAPH route={route?.To.Collider.name} plan={planned != null} launch={planned?.launch}");
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 10; i++)
            {
                float edge = side < 0 ? target.Bounds.min.x : target.Bounds.max.x;
                var from = floor.Surface(edge + side * (.7f + i * .4f));
                var landing = target.Surface(edge - side * .7f);
                var link = new PlatformGraph.Link { From = floor, To = target, Launch = from, Landing = landing };
                var plan = new JumpPlan { link = link, launch = from, landing = landing, parameters = new JumpParameters() };
                bool clear = AdaptiveJumpPlanner.ComputeVelocity(plan, from, motor);
                motor.ArcClear(from, new Vector3(plan.vx, plan.vy, 0), plan.flightTime, target.Collider, out var hit);
                report.AppendLine($"from={from} landing={landing} clear={clear} hit={hit.collider?.name} point={hit.point} normal={hit.normal}");
            }
            Directory.CreateDirectory("Logs/M62"); File.WriteAllText("Logs/M62/start-arcs.txt", report.ToString());
        }
        static void Require(bool value, string name)
        { if (!value) throw new InvalidOperationException("M62 check failed: " + name); }
    }
}
