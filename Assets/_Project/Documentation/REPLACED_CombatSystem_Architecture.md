# REPLACED 전투 시스템 데드카피 — 아키텍처 설계서

> **목표**: REPLACED(Sad Cat Studios)의 전투 시스템을 Unity 2D 횡스크롤 환경에서 100% 재현
> **엔진**: Unity 2022.3+ LTS / 2D URP
> **문서 버전**: v1.0 (2026-03-23)

---

## 1. 레퍼런스 분석 요약

### 1.1 REPLACED 전투 핵심 특징

REPLACED는 Batman Arkham 시리즈의 프리플로우 전투를 2.5D 횡스크롤로 옮긴 게임이다. Sad Cat Studios 공동 창립자 Igor Gritsay는 "아캄 전투 시스템을 2D로 가장 근접하게 재현한 것"이라 표현했다.

**핵심 메카닉 5가지:**

| 메카닉 | 설명 | 인디케이터 |
|--------|------|-----------|
| **근접 콤보** | 유려한 연타 체인, 자동 워핑으로 적 사이를 이동 | 없음 |
| **회피(Dodge)** | 적의 회피 불가 공격에 대응, 대시+무적 프레임 | 🔴 빨간 신호 |
| **카운터(Counter)** | 적의 카운터 가능 공격에 타이밍 맞춰 반격 | 🟡 노란 신호 |
| **처형(Execution)** | 연속 히트 또는 특정 조건 충족 시 시네마틱 킬 | 처형 가능 표시 |
| **헉슬리 건(Huxley Gun)** | 콤보로 충전, 원거리 공격 & 시네마틱 피니셔 | 충전 게이지 |

### 1.2 전투 흐름 사이클

```
[Idle] → 공격 입력 → [Strike] → 적 방향 자동 워핑
  ↑                      ↓
  ├── 🔴 빨간 신호 → [Dodge] → i-frame 이동 → [Idle/Strike]
  ├── 🟡 노란 신호 → [Counter] → 반격 애니메이션 → [Strike]
  ├── 게이지 충전 완료 → [Huxley Shot] → 원거리/피니셔
  └── 적 체력 임계 → [Execution] → 시네마틱 처형 → [Idle]
```

### 1.3 아캄과의 차이점

- **2.5D 제약**: Z축 없이 X-Y 평면만 사용, 워핑 거리가 더 짧음
- **치명성(Lethality)**: 아캄과 달리 적을 즉사시킬 수 있음 (처형 시스템)
- **헉슬리 건**: 아캄의 가젯과 유사하나, 콤보 게이지 기반 충전 방식이 독특
- **아머 시스템**: 특정 적은 Heavy Attack으로만 방어 파괴 가능

---

## 2. 시스템 아키텍처

### 2.1 전체 모듈 맵

```
┌─────────────────────────────────────────────────────┐
│                    CombatDirector                     │
│  (전투 흐름 총괄, 턴 관리, 슬로우모션, 카메라 큐)     │
└──────────┬──────────┬──────────┬──────────┬─────────┘
           │          │          │          │
    ┌──────▼──┐ ┌─────▼────┐ ┌──▼───────┐ ┌▼─────────┐
    │ Player  │ │  Enemy   │ │   Hit    │ │  Combat  │
    │Controller│ │   AI     │ │ Reaction │ │   Math   │
    │  (FSM)  │ │(BT+Coord)│ │ Manager  │ │ (공식)   │
    └────┬────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘
         │          │           │            │
    ┌────▼──────────▼───────────▼────────────▼────────┐
    │              CombatEventBus (이벤트 버스)         │
    │  OnAttackHit, OnDodge, OnCounter, OnExecution,   │
    │  OnHuxleyCharge, OnEnemyTelegraph, OnComboChange │
    └─────────────────────────────────────────────────┘
```

### 2.2 핵심 컴포넌트 목록

| 컴포넌트 | 책임 | ScriptableObject 데이터 |
|----------|------|------------------------|
| **PlayerCombatFSM** | 플레이어 상태 전환 (Idle→Strike→Dodge→Counter→Execute) | PlayerCombatConfig |
| **FreeflowWarpSystem** | 공격 시 적 방향 자동 이동 (워핑) | WarpConfig |
| **ComboManager** | 콤보 카운터, 콤보 윈도우 타이머 | ComboConfig |
| **DodgeController** | 회피 입력 처리, i-frame 관리 | DodgeConfig |
| **CounterSystem** | 카운터 타이밍 윈도우, 반격 분기 | CounterConfig |
| **ExecutionSystem** | 처형 조건 판정, 시네마틱 연출 | ExecutionConfig |
| **HuxleyGunSystem** | 콤보 기반 충전, 발사, 피니셔 | HuxleyConfig |
| **EnemyAIController** | 비헤이비어 트리 기반 적 AI | EnemyAIConfig |
| **AttackCoordinator** | 적 공격 턴 관리, 동시 공격 제한 | CoordinatorConfig |
| **TelegraphSystem** | 적 공격 예고 신호 (빨강/노랑) | TelegraphConfig |
| **HitReactionManager** | 피격 반응 (Flinch, Knockback, etc.) | HitReactionData |
| **CombatMathSolver** | 데미지 공식, 스케일링 | CombatFormulaConfig |
| **CombatDirector** | 전투 흐름 총괄, 카메라/슬로우 연출 | DirectorConfig |

### 2.3 데이터 흐름 다이어그램

```
Input System ──→ PlayerCombatFSM ──→ FreeflowWarpSystem ──→ 적 선택
     │                │                                        │
     │          ComboManager ◄────────────────────────── HitDetection
     │                │                                        │
     │         HuxleyGunSystem ◄── 콤보 카운트                 │
     │                │                                        │
     │                ▼                                        ▼
     │         CombatEventBus ◄────────────────── HitReactionManager
     │                │
     │                ▼
     │         CombatDirector ──→ CameraShake / SlowMotion / VFX
     │
     └──→ DodgeController ──→ i-frame 활성화
     └──→ CounterSystem ──→ EnemyTelegraph 수신 → 반격 실행
```

---

## 3. 플레이어 전투 FSM

### 3.1 상태 목록

```
                    ┌─────────┐
                    │  Idle   │◄──────────────────┐
                    └────┬────┘                    │
                Attack   │   Dodge    Counter      │
                Input    │   Input    Input        │
                    ┌────▼────┐                    │
              ┌─────┤ Strike  ├─────┐              │
              │     └────┬────┘     │              │
              │     연타  │          │              │
              │    ┌─────▼─────┐    │              │
              │    │ ComboChain│    │              │
              │    └─────┬─────┘    │              │
              │          │          │              │
         ┌────▼───┐ ┌───▼────┐ ┌───▼─────┐       │
         │ Dodge  │ │Counter │ │HeavyAtk │       │
         └────┬───┘ └───┬────┘ └───┬─────┘       │
              │         │          │              │
              │    ┌────▼──────────▼──┐           │
              │    │   Execution      │           │
              │    │ (조건 충족 시)    │           │
              │    └────────┬─────────┘           │
              │             │                     │
              └─────────────┴─────────────────────┘
                        recovery → Idle
```

### 3.2 핵심 상태별 명세

**Strike 상태:**
- Startup Frame: 3~5f (입력→히트까지)
- Active Frame: 2~4f (히트 판정 활성)
- Recovery Frame: 5~8f (후딜, 캔슬 가능 윈도우 포함)
- 워핑: Strike 진입 시 가장 가까운 적 방향으로 자동 이동
- 콤보 윈도우: Recovery 중 Attack 입력 시 다음 콤보로 전환

**Dodge 상태:**
- 빨간 인디케이터 감지 시 or 자유 입력
- i-frame: 진입 후 0~12f (약 0.2초)
- 이동 거리: 적 공격 범위 밖으로 탈출
- Recovery 후 즉시 Strike 가능 (캔슬)

**Counter 상태:**
- 노란 인디케이터 감지 시 타이밍 윈도우 내 입력
- Perfect Counter: ±3f 내 → 강화 반격 + 슬로우모션
- Normal Counter: ±8f 내 → 일반 반격
- 실패: 피격

**Execution 상태:**
- 트리거 조건: 적 HP가 임계치 이하 + 근접 거리
- 시네마틱 모드: 다른 적 일시 정지, 카메라 줌인
- 무적: 처형 애니메이션 동안 완전 무적

**HeavyAttack 상태:**
- 아머 적에게만 유효한 방패 파괴 공격
- Startup이 길지만 (8~12f) 높은 히트 리액션 우선순위
- 일반 적에게는 강넉백 적용

---

## 4. 적 AI 시스템

### 4.1 인디케이터 시스템

```csharp
public enum EnemyTelegraphType
{
    None,           // 텔레그래프 없음 (일반 공격)
    Red_Dodge,      // 🔴 회피만 가능한 공격
    Yellow_Counter  // 🟡 카운터 가능한 공격
}
```

- 적이 공격 준비에 들어가면 머리 위에 색상 인디케이터 표시
- 빨강: 플레이어는 반드시 Dodge해야 함 (카운터 불가)
- 노랑: 플레이어가 Counter 타이밍에 맞추면 반격 가능
- 인디케이터 표시 시간: 0.3~0.5초 (난이도에 따라 조절)

### 4.2 적 공격 턴 관리 (Attack Coordinator)

```
AttackCoordinator
├── maxSimultaneousAttackers: 2 (동시 공격 가능 적 수)
├── attackCooldown: 1.0~2.5s (적 간 공격 간격)
├── breathingTimer: 0.5s (연속 공격 사이 여유)
└── threatQueue: Queue<EnemyAI> (공격 대기열)
```

- REPLACED 스타일: 플레이어가 콤보 중일 때 적들은 대기
- 콤보 끊김 or 플레이어 idle → 적 공격 턴 개시
- 최대 2명까지 동시 공격 (나머지는 포위하며 대기)

### 4.3 적 유형

| 유형 | 공격 패턴 | 텔레그래프 | 특수 |
|------|-----------|-----------|------|
| **일반 졸개** | 단타 펀치/킥 | 🟡 Yellow | 없음 |
| **아머 병사** | 강공격 | 🔴 Red | Heavy Attack으로만 아머 파괴 |
| **돌진형** | 차지 공격 | 🔴 Red | 회피 후 스턴 상태 |
| **원거리형** | 사격 | 🔴 Red | 총알 회피 필요 |
| **엘리트** | 콤보 공격 | 🟡+🔴 혼합 | 높은 HP, 다양한 패턴 |

---

## 5. 헉슬리 건 & 처형 시스템

### 5.1 헉슬리 건 충전 메카닉

```
충전 공식:
  chargePerHit = baseCharge × comboMultiplier
  comboMultiplier = 1.0 + (comboCount × 0.05)  // 콤보가 높을수록 빨리 충전

충전 단계:
  0% ─────── 33% ──────── 66% ──────── 100%
  [비활성]   [1발 사격]   [2발 사격]   [피니셔 가능]

감소:
  - 피격 시: -20% 감소
  - 콤보 끊김: -10% 감소
  - 시간 경과: 비전투 5초 후 초당 -5% 감소
```

### 5.2 처형(Execution) 조건

```
조건 ALL 충족:
  1. 대상 적 HP ≤ executionThreshold (기본 20%)
  2. 플레이어와 적 거리 ≤ executionRange (기본 2.0 유닛)
  3. 플레이어가 Strike, Idle, ComboChain 상태 중 하나
  4. 처형 쿨다운 경과 (연속 처형 방지)

특수 처형 (Huxley 피니셔):
  - 헉슬리 게이지 100% + Execution 조건 동시 충족
  - 강화된 시네마틱 연출 + 주변 적에게 범위 데미지
```

---

## 6. 히트 리액션 시스템

### 6.1 리액션 우선순위

| 우선순위 | 리액션 | 적용 상황 |
|---------|--------|----------|
| 1 (낮음) | Flinch | 약공격 피격 |
| 2 | Hit Stun | 콤보 중 연속 피격 |
| 3 | Stagger | 강공격 피격 |
| 4 | Knockback | 카운터 반격, Heavy Attack |
| 5 | Knockdown | 처형 전 쓰러뜨리기 |
| 6 | Launch | 에어 콤보 시작 |
| 7 (높음) | Execution Kill | 처형 시네마틱 |

### 6.2 히트스탑 & 카메라 연출

```
히트스탑 프레임:
  약공격: 2~3f (미세한 멈춤)
  강공격: 4~6f (묵직한 멈춤)
  카운터: 6~8f (임팩트 강조)
  처형:  10~15f (시네마틱 전환용)

카메라 셰이크:
  약공격: intensity=0.1, duration=0.05s
  강공격: intensity=0.3, duration=0.1s
  카운터: intensity=0.5, duration=0.15s
  처형:  zoom + shake 복합 연출
```

---

## 7. 전투 수치 (Combat Math)

### 7.1 기본 데미지 공식

```
finalDamage = baseDamage × attackMultiplier × comboScaling × armorReduction

attackMultiplier:
  Light Attack: 1.0
  Heavy Attack: 2.0
  Counter: 1.5
  Huxley Shot: 1.8
  Execution: 9999 (즉사)

comboScaling:
  combo 1~5:  1.0
  combo 6~10: 1.1
  combo 11~20: 1.2
  combo 21+:  1.3

armorReduction:
  일반: 1.0 (감소 없음)
  아머: 0.2 (Heavy Attack 전까지 80% 감소)
  아머 파괴 후: 1.0
```

### 7.2 콤보 시스템 수치

```
콤보 윈도우: 0.8초 (마지막 히트로부터)
콤보 리셋: 피격, 2초 이상 비공격
콤보 보너스:
  x5: "Good" → 헉슬리 +5% 추가 충전
  x10: "Great" → 적 공격 빈도 감소
  x20: "Awesome" → 헉슬리 +10% 추가 충전 + 공격력 1.2배
  x50: "Unstoppable" → 처형 임계치 30%로 상향
```

---

## 8. 폴더 구조 (Unity 프로젝트)

```
Assets/_Project/
├── Scripts/
│   ├── Combat/
│   │   ├── Core/
│   │   │   ├── CombatDirector.cs
│   │   │   ├── CombatEventBus.cs
│   │   │   └── CombatConstants.cs
│   │   ├── Player/
│   │   │   ├── PlayerCombatFSM.cs
│   │   │   ├── States/
│   │   │   │   ├── IdleState.cs
│   │   │   │   ├── StrikeState.cs
│   │   │   │   ├── DodgeState.cs
│   │   │   │   ├── CounterState.cs
│   │   │   │   ├── HeavyAttackState.cs
│   │   │   │   └── ExecutionState.cs
│   │   │   ├── FreeflowWarpSystem.cs
│   │   │   ├── ComboManager.cs
│   │   │   ├── DodgeController.cs
│   │   │   ├── CounterSystem.cs
│   │   │   ├── HuxleyGunSystem.cs
│   │   │   └── ExecutionSystem.cs
│   │   ├── Enemy/
│   │   │   ├── EnemyAIController.cs
│   │   │   ├── BehaviorTree/
│   │   │   │   ├── BTNode.cs
│   │   │   │   ├── BTSelector.cs
│   │   │   │   ├── BTSequence.cs
│   │   │   │   └── Leaves/
│   │   │   ├── AttackCoordinator.cs
│   │   │   ├── TelegraphSystem.cs
│   │   │   └── EnemyTypes/
│   │   ├── HitReaction/
│   │   │   ├── HitReactionManager.cs
│   │   │   ├── HitReactionFSM.cs
│   │   │   └── Reactions/
│   │   └── Math/
│   │       ├── CombatMathSolver.cs
│   │       ├── DamageCalculator.cs
│   │       └── ComboScaling.cs
│   └── Common/
│       ├── StateMachine/
│       ├── Hitbox/
│       └── Utils/
├── Data/
│   ├── CombatConfig/
│   │   ├── PlayerCombatConfig.asset
│   │   ├── ComboConfig.asset
│   │   ├── DodgeConfig.asset
│   │   ├── CounterConfig.asset
│   │   ├── HuxleyConfig.asset
│   │   ├── ExecutionConfig.asset
│   │   └── CombatFormulaConfig.asset
│   ├── EnemyConfig/
│   │   ├── GruntConfig.asset
│   │   ├── ArmoredConfig.asset
│   │   ├── ChargerConfig.asset
│   │   ├── RangedConfig.asset
│   │   └── EliteConfig.asset
│   └── HitReaction/
│       ├── FlinchData.asset
│       ├── StaggerData.asset
│       ├── KnockbackData.asset
│       └── ...
├── Animations/
│   ├── Player/
│   └── Enemy/
├── Prefabs/
│   ├── Player/
│   ├── Enemies/
│   └── VFX/
└── Documentation/
    └── REPLACED_CombatSystem_Architecture.md (이 파일)
```

---

## 9. 스킬 & 에이전트 매핑

### 9.1 커스텀 스킬 구성

| 스킬 이름 | 담당 영역 | 트리거 키워드 |
|-----------|----------|--------------|
| `replaced-combat:player-controller` | 플레이어 FSM, 워핑, 콤보, 회피, 카운터 | 프리플로우, 워핑, 콤보, 회피, 카운터, 인풋 버퍼 |
| `replaced-combat:execution-huxley` | 처형 시스템, 헉슬리 건 | 처형, 피니셔, 헉슬리, 건, 충전, 시네마틱 킬 |
| `replaced-combat:enemy-ai` | 적 AI, 텔레그래프, 공격 턴 관리 | 적 AI, 텔레그래프, 인디케이터, 공격 턴, 어그로 |
| `replaced-combat:hit-reaction` | 히트 리액션, 히트스탑, 넉백 | 히트 리액션, 경직, 넉백, 히트스탑, 피격 |
| `replaced-combat:combat-math` | 데미지 공식, 콤보 스케일링 | 데미지, 공식, 밸런스, 스케일링, 콤보 보너스 |

### 9.2 에이전트 구성

| 에이전트 | 역할 | 사용 스킬 |
|---------|------|----------|
| **Combat Director Agent** | 전체 전투 시스템 통합, 아키텍처 결정 | 모든 스킬 참조 |
| **Player Controller Agent** | 플레이어 전투 조작감 구현 | player-controller, combat-math |
| **Enemy AI Agent** | 적 행동 패턴, 텔레그래프 구현 | enemy-ai, combat-math |
| **Hit Reaction Agent** | 피격 반응, 주스/피드백 구현 | hit-reaction, combat-math |
| **Execution System Agent** | 처형 & 헉슬리 건 구현 | execution-huxley, combat-math |

---

## 10. 구현 우선순위 로드맵

### Phase 1: 기초 (1~2주)
- [ ] PlayerCombatFSM + 기본 상태 전환
- [ ] Hitbox/Hurtbox 시스템
- [ ] CombatEventBus
- [ ] 기본 Strike → Idle 루프

### Phase 2: 프리플로우 코어 (2~3주)
- [ ] FreeflowWarpSystem (자동 워핑)
- [ ] ComboManager (콤보 카운팅)
- [ ] DodgeController (i-frame)
- [ ] CounterSystem (카운터 타이밍)

### Phase 3: 적 AI (2~3주)
- [ ] EnemyAIController (BT 기본)
- [ ] TelegraphSystem (빨강/노랑 인디케이터)
- [ ] AttackCoordinator (턴 관리)
- [ ] 적 유형 3종 (졸개, 아머, 돌진)

### Phase 4: 피니셔 & 주스 (1~2주)
- [ ] ExecutionSystem (처형)
- [ ] HuxleyGunSystem (충전 + 발사)
- [ ] HitReactionManager (전체 리액션)
- [ ] 히트스탑, 카메라 셰이크, VFX

### Phase 5: 밸런싱 & 폴리시 (1~2주)
- [ ] CombatMathSolver 전체 공식 튜닝
- [ ] 난이도 곡선 조정
- [ ] 피드백 이터레이션
