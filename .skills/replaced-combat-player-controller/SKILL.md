---
name: replaced-combat:player-controller
description: Unity 2D 횡스크롤 REPLACED 스타일 프리플로우 전투의 플레이어 컨트롤러 스킬. Batman Arkham 스타일의 프리플로우 전투를 2D로 구현. PlayerCombatFSM, FreeflowWarpSystem, ComboManager, DodgeController, CounterSystem, InputBuffer 등을 담당.
context: fork
---

# REPLACED 플레이어 전투 컨트롤러 스킬

## 기획 의도

REPLACED의 핵심적인 전투 감각은 **유려한 워핑(Warping)** 과 **리듬감 있는 콤보 체인** 입니다.
플레이어는 적들 사이를 매끄럽게 이동하며, 자신의 흐름을 유지하면서 무한한 콤보를 이어나갑니다.
이 스킬은 해당 느낌을 구현하기 위한 플레이어 컨트롤러의 핵심 시스템들을 정의합니다.

- **워핑의 즐거움**: 공격 시 자동으로 다음 적으로 이동하는 유려함
- **판정 투명성**: 모든 액션의 프레임 데이터가 명시되어 플레이어가 리듬감을 예측 가능
- **캔슬의 자유도**: 언제든 회피나 카운터로 액션을 캔슬하는 느낌
- **진행성**: 콤보 배수가 증가할수록 게이지와 리포드(보상)가 쌓인다는 느낌

---

## 핵심 원칙 (5가지)

### 1. 워핑 우선 (Warp-First Combat)
- **공격 진입 시** 자동으로 입력 방향 + 거리 기반 최적 타겟 선택
- **거리 범위**: 10~20 유닛 이내의 활성 적 중 선택
- **워핑 방식**: DOTween을 이용한 0.1~0.15초 스무드 이동
- **워핑 중 상태**: 이동 중에는 히트박스 비활성 (무적 X, 다만 판정 없음)

### 2. 캔슬 윈도우 (Cancelable Frames)
- **모든 공격 상태**에는 Dodge/Counter 진입 가능한 프레임 구간 존재
- **예시**: Light Attack (startup 5f → active 8f → recovery 12f) → recovery 8f부터 Dodge 캔슬 가능
- **목적**: 플레이어가 "반응형" 게임플레이를 경험하도록 설계

### 3. 인풋 버퍼링 (Input Buffering)
- **버퍼 크기**: 최근 1개 입력만 저장
- **버퍼 유효 시간**: 0.15초
- **소비 타이밍**: Recovery 상태의 마지막 프레임에서 버퍼 소비 → 자동으로 다음 액션 진입
- **효과**: 선입력의 자연스러운 연결감

### 4. 프레임 데이터 투명성 (Frame Data Transparency)
- **모든 공격**에 startup, active, recovery 프레임 수 명시
- **예시**:
  - Light Strike: (5, 8, 12)
  - Heavy Strike: (10, 15, 20)
  - Huxley Shot: (8, 12, 18)
- **용도**: 플레이어가 "리듬감" 있게 콤보를 익힐 수 있음

### 5. 상태 기반 분기 (State-Based Branching)
- **같은 입력이라도** 현재 FSM 상태에 따라 다른 액션으로 분기
- **예시**:
  - `Idle + Attack` → Strike (첫 공격)
  - `Strike + Attack` → ComboChain (다음 콤보 진입)
  - `Hit + Attack` → 무시 (피격 중 입력 무효)

---

## PlayerCombatFSM 아키텍처

### 상태 기본 클래스

```csharp
public abstract class CombatState
{
    protected PlayerCombatController controller;
    protected CombatContext context;

    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Update(float deltaTime) { }
    public virtual void HandleInput(InputData input) { }

    // 상태 이름 (디버깅용)
    public abstract string StateName { get; }
}
```

### FSM 상태 목록

| 상태 | 진입 조건 | 주요 기능 | 다음 상태 |
|------|---------|---------|---------|
| **Idle** | 전투 시작, 모든 액션 완료 | 입력 대기 | Strike, Dodge, Counter, Death |
| **Strike** | 공격 입력 (Light) | 기본 공격, 워핑 | ComboChain, Hit, Idle |
| **ComboChain** | Strike 중 공격 입력 (Light/Heavy) | 콤보 이어가기 | ComboChain, HeavyAttack, Hit, Idle |
| **HeavyAttack** | 콤보 3연 후 공격 입력 또는 Heavy 직접 입력 | 강공격 | Execution, ComboChain, Hit, Idle |
| **Execution** | HeavyAttack 캔슬, 특정 조건 | 처형 모션, 게이지 소비 | Idle, Hit |
| **HuxleyShot** | 게이지 쌓임, 특정 입력 | 특수 기술 | Idle, Hit |
| **Dodge** | Dodge 입력 + 방향 | 회피, i-frame, 콤보 리셋 | Idle, Hit, Counter (Dodge 중 카운터 감지) |
| **Counter** | 노란 인디케이터 감지 + Counter 입력 | 반격, 슬로우모션 | ComboChain, Hit, Idle |
| **Hit** | 적 공격 피격 | 피격 모션, 무적 시간 | Idle, Death, Counter (빠른 반격) |
| **Death** | HP ≤ 0 | 사망 모션, 입력 무효 | (게임 오버) |

### CombatContext (공유 데이터)

```csharp
public class CombatContext
{
    // 콤보 관련
    public int ComboCounter { get; set; } = 0;
    public float ComboWindowTimer { get; set; } = 0f; // 0.8초
    public int LastComboCount { get; set; } = 0;

    // 타겟 관련
    public Transform CurrentTarget { get; set; }
    public List<Transform> ActiveEnemies { get; set; } = new List<Transform>();

    // 게이지
    public float HuxleyGauge { get; set; } = 0f;
    public const float MaxHuxleyGauge = 100f;

    // 상태 제어
    public bool IsWarping { get; set; } = false;
    public bool CanCancel { get; set; } = true;

    // 프레임 카운팅
    public int FrameCounter { get; set; } = 0;

    public void ResetCombo()
    {
        LastComboCount = ComboCounter;
        ComboCounter = 0;
        ComboWindowTimer = 0f;
    }
}
```

### FSM 상태 전환 테이블 (간략)

```
Idle:
  - Attack Input → Strike (워핑 시작)
  - Dodge Input → Dodge
  - Counter Input (황색 표시) → Counter
  - HP <= 0 → Death

Strike:
  - Recovery 완료 + 버퍼 입력 있음 → ComboChain
  - Recovery 완료 + 버퍼 없음 → Idle
  - Dodge Input (캔슬 윈도우) → Dodge
  - Counter Input (캔슬 윈도우) → Counter
  - Damage taken → Hit

ComboChain:
  - Light Input (3회 미만) → ComboChain
  - Heavy Input 또는 3회 후 Light → HeavyAttack
  - Time > 0.8초 또는 피격 → ComboReset, Idle
  - Dodge/Counter (캔슬 윈도우) → Dodge/Counter

HeavyAttack:
  - Recovery 시작 시 Execution 체크 (게이지 충분?) → Execution
  - Recovery 완료 → Idle 또는 ComboChain

Dodge:
  - i-frame 12f 후 → Idle
  - 피격 중 → Hit (i-frame 무효)

Counter:
  - Perfect window (±3f) → 강화 반격, 콤보 +1, 슬로우모션
  - Normal window (±8f) → 일반 반격, 콤보 +0
  - Miss → Hit (피격)
  - 성공 시 → ComboChain

Hit:
  - Knockback duration 완료 → Idle
  - Counter input (빠른 반격) + Counter 창 내 → Counter
  - Damage 누적 → Death

Death:
  - (모든 입력 무효)
```

---

## FreeflowWarpSystem 상세 설계

### TargetSelector (목표물 선택 알고리즘)

```csharp
public class TargetSelector
{
    private float maxWarpDistance = 20f;
    private float angleToleranceDegrees = 100f;

    public Transform SelectTarget(
        Transform playerTransform,
        Vector2 inputDirection,
        List<Transform> activeEnemies)
    {
        // 1. 거리 필터: 20 유닛 이내 적만
        var candidateEnemies = activeEnemies
            .Where(e => Vector2.Distance(playerTransform.position, e.position) <= maxWarpDistance)
            .ToList();

        if (candidateEnemies.Count == 0)
            return null;

        // 2. 입력 방향 기반 가중치 계산
        // - 입력 방향 일치도: 각도 차이 (0~100도 내)
        // - 거리 페널티: 더 가까운 적 우선
        // - 지난 적 회피: 이미 공격한 적은 가중치 감소

        Transform bestTarget = null;
        float bestScore = float.MinValue;

        foreach (var enemy in candidateEnemies)
        {
            Vector2 toEnemy = ((Vector2)enemy.position - (Vector2)playerTransform.position).normalized;
            float angleDiff = Vector2.Angle(inputDirection, toEnemy);

            // 스코어 = 방향 가중치(50%) + 거리 가중치(50%)
            float directionScore = Mathf.Max(0, 1f - angleDiff / angleToleranceDegrees) * 50f;
            float distanceScore = (1f - GetNormalizedDistance(playerTransform, enemy)) * 50f;
            float totalScore = directionScore + distanceScore;

            if (totalScore > bestScore)
            {
                bestScore = totalScore;
                bestTarget = enemy;
            }
        }

        return bestTarget;
    }

    private float GetNormalizedDistance(Transform player, Transform enemy)
    {
        float distance = Vector2.Distance(player.position, enemy.position);
        return Mathf.Clamp01(distance / maxWarpDistance);
    }
}
```

### WarpMotion (워핑 이동 구현)

```csharp
public class WarpMotion
{
    private Tweener activeTween;
    private float warpDuration = 0.12f; // 약 7프레임 (60fps 기준)
    private AnimationCurve easeInOutCubic = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public void ExecuteWarp(
        Transform playerTransform,
        Transform targetTransform,
        System.Action onWarpComplete)
    {
        // 1. 기존 트윈 중지
        activeTween?.Kill();

        Vector3 startPos = playerTransform.position;
        Vector3 endPos = targetTransform.position + (Vector3)GetOptimalOffset(targetTransform);

        // 2. DOTween 기반 이동
        activeTween = DOTween
            .To(
                () => playerTransform.position,
                x => playerTransform.position = x,
                endPos,
                warpDuration)
            .SetEase(Ease.InOutCubic)
            .OnComplete(() => onWarpComplete?.Invoke());

        // 3. 워핑 중 히트박스 비활성 (context.IsWarping = true)
    }

    private Vector2 GetOptimalOffset(Transform target)
    {
        // 타겟의 앞쪽(왼쪽/오른쪽)에 착지
        // 예: 타겟 앞 0.5 유닛
        return Vector2.right * 0.5f;
    }

    public void StopWarp()
    {
        activeTween?.Kill();
    }
}
```

### 워핑 진입 조건

- **Strike 시작**: 입력 시 자동으로 TargetSelector 실행 → 타겟 선택 → WarpMotion 실행
- **Counter 성공**: 반격 모션 중 타겟으로 워핑
- **Execution 진입**: 처형 모션을 위해 타겟에 정확히 워핑

---

## 콤보 시스템

### ComboManager

```csharp
public class ComboManager
{
    private int comboCounter = 0;
    private float comboWindowTimer = 0f;
    private const float ComboWindowDuration = 0.8f; // 48프레임
    private const int MaxComboCount = 999;

    // 콤보 보너스 임계치
    private int[] bonusThresholds = { 5, 10, 20, 50 };

    public void Update(float deltaTime)
    {
        if (comboWindowTimer > 0)
            comboWindowTimer -= deltaTime;
        else if (comboCounter > 0)
            ResetCombo(); // 윈도우 만료
    }

    public void IncrementCombo(int amount = 1)
    {
        comboCounter = Mathf.Min(comboCounter + amount, MaxComboCount);
        comboWindowTimer = ComboWindowDuration;

        // 보너스 임계치 체크
        foreach (var threshold in bonusThresholds)
        {
            if (comboCounter == threshold)
                OnComboThresholdReached(threshold);
        }
    }

    public void ResetCombo()
    {
        comboCounter = 0;
        comboWindowTimer = 0f;
    }

    public int GetComboCount() => comboCounter;
    public bool IsComboActive() => comboWindowTimer > 0;
}
```

### 콤보 체인 패턴

```
Light → Light → Light → Heavy (3연 후 강공격 강제)
또는
Light → Light → Heavy (조기 강공격)
또는
Light → Heavy (바로 강공격)
```

### 콤보 리셋 조건

1. **피격** (Hit 상태로 전환)
2. **시간 초과** (ComboWindowTimer > 0.8초)
3. **사망** (Death 상태)
4. **회피 (Dodge)** ← 회피 시 콤보 카운트는 유지하되 윈도우 리셋

---

## 회피(Dodge) 시스템

### DodgeController

```csharp
public class DodgeController
{
    private const int IFrameDuration = 12; // 프레임
    private const float IFrameTimeSeconds = 0.2f; // 12f / 60fps

    private int iFrameCounter = 0;
    private Vector2 dodgeDirection = Vector2.zero;
    private float dodgeSpeed = 15f; // 유닛/초

    public void StartDodge(Vector2 inputDirection)
    {
        dodgeDirection = inputDirection.normalized;
        iFrameCounter = IFrameDuration;
    }

    public void Update(Transform playerTransform, float deltaTime)
    {
        if (iFrameCounter > 0)
        {
            // i-frame 중 이동
            playerTransform.position += (Vector3)(dodgeDirection * dodgeSpeed * deltaTime);
            iFrameCounter--;
        }
    }

    public bool IsInvulnerable() => iFrameCounter > 0;
    public bool IsDodgeComplete() => iFrameCounter <= 0;
}
```

### 회피 특성

- **진입**: Dodge 버튼 + 방향 입력
- **i-frame**: 0~12프레임 (약 0.2초)
- **이동**: 입력 방향으로 초당 15 유닛 이동
- **쿨다운**: 없음 (연속 회피 가능, 단 콤보 리셋)
- **캔슬 가능**: 회피 중 카운터 입력 가능 (노란 인디케이터)
- **빨간 인디케이터 대응**: 적의 즉시 공격 예고 → 빨간 점멸 표시

---

## 카운터(Counter) 시스템

### CounterSystem

```csharp
public class CounterSystem
{
    // 프레임 기반 윈도우 (60fps 가정)
    private const int PerfectCounterFrameWindow = 6; // ±3f
    private const int NormalCounterFrameWindow = 16; // ±8f

    private int telegraphFrameCounter = 0;
    private bool isCounterWindowActive = false;
    private float slowMotionScale = 0.3f;

    public void OnEnemyTelegraph(int startFrame)
    {
        // CombatEventBus.OnEnemyTelegraph 이벤트 수신
        telegraphFrameCounter = startFrame;
        isCounterWindowActive = true;
    }

    public CounterResult AttemptCounter(int currentFrame)
    {
        if (!isCounterWindowActive)
            return CounterResult.Miss;

        int framesDiff = Mathf.Abs(currentFrame - telegraphFrameCounter);

        if (framesDiff <= PerfectCounterFrameWindow)
        {
            return CounterResult.Perfect; // ±3f
        }
        else if (framesDiff <= NormalCounterFrameWindow)
        {
            return CounterResult.Normal; // ±8f
        }

        return CounterResult.Miss;
    }

    public void ExecutePerfectCounter()
    {
        // 강화 반격 + 슬로우모션
        Time.timeScale = slowMotionScale;
        Invoke(nameof(ResetTimeScale), 0.5f);
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1f;
    }
}

public enum CounterResult
{
    Perfect,  // ±3f: 콤보 +2, 슬로우모션, 강화 반격 모션
    Normal,   // ±8f: 콤보 +1, 일반 반격 모션
    Miss      // 피격 상태로 전환
}
```

### 카운터 특성

- **진입**: 노란 인디케이터 감지 + Counter 버튼 입력
- **Perfect Counter** (±3f):
  - 콤보 +2
  - 슬로우모션 0.3배속 (0.5초)
  - 강화된 반격 모션 (높은 대미지)
- **Normal Counter** (±8f):
  - 콤보 +1
  - 일반 반격 모션
- **Miss**: 카운터 실패 → Hit 상태 (피격)
- **이벤트**: `CombatEventBus.OnEnemyTelegraph(startFrame)` 수신

---

## 인풋 버퍼링 시스템

### InputBuffer

```csharp
public class InputBuffer
{
    private InputData bufferedInput = null;
    private float bufferTimeRemaining = 0f;
    private const float BufferDuration = 0.15f; // 약 9프레임

    public void BufferInput(InputData input)
    {
        bufferedInput = input;
        bufferTimeRemaining = BufferDuration;
    }

    public void Update(float deltaTime)
    {
        if (bufferTimeRemaining > 0)
            bufferTimeRemaining -= deltaTime;
        else
            bufferedInput = null;
    }

    public InputData ConsumeBuffer()
    {
        InputData result = bufferedInput;
        bufferedInput = null;
        bufferTimeRemaining = 0f;
        return result;
    }

    public bool HasBufferedInput() => bufferedInput != null;
    public InputData PeekBuffer() => bufferedInput;
}

public class InputData
{
    public enum InputType { Attack, Dodge, Counter, Heavy }

    public InputType Type { get; set; }
    public Vector2 Direction { get; set; }
}
```

### 버퍼 소비 흐름

1. **Recovery 상태 진행**
2. **Recovery 프레임의 마지막 프레임** → `inputBuffer.ConsumeBuffer()` 호출
3. **버퍼 있음** → 즉시 다음 상태 진입 (ComboChain, Dodge 등)
4. **버퍼 없음** → Idle 상태로 복귀

---

## 코드 작성 시 체크리스트 (10항목)

- [ ] **프레임 카운팅 정확성**: 모든 상태에서 `context.FrameCounter` 증가하고, 예상 프레임 수와 일치하는지 테스트
- [ ] **워핑 거리 검증**: 타겟 선택 후 거리가 10~20 유닛 범위 내인지 확인
- [ ] **버퍼 타이밍**: Recovery 상태의 정확히 마지막 프레임에서만 버퍼 소비
- [ ] **캔슬 윈도우 경계**: 각 상태의 "캔슬 가능 프레임 구간"이 설계와 일치하는지 확인
- [ ] **콤보 윈도우 관리**: 새로운 공격 진입 시 콤보 윈도우 타이머 리셋 (0.8초)
- [ ] **이벤트 구독 해제**: Counter/Dodge 종료 시 `CombatEventBus` 구독 정리
- [ ] **상태 전환 가드**: 잘못된 상태 전환 방지 (예: Hit 중 ComboChain 진입 금지)
- [ ] **무적 시간 관리**: Hit 상태 진입 시 i-frame 설정, 종료 시 해제
- [ ] **게이지 관리**: HeavyAttack 성공 시 HuxleyGauge += 10, Execution 시 -= 30 등 명시
- [ ] **UI 동기화**: 콤보 카운터, 게이지 바, 상태 표시자를 매 프레임 갱신

---

## Out of Scope (이 스킬이 다루지 않는 것)

이 스킬은 다음 내용은 **별도 스킬**로 분리합니다:

1. **적 AI**: 적의 행동 패턴, 의사결정, 네비게이션
2. **데미지 계산**: 대미지 공식, 크리티컬, 속성 시스템
3. **히트 리액션**: 피격 시 적의 넉백, 경직, 띄우기 모션
4. **카메라 연출**: 카메라 흔들림, 줌 인/아웃, 슬로우모션 카메라 연출
5. **UI 렌더링**: 콤보 텍스트 애니메이션, 수치 표시
6. **VFX/SFX**: 타격음, 콤보 폭발 이펙트, 워핑 트레일 이펙트
7. **게임 상태 관리**: 일시정지, 전투 시작/종료, 라운드 관리
8. **저장/로드**: 플레이어 진행도 저장

---

## 참고 자료 및 의존성

### 참고할 게임
- **Batman Arkham Asylum/City**: Freeflow Combat 시스템 (원본 영감)
- **Hades**: 빠른 액션, 캔슬 가능한 공격, 인풋 버퍼링
- **Devil May Cry**: 스타일 게이지, 콤보 시스템, 캔슬 문화

### 필수 라이브러리
- **DOTween**: 워핑 애니메이션
- **CombatEventBus**: 이벤트 기반 통신 (Enemy → Player 카운터 신호)

### 핵심 클래스 의존성
```
PlayerCombatController
  ├── PlayerCombatFSM (모든 상태 관리)
  ├── CombatContext (공유 데이터)
  ├── InputBuffer (선입력 관리)
  ├── ComboManager (콤보 카운팅)
  ├── FreeflowWarpSystem
  │   ├── TargetSelector
  │   └── WarpMotion (DOTween)
  ├── DodgeController (i-frame, 이동)
  └── CounterSystem (완벽성 판정)
```

### 테스트 환경 설정
- **프레임 로드 테스트**: 60fps 고정에서 모든 타이밍 검증
- **상태 전환 디버거**: FSM 상태 로깅으로 예상 전환 경로 추적
- **프레임 데이터 시각화**: 각 공격의 startup/active/recovery 구간을 에디터에서 표시

---

## 최종 설계 정리

REPLACED의 플레이어 전투 컨트롤러는 **프리플로우 어포던스** 를 극대화합니다:

1. **워핑의 자동화** → 플레이어는 "어느 적을 칠지" 입력하기만 함
2. **명확한 프레임 데이터** → 플레이어가 리듬감을 예측 가능하게 함
3. **캔슬의 자유도** → "언제든지 벗어날 수 있다"는 느낌
4. **콤보 진행성** → 계속 이어갈수록 보상이 쌓임
5. **상태 기반 분기** → 게임이 "나는 지금 이 상태야"를 명확히 알림

이를 통해 플레이어는 **유려함**, **반응성**, **리듬감** 을 동시에 느낄 수 있습니다.
