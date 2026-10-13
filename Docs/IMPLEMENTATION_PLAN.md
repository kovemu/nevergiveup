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
