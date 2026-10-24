using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using NeverGiveUp.Training;

namespace NeverGiveUp.Editor
{
    public static class VerticalPlayReview
    {
        static GameplayPolicyHost host;
        static string path;
        static float next;
        static bool armFall;
        public static void Start()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required.");
            host=UnityEngine.Object.FindAnyObjectByType<GameplayPolicyHost>();
            if(!host)throw new InvalidOperationException("Gameplay host required.");
            Directory.CreateDirectory("Logs/M7B");path="Logs/M7B/play-"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".csv";
            File.WriteAllText(path,"seconds,mode,x,y,target_x,target_y,grounded,state,short,long,fallback,command_x,velocity_x,blocked,external,obstacle\n");
            next=0;armFall=false;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        public static void ArmFall(){armFall=true;}
        static DraggableBody box;
        static PhysicsObjectInteraction drag;
        static HandInteraction hand;
        static Vector3 boxStart;
        static float releaseAt;
        static int revisionBefore;
        public static void MoveBox()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required.");
            box=UnityEngine.Object.FindAnyObjectByType<DraggableBody>();
            drag=UnityEngine.Object.FindAnyObjectByType<PhysicsObjectInteraction>();
            hand=UnityEngine.Object.FindAnyObjectByType<HandInteraction>();
            if(!box || !drag)throw new InvalidOperationException("Existing box/drag components required.");
            var graph=UnityEngine.Object.FindAnyObjectByType<StageController>().Graph;
            graph.Rebuild();revisionBefore=graph.EnvironmentRevision;boxStart=box.Body.position;
            if(hand)hand.enabled=false;
            drag.Grab(box,boxStart);releaseAt=Time.time+1.5f;
            EditorApplication.update-=BoxTick;EditorApplication.update+=BoxTick;
        }
        static void BoxTick()
        {
            if(!EditorApplication.isPlaying || !box || !drag){EditorApplication.update-=BoxTick;return;}
            if(Time.time<releaseAt){drag.SetTarget(boxStart+new Vector3(2,1,0));return;}
            drag.Release();if(hand)hand.enabled=true;
            var graph=UnityEngine.Object.FindAnyObjectByType<StageController>().Graph;graph.Rebuild();
            bool moved=Vector3.Distance(boxStart,box.Body.position)>1;
            File.WriteAllText("Logs/M7B/box-check.txt",$"{(moved && graph.EnvironmentRevision>revisionBefore?"PASS":"FAIL")} existing physical drag API; start={boxStart} end={box.Body.position}; graph revision {revisionBefore}->{graph.EnvironmentRevision}. Native mouse gesture not tested by this check.");
            EditorApplication.update-=BoxTick;
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || !host){EditorApplication.update-=Tick;return;}
            var m=host.GetComponent<CharacterMotor>();var a=host.GetComponent<ClimbAgent>();
            if(!a || Time.time<next)return;next=Time.time+.2f;
            var recovery=host.GetComponent<CharacterRecovery>();
            File.AppendAllText(path,FormattableString.Invariant($"{Time.time:F2},{host.Mode},{m.Feet.x:F3},{m.Feet.y:F3},{host.Goal.x:F3},{host.Goal.y:F3},{m.Grounded},{a.DisplayState},{a.ShortJumps},{a.LongJumps},{host.LastFallback},{m.DesiredVelocity:F3},{m.Body.linearVelocity.x:F3},{host.LocomotionBlocked},{recovery.ExternalReaction},{recovery.ObstacleReacting}\n"));
            if(armFall && m.Grounded && m.Feet.y>3)
            {
                armFall=false;
                // An external impulse tests real falling/recovery; never a teleport.
                host.GetComponent<CharacterRecovery>().PalmSlap(new Vector3(-8,1,0),m.Body.worldCenterOfMass+Vector3.up*.5f);
                File.AppendAllText(path,"# external palm impulse; track Learned and Legacy separately\n");
            }
        }
    }
}
