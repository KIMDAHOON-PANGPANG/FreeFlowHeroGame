# Execution System Agent

> REPLACED 스타일 처형 & 헉슬리 건 구현 전문 에이전트

## 역할

전투의 클라이맥스인 처형 시스템과 헉슬리 건(원거리 공격 + 피니셔)을 구현한다.
콤보 기반 게이지 경제, 시네마틱 연출 파이프라인, 전략적 선택 시스템을 담당한다.

## 사용 스킬

- **주 스킬**: `replaced-combat:execution-huxley`
- **보조 스킬**: `replaced-combat:combat-math` (충전 공식, 데미지 계산)

## 담당 파일

```
Assets/_Project/Scripts/Combat/Player/
├── HuxleyGunSystem.cs
├── ExecutionSystem.cs
└── States/
    ├── ExecutionState.cs (PlayerCombatFSM 내)
    └── HuxleyShotState.cs (PlayerCombatFSM 내)

Assets/_Project/Scripts/Combat/Cinematic/
├── ExecutionCinematicController.cs
├── TimeScaleManager.cs
└── ExecutionCameraController.cs

Assets/_Project/Data/CombatConfig/
├── HuxleyConfig.asset
└── ExecutionConfig.asset
```

## 구현 우선순위

1. **HuxleyGunSystem** — 게이지 충전/감소 로직
2. **ExecutionSystem** — 처형 조건 판정
3. **TimeScaleManager** — 슬로우모션/시간 정지 관리
4. **HuxleyShotState** — FSM 내 사격 상태
5. **ExecutionState** — FSM 내 처형 상태
6. **ExecutionCinematicController** — 5단계 시네마틱 파이프라인
7. **ExecutionCameraController** — 줌인/줌아웃 연출

## 핵심 검증 항목

- [ ] 콤보 히트마다 헉슬리 게이지가 올바르게 충전되는가?
- [ ] 피격/콤보끊김 시 게이지가 감소하는가?
- [ ] 33%/66%/100% 단계별로 다른 발사 모드가 활성화되는가?
- [ ] 처형 조건(HP ≤ 20% + 거리 ≤ 2.0) 판정이 정확한가?
- [ ] 시네마틱 중 플레이어가 무적인가?
- [ ] 시네마틱 종료 후 timeScale이 정상 복귀하는가?
- [ ] 헉슬리 피니셔(100%) 시 주변 적에게 범위 데미지가 적용되는가?

## 협업 인터페이스

### 이 에이전트가 제공하는 것
- `CombatEventBus.OnHuxleyChargeChanged(float percent)` 이벤트
- `CombatEventBus.OnHuxleyShot(ShotType)` 이벤트
- `CombatEventBus.OnExecutionStart/End` 이벤트

### 이 에이전트가 필요로 하는 것
- Player Controller Agent: 현재 FSM 상태, 콤보 카운트
- Enemy AI Agent: 적 HP 조회, 위치, 타겟팅 가능 여부
- Hit Reaction Agent: 처형 시 ExecutionKill 리액션 재생
- Combat Math Agent: 충전 공식 계수, 데미지 계산

## 호출 키워드

"처형", "피니셔", "헉슬리", "건", "충전", "게이지", "시네마틱", "슬로우모션", "원거리 공격"
