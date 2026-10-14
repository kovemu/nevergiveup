# NeverGiveUp — M4.0: BlueClay Visual Model

## Scope and protection

M4.0 is an **art-only character modeling pass**. It is intentionally separate from
M3.3 `CharacterBrain`, `CharacterMotor`, `CharacterRecovery`, hand physics,
route graph and the current procedurally animated `BlueClay.prefab`.

DO NOT re-run `NeverGiveUp > Build Prototype Scenes`.
DO NOT replace the existing character prefab with this static model.

## Current art asset

- `Art/Characters/BlueClay_M4_Concept.obj`: original single-piece organic mesh.
- `Art/Characters/BlueClay_M4_Concept.mtl`: portable blue-clay color for OBJ import.
- Designed for Unity Y-up at around 2 units in height, compatible in scale with the
  current ~1.9-unit capsule. Head is round, face completely blank (no eyes, nose,
  mouth or markings). Belly, arms and feet are smoothly joined, not separately
  visible geometric primitives. Arms rest slightly outstretched.
- Current mesh is static and triangulated (~43k faces). It has **no rig, bones,
  animation clips or skin weights**. Retopology is recommended before
  serious character deformation. M4.1 will handle rig and motion.

## Preview in Unity

1. Close Unity before switching branches / pulling code, then use
   `git fetch origin` and `git switch --track origin/feat/character-polish`
   if the branch is not present locally, followed by `git pull --ff-only`.
2. Open the current Unity 6000.6.0f1 project and wait until imports finish.
3. From the main menu, choose
   **NeverGiveUp > M4 > Create Clay Preview (No Gameplay Changes)**.
4. A **separate** `BlueClay_M4_Preview.unity` scene opens with a camera,
   light, floor and new model. Rotate the Scene view, or run Play to
   see the Game camera's static presentation.
5. Review the silhouette from both side and front. The preview tool generates
   `BlueClay_M4_Visual.prefab` and a URP material inside `Art/Characters`.
   Those are local generated assets, not a substitute for the game character.
6. To return to the unchanged game, open `Prologue.unity` manually.

If the M4 preview scene already exists, the tool asks permission before
replacing **that preview scene only**. It does not alter Prologue,
MainPrototype or the generated M0–M3 character prefab.

## M4.0 visual acceptance

- One continuous soft body silhouette.
- Completely featureless face.
- Rounded top-heavy head, slightly plump middle, short legs and feet,
  outstretched short arms.
- Blue matte clay material, no outfit or added visual details.
- Looks readable in orthographic side view and does not require a face
  to communicate the player's actions.

## M4.1 — next step AFTER model approval

Retopologize / rig the finalized model, make idle, arms-out run, short/long
jump, landing, stagger, fall, get-up, seated rest and retry motion.
Blend motion on a child visual rig; keep canonical Rigidbody and AI unchanged.
An OBJ alone cannot deliver skeletal animation.

Unity Editor was unavailable to the modeling environment; compilation,
material import, rigging and Play Mode visual quality are **unverified**.
