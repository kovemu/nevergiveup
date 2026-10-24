# M7-A local experiment

This is an independent training experiment, not a replacement for CharacterBrain
in Prologue or MainPrototype. AITraining has 16 arenas, each with exactly one
ClimbAgent controlling the existing CharacterMotor. No PlatformGraph or jump
planner supplies actions or takeoff points. The M6 visual prefab and clips are
reused on arena zero; training presentation never changes the mesh or physics root.

## Installed environment

- Unity 6000.6.0f1, ML-Agents 4.1.0, Inference Engine 2.6.1.
- Isolated Python 3.10.12: `D:/unity_project/ngu-ml/env/python.exe`.
- PyTorch 2.2.1+cu121: CUDA recognizes NVIDIA GeForce RTX 3060.
- Python mlagents and mlagents-envs 1.2.0.dev0 from official release/4.1.0
  commit `ee0a08ccae597094003844d0121317f9790a1676`. This is the version string
  in that release source, not a claim that PyPI 1.2.0 was released.
- ONNX 1.15.0 and ONNX Runtime 1.18.1 for independent model evaluation.
- Exact installed dependency snapshot: python-lock.txt.

Official compatibility source:
https://github.com/Unity-Technologies/ml-agents/blob/release/4.1.0/com.unity.ml-agents/Documentation~/Installation.md

## Policy contract

42 normalized observations: XY velocity (2), grounded (1), relative goal XY (2),
facing (1), recent horizontal/jump (2), preparation/landing (2), collision age (1),
9 downward probes each with hit and relative height (18), 6 side probes each
with range and obstacle-top height (12), overhead clearance (1).
No absolute positions, named platforms, demonstration trajectories, or image input.

Discrete branches: horizontal left/neutral/right; jump none/short/long.
Jump requires a released trigger, grounded state and completed landing gate.
Preparation is .4s, landing .25s. Walk 2.8, turn 150 deg/s, jump horizontal impulse
up to 65% of the independent 6.4 cap. Air control remains Motor's normal 3 units/s².
PPO MLP: two 128-unit layers, no recurrent state.

## Reward and termination

- Signed goal-distance progress: .08 per unit (moving away reverses the reward).
- Time: -.0004 each .02s physics step.
- Stable new maximum support height: .08 per unit, once per new height.
- Goal: +2 only after grounded arrival at low speed for .25s.
- Jump: -.025, extra -.06 for flat-ground unnecessary jumps.
- Side/ceiling contact: -.025, at most once per .25s.
- Nonzero direction reversal: -.005.
- Failure: -.5 after 18s, falling 3 units below the floor, or leaving the arena.

Only training episodes teleport/reset. Existing gameplay retains physical falls.
Reset layouts are mirrored and randomized using per-arena deterministic seeds.
Level 1 flat, 2 low box, 3 elevated target, 4 consecutive heights, 5 shifted box.
Goals, not jump locations, are specified. Level 4/5 currently have a lower safety
floor: they test climbing sequences, not a true gap curriculum.

## Tools

Unity menu `NeverGiveUp / AI Training` creates the new scene, opens it, and builds
only it to `Builds/M7A/NGUTraining.exe` without changing normal build scenes.
The builder refuses to overwrite an existing AITraining scene.
`ppo-climb.yaml` starts flat-ground training. Training acceleration is local to
the training executable, not the game or Editor.
`evaluate.py` runs the same physics executable with disjoint evaluation seeds,
either uniform random actions or exported deterministic ONNX policy. It records
all episode stats; random baseline is not labeled Legacy AI.

Status and measured results are recorded separately; creating these tools is not
evidence of successful training or generalization.

## Frozen model and gameplay

The final model is platform-01 (150,022 steps initialized from box-01), SHA256
45207ed8777b4f468c77937001916c07eb652e63b2ee5983f92b396d55afab84.
Training is complete at the reduced scope. See ../Docs/M7A_HANDOFF.md.
Evaluation/*.json contains held-out raw episode samples; do not equate these
simple-arena results with whole-game completion.

AITraining previews local InferenceOnly. TrainingBuildMode changes the build copy
to Default so a trainer can connect; it never edits the saved inference scene.
GameplayPolicyHost selects a single movement authority, supports manual Legacy
reversion and automatic timeout fallback. Existing stages default to Legacy.
Only TrainingArena permits episode reset/teleport; gameplay does not.
