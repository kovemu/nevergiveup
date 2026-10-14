# NeverGiveUp — M3

Unity **6000.6.0f1**, URP, Windows PC, 2.5D. 얼굴 없는 파란 클레이 캐릭터가 스스로
올라가고, 마우스 손은 작은 직접 간섭과 상자 조작을 담당합니다.

## M4.0 — Character Art Preview (feat/character-polish)

A seamless faceless blue clay OBJ character model is now available at
`Assets/_NeverGiveUp/Art/Characters/BlueClay_M4_Concept.obj`.
Open **NeverGiveUp > M4 > Create Clay Preview (No Gameplay Changes)** in Unity
to generate a **separate, non-destructive preview**. This is the static shape
approval pass, not a replacement for the rigged gameplay actor. The existing
M3 character, AI, input and scenes remain unchanged.

See [Docs/M4_CHARACTER_MODEL.md](Docs/M4_CHARACTER_MODEL.md) for the exact steps,
constraints and M4.1 rig/animation handoff.

## M3.3 — Actual strategy learning and loop-aware navigation

**Code-only update.** Pull `feat/core-prototype` in your existing Unity 6000.6.0f1
project and run `Prologue`. Do not rebuild or upgrade existing scenes again.

- Every **AI-initiated** jump now has a `JumpAttempt`, including the no-route
  edge probes that previously bypassed learning. Player-thrown falls are not
  confused with an AI jump, and external interference is still excluded from
  self-inflicted failure rewards.
- `LearnedAction.mode` stores the chosen **Short/Long** strategy. The
  planner evaluates that learned mode first, switches only if the current
  mode is physically infeasible, and records the **actual** mode in each
  observation. Neither mode changes its configured vertical height.
- Shortfalls, side collisions, blockages and uncertain failures can propose
  switching between the two jump modes; overshoots or ceiling hits favor
  lower jumps. Timing, approach, landing and horizontal steering are still
  refined independently.
- A physically completed jump on the same floor past a small obstacle
  counts as success. An exploratory jump which merely returns to its
  launch floor cannot produce a false success.
- Route costs now incorporate previous physical failures as well as
  repeated platform transitions when there has been no meaningful ascent.
  The same route remains available when it is the only path. New heights
  clear the temporary loop counters; the character never decides to quit.
- For graph links with no valid trajectory, the AI performs an observable,
  bounded exploratory jump instead of silently skipping the attempt.
- The learning JSON schema is now version 4 and the M3.3 map signature is
  distinct. Existing files remain on disk; learning begins fresh under
  the new logic.

**Manual acceptance checks (not run by GitHub editing tools):**

1. Let the AI fail the same ledge at least 5-10 times without hand interference.
   Verify trial counts actually rise, attempted jump modes vary when needed,
   and the debug display changes from `Short` to `Long` on suitable failures.
2. Give it a small same-floor hurdle. Confirm the first successful jump is
   not incorrectly marked as failure, and normal movement resumes.
3. Deliberately make an unreachable ledge. Confirm every AI jump is recorded;
   after repeated failure, the character rests and resumes without teleporting.
4. Introduce an alternate route / movable box. Confirm repeated failing
   edges become less preferred and the AI can change route, but still keeps
   trying when no alternative exists.
5. Revisit a successful ledge after learning. Verify successful mode and
   launch strategy are prioritized. Test Prologue -> MainPrototype transition.
6. Check Console errors and `CharacterBrain/Show Learning Debug`.
   A real improvement in completion rate is not guaranteed before Unity
   physics and learning-policy tuning.

## M3.2 — Two fixed jump heights & box-top continuation

Code-only patch; no scene/prefab regeneration and no `Upgrade Open Scenes` rerun required.
Pull `feat/core-prototype`, let Unity compile, then launch `Prologue`.

- **SHORT** jump: target height **1.55 Unity units**, **LONG** jump: **3.0 units**;
  both are additionally limited by existing `CharacterMotor/Jump Speed` (7.8 by default).
  Set `Short Jump Height` and `Long Jump Height` on the existing Blue Clay
  `CharacterMotor` Inspector to tune; the choices remain exactly two.
- The AI chooses SHORT when a physically achievable landing allows it; LONG
  only for a higher/distant landing. It still learns run-up, launch position,
  horizontal timing, air steering, and landing. It cannot learn arbitrary
  additional vertical strengths.
- Replan after landing on a stationary movable box, using that box as the
  current physical support and the next reachable platform as the target.
  Passive support jitter no longer discards a valid launch plan.
- When a full route is not available from a box, consider a reachable onward
  ledge. This does not conjure a route through an impossible obstacle.
- The no-route exploratory hop also uses SHORT or LONG, never the old
  unrestricted default vertical impulse.
- Previous JSON files are left intact but are not loaded into the changed
  fixed-height trajectory policy; new learning is stored with the M3.2 map signature.

**Check locally (Unity not available in the implementation environment):**

1. On a low Prologue ledge the debug overlay should show SHORT and a visibly
   lower apex. It should not jump excessively high after reaching the ledge.
2. Place a movable box, let the character land and stabilize on it, then
   observe an onward jump toward the next platform. It must not stand frozen.
3. Move the box under the character; the plan should be reconsidered rather
   than teleporting or standing on stale coordinates.
4. Where a taller ledge needs LONG, the planner should select LONG rather
   than increasing SHORT height.
5. Revisit the same jump and inspect attempts/success counts. Gameplay QA
   and physics tuning must be done in the local Unity Editor.

## M3.1 — AI perception, routing and persistence fix

This is a **code-only** update to the existing M3 prototype. Pull `feat/core-prototype`,
allow Unity to recompile, and press Play in the existing `Prologue` scene.
**Do not run Build Prototype Scenes again.** Existing M3 scene upgrades remain valid.

- Multiple forward-height spherecasts distinguish wall/box faces from ordinary floor slopes.
- Low steps and boxes are recognized as possible intermediate landing supports.
- A jump counts as successful when the character actually stabilizes on the intended
  platform, even if it is not exactly on the planned X coordinate.
- Path selection favors progress toward the summit and discourages immediate
  backtracking; necessary descents are still allowed.
- When the route graph cannot find a full path, the character moves toward the ledge,
  tries a physically bounded jump, and tries again. It never teleports to the goal.
- After several unsuccessful/blocked attempts the character **sits briefly** (wordless
  crouched clay pose), stands up and continues trying. Rest never means giving up.
- Dragging a box causes the route context to be re-evaluated. The character may jump
  onto a stationary reachable box, try jumping over an obstruction, or keep trying
  when a blockage is impossible. It does **not** pick up or push objects itself.
- The map-signature prefix changed for M3.1. Old M3 JSON learning is preserved on
  disk, but **not loaded** into this revised planner: learning begins fresh.

**Manual Unity QA (not run in the code-editing environment):**
1. In the Prologue, let the AI climb the low obstacle. It should continue toward the
   exit, not jump back off the same ledge in an endless loop.
2. Place a box ahead and move it while the AI approaches. It should notice the new
   obstruction, try a reachable top/alternate local jump, and re-plan after movement.
3. Block the path with a physically impassable arrangement. The AI must keep attempting,
   occasionally sit for a short rest, and then resume, without teleporting.
4. Flick it from an upper platform. It should fall under physics, land below and
   reconsider the route without respawning.
5. Revisit a previously solved jump. Check that learned attempts and successful
   actions are retained in the development-only on-screen AI debug information.

Unity compilation, Play Mode results and learned performance are **not verified**
by the GitHub editing environment and need local Unity validation.

## 기존 프로젝트에 적용

1. `feat/core-prototype`을 pull하고 Unity 6000.6.0f1로 엽니다.
2. 직접 수정한 씬이 있다면 **Build Prototype Scenes를 다시 실행하지 마세요.**
3. `Prologue` 씬을 열고 **NeverGiveUp → Upgrade Open Scenes to M3 (Preserve Layout)**를 실행한 뒤 저장합니다.
4. `MainPrototype` 씬에도 같은 작업을 합니다. 씬 백업은 해당 Scenes 폴더의 `M3Backups`에 생성됩니다.
5. `Prologue`에서 Play. 기존 프롤로그→본편→정상 완료 흐름은 유지됩니다.

업데이트 메뉴는 기존 발판·캐릭터 외형·카메라·배치·프리팹 에셋을 교체하지 않습니다.
실제 안전 바닥과 좌우 경계를 추가하며, 기존 기본값과 정확히 일치하는 딱콩/회복 값만
M3 값으로 변경합니다. 다른 Inspector 사용자 설정은 보존합니다. Undo가 가능합니다.
씬을 업데이트하지 않아도 Play 시작 시 누락된 안전 바닥과 발판 그래프를 런타임에 생성합니다.

아직 씬을 생성하지 않은 프로젝트에서만 **NeverGiveUp → Build Prototype Scenes**를 실행합니다.
기존 생성 씬/프리팹을 덮어쓰는 재생성에는 확인창이 유지됩니다.

## 조작과 물리

- 캐릭터 왼쪽 클릭은 오른쪽 딱콩, 오른쪽 클릭은 왼쪽 딱콩입니다.
- 동일한 버튼을 누른 채 가까이 접촉하면 약한 수평 밀기가 이어집니다. 캐릭터 집기/공중 운반은 없습니다.
  원격 M0–M2에는 별도 밀기 코드가 없어 기존 클릭 입력에 접촉 유지 동작을 연결했습니다.
- 상자는 기존 방식대로 누르고 드래그한 뒤 놓습니다.
- 기본 딱콩 힘 1.5, 위쪽 편향 0.05, 넘어짐 기준 1.0, 추가 회전 충격 0.6.
- 질량은 1을 유지합니다. 접지 마찰과 지면 감쇠로 미끄러짐을 줄이며, 충돌에서 전달된
  충격을 중복 적용하지 않습니다. 약한 충격에는 비틀거림과 제어 회복 시간이 있습니다.
- 넘어짐→안정화→일어설 공간 확인→토크 기반 일어나기. 공간이 없으면 주변 지지 공간으로
  힘을 가해 조금씩 이동합니다. 회복 시간 초과를 이유로 캐릭터를 순간이동시키지 않습니다.

## 추락·경로

**일반 추락 리스폰은 제거되었습니다.** 실제 아래 발판이나 안전 바닥에 착지한 뒤,
현재 지지 Collider에서 목표 발판까지 그래프 경로를 다시 선택합니다.
웨이포인트는 마지막 목표 위치 등 기존 저작 데이터로 유지합니다.

`Stage/Terrain` 아래 Collider들을 그래프에 등록합니다. 상자도 지지 발판이 될 수 있습니다.
현재 물리 한계 내의 연결을 계산하고, 움직인 상자는 0.6초마다 다시 반영합니다.
안전 바닥은 기존 최하단 지형보다 2.6 유닛 아래에 추가됩니다. 기본 캐릭터의 머리 공간과
첫 발판으로 돌아가는 점프를 고려한 값입니다. 맵 크기나 점프 한계를 바꾸면 함께 조정하세요.

`RequestRespawn()`은 이전 코드 호환용 이름만 유지하며 이제 위치를 바꾸지 않고 재탐색만 요청합니다.
`ResetAt`/`Teleport`는 기술 복구용 API만 남아 있고 정상 게임플레이에서는 호출되지 않습니다.

## 실제 경험 기반 학습

- 구간 ID + 주변 지형/상자 상태를 묶어 별도 행동 후보와 관측 성과를 기억합니다.
- 도약 위치, 접근 속도, 기다리는 시간, 수평 속도 배율, 목표 착지점, 공중 보정,
  점프 궤적 여유를 제한된 범위 안에서 조정합니다.
- 거리 부족/초과, 옆면/천장 충돌, 불안정 착지, 막힘을 관측 증거에 따라 구분합니다.
  판단할 증거가 없으면 `Unknown`으로 기록하고 작은 범위만 탐색합니다.
- 실패 방향에 맞춘 새 후보를 만들고, 실제 성과와 불확실성을 반영해 선택합니다.
  안정적으로 성공한 후보를 우선 재사용하며 성공률이 떨어지면 다른 후보를 탐색합니다.
- 단순 충돌은 성공이 아닙니다. 목표 위치 근처에서 실제 지지 접촉과 안정 상태가
  0.3초 유지되어야 성공으로 기록합니다. 강제 성공, 순간이동, 점프 한계 증가는 없습니다.
- 손 간섭 버전/시각과 움직인 상자의 시각을 시도에 연결합니다. 외부 간섭, 바뀐 환경,
  중단된 시도는 기록하되 자기 능력 실패 보상에서는 제외합니다.
- 시도 기록에는 실제 도약 위치·첫 물리 스텝 속도, 명령 속도, 착지, 안정성, 파라미터가 포함됩니다.

학습 JSON은 `Application.persistentDataPath/NeverGiveUp-learning-<map signature>.json`에 저장됩니다.
Windows에서는 보통 `%USERPROFILE%/AppData/LocalLow/<CompanyName>/<ProductName>/` 아래입니다.
스키마 버전은 3이며 맵 식별자와 맞지 않는 기억은 불러오지 않습니다. 지형 변경 시 별도 파일을 사용합니다.
최대 128개 문맥, 문맥당 8개 행동, 최근 64개 관측을 유지하며 구간별 누적 성과는 따로 남습니다.
10초 간격, 일시정지, 종료 때 저장합니다. JSON 오류/쓰기 실패가 발생해도 세션 내 학습은 계속됩니다.
`CharacterBrain/Persist Learning`을 끄면 기존 JSON을 읽거나 쓰지 않고 해당 실행 내에서만 학습합니다.

학습 데이터 초기화: Play를 중지하고 해당 `NeverGiveUp-learning-*.json` 파일을 삭제합니다.
캐릭터의 성장 정도나 반드시 몇 번 만에 성공한다는 결과는 강제하지 않습니다.

## 개발 관찰 및 주요 코드

Editor와 Development Build에서만 `CharacterBrain/Show Learning Debug`로 상태, 목표 발판,
시도/성공/제외 수, 최근 결과, 파라미터, 성공 기억 재사용과 간섭 여부를 확인합니다.
일반 플레이어 빌드에는 개발 수치를 표시하지 않습니다.

- `CharacterMotor`, `CharacterRecovery`, `CharacterAnimation`: 실제 이동·접촉·회복·기존 실루엣 애니메이션.
- `HandInteraction`, `PhysicsObjectInteraction`, `DraggableBody`: 기존 입력과 외부 간섭 추적.
- `PlatformGraph`, `StageController`: 지지 발판·물리 한계 연결·재탐색·안전 바닥.
- `CharacterBrain`: 기존 FSM의 경로 선택/접근/관찰/점프/착지/회복 흐름.
- `AdaptiveJumpPlanner`: 유한한 점프 계획과 관측 기반 파라미터 개선.
- `JumpAttempt`: 실제 시도 관측과 결과 분류.
- `LearningMemory`: 문맥별 행동 성과, 성공 경험, JSON.
- `PrototypeSceneBuilder`: 기존 생성 기능 및 배치를 보존하는 M3 업데이트 메뉴.

## 알려진 제약

- 구현 환경에는 Unity Editor/MCP가 없습니다. **Unity 컴파일·Play Mode 실행·학습 성능은 미확인**입니다.
  개발 중 C# 구문·메타파일·금지된 일반 리스폰 호출을 확인했습니다.
- 딱콩 0.6–1.0 유닛 이동은 초기 튜닝 목표이며 실측된 결과가 아닙니다. 움직이던 속도,
  바닥 재질·경사·접촉 지점에 따라 이동 거리가 달라집니다.
- 그래프는 현재 작은 2.5D Collider 지형용입니다. 기하학적으로 가능한 연결도 천장이나
  이동 물체 때문에 실패할 수 있으며 경험으로 다시 시도합니다. 범용 퍼즐 해결 AI가 아닙니다.
- 완전히 갇혀 일어날 공간/경로가 없다면 현재 위치에서 계속 재탐색합니다. 해결을 가장해
  자동 순간이동하거나 충돌을 무시하지 않습니다.
- 복잡한 MeshCollider/겹친 지형의 실패 분류는 불확실할 수 있습니다. 보수적으로 Unknown 또는
  EnvironmentChanged를 사용합니다. 실제 성장 속도와 물리 튜닝은 후속 Unity 세션에서 확인해야 합니다.
