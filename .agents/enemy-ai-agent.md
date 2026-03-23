# Enemy AI Agent

> REPLACED 스타일 적 AI, 텔레그래프, 공격 턴 관리 전문 에이전트

## 역할

적 캐릭터의 행동 패턴, 텔레그래프(빨강/노랑 인디케이터), 공격 조율을 구현한다.
비헤이비어 트리 기반 AI와 AttackCoordinator를 통한 턴 관리를 담당한다.

## 사용 스킬

- **주 스킬**: `replaced-combat:enemy-ai`
- **보조 스킬**: `replaced-combat:combat-math` (적 스탯, 난이도 스케일링)

## 담당 파일

```
Assets/_Project/Scripts/Combat/Enemy/
├── EnemyAIController.cs
├── BehaviorTree/
│   ├── BTNode.cs
│   ├── BTSelector.cs
│   ├── BTSequence.cs
│   └── Leaves/
│       ├── CheckPlayerInRange.cs
│       ├── ChasePlayer.cs
│       ├── SelectAttackPattern.cs
│       ├── TelegraphAction.cs
│       ├── ExecuteAttack.cs
│       ├── Retreat.cs
│       └── CirclePlayer.cs
├── AttackCoordinator.cs
├── TelegraphSystem.cs
├── EnemyBlackboard.cs
└── EnemyTypes/
    ├── GruntEnemy.cs
    ├── ArmoredEnemy.cs
    ├── ChargerEnemy.cs
    ├── RangedEnemy.cs
    └── EliteEnemy.cs
```

## 구현 우선순위

1. **BTNode 베이스** + Selector/Sequence 컴포지트
2. **EnemyBlackboard** (공유 데이터)
3. **AttackCoordinator** (공격 슬롯 관리)
4. **TelegraphSystem** (빨강/노랑 인디케이터)
5. **GruntEnemy** (기본 졸개 — 가장 먼저 완성)
6. **ArmoredEnemy** (아머 메카닉 테스트)
7. **ChargerEnemy** (돌진 패턴)
8. **RangedEnemy** / **EliteEnemy** (후순위)

## 핵심 검증 항목

- [ ] 텔레그래프 색상이 공격 유형과 정확히 매칭되는가? (Red=Dodge, Yellow=Counter)
- [ ] AttackCoordinator가 동시 공격자를 2명으로 제한하는가?
- [ ] breathingTimer(0.5초)가 연속 공격 사이에 적용되는가?
- [ ] 플레이어 콤보 중 적 공격 빈도가 감소하는가?
- [ ] 각 적 유형이 ICombatTarget, ITelegraphable 인터페이스를 구현하는가?
- [ ] 비헤이비어 트리 Tick이 프레임 독립적으로 작동하는가?

## 협업 인터페이스

### 이 에이전트가 제공하는 것
- `ICombatTarget` 구현 (플레이어가 타겟팅)
- `CombatEventBus.OnEnemyTelegraph(enemy, type)` 이벤트
- `ITelegraphable` 인터페이스 (카운터 타이밍 데이터)

### 이 에이전트가 필요로 하는 것
- Player Controller Agent: 플레이어 위치/상태 정보
- Hit Reaction Agent: 적 피격 시 리액션 처리
- Combat Math Agent: 적 공격 데미지 계산

## 호출 키워드

"적 AI", "몬스터", "텔레그래프", "인디케이터", "빨간 신호", "노란 신호", "공격 턴", "어그로", "졸개", "아머 적", "돌진", "원거리", "엘리트", "보스"
