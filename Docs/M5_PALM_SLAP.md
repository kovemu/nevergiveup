# M5 — Palm slap integration

Baseline: `6add864` on `feat/core-prototype` (M3.3). Code-only update: pull and open
existing Prologue/MainPrototype scenes. **Do not regenerate or upgrade scenes.**
No package, ProjectSettings, scene, prefab, graph, planner or learning-schema changes.

## Input

Left press on a box owns the entire gesture until release/cancel. Other left presses
start a palm stroke. Release evaluates only the last 100ms of path against current
character colliders; one release can deliver one impulse. Earlier crossings expire.
No click flick, held push, right-click attack, lifting or character dragging remains.
Focus loss, viewport exit, pause, missing mouse and component disable cancel safely.

Motion uses viewport-height units per second, a minimum 25ms observation and .008
viewport-height net displacement. Cursor warps over 12 heights/s and frame gaps over
150ms reset sampling. All recent positions are projected using the release camera,
so camera-follow motion alone cannot create a hit. Small strokes and stopped strokes
produce no attack. Speed maps continuously through a clamped Inspector curve.

## Initial Inspector values (not gameplay-tuned)

| Component | Field | Default |
| --- | --- | --- |
| HandInteraction | Minimum / Maximum Slap Speed | 0.15 / 3 viewport heights/s |
| HandInteraction | Minimum / Maximum Slap Impulse | 0.25 / 4.5 N s |
| HandInteraction | Slap Response | EaseInOut, 0–1 |
| HandInteraction | Slap Radius | 0.22 world units |
| HandInteraction | Slap Cooldown | 0.18 s |
| CharacterRecovery | Airborne Impulse Multiplier | 0.85 |
| CharacterRecovery | Angular Impulse Multiplier | 0.65 |
| CharacterRecovery | Maximum Knockback Speed | 10 units/s |
| CharacterRecovery | Maximum Angular Speed | 7 rad/s |
| CharacterRecovery | Stumble / Knock Down Threshold | 0.4 / 1.6 N s |

Old flick settings are retired; new fields initialize on existing components.
`ApplyM3Defaults` and `FlickCount` remain compatibility shims for old editor/scene code.
The old serialized collision-recovery configuration is preserved.

## Physics, AI and learning

Each hit adds momentum once, plus contact lever-arm torque scaled separately. The
predicted resulting linear/angular speed is bounded; multiple hits before a physics
step account for already queued impulses. Gravity and dynamic collisions remain active.
Weak hits preserve balance; stronger hits unlock Z rotation. Hits during get-up reset
its stability timer. Get-up continues to require support, stability and upright clearance.

Motor control is suppressed briefly on the ground and until actual landing for a struck
flight. Brain still ticks outcome observation while movement is suppressed, then replans
from real support after recovery. No new respawn or position-reset calls are introduced.
`player-palm-slap` is recorded through existing external versions/timestamps. Finish also
checks versions, covering interruption before the next observation tick. External outcomes
stay in recent observations but do not update clean failure rewards. M3.3 JSON version 4,
map signatures, successful experience and SHORT/LONG heights are unchanged.

## Animation

No FBX, .anim or Animator Controller assets were found in this repository. Primitive
clay animation remains the default, with head wobble and slower airborne arm/leg flailing.
The rest sequence has separate SittingDown, Resting, GettingUpFromSit states. Physical
recovery uses GettingUp independently and never resumes Run before physics recovery.

Optional later rig integration: put an Animator on a **visual child**, assign it and
Visual on CharacterAnimation, and provide in-place states on layer 0 named NGU_Idle,
NGU_Run, NGU_Inspect, NGU_JumpShort, NGU_JumpLong, NGU_Fall, NGU_Land, NGU_Stumble,
NGU_KnockDown, NGU_SitDown, NGU_Rest, NGU_GetUpFromSit, NGU_GetUpFromKnockDown.
Humanoid upper arms/legs/head auto-bind; generic rigs require those five transform fields.
Root motion is off; visual-root position/rotation is restored, not written to the Rigidbody.
During physical tumbling, Animator pauses and only the mapped bones receive procedural
rotations from captured poses. They are restored before Animator resumes. No simultaneous
Animator/procedural writes to those bones. Missing states are not crossfaded into.

Imported clips must be authored/imported in-place, including Hips motion appropriate to
this capsule. Unavailable clips, skinning, clip timing and mesh-floor alignment cannot be
validated until the real rig is supplied. Full ragdoll is not included.

## Validation and local execution

Implementation environment: no Unity Editor/MCP, .NET SDK or C# compiler. All 18 gameplay
and editor C# sources passed tree-sitter syntax parsing; this is **not Unity compilation**.
Metadata GUID/reference hygiene and whitespace checks were run. No Play Mode or PhysX
results are claimed.

Added `NeverGiveUp > Validation > Check Palm Input and Learning` (also callable in batch
as `NeverGiveUp.Editor.PalmSlapChecks.Run`). It checks stationary/short strokes, speed
ordering, 20/30/60/120/240fps, left/up direction, expired paths, cursor warps, stalled frames,
resolution normalization, and exclusion of external failures without changing scenes or
saving a JSON file. **This Unity check has not run in the implementation environment.**

Open Prologue, enable HandInteraction/Show Slap Debug, then Play. Debug displays speed,
direction, requested impulse, hit/miss, grounded state, recovery state, velocity, angular
velocity and external-control suppression. Learning debug remains available. Both debug
panels are excluded from ordinary release builds. Repeat in MainPrototype.

The following requested runtime checks are **pending local Unity execution**:

- Input 1–8: click/slow/fast/miss/left-right/diagonal, unchanged box drag, focus/exit cancellation.
- Physics 9–16: weak/strong ground hit, rising/falling/repeated air hit, real collisions,
  AI suppression, speed caps and CCD under actual frame rates.
- AI/animation 17–24: external observation/exclusion, flail, rig/root separation, physical
  get-up versus seated rest, autonomous resumption and both stage flows.

Remaining tuning: perceived impulse/displacement varies with momentum, contact height,
friction and terrain. No measured displacement or validated aesthetic result is available.
Hits query the release-time character pose against the recent hand path; historical moving
character poses are not rewound. This is intentional release-time collision detection.
