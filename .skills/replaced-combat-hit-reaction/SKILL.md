---
name: replaced-combat:hit-reaction
description: Unity 2D 횡스크롤 REPLACED 스타일 히트 리액션 매니저 스킬. 피격 시 적/플레이어에게 적용되는 히트 리액션(Flinch, Hit Stun, Stagger, Knockback, Knockdown, Execution Kill) 선택/실행, 히트스탑/카메라셰이크 연출, 넉백 물리를 담당한다. "히트 리액션", "hit reaction", "flinch", "stagger", "knockback", "히트스탑", "hit stop", "카메라 셰이크", "camera shake", "경직", "넉백", "피격 모션", "피격 반응" 등의 키워드에 트리거된다.
context: fork
---

# REPLACED 스타일 히트 리액션 매니저

## 1. 기획 의도

REPLACED의 전투는 **때리는 맛**이 핵심이다. 단순한 데미지 수치의 감소가 아니라, 플레이어와 적 모두가 체감하는 타격감과 현장감을 극대화해야 한다.

- **히트스탑(Hit Stop)**: 프레임 정지로 임팩트 강조 (2~15프레임)
- **넉백(Knockback)**: 적절한 거리감과 무게감 표현 (3~5 유닛)
- **카메라 셰이크(Camera Shake)**: 이 아래 진동으로 충격 연출
- **애니메이션 + VFX + SFX**: 종합적인 감각 자극

이 세 가지가 결합될 때, 게임은 부드럽고 반응성 높은 전투 경험을 제공한다.

---

## 2. 핵심 원칙 (4가지)

### 2.1 타격감 최우선
모든 히트에 히트스탑 + 카메라셰이크 + VFX가 세트로 적용된다. 피격 없이 끝나는 공격은 없다.

### 2.2 우선순위 기반 처리
강한 리액션(Knockdown)이 약한 리액션(Flinch)을 덮어씀. 동시에 여러 히트가 들어올 때 우선순위를 따른다.

### 2.3 데이터 드리븐 설계
모든 파라미터(히트스탑 프레임, 넉백 거리, 카메라셰이크 강도)는 `HitReactionData` ScriptableObject에서 관리. 코드 수정 없이 배런스 조정 가능.

### 2.4 파이프라인 처리 보장
판정 → 히트스탑 → 리액션 → 리커버리의 순서가 항상 일관되게 실행된다.

---

## 3. 히트 리액션 타입 정의 (7가지)

| 우선순위 | 타입 | 개념 | 히트스탑 | 스턴 시간 | 넉백 거리 | 활용 |
|----------|------|------|---------|----------|----------|------|
| 1 | Flinch | 약한 경직 | 2~3f | 없음 | 0 | 약공격, 연속 피격 |
| 2 | Hit Stun | 콤보 연속 피격 | 4~6f | 4f | 0.5~1 | 공격 체인, 연타 |
| 3 | Stagger | 강한 경직 + 밀림 | 6~8f | 10f | 2~3 | 강공격, 무거운 무기 |
| 4 | Knockback | 넉백 (서기 유지) | 6f | 5f | 3~5 | 카운터, Heavy 공격 |
| 5 | Knockdown | 완전 쓰러짐 | 8f | 15~20f | 4~6 | 강한 넉백, 보스 기술 |
| 6 | Launch | 공중으로 띄움 | 6f | 8f | (0, 4~6) | 에어 콤보 개시 |
| 7 | Execution Kill | 처형 시네마틱 | 10~15f | 무한 | 0 | 그것까지 기술 |

---

## 4. HitReactionManager 아키텍처

### 4.1 주요 메서드

```csharp
public class HitReactionManager : MonoBehaviour
{
    private HitReactionFSM hitReactionFSM;
    private Rigidbody2D rigidBody2D;
    private Animator animator;
    private CameraShakeController cameraShake;
    private CombatEventBus eventBus;

    /// 히트 판정 수신 및 처리 진입점
    public void ProcessHit(HitData hitData, ICombatTarget target)
    {
        if (!ValidateHit(hitData, target)) return;

        HitReactionType reaction = DetermineReaction(hitData);
        HitReactionData reactionData = GetReactionData(reaction);

        if (!CanOverrideCurrentReaction(reactionData.priority))
        {
            return; // 현재 우선순위 높음, 무시
        }

        StartCoroutine(ExecuteHitReactionPipeline(reactionData, target, hitData));
    }

    /// 파이프라인: HitStop → Reaction → Recovery
    private IEnumerator ExecuteHitReactionPipeline(
        HitReactionData reactionData,
        ICombatTarget target,
        HitData hitData)
    {
        hitReactionFSM.TransitionTo(HitReactionState.HitStop);
        eventBus.Publish(new OnHitReactionStart(target, reactionData.reactionType));

        // 1. 히트스탑 (프레임 정지)
        yield return StartCoroutine(ApplyHitStop(reactionData.hitStopFrames));

        // 2. 카메라 셰이크 (즉시 시작, 병렬)
        cameraShake.PlayImpulse(
            reactionData.cameraShakeIntensity,
            reactionData.cameraShakeDuration);

        // 3. 리액션 재생
        hitReactionFSM.TransitionTo(HitReactionState.Reacting);
        animator.SetTrigger(reactionData.animationTrigger);

        // 4. 넉백 적용 (병렬)
        if (reactionData.knockbackForce.magnitude > 0)
        {
            StartCoroutine(ApplyKnockback(
                reactionData.knockbackForce,
                reactionData.knockbackDuration));
        }

        // 5. VFX 및 SFX 재생
        PlayVFXAndSFX(reactionData);

        // 6. 리커버리 대기
        hitReactionFSM.TransitionTo(HitReactionState.Recovering);
        yield return new WaitForSeconds(reactionData.stunDuration);

        // 7. 정상 상태로 복귀
        hitReactionFSM.TransitionTo(HitReactionState.Idle);
        eventBus.Publish(new OnHitReactionEnd(target));
    }

    /// 히트 유효성 검사 (친아군 판정, 무적 프레임 등)
    private bool ValidateHit(HitData hitData, ICombatTarget target)
    {
        if (target == null) return false;
        if (target.IsInvulnerable) return false;
        if (target.Team == hitData.AttackerTeam) return false;
        return true;
    }

    /// 공격 강도/타입에 따른 리액션 결정
    private HitReactionType DetermineReaction(HitData hitData)
    {
        if (hitData.IsExecutionKill) return HitReactionType.ExecutionKill;
        if (hitData.IsLaunchAttack) return HitReactionType.Launch;
        if (hitData.IsKnockdown) return HitReactionType.Knockdown;
        if (hitData.AttackPower >= 80) return HitReactionType.Knockback;
        if (hitData.AttackPower >= 50) return HitReactionType.Stagger;
        if (hitData.IsComboAttack) return HitReactionType.HitStun;
        return HitReactionType.Flinch;
    }

    /// 현재 진행 중인 리액션이 새로운 리액션으로 덮어씌워질 수 있는지 확인
    private bool CanOverrideCurrentReaction(int newPriority)
    {
        if (hitReactionFSM.CurrentState == HitReactionState.Idle)
            return true;

        int currentPriority = GetCurrentReactionPriority();
        return newPriority > currentPriority;
    }
}
```

### 4.2 HitReactionFSM (상태 머신)

```csharp
public enum HitReactionState
{
    Idle,        // 정상 상태
    HitStop,     // 시간 정지 (2~15f)
    Reacting,    // 피격 애니메이션 재생 중
    Recovering,  // 스턴 상태, 이동 불가
}

public class HitReactionFSM
{
    public HitReactionState CurrentState { get; private set; }

    public void TransitionTo(HitReactionState newState)
    {
        Debug.Log($"HitReaction: {CurrentState} → {newState}");
        CurrentState = newState;
    }
}
```

---

## 5. HitReactionData ScriptableObject

```csharp
[CreateAssetMenu(menuName = "REPLACED/HitReactionData")]
public class HitReactionData : ScriptableObject
{
    [SerializeField] public HitReactionType reactionType;
    [SerializeField] public int priority;

    [Header("히트스탑")]
    [SerializeField, Range(0, 20)] public int hitStopFrames = 6;

    [Header("카메라 셰이크")]
    [SerializeField, Range(0f, 2f)] public float cameraShakeIntensity = 1f;
    [SerializeField, Range(0f, 1f)] public float cameraShakeDuration = 0.3f;

    [Header("넉백")]
    [SerializeField] public Vector2 knockbackForce = new Vector2(4f, 0f);
    [SerializeField, Range(0f, 1f)] public float knockbackDuration = 0.2f;
    [SerializeField] public AnimationCurve knockbackFalloff =
        AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("경직 (Stun)")]
    [SerializeField, Range(0f, 5f)] public float stunDuration = 0.4f;

    [Header("애니메이션")]
    [SerializeField] public string animationTrigger = "hit_flinch";

    [Header("이펙트")]
    [SerializeField] public GameObject vfxPrefab;
    [SerializeField] public AudioClip sfxClip;
    [SerializeField, Range(0f, 1f)] public float sfxVolume = 1f;
}
```

---

## 6. 히트스탑 구현

히트스탑은 **Time.timeScale 조절** 방식으로 구현한다.

```csharp
private IEnumerator ApplyHitStop(int frames)
{
    float originalTimeScale = Time.timeScale;
    Time.timeScale = 0f; // 전체 시간 정지

    float hitStopDuration = frames / 60f; // 60fps 기준
    float elapsed = 0f;

    while (elapsed < hitStopDuration)
    {
        elapsed += Time.realtimeSinceStartup * 0f; // 실제 시간으로 대기
        yield return null;
    }

    Time.timeScale = originalTimeScale; // 복귀
}
```

### 6.1 히트스탑 지속시간 기준

| 공격 강도 | 히트스탑 | 사례 |
|---------|---------|------|
| 약공격 | 2~3f | 손, 가벼운 무기 |
| 일반 공격 | 4~6f | 일반 베기, 펀치 |
| 강공격 | 8~10f | 큰 검, 강력한 기술 |
| 특수 기술 | 10~15f | 보스 필살기, 처형 |

---

## 7. 넉백 물리

Rigidbody2D의 AddForce를 사용하되, 감쇠 커브로 자연스럽게 연출한다.

```csharp
private IEnumerator ApplyKnockback(Vector2 force, float duration)
{
    Vector2 initialForce = force;
    float elapsed = 0f;

    rigidBody2D.velocity = Vector2.zero; // 기존 속도 제거

    while (elapsed < duration)
    {
        float t = elapsed / duration;
        float falloff = knockbackFalloff.Evaluate(t);
        Vector2 currentForce = initialForce * falloff;

        rigidBody2D.velocity = currentForce / Time.deltaTime;

        elapsed += Time.deltaTime;
        yield return null;
    }

    rigidBody2D.velocity = Vector2.zero;
}

/// 벽 충돌 감지 시 추가 스턴 적용
private void OnCollisionEnter2D(Collision2D collision)
{
    if (collision.gameObject.CompareTag("Wall"))
    {
        AddExtraStun(0.2f); // 추가 0.2초 경직
        TriggerWallImpactVFX(collision.contacts[0]);
    }
}
```

---

## 8. 카메라 셰이크 (Cinemachine Impulse)

```csharp
private void PlayCameraShake(float intensity, float duration)
{
    if (cinemachineImpulseSource == null) return;

    cinemachineImpulseSource.m_ImpulseDefinition.m_TimeEnvelope.m_AttackTime = 0.05f;
    cinemachineImpulseSource.m_ImpulseDefinition.m_TimeEnvelope.m_SustainTime = duration;
    cinemachineImpulseSource.m_ImpulseDefinition.m_TimeEnvelope.m_DecayTime = 0.2f;

    cinemachineImpulseSource.GenerateImpulse(intensity);
}
```

---

## 9. CombatEventBus 연동

```csharp
// 수신
eventBus.Subscribe<OnAttackHit>(hitEvent =>
{
    HitReactionManager targetManager =
        hitEvent.Target.GetComponent<HitReactionManager>();
    targetManager.ProcessHit(hitEvent.HitData, hitEvent.Target);
});

// 발신
public class OnHitReactionStart
{
    public ICombatTarget Target { get; set; }
    public HitReactionType ReactionType { get; set; }
}

public class OnHitReactionEnd
{
    public ICombatTarget Target { get; set; }
}
```

---

## 10. 코드 작성 시 체크리스트

1. **우선순위 충돌**: 새 리액션이 진행 중인 리액션을 덮어씀. `CanOverrideCurrentReaction()` 호출 필수.
2. **히트스탑 중 물리**: AddForce는 Time.timeScale 영향받음. timeScale 0일 때 별도 처리 필요.
3. **애니메이션 상태 동기화**: Animator와 FSM이 항상 같은 상태를 가리키도록.
4. **넉백 벡터 방향**: 데미지 출처 방향으로 정규화 필수 (뒤로 날아가야 함).
5. **VFX 스폰 위치**: 히트 지점(HitData.ContactPoint) 기준으로 배치.
6. **SFX 재생**: 히트스탑 직후 (frameTime이 흘렀을 때) 재생하여 음성이 끊기지 않도록.
7. **리커버리 시간 초과**: stunDuration이 과도하면 게임이 답답함. 평균 0.3~0.8초 권장.
8. **무적 프레임**: 히트 직후 0.2초간 추가 히트 무시하거나, 우선순위 높은 공격만 받기.

---

## 11. 이 스킬이 다루지 않는 것

- **데미지 계산**: HP 감소, 크리티컬 판정은 다른 시스템에서 담당.
- **플레이어 FSM**: 플레이어의 행동(점프, 대시, 공격)은 PlayerController가 관리.
- **적 AI 로직**: 적이 피격 후 어떤 패턴으로 반격할지는 EnemyAI가 결정.
- **UI 연출**: 데미지 넘버, 플로팅 텍스트는 CombatUI가 담당.

---

## 12. 참고 데이터

### 히트스탑 프레임 변환
- 60fps 기준: 1f = 1/60초 ≈ 0.0167초
- 2f = 0.033초, 6f = 0.1초, 10f = 0.167초

### 넉백 거리 가이드
- 0.5 유닛: 거의 느껴지지 않음
- 2~3 유닛: 적절한 반응성 (권장)
- 5+ 유닛: 매우 강한 임팩트 (보스 기술)

### 카메라 셰이크 강도
- 0.2: 미묘한 진동
- 0.5~1.0: 일반 공격 (권장)
- 1.5~2.0: 극적 연출
