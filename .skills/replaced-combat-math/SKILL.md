---
name: replaced-combat:combat-math
description: Unity 2D 횡스크롤 REPLACED 스타일 전투 수치 밸런싱 스킬. 데미지 공식, 콤보 스케일링, 헉슬리 충전 수식, 아머 감소율, 처형 임계치, 난이도 곡선을 담당한다. "데미지 공식", "damage formula", "콤보 스케일링", "combo scaling", "밸런스", "balance", "수치", "공식", "스케일링", "난이도 곡선", "아머 감소", "처형 임계치" 등의 키워드에 트리거된다.
context: fork
---

# REPLACED 전투 수학 시스템

## 기획 의도

REPLACED의 전투 시스템은 수학적으로 엄밀하게 설계된 밸런스 위에 구축된다. 모든 공격이 적절한 임팩트를 제공하면서도, 콤보를 쌓는 것이 실질적인 보상으로 돌아오는 게임플레이를 만들어낸다. 이는 플레이어에게 공격을 이어가야 한다는 동기부여를 강하게 제공하며, 동시에 방어 실패가 명확한 페널티로 느껴지도록 한다.

---

## 핵심 원칙 4가지

### 1. 콤보가 왕
콤보를 유지할수록 모든 수치가 플레이어에게 유리해진다. 데미지 배율이 올라가고, 헉슬리 충전이 빨라지며, 적이 약해진다.

### 2. 체감 가능한 차이
공격 유형별 데미지 차이가 명확하게 느껴져야 한다. 약공과 강공의 임팩트 차이, 카운터의 특별함, 처형의 강력함이 모두 수치로 뒷받침된다.

### 3. 데이터 드리븐
모든 수식의 계수는 `CombatFormulaConfig` ScriptableObject에 외부화되어 있다. 프로그래머의 도움 없이 기획자가 에디터에서 실시간으로 수정하고 테스트할 수 있다.

### 4. 난이도는 수치로
적의 HP, 공격력, 텔레그래프 시간 등 모든 요소가 난이도별 배율을 통해 조절된다.

---

## 기본 데미지 공식

```csharp
finalDamage = baseDamage × attackMultiplier × comboScaling × armorReduction

// 반환값: float (최종 데미지)
// CombatMathSolver.CalculateDamage(HitData hitData) → float
```

### 공격 유형별 배율 (attackMultiplier)

- **Light Attack**: 1.0배 (기본 데미지)
- **Heavy Attack**: 2.0배 (큰 임팩트, 느린 속도)
- **Counter Attack**: 1.5배 (정확한 타이밍 보상)
- **Huxley Shot**: 1.8배 (특수 기술)
- **Execution**: 9999 (일격필살, 조건 제한)

### 콤보 스케일링 (comboScaling)

콤보 카운트에 따른 데미지 배율. 콤보를 이어갈수록 공격력이 증가한다.

| 콤보 구간 | 배율 |
|---------|------|
| 1~5     | 1.0배 |
| 6~10    | 1.1배 |
| 11~20   | 1.2배 |
| 21+     | 1.3배 |

### 아머 감소율 (armorReduction)

- **아머 없음**: 1.0배 (일반 데미지)
- **아머 상태**: 0.2배 (80% 감소, 방어 느낌)
- **아머 파괴 후**: 1.0배 (노출됨, 취약)

### 구현 예시

```csharp
public class CombatMathSolver
{
    public static float CalculateDamage(HitData hitData)
    {
        float baseDmg = hitData.baseDamage;
        float atkMult = GetAttackMultiplier(hitData.attackType);
        float comboScl = CalculateComboScaling(hitData.comboCount);
        float armorRed = GetArmorReduction(hitData.targetHasArmor);

        return baseDmg * atkMult * comboScl * armorRed;
    }
}
```

---

## 콤보 보너스 시스템

콤보가 특정 구간에 도달하면 특별한 보상이 주어진다.

### x5 "Good"
- 헉슬리 충전 +5% 추가

### x10 "Great"
- 적 공격빈도 -20% (적이 공격 자주 못함)

### x20 "Awesome"
- 헉슬리 충전 +10% 추가
- 플레이어 공격력 1.2배

### x50 "Unstoppable"
- 처형 임계치 20% → 30% (더 쉽게 일격필살)
- 시각/음향 피드백 강화

### 이벤트 시스템

```csharp
public class ComboManager
{
    // OnComboMilestone(comboCount, milestoneType)
    public event System.Action<int, MilestoneType> OnComboMilestone;

    private void CheckMilestone(int combo)
    {
        if (combo == 5) OnComboMilestone?.Invoke(5, MilestoneType.Good);
        if (combo == 10) OnComboMilestone?.Invoke(10, MilestoneType.Great);
        if (combo == 20) OnComboMilestone?.Invoke(20, MilestoneType.Awesome);
        if (combo == 50) OnComboMilestone?.Invoke(50, MilestoneType.Unstoppable);
    }
}
```

---

## 헉슬리 충전 공식

헉슬리(Huxley)는 특수 어빌리티 차징 게이지다.

```csharp
chargePerHit = baseCharge(5%) × (1 + comboCount × 0.05)
```

### 기본 예시

- 콤보 1: 5% 충전
- 콤보 5: 5% × (1 + 5 × 0.05) = 6.25% 충전
- 콤보 10: 5% × (1 + 10 × 0.05) = 7.5% 충전
- 콤보 20: 5% × (1 + 20 × 0.05) = 10% 충전

### 감소 규칙

- **피격 시**: -20% 즉시 감소
- **콤보 끊김**: -10% 감소
- **비전투 5초 경과 후**: 초당 -5% 감소

### 구현 예시

```csharp
public void OnHitTaken(HitData hitData)
{
    huxleyCharge -= 0.20f;
}

public void OnComboBreak()
{
    huxleyCharge -= 0.10f;
}

private void TickOutOfCombat(float deltaTime)
{
    if (timeSinceLastHit > 5f)
    {
        huxleyCharge -= 0.05f * deltaTime;
    }
}
```

---

## 아머 시스템 수치

### 아머 체력

- 기본 아머 HP: 50 포인트
- 별도 체력바로 표시 (일반 체력과 분리)

### 아머에 대한 공격력

- **Light Attack**: 0 데미지 (아머에 무효)
- **Heavy Attack**: 25 데미지 (2타에 파괴)
- **Counter Attack**: 10 데미지
- **Execution**: 아머 무시 (바로 최종타)

### 아머 파괴 시 반응

1. **Stagger 리액션**: 즉시 경직 애니메이션 재생
2. **3초 취약 상태**: 모든 공격에 1.5배 데미지
3. **시각 피드백**: 반짝임, 흔들림 등

### 아머 재생성

- 전투 30초 경과 시 자동으로 체력 시작(총 50 재생)
- 콤보 끊김 시 재생 시작

---

## 난이도 스케일링 테이블

### 스케일링 배율표

| 항목 | Easy | Normal | Hard |
|------|------|--------|------|
| 적 HP 배율 | 0.7배 | 1.0배 | 1.5배 |
| 적 공격력 배율 | 0.6배 | 1.0배 | 1.4배 |
| 텔레그래프 시간 배율 | 1.5배 | 1.0배 | 0.7배 |
| 동시공격자 수 | 1명 | 2명 | 3명 |
| 공격빈도 배율 | 0.6배 | 1.0배 | 1.3배 |

### DifficultyConfig ScriptableObject

```csharp
[System.Serializable]
public class DifficultyConfig : ScriptableObject
{
    public float enemyHPMultiplier = 1.0f;
    public float enemyAttackPowerMultiplier = 1.0f;
    public float telegraphTimeMultiplier = 1.0f;
    public int maxConcurrentEnemies = 2;
    public float attackFrequencyMultiplier = 1.0f;
}
```

---

## CombatFormulaConfig ScriptableObject 설계

### 필드 목록

```csharp
[System.Serializable]
public class CombatFormulaConfig : ScriptableObject
{
    // 공격 배율
    [Header("Attack Multipliers")]
    public float lightAttackMultiplier = 1.0f;
    public float heavyAttackMultiplier = 2.0f;
    public float counterAttackMultiplier = 1.5f;
    public float huxleyShotMultiplier = 1.8f;
    public float executionDamage = 9999f;

    // 콤보 스케일링
    [Header("Combo Scaling")]
    public float comboScaling_1_5 = 1.0f;
    public float comboScaling_6_10 = 1.1f;
    public float comboScaling_11_20 = 1.2f;
    public float comboScaling_21_plus = 1.3f;

    // 헉슬리 충전
    [Header("Huxley Charge")]
    public float baseChargePerHit = 0.05f;
    public float comboChargeBonus = 0.05f;
    public float chargeOnHit = -0.20f;
    public float chargeOnComboBreak = -0.10f;
    public float passiveDecayPerSecond = 0.05f;

    // 아머 시스템
    [Header("Armor System")]
    public float defaultArmorHP = 50f;
    public float heavyAttackArmorDamage = 25f;
    public float armorVulnerabilityDuration = 3f;
    public float armorReductionFactor = 0.2f;
    public float armorRegenWaitTime = 30f;

    // 난이도
    [Header("Difficulty Scaling")]
    public DifficultyConfig[] difficultyConfigs;
}
```

### 에디터에서 수정

Unity Editor의 Inspector에서 이 config를 선택하면 모든 값을 실시간으로 조정할 수 있다. 게임을 멈추지 않고 값을 변경하면 즉시 반영된다 (단, 런타임 수정).

---

## 코드 작성 시 체크리스트 (6항목)

1. **모든 계수는 CombatFormulaConfig에서 참조**
   - 하드코딩된 숫자가 없어야 함

2. **콤보 카운트를 정확히 추적**
   - 적중할 때마다 +1, 피격 또는 시간초과로 리셋

3. **HitData 구조체 활용**
   - 공격 유형, 콤보 수, 목표 아머 상태 등을 한곳에 담음

4. **이벤트 기반 콤보 보너스**
   - OnComboMilestone 이벤트 발동 시 각 시스템이 독립적으로 반응

5. **난이도별 스케일 적용**
   - 모든 적 스탯 초기화 시 DifficultyConfig 배율 곱하기

6. **테스트 데이터 로깅**
   - CalculateDamage 호출 시 콘솔에 계산 과정 출력 (Debug.Log)

---

## 이 스킬이 다루지 않는 것

- **플레이어 조작 입력**: InputManager는 별도 시스템
- **적 AI 결정 로직**: EnemyAIController는 독립적인 스킬
- **히트 리액션 연출**: AnimationController와 VFX는 별개 영역
- **UI 표시**: 콤보 카운터 UI, 헉슬리 게이지 바 구현
- **사운드 재생**: 타격음, 콤보 음성 등

---

## 최종 요약

REPLACED의 전투 수학은 **콤보 강화**, **아머 시스템**, **헉슬리 충전** 세 축으로 이루어진다. 모든 수치는 설정 가능하며, 난이도별로 유연하게 조절할 수 있다. 이를 통해 초보자부터 고난도 플레이어까지 모두 만족할 수 있는 밸런스를 구현한다.
