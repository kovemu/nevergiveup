# M7-D walking-first learned control

2026-10-27 (Asia/Seoul), Unity 6000.6.0f1, branch `feat/core-prototype`, baseline `4cf1e445b9e2476cc66d930de8de77a1f1d2a17e`.

## Cause and implementation

`ClimbAgent.OnActionReceived` previously accepted every grounded, settled policy jump request. Its pending-jump branch then stopped walking throughout preparation. The frozen model repeatedly requests LONG, so this cycle displaced ordinary ground locomotion. The correction is enabled by `GameplayPolicyHost.correctPolicyActions` (Inspector); evaluation uses `--correction 0|1`, default 0. No PPO training or model change was performed.

`PolicyActionCorrection` rejects unnecessary jumps on walkable routes, allows walking in the policy-selected direction, brakes near a walkable destination, and keeps existing obstacle/cliff probes. It searches only walk-reachable takeoff positions in the requested direction. SHORT is preferred only when its current or approachable capsule trajectory is feasible; otherwise LONG is considered. Already-grounded edge positions are checked directly, rather than being rejected by walking approach margins.

`PolicyRoute.ArcForInput` evaluates the actual discrete launch velocity and existing fixed-step air acceleration/gravity, with capsule overlap/sweep, headroom and safe landing bounds. A feasible future air-control sequence is permission to attempt, not a guarantee that PPO will execute that sequence. The original graph, route costs and observation target remain intact. A local blocker may be hopped over onto the current support while approaching a distant route target.

The model still supplies directions and jump requests. A model-selected approach can be retained for at most .6 seconds/.8 metres; no new approach direction is invented. Once a validated jump is pending, its launch direction is retained through the short preparation, then geometry is checked again. Facing alignment remains required before preparation/launch. Unsafe cliffward walking choices are masked, with .25-second hysteresis at the probe boundary; stop remains available. Air control remains learned.

Failed attempts are keyed by launch support, target, mode and local position. After two missed launches/landings, that region is excluded for four seconds and alternatives are reconsidered. This is a local retry delay, not a global jump cooldown or Legacy fallback. Player interference invalidates the current attempt. Same-floor obstacle hops must land beyond the blocker, not merely on the same floor.

## Metrics and evidence interpretation

Physical walking time/distance are measured from grounded Rigidbody motion under a walking command, excluding jump preparation and blocked recovery. Run animation time is measured separately. `BlockedJumpChecks` counts rejected action callbacks/revalidations, **not distinct jumps**; `UnnecessaryJumpChecks` is the walkable-route subset. Downgrades count actual LONG-to-SHORT launches. Landing delay is reported only for landings followed by walking, with sample count; initial spawn touchdown can be included. Animation means omit actors without a ready visual adapter and include an availability metric.

Evaluation files are under `Training/Evaluation/M7D/`. They use the same executable, model, seed 900001, 16 parallel arenas, physics, stage and episode budget with correction OFF/ON. The existing evaluator stops after a completion batch reaches 64 episodes, so a batch can contain 65. This is a same-configuration aggregate comparison, **not a paired per-map statistical trial**; asynchronous completion order and floating-point physics can change which generated episodes finish first. A 100% result in this sample is not a general guarantee.

## Final independent evaluation (2026-10-27)

Rebuilt Evaluation player after the runtime fixes. Both evaluator processes exited 0. Each row uses seed 900001 and a requested budget of 64 episodes.

| Map | OFF success | ON success | OFF mean SHORT/LONG | ON mean SHORT/LONG | OFF/ON physical walk seconds |
| --- | --- | --- | --- | --- | --- |
| Flat | 64/64 | 65/65 | 0 / 2 | 0 / 0 | 0.115 / 7.954 |
| 6 ledges | 64/64 | 64/64 | 0 / 8.266 | 3 / 3 | 0.397 / 2.529 |
| 11 ledges | 57/64 | 65/65 | 0 / 19.391 | 4 / 7 | 0.734 / 3.676 |
| 16 ledges | 43/64 | 64/64 | 0 / 36.453 | 6 / 10 | 1.169 / 5.797 |

16-ledge physical walking distance increased from 0.400 to 5.937 units; mean preparation time decreased from 16.115 to 5.631 seconds. ON mean landing-to-walking delay among measured samples was 0.314 seconds (13 samples/episode). No ON evaluation collision/fall was recorded in this batch. This is evidence for these configurations, not universal obstacle handling. Emotional rest runs in the gameplay host and was verified separately in the rendered Editor, not inferred from headless arena statistics.

Rendered ClimbPlaytest replays (about 59 seconds each): the earlier OFF baseline had 0.280 seconds of physical walking / 0.092 units, 0.120 seconds of Run animation and 0 SHORT / 35 LONG, without completion. The final ON replay had 7.320 seconds of physical walking / 7.796 units, 7.320 seconds of Run animation, 5.660 seconds of preparation and 6 SHORT / 10 LONG; it reached the goal without failed probes or voluntary rests. These are individual replays with timing variability, not average outcomes. OFF was recorded before the later stationary-box attribution fix; its untouched box was outside the ascent route. The rebuilt independent OFF/ON batch above uses the same final executable for both modes.

## Moved-cube behavior

The user rejected omniscient stopping when a distant link is obstructed. With correction enabled, `PolicyRoute` now retains structural approach intent if its collision-clear full route fails. Safe trajectory selection remains first choice. Only at a nearby obstacle/edge may requested jumps become bounded collision probes using the existing jump strengths; three local probes exhaust the current budget. Repeated failed attempts without progress trigger the existing SitDown, Rest and GetUpFromSit animation chain, then renew attempts automatically. Ordinary recoil does not clear the failure count; genuine player interference does. No unconditional SitDown-on-contact behavior is restored.

No-progress discouragement requires six seconds without meaningful grounded progress plus three failed attempts or four stalled direction reversals. The existing sit/stand clip durations surround 2.5 seconds of rest. Route progress, genuine player intervention or completion renews the local retry budget. The actor remains Learned; rest never enables the old movement controller.

The real held-cube test exposed a second cause: `CharacterMotor.ObserveContact` notified external intervention on every contact with any held box, including an entirely stationary obstruction. That erased failed-attempt history and renewed probes indefinitely. Notification now requires the box itself to move toward the actor, or meaningful movement of its current supporting box. Contact normals are oriented toward this actor before the velocity test; actual Unity collision recording showed the opposite collider-pair normal in the moving-box fixture. Stationary self-contact no longer becomes player assistance. Palm notifications and physical impulses are unchanged.

The regression failed before this correction and passed afterward. All 22 M7-D real-physics checks pass, including stationary held obstacle, moving held obstacle, stationary held support and sideways moving support. All 18 M7-C checks pass. The real dynamic spring-held cube test approached the third ledge, made three failed probes, played SitDown/Rest/GetUpFromSit, and launched further attempts while the fourth ledge stayed blocked. `Evidence/M7D/held-cube-rest.png` shows the actual seated character. Cube motion uses the existing drag API, with an overhead carry path to avoid accidentally striking the actor during setup; this is not native mouse gesture validation.

Final moved-cube replay: first SitDown at 16.917 s after three failed probes; Rest 18.238 s; GetUpFromSit 20.746 s; fourth probe 23.048 s. The cube was carried away around 32–37 s. The actor resumed ascent and reached the final goal at 91.884 s, remaining Learned throughout. A further hesitation/rest on ledge 9 shows that inefficient reversals remain possible. CSV and actual cube positions are preserved in `Evidence/M7D/held-cube-character.csv` and `held-cube-position.csv`. Missing-model safety stop and valid-model Learned restart both passed (`no-legacy.txt`).

Final physical palm-API regression: an external impulse on the fourth ledge produced KnockDown at 7.229 s, GetUpFromKnockDown at 9.017 s, then Run by 12.019 s on a lower ledge. The actor continued ascending above y=12 while remaining Learned. This run landed on a lower ledge, not the bottom floor; it verifies falling/recovery/replanning, not every possible bottom-floor recovery. See `Evidence/M7D/post-fall.csv`. The existing box spring API moved (-10,.55) to (-8,1.55), with graph revision 18→22. Native mouse timing remains outside these API checks.

## Preservation

Final Windows x64 normal-player build succeeded with 0 errors at 2026-10-27 12:22 KST: `Builds/M7D/NeverGiveUp.exe`. The Evaluation build also succeeded with 0 errors and its executable was exercised by all eight OFF/ON evaluations. The normal-player executable was built, not separately mouse-playtested; rendered gameplay validation used the Unity Editor. Unity was left out of Play Mode. Independent focused review found no remaining high-priority issue after moving-support attribution was covered.

No scene layout, character/animation asset, Blender file, Rigidbody/gravity/jump-height setting, palm/box physics, or ONNX weights were changed. The vertical candidate SHA-256 remains `da5bb509925ce77ff82755ceff4f8128c20855c6965c49e82ad726fcd6e09f95`. Existing dirty scene/settings/model metadata/preview files were preserved and excluded from this change. Source/patch backups are in `D:/unity_project/ngu-ml/m7d-baseline-4cf1e44/`.

Changed runtime files: `PolicyActionCorrection`, `PolicyRoute`, `GameplayPolicyHost`, `ClimbAgent`, `TrainingPresentation`, `VerticalArena`, and the external-contact attribution in `CharacterMotor`. Editor changes provide bounded probes, replay CSVs and builds; `Training/evaluate.py` adds the correction parameter. Existing automatic/manual Learned-to-Legacy switching remains absent.

## Known scope limits

- The frozen model can still choose inefficient walking/reversals or wait when it cannot find a safe requested-input launch. No rule controller replaces its general decisions.
- Native mouse swipe timing is separate from the palm sampler/physics API regression. API success must not be presented as native gesture acceptance.
- Earlier failed experiments were retained locally: flat v1 0/64 (overshoot), v3 6-floor 19/64 and 16-floor 30/64 (approach/preparation reversals and cliff-input rejection). They are superseded only where final evidence explicitly demonstrates improvement.
- Console history was not cleared. Validation commands issued while Play Mode was stopped produced two explicit `Play Mode required` errors; subsequent checks ran in Play Mode. These are validation-dispatch errors, not a clean-console claim.
