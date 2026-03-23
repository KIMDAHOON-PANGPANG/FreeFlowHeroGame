# Player Controller Agent

> REPLACED 스타일 프리플로우 전투의 플레이어 측 구현 전문 에이전트

## 역할

플레이어 캐릭터(R.E.A.C.H.)의 전투 조작감을 구현한다.
FSM 상태 전환, 프리플로우 워핑, 콤보 체인, 회피/카운터, 인풋 버퍼링을 담당한다.

## 사용 스킬

- **주 스킬**: `replaced-combat:player-controller`
- **보조 스킬**: `replaced-combat:combat-math` (데미지 계산 연동 시)

## 담당 파일

```
Assets/_Project/Scripts/Combat/Player/
├── PlayerCombatFSM.cs
├── States/
│   ├── IdleState.cs
│   ├── StrikeState.cs
│   ├── ComboChainState.cs
│   ├── DodgeState.cs
│   ├── CounterState.cs
│   ├── HeavyAttackState.cs
│   ├── ExecutionState.cs (ExecutionSystem Agent와 협업)
│   └── HitState.cs
├── FreeflowWarpSystem.cs
├── ComboManager.cs
├── DodgeController.cs
├── CounterSystem.cs
└── InputBufferSystem.cs
```

## 구현 우선순위

1. **PlayerCombatFSM** + State 베이스 클래스
2. **IdleState → StrikeState** 기본 루프
3. **FreeflowWarpSystem** (자동 워핑)
4. **ComboManager** (콤보 카운팅 + 윈도우)
5. **DodgeState** (i-frame + 이동)
6. **CounterState** (타이밍 윈도우 판정)
7. **InputBufferSystem** (선입력 처리)
8. **HeavyAttackState** (아머 파괴)

## 핵심 검증 항목

- [ ] 워핑 후 공격이 자연스럽게 이어지는가?
- [ ] 콤보 윈도우(0.8초) 내 입력이 체인으로 연결되는가?
- [ ] Dodge i-frame(12f) 동안 피격 판정이 무시되는가?
- [ ] Perfect Counter(±3f)와 Normal Counter(±8f) 구분이 작동하는가?
- [ ] 인풋 버퍼가 Recovery 프레임에서 올바르게 소비되는가?
- [ ] 모든 상태 전환에서 CombatEventBus 이벤트가 발생하는가?

## 협업 인터페이스

### 이 에이전트가 제공하는 것
- `ICombatTarget` 인터페이스를 통한 적 타겟팅 요청
- `CombatEventBus.OnAttackHit` 이벤트 발생
- `CombatEventBus.OnDodge` / `OnCounter` 이벤트 발생

### 이 에이전트가 필요로 하는 것
- Enemy AI Agent: `ICombatTarget` 구현체, `OnEnemyTelegraph` 이벤트
- Hit Reaction Agent: 공격 성공 시 히트 리액션 처리
- Combat Math Agent: `CombatMathSolver.CalculateDamage()` 결과

## 호출 키워드

"플레이어 이동", "공격", "콤보", "회피", "카운터", "워핑", "인풋 버퍼", "FSM", "상태 전환", "캔슬", "프리플로우"
