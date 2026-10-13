# NeverGiveUp — M0–M2 prototype

Unity **6000.6.0f1**, URP, Windows PC. 자동으로 전진하는 파란 클레이 캐릭터에게
마우스로 물리적 영향을 주는 2.5D 프로토타입입니다.

## 실행

1. `feat/core-prototype` 브랜치를 checkout/pull 합니다.
2. Unity Hub에서 이 저장소 폴더를 **6000.6.0f1**로 엽니다. 패키지 import와 스크립트 컴파일을 기다립니다.
3. 상단 메뉴 **NeverGiveUp → Build Prototype Scenes**를 한 번 실행합니다.
4. 자동으로 열린 `Assets/_NeverGiveUp/Scenes/Prologue.unity`에서 **Play**를 누릅니다.
5. 프롤로그 목표에 도달하면 `MainPrototype`으로 자동 전환됩니다. 마지막 발판에서 `PROTOTYPE COMPLETE`가 표시됩니다.

수동 GameObject 조립은 필요 없습니다. 생성기는 두 씬, 파란 캐릭터·상자 프리팹,
무광 재질, 경로, 카메라, 입력, 조명을 연결합니다. 두 씬을 Build Settings 맨 앞에
등록하고 기존 SampleScene과 나머지 씬은 유지합니다. 기존 씬을 다시 생성할 때는
덮어쓰기 확인창이 나옵니다. 직접 수정한 생성 씬/프리팹을 보존하려면 재생성하지 마세요.
기존 재질이 있으면 재사용합니다.

## 조작

- 캐릭터 **왼쪽 부분 클릭 → 오른쪽**, **오른쪽 부분 클릭 → 왼쪽**으로 딱콩.
  호버 시 선과 `FLICK >`/`< FLICK` 표시가 방향을 미리 보여줍니다.
- 상자를 누른 채 드래그합니다. 놓으면 중력으로 떨어집니다.
- 캐릭터 이동 키는 없습니다. 점프·재시도·일어나기·복귀는 자동입니다.
- Game 뷰를 벗어나거나 창 포커스를 잃으면 드래그와 커서 점유가 해제됩니다.

## 구성 및 조절

모든 새 코드는 `Assets/_NeverGiveUp/Scripts/` 아래에 있습니다.

| 파일 | 책임 / Inspector 설정 |
|---|---|
| Character/CharacterMotor.cs | Rigidbody 가속, 점프, 지면·전방 감지. Move Speed / Jump Speed |
| Character/CharacterRecovery.cs | 충격, 동적 넘어짐, 대기, 토크 기반 일어나기. Down Duration / Get Up Duration |
| Character/CharacterAnimation.cs | 얼굴 없는 Primitive 클레이 실루엣의 달리기·움츠림·착지·팔/다리 움직임 |
| AI/CharacterBrain.cs | FSM, 웨이포인트, 관찰, 정체 감지, 실패 기억, 제한된 재시도 |
| Interaction/HandInteraction.cs | Input System 마우스 입력·손 커서·딱콩 방향. Flick Force |
| Interaction/PhysicsObjectInteraction.cs | 제한 속도·거리·힘의 스프링 드래그. Spring / Damping / Max Acceleration |
| Interaction/DraggableBody.cs | 상자 물리 설정 및 구간 밖 낙하 후 원위치 복귀 |
| Camera/CameraFollow.cs | 독립된 카메라 추적·Look Ahead·낙하 완충 |
| Stage/StageWaypoint.cs | 발 위치 기준 목표점. Jump To Next가 켜지면 다음 지점으로 점프 |
| Stage/StageController.cs | 경로 진행, 안전 시작점, 겹침을 피하는 복귀 위치 선택 |
| Core/GameManager.cs | 프롤로그→본편 전환, 튜토리얼 한 줄, 완료 표시 |
| Editor/PrototypeSceneBuilder.cs | 씬·프리팹·재질 생성 및 빌드 씬 등록 |

목표점 위치는 생성된 씬의 `Stage/Waypoints - feet positions`에서 변경합니다.
발판과 점프 위치는 함께 수정해야 합니다. Inspector의 Jump Speed는 자동 계산되는
경로 점프 속도의 상한이며 일반 장애물 점프에도 사용됩니다.
실패 횟수는 세션 내에서만 유지합니다. 4회 단위로 정체가 반복되거나 일어나기가
불가능하거나 맵 아래로 떨어지면 해당 스테이지 시작점으로 돌아갑니다.

## 구현 환경의 한계

- 이 브랜치에는 런타임 C#와 Editor 생성 도구가 들어 있습니다. `.unity`/`.prefab`/재질
  실물은 위 메뉴를 통해 사용자 Unity Editor에서 생성됩니다.
- 구현 환경에 Unity Editor/Unity MCP가 없어 **Unity 컴파일 및 Play Mode 실행은 미확인**입니다.
  C# 구문과 Git 메타데이터의 기본 확인만 수행했습니다.
- AI는 이 짧은 사전 설계 경로용입니다. 임의 퍼즐 해결이나 플레이어가 완전히 막은 길의
  돌파를 보장하지 않으며, 막히면 계속 재시도하거나 시작점으로 복귀합니다.
- 단일 Rigidbody 회복이며 풀 래그돌이 아닙니다. 실제 플레이 감각과 물리 튜닝은 별도 세션 대상입니다.
- 엔딩·1인칭 전환·저장·모바일·외부 AI·유료 에셋은 없습니다.
