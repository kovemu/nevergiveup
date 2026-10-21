using System;
using UnityEditor;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    /// <summary>Deterministic input/learning contracts. Does not simulate PhysX or alter scenes/files.</summary>
    public static class PalmSlapChecks
    {
        [MenuItem("NeverGiveUp/Validation/Check Palm Input and Learning")]
        public static void Run()
        {
            var sampler = new HandDragSampler();
            sampler.Add(Vector2.zero, 0f); sampler.Add(Vector2.zero, 0.1f);
            Require(!sampler.Velocity(0.1f, out _), "stationary click");
            sampler.Clear(); sampler.Add(Vector2.zero, 0f); sampler.Add(Vector2.right * 0.01f, 0.005f);
            Require(!sampler.Velocity(0.005f, out _), "too short stroke");
            float slow = Stroke(sampler, 60, Vector2.right * 0.2f);
            float fast = Stroke(sampler, 60, Vector2.right * 2f);
            Require(fast > slow * 9f, "continuous speed");
            foreach (int fps in new[] { 20, 30, 60, 120, 240 })
                Require(Mathf.Abs(Stroke(sampler, fps, Vector2.right * 2f) - fast) < 0.001f, "frame rate " + fps);
            Stroke(sampler, 60, new Vector2(-1f, 1f));
            Require(sampler.Velocity(1f, out Vector2 diagonal) && diagonal.x < 0 && diagonal.y > 0, "diagonal/left direction");
            Vector2 end = sampler.Latest;
            for (int i = 1; i <= 12; i++) sampler.Add(end, 1f + i / 60f);
            Require(!sampler.Velocity(1.2f, out _), "old crossing expires during hold");
            sampler.Clear(); sampler.Add(Vector2.zero, 0f); sampler.Add(Vector2.one, 0.01f);
            Require(!sampler.Velocity(0.01f, out _), "cursor warp rejected");
            sampler.Clear(); sampler.Add(Vector2.zero, 0f); sampler.Add(Vector2.one, 0.5f);
            Require(!sampler.Velocity(0.5f, out _), "stalled frame rejected");
            // Pixel normalization at two different resolutions yields the same stroke.
            Require(Mathf.Abs(PixelStroke(sampler, 720) - PixelStroke(sampler, 1440)) < 0.001f, "resolution");
            var memory = new LearningMemory("m5-contract-check-no-file", false);
            JumpExperience section = memory.Section("test");
            LearnedAction action = section.actions[0];
            memory.Observe(section, action, new JumpObservation {
                interfered = true, interferenceSource = "player-palm-slap",
                outcome = JumpOutcome.ExternalInterference, physicalOutcome = JumpOutcome.Shortfall,
                parameters = new JumpParameters() }, 4f);
            Require(section.excluded == 1 && section.trials == 0 && action.trials == 0 &&
                memory.Data.recent.Count == 1, "external failure excluded but recorded");
            Require(memory.Data.version == 4, "M3.3 schema preserved");
            Debug.Log("Palm input/learning checks passed. Physics and Play Mode still require separate execution.");
        }
        private static float Stroke(HandDragSampler sampler, int fps, Vector2 velocity)
        {
            sampler.Clear();
            for (int i = 0; i <= fps; i++) sampler.Add(velocity * (i / (float)fps), i / (float)fps);
            Require(sampler.Velocity(1f, out Vector2 result), "valid stroke");
            return result.magnitude;
        }
        private static float PixelStroke(HandDragSampler sampler, float height)
        {
            sampler.Clear();
            for (int i = 0; i <= 6; i++) sampler.Add(Vector2.right * (i / 60f * height) / height, i / 60f);
            Require(sampler.Velocity(0.1f, out Vector2 result), "pixel stroke");
            return result.magnitude;
        }
        private static void Require(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Palm check failed: " + name); }
    }
}
