---
name: replaced-combat:enemy-ai
description: Unity 2D 횡스크롤 REPLACED 스타일 적 AI 전문 스킬. 비헤이비어 트리(BT) 기반 적 AI, 텔레그래프 시스템(빨강/노랑 인디케이터), AttackCoordinator(공격 턴 관리), 적 유형별 패턴 설계를 담당한다. "적 AI", "enemy AI", "텔레그래프", "telegraph", "인디케이터", "indicator", "빨간 신호", "노란 신호", "공격 턴", "attack turn", "어그로", "aggro", "공격 조율", "attack coordinator", "졸개", "아머", "돌진", "원거리", "엘리트" 등의 키워드에 트리거된다.
context: fork
---

# REPLACED 적 AI 전투 시스템

## 1. 기획 의도

REPLACED의 적들은 플레이어에게 **리듬을 만들어주는 존재**이다. 빨강/노랑 인디케이터로 플레이어가 언제 회피하고 언제 카운터할지 명확히 알려준다. 적들은 순서대로 공격하며 플레이어가 **프리플로우 전투의 흐름을 유지**할 수 있게 돕는다.

각 적의 AI는 고정적이지 않고, 상황에 따라 공격 패턴을 변동시켜 플레이어를 지루하게 하지 않는다. 적이 강할수록 더 정교한 패턴 조합과 즉각적인 대응을 시도한다.

---

## 2. 핵심 원칙 (5가지)

### 2.1 텔레그래프 명확성
모든 공격은 **반드시 시각적 예고 신호**를 가져야 한다. 플레이어는 시각 피드백만으로 회피 또는 카운터 결정을 내린다.

### 2.2 공격 턴 관리
동시에 공격하는 적의 수를 제한한다 (최대 2마리). 이를 통해 플레이어의 반응 난이도를 조절하고, 전투의 흐름을 유지한다.

### 2.3 호흡 시간 보장
연속 공격 사이에 **플레이어가 반응할 최소 시간**(약 0.5~1초)을 확보한다. 숨 쉴 틈 없는 전투는 피한다.

### 2.4 행동 다양성
같은 유형의 적이라도 각 턴마다 랜덤으로 패턴을 선택하여 플레이어가 적을 완전히 예측할 수 없게 한다.

### 2.5 난이도 스케일링
텔레그래프 표시 시간, 공격 빈도, 적의 이동 속도를 난이도별로 조절하여 게임 난이도를 선형으로 상승시킨다.

---

## 3. 텔레그래프 시스템

텔레그래프는 적의 공격 의도를 플레이어에게 미리 알리는 시각 신호 체계이다.

### 3.1 텔레그래프 타입 (Enum)

```csharp
public enum EnemyTelegraphType
{
    None,              // 신호 없음
    Red_Dodge,         // 빨강: 회피 필수 (피해 회피)
    Yellow_Counter     // 노랑: 카운터 권장 (카운터 이득)
}
```

### 3.2 TelegraphSystem 컴포넌트

```csharp
public class TelegraphSystem : MonoBehaviour
{
    private EnemyTelegraphType currentTelegraph = EnemyTelegraphType.None;
    private float telegraphDuration = 0.4f; // 난이도별 조절: Easy 0.5s, Normal 0.4s, Hard 0.3s
    private float telegraphTimer = 0f;

    private SpriteRenderer spriteRenderer;
    private Image telegraphIcon; // UI 텔레그래프 아이콘

    public void DisplayTelegraph(EnemyTelegraphType type)
    {
        currentTelegraph = type;
        telegraphTimer = telegraphDuration;

        // 색상 변경 (적 몸체)
        switch (type)
        {
            case EnemyTelegraphType.Red_Dodge:
                spriteRenderer.color = Color.red;
                break;
            case EnemyTelegraphType.Yellow_Counter:
                spriteRenderer.color = Color.yellow;
                break;
        }

        // 아이콘 표시 (머리 위)
        if (telegraphIcon != null)
            telegraphIcon.enabled = true;

        CombatEventBus.RaiseEnemyTelegraph(gameObject, type);
    }

    public void Update()
    {
        if (telegraphTimer > 0f)
        {
            telegraphTimer -= Time.deltaTime;
            if (telegraphTimer <= 0f)
            {
                ClearTelegraph();
            }
        }
    }

    private void ClearTelegraph()
    {
        currentTelegraph = EnemyTelegraphType.None;
        spriteRenderer.color = Color.white;
        if (telegraphIcon != null)
            telegraphIcon.enabled = false;
    }
}
```

### 3.3 텔레그래프 타이밍
- **Easy**: 0.5초
- **Normal**: 0.4초
- **Hard**: 0.3초

---

## 4. AttackCoordinator (공격 조율자)

전체 맵에 존재하는 모든 적의 공격 스케줄을 중앙에서 관리한다.

### 4.1 Singleton 패턴

```csharp
public class AttackCoordinator : MonoBehaviour
{
    public static AttackCoordinator Instance { get; private set; }

    private List<Enemy> activeAttackers = new List<Enemy>();
    private float maxSimultaneousAttackers = 2;

    private float attackCooldown = 1.5f; // 난이도별 조절: Easy 2.5s, Normal 1.5s, Hard 1.0s
    private float cooldownTimer = 0f;
    private float breathingTimer = 0.5f; // 공격 후 최소 대기 시간

    private bool playerIsCombo = false;
    private float comboDebuff = 0.7f; // 플레이어 콤보 중 공격 빈도 30% 감소

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public bool RequestAttackSlot(Enemy enemy)
    {
        // 슬롯이 비어있고 쿨다운이 끝났으면 승인
        if (activeAttackers.Count < maxSimultaneousAttackers && cooldownTimer <= 0f)
        {
            activeAttackers.Add(enemy);
            enemy.OnAttackGranted();
            return true;
        }

        return false;
    }

    public void ReleaseAttackSlot(Enemy enemy)
    {
        activeAttackers.Remove(enemy);

        // 호흡 시간 설정
        float finalCooldown = playerIsCombo ? attackCooldown * comboDebuff : attackCooldown;
        cooldownTimer = finalCooldown + breathingTimer;
    }

    public void OnPlayerComboStart()
    {
        playerIsCombo = true;
    }

    public void OnPlayerComboEnd()
    {
        playerIsCombo = false;
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }
}
```

### 4.2 주요 메서드
- `RequestAttackSlot(Enemy)` → bool: 공격 허가 요청
- `ReleaseAttackSlot(Enemy)`: 공격 슬롯 반환 및 쿨다운 시작
- `OnPlayerComboStart()`: 플레이어 콤보 시작 신호
- `OnPlayerComboEnd()`: 플레이어 콤보 종료 신호

---

## 5. 비헤이비어 트리 아키텍처

적 AI는 비헤이비어 트리(BT) 기반으로 설계되어, 공격, 회피, 추적 등의 복잡한 결정을 계층적으로 처리한다.

### 5.1 BTNode 기본 구조

```csharp
public abstract class BTNode
{
    public enum Status { Running, Success, Failure }

    protected Blackboard blackboard;
    protected Enemy owner;

    public BTNode(Enemy owner)
    {
        this.owner = owner;
        this.blackboard = owner.Blackboard;
    }

    public abstract Status Tick();
}

public class BTSelector : BTNode
{
    private List<BTNode> children = new List<BTNode>();

    public override Status Tick()
    {
        foreach (var child in children)
        {
            var status = child.Tick();
            if (status != Status.Failure)
                return status;
        }
        return Status.Failure;
    }
}

public class BTSequence : BTNode
{
    private List<BTNode> children = new List<BTNode>();

    public override Status Tick()
    {
        foreach (var child in children)
        {
            var status = child.Tick();
            if (status != Status.Success)
                return status;
        }
        return Status.Success;
    }
}
```

### 5.2 주요 Leaf 노드들

```csharp
// 플레이어 범위 체크
public class CheckPlayerInRange : BTNode
{
    private float detectionRange = 8f;

    public override Status Tick()
    {
        float distToPlayer = Vector2.Distance(owner.transform.position,
                                             GameManager.Player.transform.position);
        blackboard.Set("distToPlayer", distToPlayer);

        return distToPlayer <= detectionRange ? Status.Success : Status.Failure;
    }
}

// 플레이어 추격
public class ChasePlayer : BTNode
{
    private float moveSpeed = 3f;

    public override Status Tick()
    {
        Vector2 direction = (GameManager.Player.transform.position - owner.transform.position).normalized;
        owner.Move(direction * moveSpeed);
        return Status.Running;
    }
}

// 공격 패턴 선택
public class SelectAttackPattern : BTNode
{
    public override Status Tick()
    {
        // 적 유형별 패턴 풀에서 랜덤 선택
        AttackPattern pattern = owner.GetRandomAttackPattern();
        blackboard.Set("selectedPattern", pattern);
        return Status.Success;
    }
}

// 텔레그래프 표시
public class Telegraph : BTNode
{
    public override Status Tick()
    {
        var pattern = blackboard.Get<AttackPattern>("selectedPattern");
        owner.TelegraphSystem.DisplayTelegraph(pattern.telegraphType);
        blackboard.Set("telegraphTime", pattern.telegraphDuration);
        return Status.Success;
    }
}

// 공격 실행
public class ExecuteAttack : BTNode
{
    public override Status Tick()
    {
        // AttackCoordinator에 슬롯 요청
        if (AttackCoordinator.Instance.RequestAttackSlot(owner))
        {
            owner.PerformAttack();
            return Status.Running;
        }
        return Status.Failure; // 공격 슬롯 없음
    }
}

// 후퇴
public class Retreat : BTNode
{
    private float retreatDistance = 2f;
    private float moveSpeed = 4f;

    public override Status Tick()
    {
        Vector2 direction = (owner.transform.position -
                           GameManager.Player.transform.position).normalized;
        owner.Move(direction * moveSpeed);
        return Status.Running;
    }
}

// 플레이어 주위 회전
public class CirclePlayer : BTNode
{
    private float circleRadius = 4f;
    private float moveSpeed = 2f;

    public override Status Tick()
    {
        Vector2 toPlayer = (GameManager.Player.transform.position -
                          owner.transform.position).normalized;
        Vector2 circleDir = new Vector2(-toPlayer.y, toPlayer.x).normalized;
        owner.Move(circleDir * moveSpeed);
        return Status.Running;
    }
}
```

### 5.3 Blackboard (공유 데이터)

```csharp
public class Blackboard
{
    private Dictionary<string, object> data = new Dictionary<string, object>();

    public void Set<T>(string key, T value)
    {
        data[key] = value;
    }

    public T Get<T>(string key, T defaultValue = default)
    {
        return data.ContainsKey(key) ? (T)data[key] : defaultValue;
    }

    public bool Has(string key) => data.ContainsKey(key);
}
```

---

## 6. 적 유형 템플릿 (5종)

### 6.1 일반 졸개 (Grunt)

```csharp
// 특징: 낮은 체력, 단타 공격, Yellow 인디케이터
public class GruntEnemy : Enemy
{
    protected override void InitializeAttackPatterns()
    {
        patterns = new List<AttackPattern>
        {
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Yellow_Counter,
                telegraphDuration = 0.4f,
                damageAmount = 10f,
                cooldown = 2f
            }
        };
    }
}
```

### 6.2 아머 병사 (Armored)

```csharp
// 특징: 방어막(실드), 강한 공격, Red 인디케이터, Heavy 공격으로만 파괴
public class ArmoredEnemy : Enemy
{
    private float shieldHealth = 30f;
    private bool shieldActive = true;

    protected override void InitializeAttackPatterns()
    {
        patterns = new List<AttackPattern>
        {
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Red_Dodge,
                telegraphDuration = 0.3f,
                damageAmount = 20f,
                cooldown = 2.5f
            }
        };
    }

    public override void TakeHit(HitData hitData)
    {
        if (shieldActive && hitData.isHeavyAttack)
        {
            shieldHealth -= hitData.damage;
            if (shieldHealth <= 0f)
                shieldActive = false;
        }
        else if (!shieldActive)
        {
            base.TakeHit(hitData);
        }
    }
}
```

### 6.3 돌진형 (Charger)

```csharp
// 특징: 차지 동작, 빠른 돌진 공격, 회피 시 스턴, Red 인디케이터
public class ChargerEnemy : Enemy
{
    private float chargeSpeed = 12f;
    private float chargeDistance = 6f;

    protected override void InitializeAttackPatterns()
    {
        patterns = new List<AttackPattern>
        {
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Red_Dodge,
                telegraphDuration = 0.5f, // 차지 시간 포함
                damageAmount = 25f,
                cooldown = 3f,
                hasCharge = true
            }
        };
    }

    public override void OnDodgeSuccess()
    {
        // 회피 성공 시 스턴 상태
        stunnedDuration = 1.5f;
    }
}
```

### 6.4 원거리형 (Ranged)

```csharp
// 특징: 원거리 사격, 근접 시 후퇴, Red 인디케이터
public class RangedEnemy : Enemy
{
    private float optimalRange = 6f;
    private float retreatTriggerRange = 2f;

    protected override void InitializeAttackPatterns()
    {
        patterns = new List<AttackPattern>
        {
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Red_Dodge,
                telegraphDuration = 0.4f,
                damageAmount = 15f,
                cooldown = 1.5f,
                isRanged = true
            }
        };
    }

    public override void TickBehavior()
    {
        float distToPlayer = Vector2.Distance(transform.position,
                                             GameManager.Player.transform.position);

        if (distToPlayer < retreatTriggerRange)
        {
            // 플레이어가 너무 가까우면 후퇴
            behaviorTree.ExecuteNode(typeof(Retreat));
        }
        else
        {
            base.TickBehavior();
        }
    }
}
```

### 6.5 엘리트 (Elite)

```csharp
// 특징: Yellow+Red 콤보 패턴, 높은 체력, 페이즈 전환
public class EliteEnemy : Enemy
{
    private float phase1HealthThreshold = 0.66f;
    private float phase2HealthThreshold = 0.33f;

    protected override void InitializeAttackPatterns()
    {
        patterns = new List<AttackPattern>
        {
            // 페이즈 1: 단순 공격
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Yellow_Counter,
                telegraphDuration = 0.4f,
                damageAmount = 15f,
                cooldown = 2f
            },
            // 페이즈 2: 콤보 공격 (Yellow → Red)
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Yellow_Counter,
                telegraphDuration = 0.4f,
                damageAmount = 10f,
                cooldown = 1.2f,
                isCombo = true,
                nextPatternDelay = 0.8f
            },
            new AttackPattern
            {
                telegraphType = EnemyTelegraphType.Red_Dodge,
                telegraphDuration = 0.3f,
                damageAmount = 20f,
                cooldown = 0f
            }
        };
    }

    public override void TakeHit(HitData hitData)
    {
        base.TakeHit(hitData);
        float healthPercent = currentHealth / maxHealth;

        if (healthPercent <= phase2HealthThreshold && currentPhase < 2)
        {
            currentPhase = 2;
            OnPhaseTransition();
        }
        else if (healthPercent <= phase1HealthThreshold && currentPhase < 1)
        {
            currentPhase = 1;
            OnPhaseTransition();
        }
    }

    private void OnPhaseTransition()
    {
        // 페이즈 전환 특수 효과 (연기, 크라이 등)
        CombatEventBus.RaiseEnemyPhaseTransition(this, currentPhase);
    }
}
```

---

## 7. 적 필수 인터페이스

### 7.1 ICombatTarget

```csharp
public interface ICombatTarget
{
    bool IsTargetable();
    void TakeHit(HitData hitData);
    Vector3 GetPosition();
    float GetHealth();
}
```

### 7.2 ITelegraphable

```csharp
public interface ITelegraphable
{
    EnemyTelegraphType GetTelegraphType();
    float GetTelegraphDuration();
    void DisplayTelegraph(EnemyTelegraphType type);
}
```

### 7.3 IAttackCoordinated

```csharp
public interface IAttackCoordinated
{
    bool RequestAttack();
    void OnAttackGranted();
    void OnAttackDenied();
    void ReleaseAttackSlot();
}
```

---

## 8. 코드 작성 시 체크리스트

적 AI 관련 코드를 작성할 때 반드시 확인할 항목들:

1. **텔레그래프 신호**: 모든 공격이 Red 또는 Yellow 신호를 가지고 있는가? 신호 지속 시간이 난이도에 따라 조절되는가?

2. **공격 슬롯 요청**: 적이 공격을 시작하기 전에 `AttackCoordinator.RequestAttackSlot()`을 호출하는가?

3. **호흡 시간**: 공격 종료 후 `ReleaseAttackSlot()`으로 쿨다운을 시작하고 있는가? breathingTimer 값이 명합한가?

4. **패턴 다양성**: 동일 유형의 적에게 최소 2개 이상의 공격 패턴이 정의되었는가? 패턴 선택이 랜덤인가?

5. **난이도 반영**: telegraphDuration, attackCooldown 값이 Difficulty 설정에 따라 동적으로 조절되는가?

6. **Blackboard 활용**: BT 내 노드들이 Blackboard를 통해 상태를 공유하고 있는가? 중복 계산은 없는가?

7. **이벤트 발신**: 텔레그래프, 페이즈 전환, 사망 등 주요 이벤트가 CombatEventBus를 통해 발신되는가?

8. **인터페이스 구현**: Enemy 기본 클래스가 ICombatTarget, ITelegraphable, IAttackCoordinated를 모두 상속/구현하는가?

---

## 9. 이 스킬이 다루지 않는 것

이 스킬은 적 AI 및 텔레그래프 시스템에 집중하며, 다음은 별도의 스킬/모듈에서 담당한다:

- **플레이어 조작**: 입력 처리, 카운터 판정, 회피 판정은 PlayerController에서 관리
- **데미지 공식**: 최종 데미지 계산, 방어도 적용은 DamageCalculator에서 담당
- **히트 리액션 물리**: 피격 노크백, 캔슬, 상태 이상 효과는 HitReactionSystem에서 처리
- **UI 피드백**: 콤보 카운터, 점수 표시, 이펙트 재생은 CombatUI에서 관리
- **오디오**: 공격음, 히트음, 텔레그래프 음성 신호는 AudioManager에서 담당

---

**작성일**: 2026-03-23 | **버전**: 1.0 | **상태**: 완성

