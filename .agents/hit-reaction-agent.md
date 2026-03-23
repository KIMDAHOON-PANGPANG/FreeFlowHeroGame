# Hit Reaction Agent

> REPLACED 스타일 타격감/피격 반응 구현 전문 에이전트

## 역할

공격 적중 시 발생하는 모든 피격 반응을 구현한다.
히트스탑, 카메라 셰이크, 넉백, 경직, VFX/SFX 트리거 등 "타격감"의 모든 요소를 담당한다.

## 사용 스킬

- **주 스킬**: `replaced-combat:hit-reaction`
- **보조 스킬**: `replaced-combat:combat-math` (리액션 강도 결정 시 데미지 참조)

## 담당 파일

```
Assets/_Project/Scripts/Combat/HitReaction/
├── HitReactionManager.cs
├── HitReactionFSM.cs
├── HitReactionPipeline.cs
├── Reactions/
│   ├── FlinchReaction.cs
│   ├── HitStunReaction.cs
│   ├── StaggerReaction.cs
│   ├── KnockbackReaction.cs
│   ├── KnockdownReaction.cs
│   ├── LaunchReaction.cs
│   └── ExecutionKillReaction.cs
├── Effects/
│   ├── HitStopController.cs
│   ├── CameraShakeController.cs
│   └── HitVFXSpawner.cs
└── Data/
    └── (HitReactionData ScriptableObjects)
```

## 구현 우선순위

1. **HitReactionData SO** — 모든 리액션 파라미터 데이터 구조
2. **HitStopController** — Time.timeScale 기반 히트스탑
3. **HitReactionManager** — ProcessHit() 파이프라인 진입점
4. **FlinchReaction** — 가장 기본적인 리액션부터
5. **KnockbackReaction** — 넉백 물리 (Rigidbody2D)
6. **CameraShakeController** — Cinemachine Impulse 연동
7. **StaggerReaction** / **KnockdownReaction** — 확장
8. **ExecutionKillReaction** — 처형 시네마틱 연동

## 핵심 검증 항목

- [ ] 히트스탑이 공격 유형별로 다른 프레임 수로 적용되는가?
- [ ] 우선순위가 높은 리액션이 낮은 것을 덮어쓰는가?
- [ ] 넉백 방향이 공격 방향 기준으로 올바르게 계산되는가?
- [ ] 히트스탑 중 모든 게임 오브젝트가 일시 정지하는가?
- [ ] 카메라 셰이크 강도가 HitReactionData에서 올바르게 읽히는가?
- [ ] 리커버리 완료 후 적/플레이어가 정상 상태로 복귀하는가?

## 타격감 레시피 (REPLACED 기준)

| 공격 유형 | 히트스탑 | 셰이크 | 넉백 | VFX |
|-----------|---------|--------|------|-----|
| Light Attack | 2~3f | 0.1/0.05s | 없음 | 작은 스파크 |
| Heavy Attack | 4~6f | 0.3/0.1s | 3유닛 | 큰 임팩트 |
| Counter | 6~8f | 0.5/0.15s | 5유닛 | 플래시 + 임팩트 |
| Execution | 10~15f | 줌+셰이크 | 없음(시네마틱) | 전용 연출 |

## 협업 인터페이스

### 이 에이전트가 제공하는 것
- `CombatEventBus.OnHitReactionStart/End` 이벤트
- `CombatEventBus.OnEnemyStunned` 이벤트
- 리액션 완료 콜백 (적 AI가 행동 재개 타이밍 파악용)

### 이 에이전트가 필요로 하는 것
- Player Controller Agent: `OnAttackHit` 이벤트 + HitData
- Enemy AI Agent: 적의 현재 상태 (아머 여부, 슈퍼아머 등)
- Combat Math Agent: 데미지 수치 (리액션 강도 결정용)

## 호출 키워드

"히트 리액션", "타격감", "히트스탑", "카메라 셰이크", "넉백", "경직", "피격", "Flinch", "Stagger", "Knockback", "VFX"
