# Tactical Chess

체스의 기본 규칙 위에 **기물별 고유 스킬**과 **로그라이크 스타일의 증강(강화) 시스템**을 더한 2인 온라인 대전 체스 게임. Unity 6000.5.0f1 + Photon Fusion 2로 제작 중.

## 게임 개요

일반 체스 규칙(이동/캡처/체크/체크메이트/스테일메이트/캐슬링/앙파상/프로모션)을 그대로 따르되, 나이트·비숍·룩·퀸·킹 각 기물에 쿨타임 기반 고유 스킬을 부여하고, 게임 진행 중(프로모션 발생 시 또는 10턴마다) 두 플레이어가 각자 카드 3장 중 하나를 골라 증강을 쌓아가며 판을 키워가는 방식이다. 참고로 리그 오브 레전드의 "증강" 시스템에서 착안했다.

## 핵심 기능

### 체스 코어
일반 이동, 캡처, 체크/체크메이트/스테일메이트 판정, 캐슬링, 앙파상, 프로모션(폰이 마지막 줄 도달 시 나이트/비숍/룩/퀸 중 선택)까지 기본 체스 규칙을 전부 구현.

### 기물별 고유 스킬 (쿨타임제)
- **나이트 - 위협**: 이동 후 주변 적 1명을 1턴간 이동 불가로 만듦 (쿨타임 4턴)
- **비숍 - 워프**: 이동 전 상하좌우 1칸을 순간이동(공격 불가) (쿨타임 4턴)
- **룩 - 쉴드**: 이동 후 주변 아군 1명을 일정 턴간 무적으로 보호 (쿨타임 6턴)
- **퀸 - 아우라**: 아군이 적을 처치할 때마다 스택 획득, 6스택마다 아군 1명 자동 승급
- **킹 - 지휘**: 이번 턴을 소모해 다음 턴에 지정한 기물을 한 번 더 이동시킴 (쿨타임 8턴)

### 증강(강화) 시스템
총 40종(노말 15 / 레어 12 / 유니크 8 / 레전더리 5), `AugmentData` ScriptableObject로 관리. 프로모션 발생 시 또는 10턴마다 카드 선택 체크포인트가 열리며, 두 팀이 각자 독립적으로 최대 6개까지 보유 가능(팀당 별도 상한). 나이트/비숍 중 하나를 고르는 이진 선택형 증강도 지원.

### 온라인 2인 대전 (Photon Fusion 2)
방 코드를 생성/입력해 입장하는 방식(퀵매치 아님). Host가 White, 참가자가 Black으로 자동 배정되며 팀별로 카메라가 반전되어 보인다. 보드/기물은 하나를 공유하고, 클릭 좌표만 RPC로 중계해 양쪽 클라이언트가 동일한 로직을 재실행하는 방식으로 동기화한다. 턴 진행, 스킬 발동, 카드 선택(증강/승급/이진선택) 결과까지 전부 이 방식으로 동기화되며, 매치 재시작과 타이틀로 나가기 기능도 지원한다.

### UI
스킬 버튼(쿨타임 표시, 내 턴이 아닐 때 반투명 처리), 카드 선택 화면(증강/승급/이진선택 - 팀별 개별 진행, 선택 시 체크 표시 + 대기 상태), 보유 증강 확인 패널(백/흑 토글, 좌우 스크롤), 게임오버 화면(재시작/타이틀로 이동 버튼).

### VFX
위협(적색)/쉴드(청색)/지휘(황색)/워프(연보라)/프로모션(색종이 폭죽) 5종 스킬 이펙트와 기물 선택 시 바닥 이펙트. 위협/쉴드는 실제 효과 지속시간과 정확히 동기화되어 표시된다.

### SFX
이동, 캡처, 턴 종료, 체크 진입, 게임 시작/승리/패배/무승부, 스킬 4종(위협/쉴드/워프/지휘), 프로모션, 스킬 버튼 클릭, 증강 카드 등장/선택 완료까지 총 16개 상황에 효과음이 매핑되어 있다.

## 기술 스택

- **엔진**: Unity 6000.5.0f1 (Universal Render Pipeline)
- **네트워킹**: Photon Fusion 2 (Host-Client 모드, `PeerMode.Single` 환경에서 개발)
- **입력**: Unity Input System
- **언어**: C# (규모가 큰 매니저는 기능별 partial class로 분할)

## 프로젝트 구조

```
Assets/Scripts/
├── Audio/          # SoundManager (SFX 16종 재생)
├── Augment/        # AugmentManager, Augmenteffects (팀별 증강 보유/효과 적용)
├── Card/           # CardSelectionManager(.Data/.Selection), CardUI (카드 선택 UI/로직)
├── Chess/
│   ├── ChessBoard/   # ChessBoard, ChessInteractionManager(.Skills/.Promotion/.CheckState), BoardInputHandler, BoardTileHighlighter
│   ├── ChessPieces/  # PieceMovement, PieceCapture, PIeceSelectionVFX
│   ├── ChessRules/   # ChessRules(.Movement/.Attack) - 이동 합법성/체크 판정
│   └── Pieces/       # Bishop/King/Knight/Pawn/Queen/Rook, ChessPieces
├── Data/           # AugmentData/AugmentDatabase/PromotionOptionData (ScriptableObject)
├── Game/           # GameManager, GameEndManager, GameOverUI
├── Network/        # BasicSpawner, ChessNetworkSync, GameStartController (Fusion 연동)
├── Skill/          # PieceSkillManager(.Skills/.Status), QueenSkill, SkillUIManager(.TurnState/.SkillHandlers/.UI)
└── UI/             # AugmentViewUI, RoomCodePanelUI, StartMenu

Assets/Data/
├── Augment/        # 증강 40종 .asset
└── Promotion/      # 승급 옵션 4종 .asset (나이트/비숍/룩/퀸)

Assets/Scenes/
├── StartScene.unity   # 타이틀 - 방 생성/코드 입장
└── GameScene.unity    # 실제 대국 화면
```

## 개발 진행 상황 (2026-08-24 ~)

- **1. 체스 기본 개념 구축 (08-24 ~ 08-27)**: 머티리얼·프리팹으로 보드/기물 구성, 이동·캡처·체크메이트·캐슬링·앙파상·프로모션 규칙
- **2. 스킬·증강 구현 (09-02 ~ 09-14)**: 기물별 고유 스킬 5종, 증강 시스템(데이터/아이콘/프리팹), Photon 설정
- **3. 정리와 온라인 대전 (09-15 ~ 09-18)**
  - **09-15**: 스크립트 전체 정리(한글 주석/region), 비대한 매니저를 partial class로 분할, 폴더 구조 정리
  - **09-16**: 증강 40종 데이터 전수 검증(문서와 100% 일치, 설명 오류 3건 수정) 및 밸런스 예측, 팀 분리(흑팀 지원) + 보드 클릭 릴레이 기반 이동 동기화 아키텍처 구축
  - **09-18**: 처음으로 실기기 2인(빌드+빌드) 테스트 진행 — 증강 카드 선택 화면 개별화(한쪽 선택이 양쪽 화면에 영향 주던 버그), 보유 증강 확인 버튼 무반응, 이진 선택 카드 회귀 버그 등 다수 수정, 게임오버 화면에 재시작/타이틀 버튼 추가
- **4. 버그 수정과 VFX (09-21 ~ 09-23)**
  - **09-21**: 멀티플레이 버그 수정, 증강 데이터 정비
  - **09-22~23**: 스킬 5종(위협/쉴드/지휘/워프/프로모션) VFX 제작·연동, URP 셰이더 호환 문제 및 가시성 수정, 위협/쉴드 VFX를 실제 효과 지속시간과 동기화하도록 개선, 방 이탈 처리
- **5. 효과음 (09-28)**: SoundManager 신규 구현, 16개 상황에 효과음 매핑 완료

## 알려진 이슈 / 남은 과제

- 두 플레이어가 정확히 같은 타이밍에 동시 클릭하는 레이스 컨디션은 아직 실기기로 검증 안 됨
- 밸런스: 퀸 라인이 스노우볼 잠재력이 가장 크고, 룩/킹 라인은 레전더리 등급 증강이 부족해 후반 파워 스파이크가 약함

## 빌드 / 실행

- Unity 에디터에서 `Assets/Scenes/StartScene.unity`부터 Play (방 생성 또는 코드 입장)
- Windows 빌드: `Builds/StandaloneWindows64/TacticalChess.exe` (최신 빌드 기준)
