# M6 Blender 캐릭터 Unity 통합 — 검증 기록

작업 브랜치: feat/core-prototype. 기준 M5: 9ee52174e42fb3e9819a2e98b4d6c671b69f82dc.
상태: 모델/애니메이션 통합 및 주요 Play Mode 검증 완료. 아래 미검증 항목이 남아 있어 전체 완료 판정은 보류한다.

## 원본과 결과물
- 기준: C:/Users/rudxo/Desktop/NeverGiveUpCharacter/NeverGiveUp_Animations_v4_manual.blend
- 작업용 복사본: 같은 폴더/NeverGiveUp_M6_Export.blend
- FBX: 같은 폴더/NeverGiveUp_M6_Animated.fbx
- 백업: 같은 폴더/M6_Integration_Backup_2026-10-22T03-29-02-615Z (Blender 원본 2개와 Unity 기존 Assets/_NeverGiveUp, ProjectSettings)
- Unity: Assets/_NeverGiveUp/Art/Characters/M6/NeverGiveUp_M6_Animated.fbx
- 모델: BlueClay_M6_Visual.prefab, 컨트롤러: NGU_M6.controller (위 M6 폴더)

원본에는 저장 명령을 실행하지 않았다. 중단 후 manual 파일의 저장 시각과 파일 해시가 달라져 재검사했다. 백업/현재 원본/내보내기 복사본의 메쉬 정점·면, 웨이트, 16개 Action의 키프레임·핸들·보간은 모두 동일하다. 본 계층도 동일하다. 상세 해시는 blender-preservation.json 참조. 4,839 정점, 67본/버텍스 그룹, 웨이트 없는 정점 0개. 외형/본/웨이트 수정 없음.

Blender 화면에서 knockdown_front를 24fps로 재생했고 기존 NGU_KnockDown의 동일 프레임 포즈와 비교했다. 수작업판은 팔을 앞쪽으로 모아 지지하고 다리를 접는 반응이 더 뚜렷하다. 게임에서는 수작업판을 선택했다. 머리의 선은 본 표시이며 얼굴 메쉬를 추가하거나 변경하지 않았다.

## 클립과 Animator
FBX는 Generic 리그로 임포트해 원래 본 변형을 그대로 사용한다. Humanoid 리타게팅을 적용했다는 의미가 아니다. 기존 본 이름/계층은 보존한다. 메쉬+Armature만 선택, 카메라/라이트 제외, leaf bones 없음, 모든 Action bake, NLA 중복 bake 없음, 24fps, simplify 0. 작업 복사본의 모든 Action에 Fake User를 유지한다.

| 런타임 상태 | 원본 Action | 클립 초 | 반복 |
|---|---|---:|---|
| NGU_Idle | 동일 | 3.000 | 예 |
| NGU_Run | 동일 | 1.000 | 예 |
| NGU_Inspect | 동일 | 2.625 | 아니오 |
| NGU_JumpShort | 동일 | 1.125 | 아니오 |
| NGU_JumpLong | 동일 | 1.458 | 아니오 |
| NGU_Fall | 동일 | 1.333 | 예 |
| NGU_Land | 동일 | 0.792 | 아니오 |
| NGU_Stumble | 동일 | 1.292 | 아니오 |
| NGU_KnockDown | knockdown_front | 1.500 | 아니오 |
| NGU_AirFlail | NGU_Fall + 절차적 본 반응 | 1.333 | 예 |
| NGU_GetUpFromKnockDown | 동일 | 4.625 | 아니오 |
| NGU_SitDown | 동일 | 2.292 | 아니오 |
| NGU_Rest | 동일 | 3.000 | 예 |
| NGU_GetUpFromSit | 동일 | 4.292 | 아니오 |

원본 NGU_GetUp, NGU_KnockDown, NGU_KnockDown_Front도 FBX 안에 보존되어 전체 원본 Action 수는 16개다. AirFlail이라는 새 Blender Action을 만들었다고 주장하지 않는다.
컨트롤러는 Base Layer의 14개 상태, 기본 Idle. ClayAnimator가 실제 Brain/Motor/Recovery 상태를 읽고 상태가 바뀔 때만 CrossFade한다. SitDown 재생 시간 1.35초, GetUpFromSit 1.8초, Land .24초, Stumble .45초. Idle/Run/Fall/Rest만 원본 루프, AirFlail은 Fall 루프를 재사용한다.

## 기존 게임과 연결
기존 BlueClay 물리 루트, Rigidbody, CapsuleCollider, Motor/Brain/Recovery/Hand/상자/학습 시스템을 보존했다. child VisualRoot_M6 아래 Animator와 FBX를 연결한다. 기존 Clay Silhouette는 유지하되 새 모델이 준비되면 숨긴다. 원시 도형 폴백을 복구하는 코드도 추가했다(기존 씬에서 6개 원시 부위가 개별 비활성화되어 있었음).
모델 높이 1.9, 발 기준 local Y=-.95, 진행 방향에 따라 child만 좌우 회전한다. 실제 씬을 수정했으며 SceneBuilder로 씬 전체를 재생성하지 않았다. SceneBuilder에는 이후 생성 시 같은 비주얼을 연결하는 호출만 추가했다.

Apply Root Motion=false. 월드 위치/회전은 Rigidbody만 소유한다. Unity용 파생 NGU_KnockDown/NGU_GetUpFromKnockDown 클립의 Hips 위치·회전만 bind 값으로 고정해 물리 회전과 중복되지 않게 한다. FBX 안의 원본과 Blender Action은 그대로다. GetUpFromKnockDown은 RecoveryProgress로 실제 Rigidbody의 기립 각도에 맞춰 진행하며, 애니메이션이 물리 회복 완료를 강제하지 않는다. 공중 반응은 매 프레임 Animator 평가 후 본에 작은 회전 델타를 한 번 적용한다. 기존 절차적 Pivot 제어는 새 모델 경로에서는 실행하지 않는다.

## 실제 검증
- Unity 6000.6.0f1 Editor에서 컴파일/임포트 후 실제 Play Mode 실행. 새 파란 스킨 메쉬와 재질 출력, 지면 정렬, T-pose 고착 없음.
- Prologue 완료 후 MainPrototype 전환, MainPrototype 정상 목표 도달.
- Idle/Run/Inspect/JumpShort/JumpLong/Fall/Land, 좌우 진행 방향 전환 확인.
- 약한 .7 / 강한 4.5 충격은 실제 Recovery.PalmSlap API로 실행. 실제 회전·낙하·착지·기립 확인. 이는 마우스 입력 성공과 별도다.
- MainPrototype 점프 중 2.5, 1.2 두 번의 공중 API 충격. AirFlail → KnockDown → GetUpFromKnockDown → Inspect → Run → 재점프 → 목표 도달. play-events.txt의 16.7~22.4초 구간 참조.
- 같은 점프는 trials=3, successes=3 유지, excluded=2→3, last=ExternalInterference, interference=player-palm-slap으로 기록. M5 입력/학습 계약 검사도 Unity 메뉴 실행으로 통과.
- 별도 런타임 blocked-path 조건 시험에서 SitDown → Rest → GetUpFromSit → Run 확인. 씬/학습 파일을 초기화하지 않았다.
- 14개 런타임 클립 각각 13시점 스킨 샘플링: NaN 정점 없음. 이는 모든 관절의 미적 품질을 보장하는 검사는 아니다. clip-sampling.txt 참조.
- rest.png, physical-getup.png는 실제 Unity 게임 카메라 캡처다. 앉은 배와 머리 형태, 물리 기립 자세를 확인했다.
- 마지막 폴백/검수 도구 수정 후 Unity 창 연결이 끊겨 동일 Editor 재생 검증은 아직 못 했다. 대신 Unity 자체 SDK/compiler/reference 설정으로 별도 출력 폴더에 런타임/에디터 코드를 컴파일했고 둘 다 종료 코드 0. 기존 DEVELOPMENT_BUILD 경고 2개 유지. compile-result.json 참조.

## 남은 검증과 제한
1. 실제 마우스 자동 드래그는 no stroke로 판정됐고 SlapCount가 증가하지 않았다. 수동 손바닥 제스처 end-to-end 검증은 미완료다. 기존 M5 입력 판정을 이 테스트에 맞춰 임의로 완화하지 않았다.
2. 상자 드래그 native 입력 성공도 확정하지 못했다. 기존 상자/타격 코드는 수정하지 않았다.
3. 최신 원시 도형 폴백 복구를 Unity Play Mode에서 다시 검증해야 한다.
4. 큰 머리와 단일 CapsuleCollider 특성상 모든 충격 각도의 메쉬/지면 비관통을 보장하지 않는다. 관찰한 기립/휴식에서는 큰 찌그러짐은 없었으나 구르기 각도 전체 검수는 남아 있다.
5. Windows 실행 파일 빌드, Humanoid 리타게팅, 추가 애니메이션 제작은 하지 않았다.

Unity 프로세스는 실행 중인데 Computer Use 창 목록에서 사라져 원래 창 ID 복구도 실패했다. 새 실행은 Already open project 경고가 나왔다. 미저장 상태를 잃지 않도록 기존 프로세스를 강제 종료하지 않고 사용자에게 창 복원을 요청했다.

## 검수 도구
NeverGiveUp > M6 > Play Review Panel. 기록은 기본 꺼짐이며 Start recording으로 명시적으로 시작한다. API 충격 버튼은 실제 Recovery를 호출하지만 마우스 제스처 검증은 아니다. Input test scene은 Play Mode에서만 MainPrototype을 다시 열어 AI를 일시 정지한다. Stop Play Mode로 테스트 변경을 폐기한다. 폴백 버튼 역시 런타임 비주얼만 제거한다. Assets 생성 메뉴는 기존 씬/프리팹을 대상으로 하며 씬 전체 생성 메뉴를 호출하지 않는다.

## 변경 범위
- Scripts/Character/ClayAnimator.cs 신규, CharacterAnimation.cs 연결/폴백 보완.
- Scripts/AI/CharacterBrain.cs 앉기/일어나기 표시 시간만 연결.
- Scripts/Editor/M6Integration.cs 및 M6PlayReview.cs 신규, PrototypeSceneBuilder.cs 연결 호출 1개.
- Art/Characters/M6 FBX, 14 anim, controller, visual prefab.
- 기존 BlueClay prefab 및 Prologue/MainPrototype에 child 연결.
- 기존 로컬 미추적 씬/프리팹/재질과 필요한 M4 에셋은 의존성 보존을 위해 포함. 기존 M3Backups, M4 preview scene 및 사용되지 않는 M4 visual prefab은 변경/커밋하지 않음. M4 visual prefab에는 원래부터 없는 GUID 참조가 있으며 M6 활성 계층에서 사용하지 않는다.
- 기존 로컬 BuildSettings의 Prologue/MainPrototype 등록을 포함. 관련 없는 QualitySettings/URPProjectSettings 로컬 변경은 별도 보존.
