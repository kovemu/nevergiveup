# M6.2 collision recognition and recovery

Validated on Windows, Unity 6000.6.0f1, 2026-10-23. Baseline c5c3407.

## Implementation

- CharacterMotor classifies side/underside contacts using normals, contact height, incoming relative speed, recent commanded motion, and sustained blocking. Floor contacts do not trigger the seated reaction. Play Mode established the incoming relative-velocity sign; separating contacts must not count as impacts.
- CharacterRecovery applies bounded Rigidbody impulse (default recoil target .85 units/s, .28 s control suppression), checks supported ground behind the actor, and uses downward response for ceilings. Normal impacts play SitDown -> Rest (.8 s) -> GetUpFromSit. Weak contacts stumble; strong impacts use physical knockdown. New hard landings/strong moving-body impacts interrupt seated recovery.
- ObstacleMemory retains up to 16 temporary geometric observations: collider, bounds, position, contact, normal, approach, takeoff, movable flag and repeats. Evidence expires after 20 seconds or is invalidated by motion/removal. No learning JSON schema changes.
- Brain records an actual jump outcome once, stops the old route, recovers, retreats safely, and replans. Predicted blocked arcs do not become fictitious failed jumps.
- Planner capsule-sweeps the ballistic path and searches alternate takeoff positions. PlatformGraph tracks changed bounds/held state and handles partial-overlap platforms using an exposed takeoff edge.
- ClayAnimator reuses the authored M6 clips. No FBX, Blender, controller or character geometry changes.

## Actual validation

Local evidence is under `Logs/M62` (ignored, not committed).

| Scenario | Evidence/result |
| --- | --- |
| Normal floor/landing | Normal MainPrototype and Prologue progression; no seated collision reaction on ordinary floor contacts. |
| Wall stop/recoil | `play-20261023_115816.txt` and `play-20261023_120849.txt`: real wall contact, impulse (-1.05,0,0), backward motion, old forward command suppressed. |
| Seated sequence | SitDown -> Rest -> GetUpFromSit; Rest about .8 s, zero horizontal velocity while seated. Camera images `Logs/M6/Captures/20261023_120833_0000_manual.png` and `_0001_manual.png` inspected. |
| Collider recognition | Contact logs name wall/ceiling, normal, point, bounds, support, state, repeated count and replan reason. |
| Replanning | Different approach selected after recovery; wall subsequently crossed. A narrow runtime wall can cause a pause while temporary avoidance expires; no continuous collision loop was observed. |
| Underside | `play-20261023_115816.txt`: downward normal, bounded downward response, recovery followed by new approach and goal completion. |
| Moving box | Existing physics drag API moved/released the box; changed bounds invalidated the route. This is API-based physics verification, not a native mouse gesture retest. |
| Palm regression | Strong palm uses KnockDown -> GetUpFromKnockDown. Existing M5 input/learning checks passed. |
| MainPrototype | Latest normal run reached goal; also reached goal after left-palm forced fall/recovery. |
| Prologue | Goal reached in `play-20261023_115816.txt`. |
| 01 Start (1) | `play-20261023_170115.txt`: physical left-palm fall, recovery, approach to 01 Start (1), successful jump/landing, then 01 Start and eventual goal. `start-arcs.txt`: graph plan selects exposed right launch (-5.94,-3.22,0). The exact historical nine-failure memory snapshot was not available; no claim of identical before/after episode. |
| Arc/memory checks | `arc-checks.txt`: ceiling rejected/open arc accepted; repeat takeoff blocked; alternate/opposite approach allowed; moved obstacle reconsidered. Original ceiling test failed before the fix. |

Unity compiled the final changes and `git diff --check` passed. Independent code review found no remaining concrete blockers in the latest recovery/overlap fixes. Hard landing during seated recovery and a strong moving-box impact while seated have code review coverage, but were not separately forced in Play Mode.

## Preservation / reproduction

Source and learning backup: `C:/Users/rudxo/Desktop/NeverGiveUpCharacter/M62_Backup_20261023_0045`.
Additional pre-M7 backup: `C:/Users/rudxo/Desktop/NeverGiveUpCharacter/M7_Backup_20261023_1702`.
Existing user scene, quality settings, preview art and M3 backup changes are excluded from this commit.
Fixture recording disables persistence on the runtime Brain; it does not delete or reset saved JSON. Ordinary gameplay before recording may legitimately update learning.

Open NeverGiveUp / M6.2 / Collision Review in Play Mode. Fixtures and scene reloads are runtime-only; stop Play Mode discards them. No scene regeneration or gameplay respawn was introduced.
