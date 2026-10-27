# M8-A 검증 결과

검증 환경: Unity 6000.6.0f1 / Windows / feat/core-prototype. 2026-10-27~28 작업.

## 구현

UI Toolkit 전용 창, Scene 마우스 배치, 5종 프리팹 팔레트, XY 편집과 Z=0 고정, 격자 스냅, 선택·복제·삭제·Undo/Redo, 네이티브 씬 저장, 시작·목표, 제작 구역, 플레이어 상자 도움 구역 표시를 추가했다. OnlyUpMain은 발판 10개를 가진 별도 샘플 씬이다.

PlatformGraph/PolicyRoute/CharacterMotor의 기존 물리 검사를 사용한다. WALK/SHORT/LONG/표본에서 불가/미검증을 구분한다. 지형·물리 설정 변경 후 이전 결과는 미검증으로 돌아간다. 한 검사당 128 Collider 제한이 있으며 큰 맵은 구역별로 검사한다. 물리 경로 후보와 실제 AI 완주는 별도 결과다.

## 확인한 결과

| 항목 | 결과 |
|---|---|
| 실제 Scene 마우스 배치 | 프리팹을 (14.5, 7.25, 0)에 배치 |
| 실제 UI 크기 변경·Undo/Redo | 너비 2.6→3.5, Undo 2.6, Redo 3.5 확인 |
| 실제 UI 위치·경로 경고 | Y=20으로 이동, 도달 경로 미확인 경고·빨간 연결 확인 |
| 저장 후 다시 열기 | 생성 물체와 변경한 Transform 유지 PASS |
| 최종 샘플 | 검증용 임시 발판 제거 후 원래 10개 발판으로 저장 |
| 편집 회귀 검사 | 15 PASS, failures=0 |
| 저장하지 않은 다른 씬 보호 | 비활성 additive 씬의 미저장 변경도 씬 교체를 차단 PASS |
| 실제 Learned AI 등반 | 한 번의 Play 실행에서 20.878초에 발판 10 목표 도달 |
| AI 동작 기록 | SHORT 4회, LONG 6회, 걷기 3.620초 / 3.773 거리 |
| 손바닥·반응·회전 회귀 | 기존 검사 18 PASS, Failures=0 |
| 상자 이동 회귀 | 기존 물리 드래그 API로 (-10,.55,0)→(-8,1.55,0), 그래프 revision 갱신 PASS |
| Windows 빌드 | Succeeded, errors=0; Builds/M8A/NeverGiveUp.exe 생성 |

AI 완주는 이 샘플의 단일 실행 결과이며 임의 맵의 완주나 성공률을 보장하지 않는다. 최종 authored-map 바닥 생성 방지 수정 후 편집 회귀 검사와 Windows 빌드를 확인했다. AI 완주 기록은 그 수정 전 동일한 샘플 지형에서 수집했다.

## 남은 수동 확인

실제 Game 뷰에서 자동화 도구의 마우스 타격을 두 번 시도했지만 입력이 stroke로 수집되지 않아 실제 제스처 성공은 확인하지 못했다. 손바닥 타격은 기존 판정·반응 API 회귀 검사로 확인했다. 상자도 실제 물리 드래그 API로 검증했으며 네이티브 마우스 드래그 전체 흐름은 미검증이다. 이 둘은 실제 마우스로 추가 확인해야 한다. Windows 실행 파일 생성은 확인했지만 배포 환경별 실행 시험은 하지 않았다.

## 보존 및 증거

Prologue, MainPrototype, ClimbPlaytest와 NGUClimbVertical 모델은 작업 시작 시 SHA256과 동일하다. 기존 사용자 변경사항과 별도 아트 파일은 커밋 대상에서 제외한다. AI 모델·애니메이션·CharacterMotor·손바닥 타격 구현은 수정하지 않았다. StageController는 새 LevelMap이 있는 씬에서만 오래된 자동 바닥 생성 처리를 건너뛴다.

Evidence/M8A의 검사 출력과 onlyup-play.csv에 원시 결과가 있다. route-warning.png는 실제 편집 화면, ai-goal.png는 실제 목표 도달 화면이다.

사용법: [M8A_LEVEL_EDITOR.md](M8A_LEVEL_EDITOR.md)
