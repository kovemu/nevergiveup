using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NeverGiveUp
{
    public enum StrategyAction { Walk, Short, Long }
    public enum StrategyOutcome { Success, Shortfall, Overshoot, CeilingCollision, SideCollision, UnsafeLanding, RepeatedNoProgress, EnvironmentChanged, PlayerInterference, Unknown }

    [Serializable]
    public sealed class StrategyExperience
    {
        public string strategyId, fromId, targetId, environment, collisionTarget;
        public int environmentVersion, direction, attempts, successes, failures;
        public StrategyAction action;
        public StrategyOutcome outcome;
        public Vector3 start, plannedLaunch, intendedLanding, actualLaunch, actualLanding;
        public bool launched, collided, playerInterference, afterStrategyChange, afterRest;
        public float startedAt, endedAt, heightGain, distanceGain;
    }
    [Serializable]
    public sealed class StrategyStatistics
    {
        public string id;
        public int attempts, successes, failures, interrupted;
        public float Value => 2f*successes/(attempts+1f)-Mathf.Min(5f,.9f*failures)+.35f/Mathf.Sqrt(attempts+1f);
    }
    [Serializable]
    public sealed class StrategyMemoryData
    {
        public int version=1;
        public string map;
        public List<StrategyStatistics> strategies=new List<StrategyStatistics>();
        public List<StrategyExperience> recent=new List<StrategyExperience>();
    }

    // Learned-only ledger. Time alone never erases evidence or changes its value.
    public sealed class StrategyExperienceMemory
    {
        public const int MaxStrategies=256, MaxRecords=512;
        StrategyMemoryData data;
        public IReadOnlyList<StrategyExperience> Recent=>data.recent;
        public IReadOnlyList<StrategyStatistics> Strategies=>data.strategies;
        public string LastSuccess {get;private set;}="none";
        public StrategyExperienceMemory(string map){data=new StrategyMemoryData{map=map};}
        public StrategyStatistics Find(string id)=>data.strategies.Find(x=>x.id==id);
        public float Value(string id)=>Find(id)?.Value??.35f;
        public void Clear(){data.strategies.Clear();data.recent.Clear();LastSuccess="none";}
        public void Record(StrategyExperience item)
        {
            var stats=Find(item.strategyId);
            if(stats==null)
            {
                if(data.strategies.Count>=MaxStrategies)data.strategies.RemoveAt(0);
                stats=new StrategyStatistics{id=item.strategyId};data.strategies.Add(stats);
            }
            bool interrupted=item.playerInterference || item.outcome==StrategyOutcome.EnvironmentChanged || item.outcome==StrategyOutcome.PlayerInterference;
            if(interrupted)stats.interrupted++;
            else
            {
                stats.attempts++;
                if(item.outcome==StrategyOutcome.Success){stats.successes++;LastSuccess=item.strategyId;}
                else stats.failures++;
            }
            item.attempts=stats.attempts;item.successes=stats.successes;item.failures=stats.failures;
            if(data.recent.Count>=MaxRecords)data.recent.RemoveAt(0);
            data.recent.Add(item);
        }
        public string ToJson()=>JsonUtility.ToJson(data,true);
        public bool Load(string json)
        {
            StrategyMemoryData incoming;
            try{incoming=JsonUtility.FromJson<StrategyMemoryData>(json);}catch(ArgumentException){return false;}
            if(incoming==null || incoming.version!=1 || incoming.map!=data.map || incoming.strategies==null || incoming.recent==null || incoming.strategies.Count>MaxStrategies || incoming.recent.Count>MaxRecords)return false;
            foreach(var s in incoming.strategies)if(s==null || string.IsNullOrEmpty(s.id) || s.attempts<0 || s.successes<0 || s.failures<0 || s.interrupted<0 || (long)s.successes+s.failures!=s.attempts)return false;
            foreach(var e in incoming.recent)if(e==null || string.IsNullOrEmpty(e.strategyId))return false;
            data=incoming;LastSuccess="none";
            foreach(var e in data.recent)if(e.outcome==StrategyOutcome.Success)LastSuccess=e.strategyId;
            return true;
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path+".tmp",ToJson());
            if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
        }
    }
}
