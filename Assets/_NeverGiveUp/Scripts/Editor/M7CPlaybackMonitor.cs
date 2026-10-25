using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeverGiveUp.Editor
{
    // Runtime evidence only; no scene, model, animation or policy asset changes.
    public static class M7CPlaybackMonitor
    {
        static CharacterMotor motor;
        static CharacterBrain brain;
        static CharacterRecovery recovery;
        static ClayAnimator visual;
        static HandInteraction hand;
        static GameplayPolicyHost host;
        static string path;
        public static void StartReview()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required");
            host=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>();
            motor=host.GetComponent<CharacterMotor>();brain=host.GetComponent<CharacterBrain>();
            recovery=host.GetComponent<CharacterRecovery>();
            visual=host.GetComponentInChildren<ClayAnimator>();hand=UnityEngine.Object.FindAnyObjectByType<HandInteraction>();
            Application.runInBackground=true;
            var so=new SerializedObject(hand);so.FindProperty("showSlapDebug").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();
            so=new SerializedObject(brain);so.FindProperty("persistLearning").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Logs/M7C");path="Logs/M7C/play-"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".csv";
            File.WriteAllText(path,"time,mode,x,y,grounded,state,action,facing,alignment,yaw,desired,vx,vy,obstacle,physical,slaps,decisionTime,horizontal,jump,pending,shorts,longs\n");
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || !motor || !visual){EditorApplication.update-=Tick;return;}
            var agent=host.GetComponent<NeverGiveUp.Training.ClimbAgent>();
            File.AppendAllText(path,FormattableString.Invariant($"{Time.time:F3},{host.Mode},{motor.Feet.x:F3},{motor.Feet.y:F3},{motor.Grounded},{brain.State},{visual.CurrentAction},{motor.Facing},{motor.VisualFacingAlignment:F4},{visual.transform.localEulerAngles.y:F3},{motor.DesiredVelocity:F3},{motor.Body.linearVelocity.x:F3},{motor.Body.linearVelocity.y:F3},{recovery.ObstacleReacting},{recovery.Active},{hand.SlapCount},{agent.LastActionTime:F3},{agent.HorizontalDecision},{agent.JumpDecision},{agent.PendingJump},{agent.ShortJumps},{agent.LongJumps}\n"));
        }
    }
}
