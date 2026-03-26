# 코어 인프라 구현 코드

> SKILL.md에서 분리된 상세 구현 코드. ServiceLocator, EventBus, JSON 임포터, 에디터 도구.

## ServiceLocator 패턴

```csharp
// ── Core/ServiceLocator.cs ──
public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    public static T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service;
        throw new Exception($"Service {typeof(T).Name} not registered");
    }

    public static bool TryGet<T>(out T service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var obj))
        {
            service = (T)obj;
            return true;
        }
        service = null;
        return false;
    }

    public static void Clear() => _services.Clear();
}
```

## ScriptableObject 이벤트 버스

```csharp
// ── Core/GameEvent.cs ──
[CreateAssetMenu(menuName = "Events/Game Event")]
public class GameEvent : ScriptableObject
{
    private readonly List<IGameEventListener> _listeners = new();

    public void Raise()
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
            _listeners[i].OnEventRaised();
    }

    public void RegisterListener(IGameEventListener listener)
        => _listeners.Add(listener);

    public void UnregisterListener(IGameEventListener listener)
        => _listeners.Remove(listener);
}

// ── 제네릭 버전 ──
[CreateAssetMenu(menuName = "Events/Game Event (Int)")]
public class GameEventInt : ScriptableObject
{
    private readonly List<IGameEventListener<int>> _listeners = new();

    public void Raise(int value)
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
            _listeners[i].OnEventRaised(value);
    }

    public void RegisterListener(IGameEventListener<int> listener)
        => _listeners.Add(listener);
    public void UnregisterListener(IGameEventListener<int> listener)
        => _listeners.Remove(listener);
}

public interface IGameEventListener
{
    void OnEventRaised();
}

public interface IGameEventListener<T>
{
    void OnEventRaised(T value);
}
```

## 이벤트 자동 생성 (MenuItem)

```csharp
#if UNITY_EDITOR
public static class EventAssetCreator
{
    [MenuItem("Tools/Events/Create Standard Events")]
    public static void CreateStandardEvents()
    {
        string[] events = {
            "OnGameStart", "OnGamePause", "OnGameResume",
            "OnPlayerDeath", "OnPlayerRespawn",
            "OnEnemyDeath", "OnWaveComplete",
            "OnLevelLoaded", "OnSceneTransition"
        };

        string dir = "Assets/_Project/Data/Events";
        if (!AssetDatabase.IsValidFolder(dir))
            CreateFolders(dir);

        foreach (var name in events)
        {
            var path = $"{dir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<GameEvent>(path) != null) continue;
            var evt = ScriptableObject.CreateInstance<GameEvent>();
            AssetDatabase.CreateAsset(evt, path);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[EventCreator] {events.Length}개 이벤트 생성 완료");
    }

    static void CreateFolders(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
```

## JSON → SO 범용 임포터 (전체 코드)

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class JsonToSOImporter
{
    [MenuItem("Tools/Import/Enemies from JSON")]
    public static void ImportEnemies()
    {
        var jsonPath = "Assets/_Project/Data/JSON/enemies.json";
        var outputDir = "Assets/_Project/Data/Generated/Enemies";
        
        var json = File.ReadAllText(jsonPath);
        var wrapper = JsonUtility.FromJson<EnemyListWrapper>(json);
        
        EnsureFolder(outputDir);
        int count = 0;
        
        foreach (var data in wrapper.enemies)
        {
            var so = ScriptableObject.CreateInstance<EnemyData>();
            so.id = data.id;
            so.displayName = data.displayName;
            so.hp = data.hp;
            so.attack = data.attack;
            so.defense = data.defense;
            so.moveSpeed = data.moveSpeed;
            so.attackRange = data.attackRange;
            so.dropTable = data.dropTable;
            so.tags = data.tags;
            
            AssetDatabase.CreateAsset(so, $"{outputDir}/{data.id}.asset");
            count++;
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Import] {count}개 EnemyData SO 생성 완료 → {outputDir}");
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

[System.Serializable] public class EnemyListWrapper { public EnemyDataRaw[] enemies; }
[System.Serializable] public class EnemyDataRaw
{
    public string id, displayName;
    public int hp, attack, defense;
    public float moveSpeed, attackRange;
    public string[] dropTable, tags;
}
#endif
```

## Data Dashboard EditorWindow

```csharp
#if UNITY_EDITOR
public class DataDashboard : EditorWindow
{
    [MenuItem("Tools/Data Dashboard")]
    static void Open() => GetWindow<DataDashboard>("Data Dashboard");

    private Vector2 _scroll;
    private string _searchFilter = "";

    void OnGUI()
    {
        EditorGUILayout.LabelField("데이터 대시보드", EditorStyles.boldLabel);
        _searchFilter = EditorGUILayout.TextField("검색", _searchFilter);
        EditorGUILayout.Space();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        var guids = AssetDatabase.FindAssets("t:ScriptableObject",
            new[] { "Assets/_Project/Data/Generated" });

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(_searchFilter)
                && !name.Contains(_searchFilter, System.StringComparison.OrdinalIgnoreCase))
                continue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(200));
            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(path);
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
            if (GUILayout.Button("Export JSON", GUILayout.Width(80)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                var json = JsonUtility.ToJson(obj, true);
                var exportPath = path.Replace(".asset", ".json")
                    .Replace("/Generated/", "/JSON/Exported/");
                Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
                File.WriteAllText(exportPath, json);
                Debug.Log($"Exported: {exportPath}");
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("모든 JSON 임포트 실행"))
            JsonToSOImporter.ImportEnemies();
    }
}
#endif
```

## 프로젝트 초기 셋업 (MenuItem)

```csharp
#if UNITY_EDITOR
public static class QuickSetup
{
    [MenuItem("Tools/Setup/Create Folder Structure")]
    public static void CreateFolderStructure()
    {
        string[] folders = {
            "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Data",
            "Assets/_Project/Scripts/Systems",
            "Assets/_Project/Scripts/Gameplay",
            "Assets/_Project/Scripts/Editor",
            "Assets/_Project/Scripts/Tools",
            "Assets/_Project/Data/JSON",
            "Assets/_Project/Data/Generated",
            "Assets/_Project/Data/Config",
            "Assets/_Project/Data/Events",
            "Assets/_Project/Prefabs/Generated",
            "Assets/_Project/Prefabs/Manual",
            "Assets/_Project/Scenes",
            "Assets/_Project/Art",
            "Assets/_Project/Audio",
            "Assets/_Project/UI",
        };

        foreach (var f in folders)
            JsonToSOImporter.EnsureFolder(f);

        AssetDatabase.Refresh();
        Debug.Log("[Setup] 폴더 구조 생성 완료");
    }
}
#endif
```

## 코드로 프리팹 생성

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Generate/Enemy Prefabs from SO")]
public static void GenerateEnemyPrefabs()
{
    var soDir = "Assets/_Project/Data/Generated/Enemies";
    var prefabDir = "Assets/_Project/Prefabs/Generated/Enemies";
    JsonToSOImporter.EnsureFolder(prefabDir);

    var guids = AssetDatabase.FindAssets("t:EnemyData", new[] { soDir });
    foreach (var guid in guids)
    {
        var so = AssetDatabase.LoadAssetAtPath<EnemyData>(
            AssetDatabase.GUIDToAssetPath(guid));

        var go = new GameObject(so.id);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = "Enemies";
        go.AddComponent<BoxCollider2D>();
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 1f;
        rb.freezeRotation = true;

        var controller = go.AddComponent<EnemyController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("enemyData").objectReferenceValue = so;
        serialized.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(go, $"{prefabDir}/{so.id}.prefab");
        Object.DestroyImmediate(go);
    }
    AssetDatabase.SaveAssets();
    Debug.Log($"[Prefab Gen] 적 프리팹 생성 완료 → {prefabDir}");
}
#endif
```
