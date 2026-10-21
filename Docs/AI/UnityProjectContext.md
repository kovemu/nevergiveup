# NeverGiveUp project context

Inspected baseline: aa32681 (2026-10-14 KST).
Confirmed from ProjectVersion, manifest, QualitySettings and EditorBuildSettings:
- Unity 6000.6.0f1, URP 17.6.0 (quality-level pipeline assets).
- Input System 1.20.0; activeInputHandler = 1. Use Mouse.current, not legacy Input.
- Initial content: SampleScene and template tutorial scripts. No existing gameplay or custom assemblies.
- Unity Test Framework 1.8.0 installed. No existing game tests.
- No connected Unity Editor/MCP or local Unity executable available in implementation environment.

Implementation is under Assets/_NeverGiveUp. Motor owns physics; Brain owns decisions;
Recovery owns knockdown/get-up; StageController owns route/respawn; GameManager owns scene transition.
No character keyboard controls. Single rigidbody constrained to XY plane, no death, no external AI.
Existing Packages and ProjectSettings are preserved. Editor builder creates standalone playable
scenes and prefabs and appends them to build scenes without removing existing scenes.
The user requested implementation only; comprehensive review, balance and long playtesting
are reserved for a separate session.

## M3 update

The M0–M2 respawn/sequential waypoint descriptions above are historical.
M3 keeps the existing MonoBehaviour responsibilities and replaces normal respawn with physical landing
and collider platform graph replanning. Stage/Terrain owns graph geometry and generated safety bounds.
The final StageWaypoint remains the goal; normal fall recovery never resets its index or the character pose.
The character mass remains 1. Ground friction/damping and a weak contact-only hand push replace exaggerated impulses.

New plain classes: LearningMemory (+ bounded serializable records), AdaptiveJumpPlanner,
JumpAttempt. One new MonoBehaviour: PlatformGraph. No package changes, external AI or training pipeline.
JSON memory is map/version-specific, under Application.persistentDataPath. Dirty data saves periodically
and on pause/exit. External interference outcomes do not update action failure values.

Existing local scenes are not in the baseline repository. The preserve-layout upgrade menu creates
scene backups and adds only missing support geometry and exact-legacy default migrations. Runtime
compatibility also adds missing geometry; it never writes source scenes/prefab assets while playing.
CameraFollow and the blue faceless primitive geometry remain unchanged.
Unity Editor remains unavailable; no Unity compilation or gameplay claims were made.

## M5 palm slap update

Extends M3.3 baseline 6add864 without regenerating assets. HandInteraction owns mutually
exclusive box/palm gestures; HandDragSampler measures recent normalized screen motion.
Recovery owns additive contact impulses and physical get-up; Motor gates AI authority;
Brain continues learning observation and replans after real landing. JSON schema/map
identity and SHORT/LONG policy are unchanged. CharacterAnimation offers a primitive
fallback and optional child Animator integration, with separate seated/physical get-up.
See Docs/M5_PALM_SLAP.md for defaults, setup, checks and unverified runtime cases.
