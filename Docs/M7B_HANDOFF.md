# M7-B vertical climbing handoff

Date: 2026-10-25 (Asia/Seoul). Branch: `feat/core-prototype`.
Baseline: `60c8ffe75c286720ff8e4dc6c5da0bb31e3f39de`.

**Status: bounded experiment completed; behavior acceptance is partial.** Actual PPO, multiple-platform climbing, physical fall/recovery/retry, and held-out vertical evaluation were performed. SHORT selection and unnecessary jumping did not meet the intended behavior. Do not describe this as a finished general-purpose climbing AI. Training is stopped as requested; gameplay iteration can continue from the preserved candidate.

## Outputs and preservation

- New scenes: `Assets/_NeverGiveUp/Scenes/AIClimbTraining.unity`, `ClimbPlaytest.unity`.
- Candidate: `Assets/_NeverGiveUp/AI/Models/NGUClimbVertical.onnx` (C trial), SHA256 `da5bb509925ce77ff82755ceff4f8128c20855c6965c49e82ad726fcd6e09f95`.
- Original `NGUClimb.onnx` unchanged: SHA256 `45207ed8777b4f468c77937001916c07eb652e63b2ee5983f92b396d55afab84`.
- Original AITraining, M7-A results/checkpoints, character meshes/animation/rig, Legacy AI and player learning JSON retained. MainPrototype and Prologue layouts were not edited by this task.
- Pre-existing user changes to MainPrototype, Prologue, QualitySettings, URP settings, original model metadata and M4/M3 backup assets are excluded from this commit.
- Backup: `D:/unity_project/ngu-ml/m7b-baseline-60c8ffe/`. Build-generated settings changes were backed up and restored separately.
- Unity was restarted only after the user's explicit instruction, with recovery scenes/log backed up first. Final Editor state is stopped in ClimbPlaytest.

## Implementation

16 parallel randomized mirrored switchback arenas. Stages 0/1/2/3/4 use 0/3/6/11/16 raised platforms; final maps average approximately 16.97 units. Width, position, height and direction vary. Every intended adjacent support must have a capsule-clear route or the map regenerates. The validator uses actual discrete launch and air-control velocities, initial overlap checks and fixed-step gravity rather than only a height/distance formula. It is a feasibility filter, not a guarantee that the policy chooses a successful action.

`PolicyRoute` uses PlatformGraph to supply only the next landing target. It never commands movement, jump timing or takeoff positions. It refreshes from the current support after falls and invalidates clearance on geometry revision. All 42 normalized observations and both three-way action branches remain compatible. Heights remain Inspector-configurable; only new scenes use SHORT=.8 and LONG=2.0. Original defaults remain 1.55/3.0. Ground speed remains 2.8; jump horizontal cap remains 6.4.

Stable new supports/height earn one-time rewards; target-distance shaping uses a per-target best-distance ledger. Reascent bonus is once per episode. Falls onto the lower map continue the episode; only training episode boundaries teleport. Gameplay adds no respawn or position reset.

A real gameplay failure exposed a capsule supported on a ledge corner outside the narrower center ground probe. Motor now supplements the probe with a recent upward lower-capsule contact, respecting the ground mask, jump-ignore interval and contact exit. Sleeping support is retained only with unchanged body/support transforms and an active collider. The original immutable Falling case now proceeds through recovery and renewed jumps. Some policy retries still repeat a failing trajectory; this is not presented as solved navigation.

Gameplay integration clears pending jump edges during recovery, permits air steering after transient obstacle recoil, and retains physical knockdown/grounded sit/getup control locks. Missing-route timeout is explicit. Learned and Legacy authority remain mutually exclusive and logs distinguish them.

## Actual training

Local Python 3.10.12, Torch 2.2.1+cu121 / RTX3060, official ML-Agents release/4.1.0 Python source (version string 1.2.0.dev0), Unity ML-Agents 4.1.0. PPO: 2x128 network, normalized observations, batch1024/buffer10240, lr .0003, 3 epochs, gamma .995, time horizon128, 16 arenas, time scale20.

| Run | Initialization | Stage | Actual additional steps |
|---|---|---:|---:|
| m7b-warm-a-01 | original m7a-platform-01 | 1 | 250061 |
| m7b-fresh-a-01 | fresh weights | 1 | 250051 |
| m7b-fresh-b-01 | fresh A | 2 + simple mixture | 500195 |
| m7b-fresh-c-01 | B | 3 + 30% simple mixture | 300132 |

Selected fresh lineage totals 1,050,378 steps. Warm/fresh A used matched seed4201/settings; independent Stage1 completion was 9.02% (12/133) versus92.97% (119/128), so fresh training was continued. B and C use separate run IDs; all checkpoints/events are retained under `D:/unity_project/ngu-ml/results/`.

C used entropy .015, optional LONG cost .1 and flat jump cost .2. LONG cost is applied only if SHORT has a clear legal takeoff somewhere on the current support; the position is not exposed as an action instruction. This did **not** teach reliable SHORT selection.

## Final held-out results

Actual Windows physics player, deterministic ONNX, seed900001, simple-stage mixture disabled, **no Legacy controller in evaluation**. Final build includes corner-support fix. Raw samples are in `Training/Evaluation/M7B/final-*.json`. Parallel completion may overshoot the requested episode minimum; table uses actual counts.

| Candidate C map | Episodes | Complete | Mean maximum support height | Mean distinct raised supports | Mean LONG |
|---|---:|---:|---:|---:|---:|
| Flat | 64 | 64/64 (100%) | 0 | 0 | 2.00 |
| 6 platforms (~5.43 high) | 128 | 127/128 (99.22%) | 5.400 | 5.977 | 8.55 |
| 11 platforms (~11.56 high) | 64 | 59/64 (92.19%) | 11.118 | 10.625 | 21.41 |
| 16 platforms (~16.97 high) | 64 | 42/64 (65.63%) | 15.150 | 14.422 | 40.89 |

SHORT count was **zero in every final evaluation**. Flat runs still contain two unnecessary LONG jumps. Final Stage4 recorded139 reascents in20/64 episodes (31.25% of all evaluated episodes; not a conditional recovery success rate). Mean reversals51.59, collisions61.13, jump-associated collisions25.92, jump falls3.14, episode duration70.24s. Strict clean LONG landings12.875 per episode /40.891 launches (~31.49%); this metric rejects any jump with a side contact, even if it eventually reaches a support.

Matched final-build B comparison:43/64 complete (67.19%), mean height15.601, LONG43.48, optional LONG17.17, collisions70.55. C:42/64, LONG40.89, optional LONG15.81, collisions61.13. C was retained as a candidate with fewer launches/contacts in this sample and more recorded recovery attempts, **not** because it conclusively outperforms B. The one-episode completion difference is not significant evidence. Parallel episode streams are not paired identical layouts, although seed, curriculum and physics match.

The old model on the new physics/3-platform test completed0/64; on its original old-physics low-platform baseline it completed128/128. These are different tasks/physics, not a direct percentage-point gain claim. Earlier C Stage4 before contact support fix was26/64; final improvement combines motor correction and the existing trained model, not additional PPO.

## Play Mode and regression evidence

- Actual blue character, original Animator and physical capsule used in ClimbPlaytest; no mesh or clips changed.
- Candidate trace `Docs/Evidence/M7B/candidate-palm-reascent.csv`: climbed to3.74, externally struck, fell to lower support2.11, completed physical knockdown/getup, reascended to4.93 then5.40 under Learned authority. Later fallback is recorded and excluded from Learned success claims.
- Final trace `final-play-first-120s.csv`: climbed to4.93, external palm at~13s caused a real multi-level fall, recovered on the low part of the map and retried upward. Repeated marginal landings near1.55 and later repeated jumping remain. The uninterrupted local trace ran much longer and did not demonstrate a clean full-map gameplay completion. The full raw trace is retained in Logs/M7B/play-20261024_223009.csv; the tracked file is its first~120s, not a selected success montage.
- Screenshot `Docs/Evidence/M7B/climbing.png` is an actual gameplay capture, including the difficult ledge region; not a generated illustration or proof of completion.
- Existing physical box drag API moved box(-10,.55,0) to(-8,1.55,0); graph revision18→22. This verifies physics movement and changed geometry recognition. Native mouse drag and successful alternate-route traversal caused by the moved box were **not** verified by this API test.
- Physics probe: SHORT actual apex .7606874 vs setting .8; LONG1.93749 vs2.0. Fixed-step integration undershoot is within .12 tolerance. Real staggered-corner support before/after forced sleep passed; cached support does not make post-launch grounding true.
- Seven tempo/default/map-identity/facing contracts passed; Palm input/learning contracts passed. Real palm knockdown/getup exercised separately. No claim of exhaustive Prologue/MainPrototype campaign replay.
- Final Windows x64 training build succeeded with0 build errors. Editor compilation succeeded. Earlier map-validator trial exceptions remain in the historical Editor log; final independent evaluation players exited normally without those exceptions. Logs were not cleared to conceal failures.
- Independent code review identified the velocity-envelope issue, missing-route watchdog, reversal cost and edge-support/sleep cases; corrections reviewed without remaining important findings in the focused review.

## Acceptance and remaining limits

Passed: new jump physics measurement; separate vertical training/gameplay scenes; dynamic intermediate supports; real bounded PPO; held-out evaluation; sequential climbing; physical fall/retry examples; preserved original model/assets/user changes; separate candidate and reproducible evidence.

Partial/failed: appropriate SHORT/LONG selection, no flat unnecessary jumps, robust avoidance of repeated ledge collisions, reliable full-map actual-character recovery. Full-map100% was not required. Geometry-change recognition passed, but alternate route behavior after a player move and exhaustive original-scene playthroughs remain unverified. Do not replace MainPrototype/Prologue's configuration automatically on this evidence.

## Reproduce / continue

Open `ClimbPlaytest.unity` and Play; it starts Learned with C and preserves existing physical reactions. Stop Play Mode to discard temporary fixture movements. Use GameplayPolicyHost's existing Legacy context action to compare controllers. Original scenes keep their previous configuration.

Build: `NeverGiveUp / M7B / Build vertical player` → `Builds/M7B/NGUVertical.exe`.
Evaluation example from repository root:

```powershell
& D:/unity_project/ngu-ml/env/python.exe Training/evaluate.py --env Builds/M7B/NGUVertical.exe --model Assets/_NeverGiveUp/AI/Models/NGUClimbVertical.onnx --stage 4 --episodes 64 --worker-id 8 --output Logs/M7B/recheck.json
```

Training configs are `Training/ppo-vertical-warm.yaml`, `ppo-vertical-b.yaml`, `ppo-vertical-c.yaml`. Use a new run ID; do not overwrite checkpoints. Original model rollback is simply retaining the original scene/model reference. No external paid API is used.
