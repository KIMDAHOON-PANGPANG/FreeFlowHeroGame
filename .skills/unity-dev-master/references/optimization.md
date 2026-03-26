# 최적화 가이드

> 2D 액션 게임 기준 핵심 최적화 패턴. 프로파일러 자동 로깅 포함.

## GC 최소화 (최우선)

```csharp
// [나쁨] 매 프레임 GC 발생
void Update()
{
    var enemies = FindObjectsOfType<Enemy>(); // 매 프레임 배열 할당
    var text = $"HP: {hp}/{maxHp}"; // 매 프레임 문자열 할당
}

// [좋음] 캐싱 + StringBuilder
private readonly List<Enemy> _enemies = new();
private readonly StringBuilder _sb = new();

void Update()
{
    // 이미 캐싱된 리스트 사용
    _sb.Clear();
    _sb.Append("HP: ").Append(hp).Append('/').Append(maxHp);
    hpText.SetText(_sb);
}
```

### 주요 GC 트리거와 대안

| GC 트리거 | 대안 |
|---|---|
| `string + string` | `StringBuilder` 또는 `string.Format` |
| `FindObjectOfType` | 캐싱 또는 ServiceLocator |
| `GetComponent<T>()` 반복 호출 | Awake에서 캐싱 |
| LINQ (Where, Select, ToList) | for 루프 + 재사용 리스트 |
| `new List<T>()` 매 프레임 | 필드로 선언, Clear() 재사용 |
| 박싱 (int → object) | 제네릭 사용 |
| `foreach` on Dictionary | for + Keys/Values 배열 캐싱 |

## 배칭 최적화

```
[2D 스프라이트 배칭 규칙]
1. 같은 텍스처 아틀라스 → 자동 배칭
2. SortingLayer/Order 연속 → 배칭 가능
3. Material이 다르면 → 배칭 불가

[실전]
- Sprite Atlas 필수 사용 (같은 카테고리 스프라이트 묶기)
- SortingLayer: Background, Terrain, Enemies, Player, Foreground, UI
- Dynamic Batching은 2D에서 기본 활성화
```

## 오브젝트 풀 적용 대상

```
[반드시 풀링할 것]
- 총알/투사체
- 히트 이펙트 VFX
- 데미지 숫자 텍스트
- 잡몹 (리스폰 빈도 높은 것)
- 오디오 소스 (SFX)

[풀링 불필요]
- 보스 (1~2개)
- 플레이어
- UI 패널 (SetActive로 관리)
```

## 프로파일러 자동 로깅

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
public class PerfLogger : MonoBehaviour
{
    [SerializeField] private float logInterval = 5f;
    
    private float _timer;
    private int _frameCount;
    private float _fpsSum;

    void Update()
    {
        _frameCount++;
        _fpsSum += 1f / Time.unscaledDeltaTime;
        _timer += Time.unscaledDeltaTime;

        if (_timer >= logInterval)
        {
            float avgFps = _fpsSum / _frameCount;
            long totalMem = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long gcMem = System.GC.GetTotalMemory(false);

            Debug.Log($"[Perf] FPS: {avgFps:F1} | " +
                      $"Mem: {totalMem / (1024*1024)}MB | " +
                      $"GC: {gcMem / (1024*1024)}MB | " +
                      $"DrawCalls: {UnityStats.batches}");

            _timer = 0;
            _frameCount = 0;
            _fpsSum = 0;
        }
    }
}
#endif
```

## 체크리스트

```
[릴리즈 전 최적화 체크]
□ Sprite Atlas 설정 완료
□ 오브젝트 풀 적용 (총알, VFX, 잡몹)
□ Update()에서 GetComponent/Find 호출 제거
□ 문자열 연산 StringBuilder로 교체
□ LINQ 런타임 사용 제거
□ 사용 안 하는 MonoBehaviour 비활성화/제거
□ 텍스처 압축 설정 (Crunch Compression)
□ 오디오 압축 설정 (Vorbis, 적절한 Quality)
□ Profiler로 30분 플레이 세션 측정
□ GC.Alloc 0 목표 (전투 중)
```
