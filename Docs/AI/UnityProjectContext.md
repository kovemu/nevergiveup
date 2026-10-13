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
