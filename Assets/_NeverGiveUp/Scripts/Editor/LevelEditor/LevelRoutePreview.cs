using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace NeverGiveUp.Editor
{
    public sealed class LevelRoutePreview : IDisposable
    {
        public enum Status { Walk, Short, Long, Blocked, Unchecked }
        public sealed class Edge { public PlatformGraph.Link link;public Status status; }
        public readonly List<Edge> Edges=new List<Edge>();
        public readonly List<string> Warnings=new List<string>();
        public bool Running{get;private set;}
        public bool Stale{get;private set;}=true;
        public bool GoalReachable{get;private set;}
        public string Summary{get;private set;}="검사 전 · AI 완주 여부는 별도 플레이 검사입니다.";
        GameObject scratch;PlatformGraph graph;PolicyRoute route;LevelMap map;LevelSection section;int cursor;
        public void Invalidate(){Stale=true;Running=false;GoalReachable=false;Warnings.Clear();Summary="편집됨 · 경로를 다시 검사하세요.";}
        public void Begin(LevelMap value,LevelSection scope)
        {
            Dispose();map=value;section=scope;Edges.Clear();Warnings.Clear();GoalReachable=false;
            if(!map || !map.actor || !map.start || !map.goal)throw new InvalidOperationException("맵의 캐릭터·시작·목표 참조를 확인하세요.");
            Transform terrain=scope?scope.transform:map.stage.transform.Find("Terrain");
            int count=terrain.GetComponentsInChildren<Collider>().Length;
            if(count>128){Stale=true;Summary="128개 초과: 구역 검사로 범위를 줄이세요. 전체 경로는 미검증입니다.";return;}
            // Bind only nonserialized caches; never invoke Awake or alter physics.
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(CharacterMotor).GetField("body",flags).SetValue(map.actor,map.actor.GetComponent<Rigidbody>());
            typeof(CharacterMotor).GetField("capsule",flags).SetValue(map.actor,map.actor.GetComponent<CapsuleCollider>());
            Physics.SyncTransforms();scratch=new GameObject("NGU route preview"){hideFlags=HideFlags.HideAndDontSave};
            graph=scratch.AddComponent<PlatformGraph>();graph.Initialize(terrain,map.actor);route=new PolicyRoute(graph,map.actor);
            foreach(var link in graph.Links)Edges.Add(new Edge{link=link,status=Status.Unchecked});
            // Show a nearest upward candidate even if the ballistic envelope rejects it.
            foreach(var target in graph.Platforms)
            {
                if(Edges.Any(x=>x.link.To==target && x.link.From.Bounds.max.y<=target.Bounds.max.y))continue;
                var from=graph.Platforms.Where(x=>x!=target && x.Bounds.max.y<target.Bounds.max.y).OrderBy(x=>Vector3.Distance(x.Bounds.center,target.Bounds.center)).FirstOrDefault();
                if(from!=null)Edges.Add(new Edge{link=new PlatformGraph.Link{From=from,To=target,Launch=from.Surface(from.Bounds.center.x),Landing=target.Surface(target.Bounds.center.x)},status=Status.Blocked});
            }
            cursor=0;Stale=false;Running=true;Summary="검사 중 · 회색 연결은 아직 미검증";
        }
        public void Tick()
        {
            if(!Running)return;
            if(!map || !graph){Invalidate();return;}
            double until=EditorApplication.timeSinceStartup+.008;
            do
            {
                if(cursor>=Edges.Count){Finish();break;}
                var edge=Edges[cursor++];if(edge.status!=Status.Unchecked)continue;
                var link=edge.link;
                if(!link.From.Collider || !link.To.Collider){Invalidate();break;}
                edge.status=Status.Blocked;
                if(link.Walk && WalkClear(link)){edge.status=Status.Walk;continue;}
                foreach(var mode in new[]{JumpMode.Short,JumpMode.Long})
                {
                    bool found=false;
                    foreach(float offset in new[]{0f,.3f,-.3f,.6f,-.6f,.9f,-.9f})
                    {
                        float x=link.Launch.x+offset;
                        if(x<link.From.Bounds.min.x+.12f || x>link.From.Bounds.max.x-.12f)continue;
                        if(route.Arc(link.From.Surface(x),link.Landing,link.To.Collider,mode)){found=true;break;}
                    }
                    if(found){edge.status=mode==JumpMode.Short?Status.Short:Status.Long;break;}
                }
            }while(EditorApplication.timeSinceStartup<until);
        }
        bool WalkClear(PlatformGraph.Link link)
        {
            var c=map.actor.GetComponent<CapsuleCollider>();var delta=link.Landing-link.Launch;
            foreach(var overlap in Physics.OverlapCapsule(link.Launch+Vector3.up*(c.radius+.035f),link.Launch+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,~0,QueryTriggerInteraction.Ignore))
                if(overlap.attachedRigidbody!=map.actor.GetComponent<Rigidbody>())return false;
            foreach(var hit in Physics.CapsuleCastAll(link.Launch+Vector3.up*(c.radius+.035f),link.Launch+Vector3.up*(c.height-c.radius+.035f),c.radius*.94f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(hit.rigidbody!=map.actor.GetComponent<Rigidbody>() && hit.collider!=link.From.Collider && hit.collider!=link.To.Collider)return false;
            return true;
        }
        void Finish()
        {
            Running=false;var visited=new HashSet<PlatformGraph.Platform>();
            var start=graph.At(map.start.position);var goal=graph.At(map.goal.Position);
            if(start!=null)visited.Add(start);
            bool added;do{added=false;foreach(var edge in Edges)if(edge.status<=Status.Long && visited.Contains(edge.link.From))added|=visited.Add(edge.link.To);}while(added);
            GoalReachable=goal!=null && visited.Contains(goal);
            foreach(var p in graph.Platforms)
            {
                bool unreachable=start!=null?!visited.Contains(p):!Edges.Any(x=>x.link.To==p && x.status<=Status.Long);
                if(!unreachable)continue;
                var area=p.Collider.GetComponentInParent<LevelSection>();
                Warnings.Add((area && area.requiresPlayerBox?"[상자 도움 의도 · 해결 미검증] ":"[도달 경로 미확인] ")+p.Collider.name);
            }
            if(start==null)Warnings.Insert(0,section?"구역 밖 시작점 · 구역 간 연결은 미검증":"시작점 아래 발판을 찾지 못했습니다.");
            if(goal==null)Warnings.Insert(0,section?"구역 밖 목표 · 전체 경로는 미검증":"목표 아래 발판을 찾지 못했습니다.");
            Summary=$"{(section?"구역":"전체")} 검사: 걷기 {Edges.Count(x=>x.status==Status.Walk)} / SHORT {Edges.Count(x=>x.status==Status.Short)} / LONG {Edges.Count(x=>x.status==Status.Long)} / 불가·표본 없음 {Edges.Count(x=>x.status==Status.Blocked)}\n"+
                (GoalReachable?"시작→목표 물리 경로 후보 있음. AI 완주 보장 아님.":"시작→목표 경로 미확인. 의도된 차단/구역 범위도 확인하세요.");
        }
        public void Draw(LevelPiece selected)
        {
            foreach(var e in Edges)
            {
                if(!e.link.From.Collider || !e.link.To.Collider)continue;
                if(selected && e.link.From.Collider.gameObject!=selected.gameObject && e.link.To.Collider.gameObject!=selected.gameObject)continue;
                Handles.color=Stale?Color.gray:e.status switch{Status.Walk=>Color.green,Status.Short=>Color.cyan,Status.Long=>new Color(1,.7f,.1f),Status.Blocked=>new Color(1,.3f,.3f),_=>Color.gray};
                var a=e.link.Launch;var b=e.link.Landing;a.z=b.z=-1.6f;
                Handles.DrawDottedLine(a,b,4);var d=(b-a).normalized;
                if(d.sqrMagnitude>.1)Handles.ConeHandleCap(0,Vector3.Lerp(a,b,.65f),Quaternion.LookRotation(d),.14f,EventType.Repaint);
            }
        }
        public void Dispose(){Running=false;if(scratch)Object.DestroyImmediate(scratch);scratch=null;}
    }
}
