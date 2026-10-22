using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace NeverGiveUp.Editor
{
 [InitializeOnLoad] public static class M6PlayReview
 {
  static bool recording; static int airHits; static float nextAirHit;
  static float nextSample; static string lastAction; static int captureNumber; static string session;
  static M6PlayReview(){ EditorApplication.update+=Observe;EditorApplication.playModeStateChanged+=Changed; }
  static void Changed(PlayModeStateChange s){if(s==PlayModeStateChange.EnteredPlayMode){session=DateTime.Now.ToString("yyyyMMdd_HHmmss");nextSample=0;lastAction=null;Directory.CreateDirectory("Logs/M6");}}
  static void Observe(){ if(!recording||!EditorApplication.isPlaying||EditorApplication.isPaused||Time.time<nextSample)return;nextSample=Time.time+0.10f;
   var actor=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();if(!actor)return;
   var m=actor.GetComponent<CharacterMotor>();var r=actor.GetComponent<CharacterRecovery>();var v=actor.GetComponentInChildren<ClayAnimator>();var hand=UnityEngine.Object.FindAnyObjectByType<HandInteraction>();
   if(!m||!m.Body||!v)return;
   string action=v.CurrentAction??"none";
   File.AppendAllText("Logs/M6/play-"+session+".tsv",$"{Time.time:F3}\t{SceneManager.GetActiveScene().name}\t{actor.State}\t{action}\t{m.Grounded}\t{r.Active}\t{r.GettingUp}\t{m.Body.position}\t{m.Body.linearVelocity}\t{m.Body.rotation.eulerAngles.z:F2}\t{hand?.SlapCount}\t{m.ExternalVersion}\n");
   if(airHits>0&&!m.Grounded&&Time.time>nextAirHit){Hit(airHits==2?2.5f:1.2f,true);airHits--;nextAirHit=Time.time+.3f;}
   if(action!=lastAction){lastAction=action;File.AppendAllText("Logs/M6/events-"+session+".txt",Time.time+" "+action+"\n"+actor.DebugSummary+"\n");}
   var drag=UnityEngine.Object.FindAnyObjectByType<PhysicsObjectInteraction>();
   if(drag&&drag.IsDragging)File.AppendAllText("Logs/M6/events-"+session+".txt",Time.time+" box drag active\n");
  }
  static void Capture(string label){var cam=Camera.main;if(!cam)return;Directory.CreateDirectory("Logs/M6/Captures");var rt=RenderTexture.GetTemporary(960,540,24);var old=cam.targetTexture;var active=RenderTexture.active;try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes("Logs/M6/Captures/"+session+"_"+(captureNumber++).ToString("D4")+"_"+label+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}finally{cam.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}}
  [MenuItem("NeverGiveUp/M6/Play Review Panel")] static void Open(){M6ReviewWindow.Open();}
  public static bool Recording => recording;
  public static void ToggleRecording(){recording=!recording;if(recording){session=DateTime.Now.ToString("yyyyMMdd_HHmmss");nextSample=0;lastAction=null;Directory.CreateDirectory("Logs/M6");}}
  public static void AirSequence(){airHits=2;nextAirHit=Time.time+.3f;}
  public static void InputScene(){SceneManager.sceneLoaded+=FreezeForInput;SceneManager.LoadScene("MainPrototype");}
  static void FreezeForInput(UnityEngine.SceneManagement.Scene scene,LoadSceneMode mode){SceneManager.sceneLoaded-=FreezeForInput;var b=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();if(b){b.enabled=false;b.GetComponent<CharacterMotor>().Stop();}InputDebug();}
  public static void Fallback(){var b=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();if(!b)return;var v=b.GetComponentInChildren<ClayAnimator>();if(v)UnityEngine.Object.DestroyImmediate(v.gameObject);var presentation=b.GetComponent<CharacterAnimation>();presentation.SendMessage("Awake");Debug.Log("M6 runtime fallback: "+b.GetComponentsInChildren<MeshRenderer>().Length+" visible primitive renderers");}
  public static void InputDebug(){var h=UnityEngine.Object.FindAnyObjectByType<HandInteraction>();if(!h)return;var so=new SerializedObject(h);so.FindProperty("showSlapDebug").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();}
  public static void Snapshot(){Capture("manual");}
  public static void Hit(float power,bool up){var r=UnityEngine.Object.FindAnyObjectByType<CharacterRecovery>();if(!r||!EditorApplication.isPlaying)return;var m=r.GetComponent<CharacterMotor>();r.PalmSlap((up?new Vector3(0.45f,1,0).normalized:Vector3.right)*power,m.Body.worldCenterOfMass+Vector3.up*0.55f);}
  public static void Rest(){var b=UnityEngine.Object.FindAnyObjectByType<CharacterBrain>();if(!b||!EditorApplication.isPlaying)return;for(int i=0;i<5;i++)b.SendMessage("NoteBlocked");}
  public static void Scene(string name){if(EditorApplication.isPlaying)SceneManager.LoadScene(name);else EditorSceneManager.OpenScene("Assets/_NeverGiveUp/Scenes/"+name+".unity");}
  public static void ValidateAssets(){
   var sb=new StringBuilder();var ctrl=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/_NeverGiveUp/Art/Characters/M6/NGU_M6.controller");
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_NeverGiveUp/Art/Characters/M6/BlueClay_M6_Visual.prefab");
   var go=UnityEngine.Object.Instantiate(prefab);var anim=go.GetComponentInChildren<Animator>();var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
   foreach(var state in ctrl.layers[0].stateMachine.states){var clip=state.state.motion as AnimationClip;if(!clip)throw new Exception("Missing motion "+state.state.name);float min=float.PositiveInfinity,max=float.NegativeInfinity;
    for(int n=0;n<=12;n++){clip.SampleAnimation(anim.gameObject,clip.length*n/12f);skin.BakeMesh(mesh);foreach(var v in mesh.vertices){if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z))throw new Exception("Invalid skin vertex");Vector3 p=go.transform.InverseTransformPoint(skin.transform.TransformPoint(v));min=Mathf.Min(min,p.y);max=Mathf.Max(max,p.y);}}
    sb.AppendLine($"{state.state.name}: {clip.length:F3}s; loop={clip.isLooping}; skinY={min:F4}..{max:F4}; keys={AnimationUtility.GetCurveBindings(clip).Length}");}
   sb.AppendLine("Vertices="+skin.sharedMesh.vertexCount+" bones="+skin.bones.Length+" rootMotion="+anim.applyRootMotion);
   UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(go);Directory.CreateDirectory("Logs/M6");File.WriteAllText("Logs/M6/assets-validation.txt",sb.ToString());Debug.Log("M6 asset sampling complete.");
  }
 }
 public sealed class M6ReviewWindow:EditorWindow
 {
  public static void Open(){GetWindow<M6ReviewWindow>("M6 Play Review");}
  void OnGUI(){GUILayout.Label("M6 - real Play Mode review",EditorStyles.boldLabel);GUILayout.Label(EditorApplication.isPlaying?"PLAYING":"EDIT MODE");
   if(GUILayout.Button("Open Prologue"))M6PlayReview.Scene("Prologue");if(GUILayout.Button("Open MainPrototype"))M6PlayReview.Scene("MainPrototype");
   if(GUILayout.Button("Validate imported clips and skin"))M6PlayReview.ValidateAssets();
   if(GUILayout.Button(M6PlayReview.Recording?"Stop recording":"Start recording"))M6PlayReview.ToggleRecording();
   if(GUILayout.Button("Run M5 input / learning checks"))PalmSlapChecks.Run();
   GUI.enabled=EditorApplication.isPlaying;
   if(GUILayout.Button("Arm two airborne API impulses"))M6PlayReview.AirSequence();
   if(GUILayout.Button("Show mouse input debug"))M6PlayReview.InputDebug();
   if(GUILayout.Button("Input test scene (AI paused, runtime only)"))M6PlayReview.InputScene();
   if(GUILayout.Button("Remove visual: runtime fallback test"))M6PlayReview.Fallback();
   if(GUILayout.Button("Weak palm impulse 0.7"))M6PlayReview.Hit(.7f,false);
   if(GUILayout.Button("Strong palm impulse 4.5"))M6PlayReview.Hit(4.5f,false);
   if(GUILayout.Button("Upward palm impulse 4.5"))M6PlayReview.Hit(4.5f,true);
   if(GUILayout.Button("Trigger blocked-path rest"))M6PlayReview.Rest();
   if(GUILayout.Button("Capture game view"))M6PlayReview.Snapshot();
   GUI.enabled=true;EditorGUILayout.HelpBox("Impulse buttons exercise the real recovery API. They do not validate mouse gesture input. Stop Play Mode to discard all runtime test changes.",MessageType.Info);
  }
 }
}