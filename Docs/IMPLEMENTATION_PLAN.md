# M0–M2 implementation plan

User-approved scope: autonomous blue faceless clay character in a 2.5D physics prototype;
mouse flick and box dragging; knockdown/get-up; waypoint FSM and bounded retry adaptation;
prologue followed by six ascending platforms. No ending, death, saving or external assets.

1. M0: CharacterMotor (ground sensing, acceleration, ballistic jumps), CharacterAnimation
   (procedural pose), CameraFollow (damped tracking), StageWaypoint and StageController
   (authored route, safe origin), editor composition tool.
2. M1: CharacterRecovery (dynamic knockdown, grounded get-up, collision-safe upright
   placement), HandInteraction (Input System, cursor/impulse feedback), DraggableBody
   and PhysicsObjectInteraction (bounded spring force with CCD).
3. M2: CharacterBrain (all requested states, obstacle/gap observation, launch preparation,
   failure counts, bounded retry variants, stalled recovery), GameManager (transition/HUD),
   complete editor-generated Prologue and MainPrototype scenes and prefabs.
4. During implementation: check C# syntax/asset metadata and repository hygiene with
   available tools. Unity compilation and Play Mode cannot be claimed without an Editor.
5. Commit and push feat/core-prototype, then stop. No post-completion review cycle.

Interfaces: Motor.Drive/Jump/Stop, Recovery.Flick/ResetAt; Brain.Initialize(stage)/RestartRoute;
StageController.Current/Advance/RequestRespawn; GameManager.CompleteStage.
Retries remain finite in intensity, indefinite in number. Respawn searches a clear capsule
at the stage origin, resetting route and motion while retaining failure history. A box
falling below the level returns to its own origin. Input focus loss releases dragging/cursor.

# M3 implementation (supersedes M0–M2 respawn/retry rules above)

Baseline: 97e6309b898583765770bd2c6e82a6766f792048. Scope supplied by user; no final review phase.

1. Preserve Rigidbody, animation silhouette, camera and box drag. Reduce direct hand impulses,
   separate stumbling from knockdown, apply grounded friction/damping, check standing clearance.
2. Replace sequential-index navigation with a small collider support graph; retain waypoint authoring
   for the final goal. Add an actual bottom floor and boundaries. Never call a position reset for falls.
3. Record actual attempts, collisions, support contacts and stable landings. Keep external interference
   timestamps separate from clean outcomes. Classify unknown evidence without invented failure causes.
4. Use bounded contextual action candidates with empirical values and optimistic exploration; propose
   parameters from observed failure direction and reuse successful parameters. Keep all motor ceilings.
5. Persist bounded versioned map-specific JSON and expose learning counters in development builds only.
6. Preserve-layout open-scene upgrade with backup, Undo, and only exact legacy defaults migrated.
7. Basic development syntax/reference/metadata checks, then commit and publish feat/core-prototype.
   No separate comprehensive review, balance evaluation, long playtest, or post-completion refactoring.

New interfaces: PlatformGraph.Support/At/Next/Context; AdaptiveJumpPlanner.Plan/ComputeVelocity/Improve;
JumpAttempt.Tick/Collision/Finish; LearningMemory.Section/Select/Observe/Save.
CharacterMotor.Contact and ExternalVersion connect physics/hand/object evidence to an active attempt.
StageController.RequestRespawn and CharacterBrain.BeginRespawn remain non-teleporting compatibility aliases.
