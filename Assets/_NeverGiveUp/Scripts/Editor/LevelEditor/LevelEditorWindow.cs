using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeverGiveUp.Editor
{
    public sealed class LevelEditorWindow : EditorWindow
    {
        const string UI="Assets/_NeverGiveUp/Scripts/Editor/LevelEditor/LevelEditorWindow.uxml";
        [SerializeField] int palette;
        [SerializeField] float grid=.25f;
        [SerializeField] LevelSection section;
        int geometryHash; double nextGeometryCheck;
        bool placing,ready;LevelPiece selected;LevelMap map;
        readonly LevelRoutePreview preview=new LevelRoutePreview();
        List<LevelSection> sections=new List<LevelSection>();
        [MenuItem("NeverGiveUp/Level Editor")]
        public static void Open(){var w=GetWindow<LevelEditorWindow>("NGU Level Editor",true,Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(x=>x.GetType().Name=="HierarchyWindow" || x.GetType().Name=="SceneHierarchyWindow")?.GetType() ?? typeof(SceneView));w.titleContent=new GUIContent("NGU Level Editor");w.minSize=new Vector2(340,400);w.Show();}
        void OnEnable(){SceneView.duringSceneGui+=SceneGUI;Selection.selectionChanged+=Sync;Undo.undoRedoPerformed+=Edited;EditorApplication.update+=UpdatePreview;}
        void OnDisable(){SceneView.duringSceneGui-=SceneGUI;Selection.selectionChanged-=Sync;Undo.undoRedoPerformed-=Edited;EditorApplication.update-=UpdatePreview;preview.Dispose();}
        T Q<T>(string name) where T:VisualElement=>rootVisualElement.Q<T>(name);
        void SetField<T>(string name,T value)
        {
            var field=Q<BaseField<T>>(name);var focus=field.focusController?.focusedElement as VisualElement;
            if(focus!=null && (focus==field || field.Contains(focus)))return;
            if(!EqualityComparer<T>.Default.Equals(field.value,value))field.SetValueWithoutNotify(value);
        }
        void Run(Action action){try{action();Q<Label>("status").text=placing?"배치 모드 · Scene 왼쪽 클릭 / Esc로 종료":"완료";}catch(Exception ex){Q<Label>("status").text=ex.Message;}}
        void Button(string name,Action action)=>Q<Button>(name).clicked+=()=>Run(action);
        public void CreateGUI()
        {
            rootVisualElement.Clear();AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UI).CloneTree(rootVisualElement);
            Q<DropdownField>("palette").choices=LevelAuthoring.Labels.ToList();Q<DropdownField>("palette").index=palette;
            Q<DropdownField>("palette").RegisterValueChangedCallback(e=>palette=Q<DropdownField>("palette").index);
            Q<FloatField>("snap").SetValueWithoutNotify(grid);Q<FloatField>("snap").RegisterValueChangedCallback(e=>grid=Mathf.Max(0,e.newValue));
            Button("create",()=>{LevelAuthoring.CreateSample();Edited();});
            Button("open",()=>{LevelAuthoring.RequireCleanScenes();EditorSceneManager.OpenScene(LevelAuthoring.ScenePath);Edited();});
            Button("save",LevelAuthoring.Save);Button("place",()=>{placing=true;SceneView.RepaintAll();Q<Label>("status").text="Scene에서 클릭해 배치하세요.";});
            Button("select",()=>placing=false);Button("front",()=>{var view=SceneView.lastActiveSceneView;if(view){view.in2DMode=true;view.LookAt(new Vector3(3,5,0),Quaternion.identity,12,true);}});
            Button("addSection",()=>{section=LevelAuthoring.AddSection(map,Q<TextField>("sectionName").value);Edited();});
            Button("focusSection",()=>{Selection.activeGameObject=section.gameObject;SceneView.lastActiveSceneView?.FrameSelected();});
            Q<DropdownField>("section").RegisterValueChangedCallback(e=>{int i=Q<DropdownField>("section").index;if(i>=0 && i<sections.Count)section=sections[i];Sync();});
            Q<Toggle>("requiresBox").RegisterValueChangedCallback(e=>{if(!section)return;Undo.RecordObject(section,"상자 도움 의도");section.requiresPlayerBox=e.newValue;LevelAuthoring.Changed(section);Edited();});
            Q<TextField>("note").RegisterValueChangedCallback(e=>{if(!section)return;Undo.RecordObject(section,"구역 메모");section.designerNote=e.newValue;LevelAuthoring.Changed(section);});
            Q<Vector2Field>("position").RegisterValueChangedCallback(e=>{if(selected)Run(()=>{LevelAuthoring.SetGeometry(selected,e.newValue,Q<Vector2Field>("size").value);Edited();});});
            Q<Vector2Field>("size").RegisterValueChangedCallback(e=>{if(selected)Run(()=>{LevelAuthoring.SetGeometry(selected,Q<Vector2Field>("position").value,e.newValue);Edited();});});
            Button("duplicate",()=>{if(selected)LevelAuthoring.Duplicate(selected,grid);Edited();});Button("delete",()=>{if(selected)LevelAuthoring.Delete(selected);Edited();});
            Button("undo",Undo.PerformUndo);Button("redo",Undo.PerformRedo);
            Q<Vector2Field>("start").RegisterValueChangedCallback(e=>Run(()=>{LevelAuthoring.SetMarker(map,true,e.newValue);Edited();}));
            Q<Vector2Field>("goal").RegisterValueChangedCallback(e=>Run(()=>{LevelAuthoring.SetMarker(map,false,e.newValue);Edited();}));
            Button("actor",()=>Selection.activeGameObject=map.actor.gameObject);
            Button("analyze",()=>Analyze(null));Button("analyzeSection",()=>Analyze(section));
            foreach(var field in rootVisualElement.Query<FloatField>().ToList())field.isDelayed=true;
            ready=true;rootVisualElement.schedule.Execute(Sync).Every(300);Sync();
        }
        void Analyze(LevelSection scope){geometryHash=GeometryHash();preview.Begin(map,scope);}
        int GeometryHash()
        {
            if(!map)return 0;
            unchecked
            {
                int hash=EditorJsonUtility.ToJson(map).GetHashCode();
                hash=hash*31+EditorJsonUtility.ToJson(map.actor).GetHashCode();
                hash=hash*31+Physics.gravity.GetHashCode();
                hash=hash*31+Time.fixedDeltaTime.GetHashCode();
                foreach(var t in map.stage.GetComponentsInChildren<Transform>(true))hash=hash*31+t.localToWorldMatrix.GetHashCode();
                foreach(var area in map.stage.GetComponentsInChildren<LevelSection>(true))hash=hash*31+area.requiresPlayerBox.GetHashCode();
                // Physics queries also see obstacles outside Terrain and additive scenes.
                foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>())
                {
                    hash=hash*31+c.GetEntityId().GetHashCode();
                    hash=hash*31+c.transform.localToWorldMatrix.GetHashCode();
                    hash=hash*31+EditorJsonUtility.ToJson(c).GetHashCode();
                }
                return hash;
            }
        }
        void Edited(){preview.Invalidate();Sync();SceneView.RepaintAll();}
        void Sync()
        {
            if(!ready)return;
            map=LevelAuthoring.Map;
            Q<Label>("mapName").text=map?map.gameObject.scene.name:"OnlyUpMain을 만들거나 열어 주세요.";
            Q<VisualElement>("editing").SetEnabled(map && !EditorApplication.isPlayingOrWillChangePlaymode);
            if(!map)return;
            sections=map.stage.GetComponentsInChildren<LevelSection>(true).ToList();
            if(!sections.Contains(section))section=sections.FirstOrDefault();
            var dropdown=Q<DropdownField>("section");dropdown.choices=sections.Select((s,i)=>$"{i+1}. {s.name}").ToList();
            if(section)dropdown.SetValueWithoutNotify(dropdown.choices[sections.IndexOf(section)]);
            Q<Toggle>("requiresBox").SetValueWithoutNotify(section && section.requiresPlayerBox);SetField<string>("note",section?section.designerNote:"");
            selected=Selection.activeGameObject?Selection.activeGameObject.GetComponent<LevelPiece>():null;
            if(selected && selected.gameObject.scene!=map.gameObject.scene)selected=null;
            Q<Label>("selectionName").text=selected?selected.name:"Scene에서 물체를 선택하세요.";
            Q<Vector2Field>("position").SetEnabled(selected);Q<Vector2Field>("size").SetEnabled(selected);
            if(selected)
            {
                SetField<Vector2>("position",selected.transform.position);SetField<Vector2>("size",selected.transform.localScale);
            }
            if(map.start)SetField<Vector2>("start",map.start.position);
            if(map.goal)SetField<Vector2>("goal",map.goal.Position);
        }
        void UpdatePreview()
        {
            if(!ready)return;
            if(EditorApplication.isPlayingOrWillChangePlaymode){placing=false;return;}
            if(selected && selected.transform.hasChanged)
            {
                if(selected.kind!=LevelPieceKind.Decoration && (Mathf.Abs(selected.transform.position.z)>.001f || selected.transform.rotation!=Quaternion.identity))
                    LevelAuthoring.SetGeometry(selected,selected.transform.position,selected.transform.localScale);
                selected.transform.hasChanged=false;
            }
            if(EditorApplication.timeSinceStartup>=nextGeometryCheck){nextGeometryCheck=EditorApplication.timeSinceStartup+.75;int hash=GeometryHash();if(hash!=geometryHash){geometryHash=hash;preview.Invalidate();}}
            preview.Tick();Q<Label>("analysis").text=preview.Summary;Q<Label>("warnings").text=string.Join("\n",preview.Warnings.Take(12))+(preview.Warnings.Count>12?$"\n외 {preview.Warnings.Count-12}개":"");
            if(preview.Running)SceneView.RepaintAll();
        }
        void SceneGUI(SceneView view)
        {
            if(!ready || !map || EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(Q<Toggle>("showLinks").value)preview.Draw(selected);
            var e=Event.current;
            if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape){placing=false;e.Use();}
            if(!placing || !section || e.alt)return;
            int control=GUIUtility.GetControlID(FocusType.Passive);if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(control);
            var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);var plane=new Plane(Vector3.forward,Vector3.zero);
            if(!plane.Raycast(ray,out float distance))return;var point=ray.GetPoint(distance);point=new Vector3(LevelAuthoring.Snap(point.x,grid),LevelAuthoring.Snap(point.y,grid),0);
            Handles.color=new Color(.3f,.8f,1,.8f);Handles.DrawWireCube(point,LevelAuthoring.Sizes[palette]);
            if(grid>.01f)
            {Handles.color=new Color(.5f,.6f,.7f,.2f);for(int i=-8;i<=8;i++){Handles.DrawLine(point+new Vector3(i*grid,-8*grid,0),point+new Vector3(i*grid,8*grid,0));Handles.DrawLine(point+new Vector3(-8*grid,i*grid,0),point+new Vector3(8*grid,i*grid,0));}}
            if(e.type==EventType.MouseDown && e.button==0){Run(()=>{LevelAuthoring.Place(map,section,(LevelPieceKind)palette,point,grid);Edited();});e.Use();}
            if(e.type==EventType.MouseMove)view.Repaint();
        }
    }
}
