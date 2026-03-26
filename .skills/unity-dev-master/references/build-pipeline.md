# 빌드 파이프라인 자동화

> CLI 빌드 + 플랫폼별 설정 + 버전 자동 증가. CI/CD 연동 가능.

## 원클릭 빌드 스크립트

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.Linq;

public static class BuildAutomation
{
    private const string BUILD_ROOT = "Builds";

    [MenuItem("Tools/Build/Windows (Dev)")]
    public static void BuildWindowsDev()
        => Build(BuildTarget.StandaloneWindows64, BuildOptions.Development);

    [MenuItem("Tools/Build/Windows (Release)")]
    public static void BuildWindowsRelease()
        => Build(BuildTarget.StandaloneWindows64, BuildOptions.None);

    [MenuItem("Tools/Build/WebGL")]
    public static void BuildWebGL()
        => Build(BuildTarget.WebGL, BuildOptions.None);

    [MenuItem("Tools/Build/Android")]
    public static void BuildAndroid()
        => Build(BuildTarget.Android, BuildOptions.None);

    static void Build(BuildTarget target, BuildOptions options)
    {
        // 버전 자동 증가
        IncrementBuildNumber();

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        var targetName = target switch
        {
            BuildTarget.StandaloneWindows64 => "Windows",
            BuildTarget.WebGL => "WebGL",
            BuildTarget.Android => "Android",
            _ => target.ToString()
        };

        var ext = target == BuildTarget.StandaloneWindows64 ? ".exe" : "";
        var path = $"{BUILD_ROOT}/{targetName}/{PlayerSettings.productName}{ext}";

        var report = BuildPipeline.BuildPlayer(scenes, path, target, options);

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"[Build] 성공: {path} ({report.summary.totalSize / (1024*1024)}MB)");
        else
            Debug.LogError($"[Build] 실패: {report.summary.result}");
    }

    static void IncrementBuildNumber()
    {
        var parts = PlayerSettings.bundleVersion.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[2], out int patch))
        {
            parts[2] = (patch + 1).ToString();
            PlayerSettings.bundleVersion = string.Join(".", parts);
        }
        PlayerSettings.Android.bundleVersionCode++;
        PlayerSettings.iOS.buildNumber =
            (int.Parse(PlayerSettings.iOS.buildNumber) + 1).ToString();
    }
}
#endif
```

## CLI 빌드 (GitHub Actions / Jenkins)

```bash
# Windows 빌드
unity -batchmode -nographics -projectPath . \
  -executeMethod BuildAutomation.BuildWindowsRelease \
  -logFile build.log -quit

# WebGL 빌드
unity -batchmode -nographics -projectPath . \
  -executeMethod BuildAutomation.BuildWebGL \
  -logFile build.log -quit
```

## GitHub Actions 예시

```yaml
# .github/workflows/build.yml
name: Unity Build
on:
  push:
    tags: ['v*']

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: game-ci/unity-builder@v4
        with:
          targetPlatform: StandaloneWindows64
          buildMethod: BuildAutomation.BuildWindowsRelease
      - uses: actions/upload-artifact@v4
        with:
          name: windows-build
          path: Builds/Windows/
```

## 빌드 전 자동 검증

```csharp
#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public class PreBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // 1. 빈 씬 참조 체크
        var scenes = EditorBuildSettings.scenes;
        foreach (var scene in scenes)
        {
            if (!System.IO.File.Exists(scene.path))
                throw new BuildFailedException($"씬 없음: {scene.path}");
        }

        // 2. 필수 SO 존재 확인
        var requiredSOs = new[] {
            "Assets/_Project/Data/Generated/MainSoundBank.asset",
            "Assets/_Project/Data/Config/GameConfig.asset"
        };
        foreach (var so in requiredSOs)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(so) == null)
                Debug.LogWarning($"[PreBuild] 누락 SO: {so}");
        }

        Debug.Log("[PreBuild] 검증 통과");
    }
}
#endif
```
