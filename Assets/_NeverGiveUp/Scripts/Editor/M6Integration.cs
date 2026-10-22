using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
namespace NeverGiveUp.Editor { public static class M6Integration {
const string Fbx = "Assets/_NeverGiveUp/Art/Characters/M6/NeverGiveUp_M6_Animated.fbx";
[MenuItem("NeverGiveUp/M6/Inspect Imported Character")] public static void Inspect() {
AssetDatabase.Refresh(); var sb=new StringBuilder(); var imp=AssetImporter.GetAtPath(Fbx) as ModelImporter; sb.AppendLine("Importer: "+imp.animationType+" scale="+imp.globalScale);
foreach(var a in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if(a is AnimationClip c) sb.AppendLine("CLIP "+c.name+" length="+c.length+" curves="+AnimationUtility.GetCurveBindings(c).Length);
var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Fbx); var go=UnityEngine.Object.Instantiate(prefab); foreach(var t in go.GetComponentsInChildren<Transform>()) if(t.name=="Hips"||t.parent==go.transform) sb.AppendLine("NODE "+t.name+" pos="+t.localPosition+" rot="+t.localEulerAngles+" scale="+t.localScale); foreach(var m in go.GetComponentsInChildren<SkinnedMeshRenderer>()) sb.AppendLine("MESH "+m.name+" bounds="+m.bounds+" bones="+m.bones.Length); UnityEngine.Object.DestroyImmediate(go);
foreach(var b in UnityEngine.Object.FindObjectsByType<CharacterBrain>(FindObjectsInactive.Include)) {sb.AppendLine("ACTOR "+b.name+" pos="+b.transform.position+" prefab="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(b));foreach(var t in b.GetComponentsInChildren<Transform>(true)) sb.AppendLine("  "+t.name+" pos="+t.localPosition+" scale="+t.localScale);}
Directory.CreateDirectory("Logs/M6"); File.WriteAllText("Logs/M6/import-inspection.txt",sb.ToString()); Debug.Log("M6 import inspection saved.");
}

const string Folder = "Assets/_NeverGiveUp/Art/Characters/M6";
const string VisualPath = Folder + "/BlueClay_M6_Visual.prefab";
const string ControllerPath = Folder + "/NGU_M6.controller";
[MenuItem("NeverGiveUp/M6/Build Animation Assets and Connect Existing Scenes")]
public static void Integrate()
{
 if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
 if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the open scene before connecting M6 assets.");
 var importer=(ModelImporter)AssetImporter.GetAtPath(Fbx);
 importer.animationType=ModelImporterAnimationType.Generic;
 importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
 importer.optimizeGameObjects=false; importer.importCameras=false; importer.importLights=false;
 importer.animationCompression=ModelImporterAnimationCompression.Off;
 importer.skinWeights=ModelImporterSkinWeights.Custom; importer.maxBonesPerVertex=8; importer.minBoneWeight=0.0001f;
 var settings=importer.defaultClipAnimations;
 foreach(var s in settings) { s.name=s.takeName.Substring(s.takeName.LastIndexOf('|')+1); s.loopTime=new[]{"NGU_Idle","NGU_Run","NGU_Fall","NGU_Rest"}.Contains(s.name);s.loopPose=false;s.lockRootRotation=true;s.lockRootPositionXZ=true;s.keepOriginalOrientation=true;s.keepOriginalPositionXZ=true; }
 importer.clipAnimations=settings; importer.SaveAndReimport();
 var model=AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
 var temp=UnityEngine.Object.Instantiate(model);
 var hips=temp.GetComponentsInChildren<Transform>().First(t=>t.name=="Hips");
 string hipsPath=AnimationUtility.CalculateTransformPath(hips,temp.transform);
 Vector3 bindPos=hips.localPosition; Quaternion bindRot=hips.localRotation;
 var originals=AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToDictionary(c=>c.name);
 var controller=AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
 if(!controller) controller=UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
 if(!controller.parameters.Any(p=>p.name=="RecoveryProgress")) controller.AddParameter("RecoveryProgress",AnimatorControllerParameterType.Float);
 var sm=controller.layers[0].stateMachine;
 string[] names={"NGU_Idle","NGU_Run","NGU_Inspect","NGU_JumpShort","NGU_JumpLong","NGU_Fall","NGU_Land","NGU_Stumble","NGU_KnockDown","NGU_AirFlail","NGU_GetUpFromKnockDown","NGU_SitDown","NGU_Rest","NGU_GetUpFromSit"};
 foreach(var name in names)
 {
  string source=name=="NGU_KnockDown"?"knockdown_front":name=="NGU_AirFlail"?"NGU_Fall":name;
  if(!originals.TryGetValue(source,out var original)) {Debug.LogWarning("Missing source action: "+source);continue;}
  string path=Folder+"/"+name+".anim";
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
  if(!clip) {clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
  EditorUtility.CopySerialized(original,clip);clip.name=name;
  if(name=="NGU_KnockDown"||name=="NGU_GetUpFromKnockDown")
  {
   foreach(var b in AnimationUtility.GetCurveBindings(clip))
   {
    if(b.path!=hipsPath) continue;
    string p=b.propertyName; float v;
    if(p.StartsWith("m_LocalPosition.")) v=bindPos[p.EndsWith("x")?0:p.EndsWith("y")?1:2];
    else if(p.StartsWith("m_LocalRotation.")) v=bindRot[p.EndsWith("x")?0:p.EndsWith("y")?1:p.EndsWith("z")?2:3];
    else continue;
    AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,original.length,v));
   }
  }
  var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??sm.AddState(name);
  state.motion=clip; state.writeDefaultValues=true;
  state.speed=name=="NGU_SitDown"?clip.length/1.35f:name=="NGU_GetUpFromSit"?clip.length/1.8f:name=="NGU_Land"?clip.length/0.24f:name=="NGU_Stumble"?clip.length/0.45f:1;
  if(name=="NGU_GetUpFromKnockDown") {state.timeParameter="RecoveryProgress";state.timeParameterActive=true;}
  if(name=="NGU_Idle") sm.defaultState=state;
  EditorUtility.SetDirty(clip);
 }
 UnityEngine.Object.DestroyImmediate(temp);
 GameObject visual=new GameObject("VisualRoot_M6");
 var instance=(GameObject)PrefabUtility.InstantiatePrefab(model); instance.transform.SetParent(visual.transform,false);
 var anim=instance.GetComponent<Animator>()??instance.AddComponent<Animator>(); anim.runtimeAnimatorController=controller;anim.applyRootMotion=false;
 var transforms=instance.GetComponentsInChildren<Transform>();
 Transform Bone(string n)=>transforms.First(t=>t.name==n);
 var clay=visual.AddComponent<ClayAnimator>();clay.Configure(anim,Bone("Left_UpperArm"),Bone("Right_UpperArm"),Bone("Left_UpperLeg"),Bone("Right_UpperLeg"),Bone("Head"));
 var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_NeverGiveUp/Art/Characters/BlueClay_M4.mat")??AssetDatabase.LoadAssetAtPath<Material>("Assets/_NeverGiveUp/Materials/BlueClay.mat");
 foreach(var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) {skin.sharedMaterial=material;skin.updateWhenOffscreen=true;}
 visual.transform.localScale=Vector3.one*1.9f;visual.transform.localPosition=Vector3.down*0.95f;visual.transform.localRotation=Quaternion.Euler(0,90,0);
 PrefabUtility.SaveAsPrefabAsset(visual,VisualPath);UnityEngine.Object.DestroyImmediate(visual);
 var prefabRoot=PrefabUtility.LoadPrefabContents("Assets/_NeverGiveUp/Prefabs/BlueClay.prefab");
 try {AttachToActor(prefabRoot);PrefabUtility.SaveAsPrefabAsset(prefabRoot,"Assets/_NeverGiveUp/Prefabs/BlueClay.prefab");}
 finally {PrefabUtility.UnloadPrefabContents(prefabRoot);}
 var originalScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
 foreach(var scenePath in new[]{"Assets/_NeverGiveUp/Scenes/Prologue.unity","Assets/_NeverGiveUp/Scenes/MainPrototype.unity"})
 {
  var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
  foreach(var b in UnityEngine.Object.FindObjectsByType<CharacterBrain>(FindObjectsInactive.Include)) AttachToActor(b.gameObject);
  UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
 }
 if(!string.IsNullOrEmpty(originalScene)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(originalScene);
 AssetDatabase.SaveAssets();Inspect();Debug.Log("M6 connected existing scenes and prefab; physics components preserved.");
}
public static void AttachToActor(GameObject actor)
{
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);if(!prefab)return;
 if(!actor.GetComponentInChildren<ClayAnimator>(true))
 {
  var v=(GameObject)PrefabUtility.InstantiatePrefab(prefab,actor.transform);
  var capsule=actor.GetComponent<CapsuleCollider>();
  float height=capsule?capsule.height:1.9f;
  v.transform.localScale=Vector3.one*height;
  v.transform.localPosition=Vector3.down*height*0.5f;
  v.transform.localRotation=Quaternion.Euler(0,90,0);
 }
 var old=actor.transform.Find("Clay Silhouette");if(old)old.gameObject.SetActive(false);
}

}}