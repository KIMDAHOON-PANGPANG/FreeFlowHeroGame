---
name: replaced-combat:execution-huxley
description: Unity 2D 횡스크롤 REPLACED 스타일 처형(Execution) 및 헉슬리 건(Huxley Gun) 시스템 스킬. 콤보 기반 게이지 충전, 원거리 사격, 시네마틱 처형 연출, 피니셔 시스템을 담당한다. "처형", "execution", "피니셔", "finisher", "헉슬리", "Huxley", "건", "gun", "충전", "charge", "시네마틱 킬", "cinematic kill", "게이지" 등의 키워드에 트리거된다.
context: fork
---

# REPLACED 처형 & 헉슬리 건 시스템

## 1. 기획 의도

REPLACED에서 처형(Execution)과 헉슬리 건(Huxley Gun)은 전투의 클라이맥스를 담당한다. 플레이어는 콤보를 쌓아 게이지를 충전하고, 축적된 게이지로 강력한 원거리 사격을 펼치거나 시네마틱 피니셔로 적을 제거한다. 이 두 시스템은 연속성 높은 전투 리듬에 절정의 순간을 만들어낸다.

## 2. 핵심 원칙

### 원칙 1: 보상으로서의 처형
처형은 플레이어가 잘 싸웠을 때에만 가능한 보상이다. 시각적 연출과 게임플레이 측면에서 모두 만족감을 제공한다.

### 원칙 2: 콤보 기반 경제
헉슬리 게이지는 오직 콤보 히트로만 충전된다. 각 콤보 카운트에 따라 충전량이 증가하며, 콤보가 끊기면 게이지도 함께 감소한다.

### 원칙 3: 시네마틱 전환
처형이나 헉슬리 피니셔 실행 시 게임 흐름을 일시 정지하고 영화 같은 연출을 펼친다. 이를 통해 전투의 클라이맥스 느낌을 극대화한다.

### 원칙 4: 전략적 선택
플레이어는 게이지를 즉각 사격에 사용할지, 아니면 피니셔를 위해 계속 모을지를 전략적으로 판단해야 한다.

## 3. 헉슬리 건 시스템 아키텍처

### HuxleyGunSystem 클래스 설계

```csharp
public class HuxleyGunSystem : MonoBehaviour
{
    [SerializeField] private HuxleyConfig config;
    [SerializeField] private CombatEventBus eventBus;

    private float currentCharge = 0f;
    private float maxCharge = 100f;
    private int comboCount = 0;

    public void OnComboHit(int hitCount)
    {
        // 충전 공식 적용
        float chargePerHit = config.baseCharge * GetComboMultiplier();
        currentCharge = Mathf.Min(currentCharge + chargePerHit, maxCharge);
        UpdateChargeStage();
        eventBus.RaiseHuxleyChargeChanged(currentCharge);
    }

    private float GetComboMultiplier()
    {
        // comboMultiplier = 1.0 + (comboCount × 0.05)
        return 1.0f + (comboCount * 0.05f);
    }

    public void OnPlayerHit()
    {
        // 피격 시 게이지 20% 감소
        currentCharge *= 0.8f;
        eventBus.RaiseHuxleyChargeChanged(currentCharge);
    }

    public void OnComboBreak()
    {
        // 콤보 끊김 시 게이지 10% 감소
        currentCharge *= 0.9f;
        comboCount = 0;
        eventBus.RaiseHuxleyChargeChanged(currentCharge);
    }

    public void FireNormalShot()
    {
        // 33% 소비
        if (currentCharge >= maxCharge * 0.33f)
        {
            currentCharge -= maxCharge * 0.33f;
            SpawnProjectile(ProjectileType.Normal);
            eventBus.RaiseHuxleyShot(ProjectileType.Normal);
        }
    }

    public void FireChargedShot()
    {
        // 66% 소비
        if (currentCharge >= maxCharge * 0.66f)
        {
            currentCharge -= maxCharge * 0.66f;
            SpawnProjectile(ProjectileType.Charged);
            eventBus.RaiseHuxleyShot(ProjectileType.Charged);
        }
    }

    public void FireFinisher()
    {
        // 100% 소비 - 최강 공격
        if (currentCharge >= maxCharge)
        {
            currentCharge = 0f;
            SpawnProjectile(ProjectileType.Finisher);
            eventBus.RaiseHuxleyShot(ProjectileType.Finisher);
        }
    }

    private void UpdateChargeStage()
    {
        float chargePercent = currentCharge / maxCharge;
        // 0%→33%(1발)→66%(2발)→100%(피니셔)
    }
}
```

### 충전 공식

```
chargePerHit = baseCharge × comboMultiplier
comboMultiplier = 1.0 + (comboCount × 0.05)
baseCharge = 5.0 (기본값)
```

### 충전 단계

| 단계 | 범위 | 효과 |
|------|------|------|
| 1발 모드 | 0%~33% | 일반 사격 가능 |
| 2발 모드 | 33%~66% | 강화 사격 가능 |
| 피니셔 모드 | 66%~100% | 시네마틱 피니셔 가능 |

### 감소 조건

- **피격**: 현재 게이지 × 0.8 (-20%)
- **콤보끊김**: 현재 게이지 × 0.9 (-10%)
- **비전투 상태**: 5초 후 매초 -5%

### 발사 모드

1. **Normal Shot**: 게이지 33% 소비, 표준 탄환 발사
2. **Charged Shot**: 게이지 66% 소비, 강화된 탄환 발사
3. **Finisher**: 게이지 100% 소비, 시네마틱 피니셔 + 광역 피해

## 4. 처형(Execution) 시스템

### ExecutionSystem 클래스 설계

```csharp
public class ExecutionSystem : MonoBehaviour
{
    [SerializeField] private ExecutionConfig config;
    [SerializeField] private CombatEventBus eventBus;
    [SerializeField] private Transform player;

    private Enemy targetEnemy;
    private bool canExecute = false;

    public void CheckExecutionCondition(Enemy enemy)
    {
        // 조건 판정: HP ≤ threshold(20%) + 거리 ≤ range(2.0)
        float hpPercent = enemy.CurrentHP / enemy.MaxHP;
        float distance = Vector2.Distance(player.position, enemy.transform.position);

        bool hpThresholdMet = hpPercent <= config.hpThreshold; // 0.2f
        bool distanceMet = distance <= config.executionRange;   // 2.0f
        bool stateAllowed = IsPlayerInValidState();              // Idle/Strike/Combo

        canExecute = hpThresholdMet && distanceMet && stateAllowed;

        if (canExecute)
        {
            targetEnemy = enemy;
            ShowExecutionPrompt(enemy);
        }
    }

    private bool IsPlayerInValidState()
    {
        // 플레이어 상태 확인: Idle, Strike, Combo
        return player.GetComponent<PlayerController>().IsInValidExecutionState();
    }

    public void ExecuteNormalExecution(Enemy target)
    {
        if (!canExecute) return;

        eventBus.RaiseExecutionStart(target, ExecutionType.Normal);
        StartCoroutine(ExecutionCinematic(target, ExecutionType.Normal));
    }

    public void ExecuteHuxleyFinisher(Enemy target, HuxleyGunSystem gunSystem)
    {
        if (gunSystem.CurrentCharge < gunSystem.MaxCharge) return;

        eventBus.RaiseExecutionStart(target, ExecutionType.HuxleyFinisher);
        StartCoroutine(ExecutionCinematic(target, ExecutionType.HuxleyFinisher));
    }

    private void ShowExecutionPrompt(Enemy enemy)
    {
        // 적 머리 위에 처형 가능 아이콘 표시
        Vector3 promptPos = enemy.transform.position + Vector3.up * 2.5f;
        Instantiate(config.executionPromptPrefab, promptPos, Quaternion.identity);
    }
}
```

### 처형 조건

```
적HP ≤ threshold (기본 20%)
+ 거리 ≤ range (기본 2.0 유닛)
+ 플레이어상태 (Idle/Strike/Combo)
```

## 5. 시네마틱 연출 파이프라인

### 처형 영화 연출 단계

```csharp
private IEnumerator ExecutionCinematic(Enemy target, ExecutionType type)
{
    // Phase 1: 시간 정지
    float prevTimeScale = Time.timeScale;
    Time.timeScale = 0.01f;
    yield return new WaitForSeconds(0.1f);

    // Phase 2: 카메라 줌인 + 플레이어 워핑
    Camera mainCam = Camera.main;
    Vector3 targetPos = target.transform.position;
    StartCoroutine(CameraZoomIn(mainCam, targetPos, 1.5f));
    player.position = Vector3.Lerp(player.position, targetPos - Vector3.right * 1.5f, 0.3f);
    yield return new WaitForSeconds(0.3f);

    // Phase 3: 처형 애니메이션 재생 (무적 상태)
    PlayerController playerCtrl = player.GetComponent<PlayerController>();
    playerCtrl.SetInvincible(true);

    Animator playerAnimator = player.GetComponent<Animator>();
    playerAnimator.SetTrigger("ExecutionAttack");
    yield return new WaitForSeconds(1.5f);

    // Phase 4: 주변 적 데미지/넉백 (헉슬리 피니셔 시에만)
    if (type == ExecutionType.HuxleyFinisher)
    {
        ApplyAOEDamage(targetPos, 5.0f);
        yield return new WaitForSeconds(0.5f);
    }

    // Phase 5: 시간 복귀 + 카메라 복귀
    target.Die();
    Time.timeScale = prevTimeScale;
    StartCoroutine(CameraZoomOut(mainCam, 1.0f));
    playerCtrl.SetInvincible(false);

    eventBus.RaiseExecutionEnd(target);
}

private IEnumerator CameraZoomIn(Camera cam, Vector3 targetPos, float zoomAmount)
{
    float originalSize = cam.orthographicSize;
    float targetSize = originalSize / zoomAmount;
    float elapsed = 0f;
    float duration = 0.3f;

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        cam.orthographicSize = Mathf.Lerp(originalSize, targetSize, elapsed / duration);
        cam.transform.position = Vector3.Lerp(cam.transform.position,
                                              targetPos + Vector3.back * 10f,
                                              elapsed / duration);
        yield return null;
    }
}

private void ApplyAOEDamage(Vector3 center, float radius)
{
    Collider2D[] enemies = Physics2D.OverlapCircleAll(center, radius);
    foreach (var col in enemies)
    {
        if (col.CompareTag("Enemy"))
        {
            Enemy enemy = col.GetComponent<Enemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(50); // 고정 피해
                enemy.ApplyKnockback(center, 10f);
            }
        }
    }
}
```

## 6. CombatEventBus 연동

### 수신하는 이벤트

```csharp
eventBus.OnAttackHit += huxleyGun.OnComboHit;        // 충전 증가
eventBus.OnComboBreak += huxleyGun.OnComboBreak;    // 게이지 감소
eventBus.OnPlayerHit += huxleyGun.OnPlayerHit;       // 게이지 감소
```

### 발신하는 이벤트

```csharp
eventBus.RaiseHuxleyChargeChanged(float charge);     // 게이지 변경 UI 갱신
eventBus.RaiseHuxleyShot(ProjectileType type);       // 사격 실행
eventBus.RaiseExecutionStart(Enemy target, ExecutionType type);  // 처형 시작
eventBus.RaiseExecutionEnd(Enemy target);             // 처형 종료
```

## 7. HuxleyConfig & ExecutionConfig ScriptableObject

```csharp
[CreateAssetMenu(fileName = "HuxleyConfig", menuName = "Combat/HuxleyGunConfig")]
public class HuxleyConfig : ScriptableObject
{
    public float baseCharge = 5.0f;
    public float maxCharge = 100f;
    public float normalShotCost = 33f;
    public float chargedShotCost = 66f;
    public float finisherCost = 100f;
    public float playerHitPenalty = 0.8f;  // ×0.8 = -20%
    public float comboBreakPenalty = 0.9f; // ×0.9 = -10%
    public float idleDecayRate = 5f;        // 초당 5%
    public float idleDecayDelay = 5f;       // 5초 후 시작
}

[CreateAssetMenu(fileName = "ExecutionConfig", menuName = "Combat/ExecutionConfig")]
public class ExecutionConfig : ScriptableObject
{
    public float hpThreshold = 0.2f;         // 20% 이하
    public float executionRange = 2.0f;     // 2.0 유닛 이내
    public GameObject executionPromptPrefab;
    public float cinematicDuration = 2.0f;
    public float timeScaleMultiplier = 0.01f;
    public float zoomMultiplier = 1.5f;
}
```

## 8. 코드 작성 시 체크리스트

- [ ] **1. 게이지 수식 정확성**: chargePerHit = baseCharge × (1.0 + comboCount × 0.05) 공식 재확인
- [ ] **2. 이벤트 등록/해제**: OnEnable에서 Subscribe, OnDisable에서 Unsubscribe 구현
- [ ] **3. Time.timeScale 복귀**: 예외 발생 시에도 반드시 원래 값으로 복구
- [ ] **4. 애니메이션 상태 관리**: 처형 중 플레이어 입력 차단 및 무적 상태 유지
- [ ] **5. 카메라 경계 검사**: 줌인 시 카메라가 월드 끝에서 벗어나지 않도록
- [ ] **6. 게이지 UI 동기화**: HuxleyChargeChanged 이벤트마다 UI 퍼센트 갱신
- [ ] **7. 상태 머신 통합**: PlayerController의 상태 머신과 처형 상태 동기화
- [ ] **8. 효과음/VFX**: 사격과 처형 시 각각 소리/파티클 이펙트 재생 (별도 시스템)

## 9. 이 스킬이 다루지 않는 것

- **플레이어 기본 조작**: 좌우 이동, 점프, 공격 입력은 PlayerController에서 관리
- **적 AI**: 적의 행동 패턴 및 공격 방식은 EnemyAI 시스템에서 관리
- **데미지 공식**: 기본 공격 데미지, 크리티컬, 방어력 계산은 별도 DamageCalculator에서 처리
