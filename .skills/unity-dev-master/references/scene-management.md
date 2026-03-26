# 씬 관리 시스템

> Additive Scene 구조 + 로딩 화면 + 씬 전환 자동화.

## 씬 구조 설계

```
[Additive Scene 패턴]
PersistentScene (항상 로드됨)
├── GameManager
├── AudioManager
├── UIManager (Canvas)
├── SaveManager
└── EventSystem

+ LevelScene (Additive 로드/언로드)
  ├── 레벨 환경
  ├── 적 스폰
  └── 레벨 로직
```

## SceneLoader

```csharp
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }
    
    [SerializeField] private CanvasGroup loadingScreen;
    [SerializeField] private UnityEngine.UI.Slider progressBar;

    private string _currentLevel;

    void Awake()
    {
        Instance = this;
        ServiceLocator.Register(this);
    }

    public void LoadLevel(string sceneName)
    {
        StartCoroutine(LoadLevelRoutine(sceneName));
    }

    private IEnumerator LoadLevelRoutine(string sceneName)
    {
        // 로딩 화면 표시
        yield return FadeIn(loadingScreen, 0.3f);

        // 이전 레벨 언로드
        if (!string.IsNullOrEmpty(_currentLevel))
        {
            var unload = SceneManager.UnloadSceneAsync(_currentLevel);
            while (!unload.isDone) yield return null;
        }

        // 새 레벨 로드
        var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        load.allowSceneActivation = false;

        while (load.progress < 0.9f)
        {
            if (progressBar) progressBar.value = load.progress;
            yield return null;
        }

        load.allowSceneActivation = true;
        yield return new WaitUntil(() => load.isDone);

        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
        _currentLevel = sceneName;

        // 로딩 화면 숨기기
        yield return FadeOut(loadingScreen, 0.3f);
    }

    private IEnumerator FadeIn(CanvasGroup cg, float dur)
    {
        cg.gameObject.SetActive(true);
        float t = 0;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = t / dur;
            yield return null;
        }
        cg.alpha = 1;
    }

    private IEnumerator FadeOut(CanvasGroup cg, float dur)
    {
        float t = 0;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = 1 - (t / dur);
            yield return null;
        }
        cg.alpha = 0;
        cg.gameObject.SetActive(false);
    }
}
```

## 씬 목록 코드 등록

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Setup/Register Scenes in Build Settings")]
public static void RegisterScenes()
{
    var scenePaths = new[]
    {
        "Assets/_Project/Scenes/PersistentScene.unity",
        "Assets/_Project/Scenes/MainMenu.unity",
        "Assets/_Project/Scenes/Stage_01.unity",
        "Assets/_Project/Scenes/Stage_02.unity",
        "Assets/_Project/Scenes/Stage_Boss.unity",
    };

    EditorBuildSettings.scenes = scenePaths
        .Select(p => new EditorBuildSettingsScene(p, true))
        .ToArray();

    Debug.Log($"[Setup] {scenePaths.Length}개 씬 등록 완료");
}
#endif
```
