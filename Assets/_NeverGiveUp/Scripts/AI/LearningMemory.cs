using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NeverGiveUp
{
    [Serializable]
    public sealed class JumpParameters
    {
        public float launchOffset;
        public float approachSpeed = 2.5f;
        public float wait = 0.18f;
        public float horizontalScale = 1f;
        public float landingOffset;
        public float airControl = 0.6f;
        public float clearance = 0.65f;
        public JumpParameters Copy() => (JumpParameters)MemberwiseClone();
        public void Bound(float moveSpeed)
        {
            launchOffset = Safe(launchOffset, -0.65f, 0.35f, 0f);
            approachSpeed = Safe(approachSpeed, 0.6f, moveSpeed, 2.5f);
            wait = Safe(wait, 0.05f, 0.6f, 0.18f);
            horizontalScale = Safe(horizontalScale, 0.78f, 1.18f, 1f);
            landingOffset = Safe(landingOffset, -0.4f, 0.7f, 0f);
            airControl = Safe(airControl, 0.15f, 1f, 0.6f);
            clearance = Safe(clearance, 0.35f, 1.2f, 0.65f);
        }
        private static float Safe(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? Mathf.Clamp(fallback, min, max) : Mathf.Clamp(value, min, max);
        public string Summary => $"launch {launchOffset:F2}, approach {approachSpeed:F2}, wait {wait:F2}, " +
            $"vx x{horizontalScale:F2}, land {landingOffset:F2}, air {airControl:F2}, arc {clearance:F2}";
    }
    [Serializable]
    public sealed class LearnedAction
    {
        public JumpParameters parameters = new JumpParameters();
        // Discrete jump decision is learned alongside the continuous controls.
        public JumpMode mode = JumpMode.Short;
        public int trials, successes;
        public float value = 0.5f;
        public Vector3 lastSuccessfulLaunch, lastSuccessfulVelocity, lastStableLanding;
    }
    [Serializable]
    public sealed class JumpExperience
    {
        public string key;
        public int trials, successes, excluded;
        public List<LearnedAction> actions = new List<LearnedAction>();
    }
    [Serializable]
    public sealed class LearningData
    {
        public int version = 4;
        public string map;
        public List<JumpExperience> sections = new List<JumpExperience>();
        public List<JumpObservation> recent = new List<JumpObservation>();
    }

    /// <summary>Bounded contextual action memory; rewards come only from observed physics outcomes.</summary>
    public sealed class LearningMemory
    {
        private LearningData data;
        private readonly string path;
        private bool dirty;
        public LearningData Data => data;
        public LearningMemory(string map, bool load = true)
        {
            data = new LearningData { map = map };
            path = Path.Combine(Application.persistentDataPath, "NeverGiveUp-learning-" + map + ".json");
            try
            {
                if (!load || !File.Exists(path) || new FileInfo(path).Length > 2000000) return;
                LearningData loaded = JsonUtility.FromJson<LearningData>(File.ReadAllText(path));
                if (loaded == null || loaded.version != 4 || loaded.map != map || loaded.sections == null || loaded.recent == null) return;
                loaded.sections.RemoveAll(x => x == null || string.IsNullOrEmpty(x.key) || x.actions == null);
                foreach (JumpExperience section in loaded.sections)
                {
                    section.actions.RemoveAll(a => a == null || a.parameters == null);
                    if (section.actions.Count > 8) section.actions.RemoveRange(8, section.actions.Count - 8);
                    foreach (LearnedAction a in section.actions)
                    {
                        a.trials = Mathf.Clamp(a.trials, 0, 1000000);
                        a.successes = Mathf.Clamp(a.successes, 0, a.trials);
                        a.value = float.IsNaN(a.value) || float.IsInfinity(a.value) ? 0.5f : Mathf.Clamp01(a.value);
                    }
                }
                if (loaded.sections.Count > 128) loaded.sections.RemoveRange(128, loaded.sections.Count - 128);
                if (loaded.recent.Count > 64) loaded.recent.RemoveRange(0, loaded.recent.Count - 64);
                data = loaded;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { Debug.LogWarning("Learning memory could not be loaded; starting fresh: " + e.Message); }
        }
        public JumpExperience Section(string key)
        {
            JumpExperience found = data.sections.Find(x => x.key == key);
            if (found != null) return found;
            if (data.sections.Count >= 128) data.sections.RemoveAt(0);
            found = new JumpExperience { key = key };
            found.actions.Add(new LearnedAction { mode = JumpMode.Short });
            found.actions.Add(new LearnedAction { mode = JumpMode.Long });
            data.sections.Add(found);
            dirty = true;
            return found;
        }
        public LearnedAction Select(JumpExperience section, out bool reusedSuccess)
        {
            if (section.actions.Count == 0)
            {
                section.actions.Add(new LearnedAction { mode = JumpMode.Short });
                section.actions.Add(new LearnedAction { mode = JumpMode.Long });
            }
            LearnedAction best = section.actions[0];
            float score = float.NegativeInfinity;
            foreach (LearnedAction action in section.actions)
            {
                // Optimism for an untried candidate; successful actions retain preference until evidence deteriorates.
                float candidate = action.trials == 0 ? 0.86f : action.value +
                    0.12f * Mathf.Sqrt(Mathf.Log(2f + section.trials) / action.trials);
                if (candidate > score) { score = candidate; best = action; }
            }
            reusedSuccess = best.successes > 0;
            return best;
        }
        public void Observe(JumpExperience section, LearnedAction action, JumpObservation result, float moveSpeed)
        {
            dirty = true;
            data.recent.Add(result);
            if (data.recent.Count > 64) data.recent.RemoveAt(0);
            if (result.interfered || result.outcome == JumpOutcome.EnvironmentChanged || result.outcome == JumpOutcome.Cancelled)
            { section.excluded++; return; }
            section.trials++; action.trials++;
            float reward = result.outcome == JumpOutcome.Success ? Mathf.Lerp(0.85f, 1f, result.stability) :
                Mathf.Clamp01(result.progress) * 0.25f;
            action.value = action.trials == 1 ? reward : Mathf.Lerp(action.value, reward, 0.4f);
            if (result.outcome == JumpOutcome.Success)
            {
                section.successes++; action.successes++;
                action.mode = result.mode;
                action.parameters = result.parameters.Copy();
                action.lastSuccessfulLaunch = result.takeoff;
                action.lastSuccessfulVelocity = result.takeoffVelocity;
                action.lastStableLanding = result.landing;
                return;
            }
            JumpParameters variation = AdaptiveJumpPlanner.Improve(result.parameters, result, moveSpeed);
            JumpMode newMode = result.mode;
            // Failures can change the STRATEGY, not merely the approach speed.
            if (result.outcome == JumpOutcome.Shortfall || result.outcome == JumpOutcome.SideCollision ||
                result.outcome == JumpOutcome.Blocked || result.outcome == JumpOutcome.Unknown)
                newMode = result.mode == JumpMode.Short ? JumpMode.Long : JumpMode.Short;
            else if (result.outcome == JumpOutcome.CeilingCollision || result.outcome == JumpOutcome.Overshoot)
                newMode = JumpMode.Short;
            // Do not flood the candidate pool with indistinguishable actions.
            foreach (LearnedAction candidate in section.actions)
                if (candidate.mode == newMode && candidate.trials == 0 &&
                    Mathf.Abs(candidate.parameters.launchOffset - variation.launchOffset) < 0.04f &&
                    Mathf.Abs(candidate.parameters.horizontalScale - variation.horizontalScale) < 0.025f)
                    return;
            if (section.actions.Count >= 8)
            {
                LearnedAction worst = null;
                foreach (LearnedAction candidate in section.actions)
                {
                    if (candidate == action || candidate.successes > 0) continue;
                    if (worst == null || candidate.value < worst.value) worst = candidate;
                }
                if (worst != null) section.actions.Remove(worst);
            }
            if (section.actions.Count < 8)
                section.actions.Add(new LearnedAction { mode = newMode, parameters = variation });
        }
        // Failure history influences ROUTE choice, while successes reduce its cost.
        // A difficult route remains available if it is the only option.
        public float RoutePenalty(string key)
        {
            JumpExperience section = data.sections.Find(x => x.key == key);
            if (section == null || section.trials == 0) return 0f;
            int failures = Mathf.Max(0, section.trials - section.successes);
            return Mathf.Min(12f, Mathf.Log(1f + failures) * 3f) /
                (1f + section.successes * 0.8f);
        }
        public void Save()
        {
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                dirty = false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            { Debug.LogWarning("Learning remains in memory; JSON save failed: " + e.Message); }
        }
    }
}
