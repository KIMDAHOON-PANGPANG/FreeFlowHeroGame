# FreeFlowHeroGame — CLAUDE.md

> REPLACED(Sad Cat Studios) 전투 시스템 100% 데드카피 프로젝트

## 프로젝트 개요

- **목표**: 인디게임 REPLACED의 전투 시스템을 Unity 2D 횡스크롤 환경에서 완벽 재현
- **엔진**: Unity 2022.3+ LTS, 2D URP
- **언어**: C# (.NET Standard 2.1)
- **입력 시스템**: **New Input System Package** (`UnityEngine.InputSystem`) — Legacy Input 사용 금지. Player Settings > Active Input Handling = "Input System Package (New)"
- **타겟 프레임**: 60fps 고정 (프레임 기반 전투 판정)
- **레퍼런스**: REPLACED by Sad Cat Studios (Batman Arkham 스타일 프리플로우 2.5D 전투)

## REPLACED 전투 핵심 메카닉 (반드시 숙지)

| 메카닉 | 요약 | 인디케이터 |
|--------|------|-----------|
| 근접 콤보 | 자동 워핑으로 적 사이를 넘나드는 유려한 연타 체인 | 없음 |
| 회피(Dodge) | 적의 🔴빨간 신호 공격에 대시+무적 프레임으로 대응 | 🔴 Red |
| 카운터(Counter) | 적의 🟡노란 신호 공격에 타이밍 맞춰 반격 | 🟡 Yellow |
| 처형(Execution) | HP 임계치 이하 적에게 시네마틱 즉사 | 처형 아이콘 |
| 헉슬리 건(Huxley) | 콤보로 충전되는 게이지 → 원거리 사격 or 피니셔 | 충전 게이지 |
| 아머 시스템 | Heavy Attack으로만 파괴 가능한 실드 | 아머 표시 |

## 프로젝트 구조

```
Assets/
├── EEJANAI_Team/          ← 외부 에셋: 격투 애니메이션 팩 (아래 리소스 섹션 참조)
│   ├── Commons/           ← EEJANAIbot 모델 + 머티리얼 + UnityToon 셰이더(선택)
│   └── FreeFighterAnimations/ ← 18개 격투 FBX/Anim/AnimatorController
├── _Project/
│   ├── Scripts/Combat/
│   │   ├── Core/          ← CombatDirector, CombatEventBus, CombatConstants
│   │   ├── Player/        ← PlayerCombatFSM, 워핑, 콤보, 회피, 카운터, 헉슬리
│   │   ├── Enemy/         ← EnemyAI(BT), AttackCoordinator, 텔레그래프, 적 유형
│   │   ├── HitReaction/   ← HitReactionManager, 히트스탑, 넉백, 카메라셰이크
│   │   └── Math/          ← CombatMathSolver, 데미지 계산, 콤보 스케일링
│   ├── Data/CombatConfig/ ← ScriptableObject 에셋들
│   ├── Resources/ActionTables/ ← 액터별 액션 JSON 테이블 (PC_Hero, Enemy_Grunt 등)
│   ├── Animations/        ← 커스텀 AnimatorController (EEJANAI 클립 리타겟)
│   ├── Prefabs/           ← 프리팹
│   └── Documentation/     ← 아키텍처 설계서

.skills/               ← 전투 시스템 전용 커스텀 스킬 (5개)
.agents/               ← 전투 시스템 전용 에이전트 정의 (5개)
```

## 에셋 리소스: EEJANAI_Team (FreeFighterAnimations)

### 개요
- **출처**: EEJANAI_Team 격투 애니메이션 팩
- **모델**: `Commons/Model/EEJANAIbot.fbx` (로봇 캐릭터)
- **머티리얼**: samplemat1~3 (UnityToon 셰이더 포함, 선택사항 — 없어도 동작)
- **샘플 씬**: `FreeFighterAnimations/Scenes/Sample.unity`
- **에디터 스크립트**: `Commons/Editor/ToonShaderInstaller.cs`

### 애니메이션 → 전투 액션 매핑

아래 표는 EEJANAI 애니메이션 18종을 REPLACED 전투 액션에 매핑한 것이다.
AnimatorController 구성 시 이 매핑을 기준으로 할 것.

| EEJANAI 애니메이션 | FBX 파일 | 전투 액션 매핑 | 용도 |
|-------------------|---------|--------------|------|
| `combo` | combo.fbx | **Light Attack 콤보 체인** | Strike 상태 기본 콤보 시퀀스 |
| `5 inch punch` | 5 inch punch.fbx | **Light Attack 1** (잽) | 콤보 첫 타 / 단타 공격 |
| `back fist` | back fist.fbx | **Light Attack 2** (백피스트) | 콤보 2타 |
| `elbow chop` | elbow chop.fbx | **Light Attack 3** (엘보) | 콤보 3타 |
| `knee strike` | knee strike.fbx | **Light Attack 4** (니킥) | 콤보 4타 |
| `low kick` | low kick.fbx | **Light Attack 변형** | 다운된 적 공격 / 변형 콤보 |
| `charge fist` | charge fist.fbx | **Heavy Attack** | 아머 파괴 전용 강공격 |
| `spinning elbow` | spinning elbow.fbx | **Counter 반격** | 카운터 성공 시 반격 모션 |
| `back kick` | back kick.fbx | **Counter 반격 (강화)** | Perfect Counter 시 강화 반격 |
| `front sweep` | front sweep.fbx | **Dodge 회피 후속** | 회피 직후 반격 공격 |
| `cressent kick` | cressent kick.fbx | **처형(Execution) 모션 1** | 일반 처형 시네마틱 |
| `axe kick` | axe kick.fbx | **처형(Execution) 모션 2** | 변형 처형 시네마틱 |
| `spinning axe kick` | spinning axe kick.fbx | **처형(Execution) 모션 3** | 화려한 처형 (높은 콤보 시) |
| `jumping uppercut` | jumping uppercut.fbx | **Launch 공격** | 에어 콤보 시작용 띄우기 |
| `jumping side kick` | jumping side kick.fbx | **에어 콤보 피니셔** | 공중 콤보 마무리 |
| `webster side kick` | webster side kick.fbx | **헉슬리 피니셔 모션** | 헉슬리 100% 시네마틱 킬 |
| `super blast` | super blast.fbx | **헉슬리 건 발사** | 원거리 사격 모션 |
| `kungfu samba` | kungfu samba.fbx | **Idle / 도발** | 전투 대기 또는 승리 포즈 |

### 에셋 사용 시 주의사항
1. **원본 파일 수정 금지**: `Assets/EEJANAI_Team/` 하위 파일은 직접 수정하지 않는다
2. **리타겟팅**: EEJANAI 클립을 프로젝트 캐릭터에 리타겟할 경우 `Assets/_Project/Animations/`에 별도 AnimatorController 생성
3. **프레임 데이터 분석 필요**: 각 FBX 애니메이션의 실제 프레임 수를 확인하여 Startup/Active/Recovery 구간을 설정해야 함
4. **UnityToon 셰이더**: 프로젝트에 필수 아님, 시각 확인용으로만 포함됨

## 커스텀 스킬 (5개)

작업 시 반드시 해당 스킬의 SKILL.md를 먼저 읽고 지침을 따를 것.

| 스킬 | 경로 | 용도 |
|------|------|------|
| `replaced-combat:player-controller` | `.skills/replaced-combat-player-controller/SKILL.md` | 플레이어 FSM, 워핑, 콤보, 회피, 카운터 구현 |
| `replaced-combat:execution-huxley` | `.skills/replaced-combat-execution-huxley/SKILL.md` | 처형, 헉슬리 건, 시네마틱 연출 구현 |
| `replaced-combat:enemy-ai` | `.skills/replaced-combat-enemy-ai/SKILL.md` | 적 AI, 텔레그래프, 공격 턴 관리 구현 |
| `replaced-combat:hit-reaction` | `.skills/replaced-combat-hit-reaction/SKILL.md` | 히트 리액션, 타격감 연출 구현 |
| `replaced-combat:combat-math` | `.skills/replaced-combat-math/SKILL.md` | 데미지 공식, 밸런스 수치 설계 |

## 에이전트 (5개)

| 에이전트 | 경로 | 역할 |
|---------|------|------|
| Combat Director | `.agents/combat-director.md` | 전투 시스템 통합 총괄 |
| Player Controller | `.agents/player-controller-agent.md` | 플레이어 전투 조작 구현 |
| Enemy AI | `.agents/enemy-ai-agent.md` | 적 AI, 텔레그래프, 턴 관리 |
| Hit Reaction | `.agents/hit-reaction-agent.md` | 타격감, 피격 반응 구현 |
| Execution System | `.agents/execution-system-agent.md` | 처형, 헉슬리 건 구현 |

## 핵심 설계 규칙

### 아키텍처 원칙
1. **이벤트 버스 통신**: 모듈 간 통신은 반드시 `CombatEventBus`를 통해서만
2. **ScriptableObject 데이터**: 모든 수치/설정은 SO로 외부화, 하드코딩 금지
3. **FSM + BT 이원 구조**: 플레이어는 FSM, 적은 Behavior Tree
4. **프레임 기반 판정**: 60fps 기준, 1프레임 = 0.0167초
5. **인터페이스 계약**: ICombatTarget, ITelegraphable 등 인터페이스로 결합도 최소화

### 네이밍 컨벤션
- 네임스페이스: `FreeFlowHero.Combat`, `FreeFlowHero.Combat.Player`, `FreeFlowHero.Combat.Enemy` 등
- 클래스: PascalCase (예: `PlayerCombatFSM`)
- 메서드: PascalCase (예: `ProcessHit()`)
- 필드: camelCase + 접두사 없음 (예: `comboCount`)
- SO 에셋: PascalCase + 접미사 Config/Data (예: `DodgeConfig.asset`)
- 이벤트: On + 동사 + 명사 (예: `OnAttackHit`, `OnEnemyTelegraph`)

### 프레임 데이터 표준
모든 공격/행동은 아래 3구간을 명시해야 함:
- **Startup**: 입력~히트 판정 시작까지
- **Active**: 히트 판정 활성 구간
- **Recovery**: 히트 판정 종료~다음 행동 가능까지

### CombatEventBus 이벤트 목록
```
OnAttackHit(HitData)           — 공격 적중
OnDodge(Vector2 direction)     — 회피 실행
OnCounter(CounterType)         — 카운터 성공
OnComboChanged(int count)      — 콤보 카운트 변경
OnComboBreak()                 — 콤보 끊김
OnHuxleyChargeChanged(float%)  — 헉슬리 게이지 변경
OnHuxleyShot(ShotType)         — 헉슬리 발사
OnExecutionStart(Enemy)        — 처형 시작
OnExecutionEnd()               — 처형 완료
OnEnemyTelegraph(Enemy, Type)  — 적 공격 예고
OnHitReactionStart(Target, Type)— 히트 리액션 시작
OnHitReactionEnd(Target)       — 히트 리액션 종료
OnEnemyStunned(Enemy)          — 적 스턴
OnEnemyDeath(Enemy)            — 적 사망
OnPlayerHit(HitData)           — 플레이어 피격
```

## 주요 수치 레퍼런스 (빠른 참조)

| 항목 | 값 | 비고 |
|------|---|------|
| 콤보 윈도우 | 0.8초 | 마지막 히트부터 다음 입력까지 |
| 인풋 버퍼 | 0.15초 | 선입력 유효 시간 |
| Dodge i-frame | 12f (0.2초) | 무적 프레임 |
| Counter Perfect | ±3f (0.05초) | 강화 반격 |
| Counter Normal | ±8f (0.13초) | 일반 반격 |
| 텔레그래프 시간 | 0.3~0.5초 | 난이도별 조절 |
| 동시 공격자 수 | 최대 2명 | AttackCoordinator |
| 호흡 시간 | 0.5초 | 연속 공격 사이 최소 간격 |
| 처형 임계치 | HP 20% 이하 | 콤보 x50 시 30% |
| 처형 거리 | 2.0 유닛 | 근접 거리 |
| 헉슬리 기본 충전 | 히트당 5% | comboMultiplier 적용 |
| 워핑 시간 | 0.1~0.15초 | DOTween/AnimationCurve |

## 구현 로드맵

- **Phase 1** (기초): PlayerCombatFSM, Hitbox/Hurtbox, CombatEventBus, 기본 Strike ✅
- **Phase 2** (프리플로우 코어): 워핑, 콤보, Dodge, Counter ✅
- **Phase 3** (적 AI): 적 충돌 수정, AnimatorController 매핑, AttackCoordinator ✅
- **Phase 4** (피니셔 & 주스): Execution, Huxley, HitReaction, 히트스탑/셰이크
- **Phase 5** (밸런싱): CombatMath 튜닝, 난이도 곡선, 피드백 이터레이션

## 작업 방식: GUI 작업 금지, 에디터 스크립트 우선

**모든 Unity 에디터 GUI 작업은 C# 에디터 스크립트로 자동화한다.**
사용자가 직접 Inspector/Hierarchy를 조작하는 일을 최소화할 것.

### 핵심 철학: 바이브 코딩 지향

Godot의 헤드리스(headless) CLI 빌드처럼, Unity에서도 **코드만으로 프로젝트를 구성**하는 것을 지향한다.
사용자는 대화로 의도를 전달하고, AI가 에디터 스크립트를 생성하여 원클릭으로 실행할 수 있게 만든다.

- **1순위 — 에디터 스크립트 자동화**: 씬, 프리팹, 컴포넌트, 레이어, 애니메이터, SO 등 모든 에디터 작업을
  `[MenuItem]` 기반 C# 스크립트로 작성한다. 사용자는 Unity 메뉴에서 클릭 한 번으로 실행만 하면 된다.
- **2순위 — 자동화 불가 시 절차서 제공**: Unity API로 자동화할 수 없는 작업(예: Package Manager 설치,
  Build Settings 변경, 특정 에디터 윈도우 조작 등)은 **스크린샷 수준의 상세 절차서**를 작성하여 공유한다.
  단계별로 정확한 메뉴 경로, 클릭 위치, 입력값을 명시한다.
- **3순위 — 절대 하지 않는 것**: "Inspector에서 값을 조절해주세요"처럼 모호한 GUI 지시는 금지.
  반드시 구체적인 코드 또는 절차를 제공한다.

### 에디터 스크립트로 처리해야 하는 작업
- **씬 구성**: 게임 오브젝트 생성, 계층 구조 세팅 → `EditorWindow` 또는 `MenuItem` 스크립트
- **프리팹 생성**: 컴포넌트 부착, 기본값 설정 포함 → `PrefabUtility` 기반 자동 생성
- **컴포넌트 부착**: 필수 컴포넌트 자동 추가 → `[RequireComponent]` + 셋업 스크립트
- **레이어/태그 설정**: 전투 관련 레이어(Hitbox, Hurtbox, Player, Enemy) 자동 등록
- **AnimatorController 구성**: EEJANAI 클립 매핑, 상태 전환 → `AnimatorController` API
- **ScriptableObject 에셋 생성**: 기본 Config 에셋들 자동 생성
- **테스트 씬 구성**: 플레이어 + 더미 적 배치 → 원클릭 셋업

### 에디터 스크립트 위치
```
Assets/_Project/Scripts/Editor/
├── CombatSceneSetup.cs        ← 전투 테스트 씬 자동 구성 (AttackCoordinator 포함)
├── PrefabFactory.cs           ← 플레이어/적 프리팹 자동 생성
├── AnimatorControllerBuilder.cs ← 플레이어 EEJANAI 애니메이션 매핑 자동화
├── EnemyAnimatorBuilder.cs    ← 적 AnimatorController 자동 생성 (Phase 3 추가)
├── CombatConfigGenerator.cs   ← SO 에셋 배치 생성
├── LayerAndTagSetup.cs        ← 레이어/태그 자동 등록
└── ActionTableEditorWindow.cs ← 액션 테이블 에디터 (JSON 시각 편집 툴)
```

### 원칙
- `[MenuItem("REPLACED/...")]`로 메뉴 등록하여 원클릭 실행
- 멱등성 보장: 여러 번 실행해도 중복 생성 없음
- 실행 결과를 Console에 로그로 출력

## 역할 분담: AI = 로직, 사용자 = 데이터 튜닝

**AI(Claude)가 하는 것:**
- 전투 시스템 로직, FSM, AI, 이벤트 시스템 등 모든 C# 코드 작성
- 에디터 스크립트 자동화 코드 작성
- 버그 수정, 리팩토링, 새 기능 구현

**사용자(기획자)가 하는 것:**
- 프레임 데이터 (선딜/활성/후딜 프레임 수), 캔슬 비율, 타이밍 값 등 **수치 조절**
- Inspector에서 ScriptableObject 에셋의 데이터 편집
- 스크립트 상단의 `★ 데이터 튜닝` 주석이 달린 상수/배열 값 직접 수정
- Unity 에디터에서 플레이 테스트 및 피드백

**코드 작성 규칙:**
- 사용자가 조절할 수 있는 값은 반드시 **스크립트 상단에 명확한 주석과 함께 분리**할 것
- 튜닝 가능한 데이터에는 `★ 데이터 튜닝:` 주석을 붙여 쉽게 찾을 수 있게 할 것
- 가능하면 `[SerializeField]`로 Inspector 노출하거나, ScriptableObject로 외부화할 것
- 하드코딩된 매직 넘버 금지 — 반드시 이름 있는 상수 또는 설정 필드로 분리
- 구현 완료 시 사용자가 튜닝할 수 있는 데이터 목록과 조절 가이드를 채팅으로 함께 전달

## 작업 시 주의사항

1. **코드 작성 전** 해당 스킬의 SKILL.md를 반드시 읽을 것
2. **이벤트 추가 시** 이 파일의 CombatEventBus 이벤트 목록을 업데이트할 것
3. **수치 변경 시** 주요 수치 레퍼런스 테이블을 함께 업데이트할 것
4. **새 적 유형 추가 시** enemy-ai 스킬의 적 유형 템플릿을 참고할 것
5. **타격감 조절 시** hit-reaction 스킬의 히트스탑/셰이크 레시피를 기준으로 할 것
6. **모듈 간 의존성 발생 시** Combat Director 에이전트 문서를 참고하여 인터페이스로 해결할 것
7. **씬/프리팹/컴포넌트 작업 시** 반드시 에디터 스크립트로 자동화, GUI 수동 작업 금지
8. **구현 완료 후** 채팅으로 기획자가 이해할 수 있는 업데이트 내역을 보고할 것
   - 기술 용어를 최소화하고, "플레이어가 체감하는 변화"와 "게임 동작의 변화" 중심으로 기술
   - 무엇이 바뀌었는지, 왜 바뀌었는지, 플레이 시 어떤 차이가 느껴지는지 포함
   - 새로 추가된 파일 목록과 수정된 파일 목록을 간략히 첨부
   - 사용자가 튜닝할 수 있는 데이터 항목과 조절 가이드를 함께 첨부
   - CLAUDE.md가 아닌 **채팅 메시지**로 매번 사용자에게 직접 전달
