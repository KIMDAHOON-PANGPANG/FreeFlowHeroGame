# UI 시스템 (코드 온리)

> UI 스택 매니저 + 코드로 Canvas/Panel 생성 + SO 데이터 바인딩.

## 아키텍처

```
UIManager (ServiceLocator)
├── UIStack (화면 스택: 팝업 위에 팝업)
├── UIFactory (프리팹→인스턴스)
└── 각 UIPanel : UIBase
    ├── Open() / Close()
    ├── OnShow() / OnHide()
    └── 데이터 바인딩 (SO 또는 이벤트)
```

## UIBase 추상 클래스

```csharp
public abstract class UIBase : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    public void Open(object data = null)
    {
        gameObject.SetActive(true);
        IsOpen = true;
        OnShow(data);
    }

    public void Close()
    {
        IsOpen = false;
        OnHide();
        gameObject.SetActive(false);
    }

    protected virtual void OnShow(object data) { }
    protected virtual void OnHide() { }
}
```

## UIManager

```csharp
public class UIManager : MonoBehaviour
{
    private readonly Dictionary<string, UIBase> _panels = new();
    private readonly Stack<UIBase> _stack = new();

    void Awake() => ServiceLocator.Register(this);

    public void Register(string id, UIBase panel)
    {
        _panels[id] = panel;
        panel.gameObject.SetActive(false);
    }

    public T Show<T>(string id, object data = null) where T : UIBase
    {
        if (!_panels.TryGetValue(id, out var panel)) return null;
        
        // 스택 최상단에 추가
        if (_stack.Count > 0)
            _stack.Peek().gameObject.SetActive(false); // 이전 패널 숨김 (선택)

        _stack.Push(panel);
        panel.Open(data);
        return panel as T;
    }

    public void CloseCurrent()
    {
        if (_stack.Count == 0) return;
        var current = _stack.Pop();
        current.Close();

        if (_stack.Count > 0)
            _stack.Peek().gameObject.SetActive(true);
    }

    public void CloseAll()
    {
        while (_stack.Count > 0)
        {
            _stack.Pop().Close();
        }
    }

    public bool IsAnyOpen => _stack.Count > 0;
}
```

## 코드로 Canvas 생성

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Setup/Create UI Canvas")]
public static void CreateUICanvas()
{
    // Canvas 루트
    var canvasGo = new GameObject("UICanvas");
    var canvas = canvasGo.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 100;

    var scaler = canvasGo.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = 0.5f;

    canvasGo.AddComponent<GraphicRaycaster>();

    // UIManager 부착
    canvasGo.AddComponent<UIManager>();

    // 레이어 구조
    string[] layers = { "Background", "Panels", "Popups", "Overlay", "Loading" };
    foreach (var layer in layers)
    {
        var go = new GameObject(layer);
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // EventSystem
    if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
    {
        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    // 프리팹 저장
    PrefabUtility.SaveAsPrefabAsset(canvasGo,
        "Assets/_Project/Prefabs/Generated/UICanvas.prefab");
    Debug.Log("[UI] Canvas 프리팹 생성 완료");
}
#endif
```

## JSON으로 UI 구조 정의

```json
// ui_panels.json
{
    "panels": [
        {
            "id": "hud",
            "layer": "Background",
            "prefab": "Prefabs/UI/HUD",
            "alwaysLoaded": true
        },
        {
            "id": "inventory",
            "layer": "Panels",
            "prefab": "Prefabs/UI/InventoryPanel",
            "animation": "slideFromRight"
        },
        {
            "id": "pause_menu",
            "layer": "Popups",
            "prefab": "Prefabs/UI/PauseMenu",
            "pauseGame": true
        },
        {
            "id": "confirm_dialog",
            "layer": "Popups",
            "prefab": "Prefabs/UI/ConfirmDialog",
            "closeOnBackdropClick": true
        }
    ]
}
```

## 화면 전환 애니메이션 (코드 온리)

```csharp
public static class UIAnimator
{
    public static IEnumerator FadeIn(CanvasGroup group, float duration = 0.3f)
    {
        group.alpha = 0;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = elapsed / duration;
            yield return null;
        }
        group.alpha = 1;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    public static IEnumerator SlideIn(RectTransform rt, Vector2 from, float duration = 0.3f)
    {
        var target = rt.anchoredPosition;
        rt.anchoredPosition = from;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOutCubic(elapsed / duration);
            rt.anchoredPosition = Vector2.Lerp(from, target, t);
            yield return null;
        }
        rt.anchoredPosition = target;
    }

    static float EaseOutCubic(float t) => 1 - Mathf.Pow(1 - t, 3);
}
```
