# M7-C gameplay patch and jump-only diagnosis

Date: 2026-10-26 (Asia/Seoul). Branch: `feat/core-prototype`. Baseline: `f91a2ff`.

## Changes

- Ordinary obstacle contact now uses the existing bounded recoil and a short Stumble/Falling response; the collision-only SitDown/Rest/GetUpFromSit chain is removed. Voluntary rest after repeated failures and physical palm/knockdown recovery remain.
- Palm radius .22 → .32; geometric history 100 → 200 ms; forward allowance 50 ms, capped at .75 capsule diameters. Impulse still uses the original 100 ms velocity window and original strength/caps/cooldown. A recent-motion check rejects stopped strokes; forward prediction additionally requires recent direction agreement. One release applies at most one impulse.
- Opposite-direction walking waits until the visual turn is complete (alignment >= .995). Both Learned and Legacy jump execution wait for facing completion before preparation/takeoff. Seated/rest/get-up and airborne presentation preserve yaw. Rendered adapters report actual alignment; unrendered agents retain simulated turning. Delayed rendering cannot silently mark a turn complete. Bounded physical recovery crawl is exempt from standing locomotion's turn gate.
- Per the latest request, Learned-to-Legacy switching and the manual switch menus are removed. No-progress/no-route conditions remain Learned. Missing model, stopped inference, or conflicting authority stop AI control without enabling CharacterBrain. Physical recovery remains active while stopped. Valid-model restart remains available. Existing scenes originally configured as Legacy are preserved; they are not automatically migrated to a different model.

## Jump-only investigation — behavior NOT fixed

The existing candidate selects excessive LONG jumps. This predates this patch: `Training/Evaluation/M7B/final-c-flat.json` records 64 flat episodes, mean 2 LONG jumps, 0 SHORT; the prior M7-B handoff explicitly marked unnecessary jumping and SHORT selection as failed acceptance criteria.

Current real ClimbPlaytest replay, after removing fallback:

- Observed interval 3.412–61.732 seconds (58.320 seconds), exclusively Learned.
- At interval end: 36 LONG launches, 0 SHORT (2 LONG launches already occurred before recording started).
- Observed animation durations: Run .094 s, Inspect 15.052 s, JumpLong 11.158 s, Fall 15.765 s, Land 10.325 s, Idle 5.299 s, Stumble .627 s.
- Sampled action callbacks contained jump values 0 and 2, never 1. Zero includes masked/no-jump callbacks; it must not be interpreted as a deliberate preference for walking.

Code explains how the policy choice becomes the visible behavior:

1. `ClimbAgent.OnActionReceived` accepts the model's jump branch whenever grounded, settled and without a pending jump. It does not require an obstacle or a walking feasibility check.
2. While a jump is pending, `FixedUpdate` stops ground locomotion, prepares, launches, and returns before the ordinary walking branch. Landing masks jump temporarily; once that mask clears, another LONG can be requested. This leaves very little sustained walking.
3. Ground speed is 2.8; configured launch horizontal speed is `6.4 * .65 = 4.16`. This and the height/progress reward structure are plausible training incentives for hopping. They are contributing hypotheses, not experimentally isolated causes. No retraining or reward ablation was performed.
4. The new turn-first requirement adds a deliberate pause on reversal. It does not explain the pre-existing flat-map LONG bias. Both turn waiting and pending-jump stopping can further reduce visible ground motion.

No jump-selection masks, reward weights, ONNX weights, route algorithm, jump impulses, heights, or preparation durations were changed to address this diagnosis. The new action properties and monitor only expose existing decisions. Pending actions are cleared only when AI is explicitly stopped after an error.

## Verification

- Unity Editor compilation and Windows x64 Development build succeeded: `Builds/M7C/NeverGiveUp.exe`, 0 build errors. Build uses ClimbPlaytest; inspection HUD is enabled only in this validation build.
- 18 real Play Mode automated checks passed (`Evidence/M7C/checks.txt`). The turn regression failed before the implementation, then passed. Coverage includes palm early/late release, reversed-away prediction, no-motion/held strokes, compound-collider single hit, bounded ordinary reaction, completed-turn movement, delayed reports, air-facing preservation and recovery crawl.
- Missing-model test: both AI controllers stopped, no Legacy activation; restoring the valid model resumed Learned exclusively (`no-legacy.txt`).
- Actual runtime wall, side and ceiling contacts were recorded, followed by recoil/fall/land/retry. Strong palm API input produced KnockDown and GetUpFromKnockDown. Collision trace also includes voluntary rest with `obstacle=False`; that is intentionally retained. Earlier Legacy observations were made before switch removal; final Learned-only diagnostic uses the new behavior.
- In the 58.32-second trace, sampled grounded-to-air rising transitions had alignment 1.0; no consecutive airborne samples changed yaw by more than .5 degrees. These sampled transitions include possible contact bounces, not just deliberate launches. The trace contains no seated poses, so it does not independently validate seated yaw.
- Existing box physics drag API moved (-10,.55,0) → (-8,1.55,0), updating graph revision 18 → 23.
- Native left-to-right and right-to-left mouse drags were attempted with Computer Use. Both were classified `no stroke` by the game. Valid native swipe timing/early/late release, diagonal swipe and native box-drag acceptance are NOT claimed; API tests are separate.
- Full 16-platform completion was not revalidated. The new turn timing can affect the frozen policy. This patch is not a fix for repeated failed trajectories or excessive hopping.
- Original and candidate ONNX hashes remain `45207ed8777b4f468c77937001916c07eb652e63b2ee5983f92b396d55afab84` and `da5bb509925ce77ff82755ceff4f8128c20855c6965c49e82ad726fcd6e09f95`. No PPO, mesh or Blender animation changes.
- Focused independent code review found no remaining blockers after corrections for recovery crawl, stale alignment and no-fallback shutdown.

## Evidence and preservation

`Evidence/M7C/learned-diagnostic-20ms.csv` is a downsample of the full raw `Logs/M7C/play-20261026_001706.csv`, retained locally. `jump-diagnosis.json` summarizes the full observed interval. `collision-play.txt` records runtime-only fixtures; stopping Play Mode discarded those objects. Historical logs, including an earlier stale-assembly build failure and an unsuccessful editor-monitor attachment, were retained rather than cleared. The final build and final tests succeeded.

Pre-existing scene, model metadata, Quality/URP settings, preview/backup assets and unrelated/generated settings are excluded from this commit and retained locally. The saved ClimbPlaytest scene and animation assets were not edited. Unity was left out of Play Mode after validation.
