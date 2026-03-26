# 오브젝트 풀 시스템

> GC 스파이크 없이 대량의 오브젝트를 생성/파괴하는 제네릭 풀 시스템.

## 코어 구현

```csharp
public class ObjectPool<T> where T : Component
{
    private readonly T _prefab;
    private readonly Transform _parent;
    private readonly Queue<T> _pool = new();
    private readonly int _maxSize;

    public ObjectPool(T prefab, int initialSize = 10, int maxSize = 100,
        Transform parent = null)
    {
        _prefab = prefab;
        _maxSize = maxSize;
        _parent = parent ?? new GameObject($"Pool_{prefab.name}").transform;

        for (int i = 0; i < initialSize; i++)
            _pool.Enqueue(CreateNew());
    }

    public T Get(Vector3 position = default, Quaternion rotation = default)
    {
        var obj = _pool.Count > 0 ? _pool.Dequeue() : CreateNew();
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.gameObject.SetActive(true);
        return obj;
    }

    public void Return(T obj)
    {
        if (_pool.Count >= _maxSize)
        {
            Object.Destroy(obj.gameObject);
            return;
        }
        obj.gameObject.SetActive(false);
        obj.transform.SetParent(_parent);
        _pool.Enqueue(obj);
    }

    private T CreateNew()
    {
        var obj = Object.Instantiate(_prefab, _parent);
        obj.gameObject.SetActive(false);
        return obj;
    }
}
```

## 다중 풀 매니저

```csharp
public class PoolManager : MonoBehaviour
{
    private readonly Dictionary<string, object> _pools = new();

    void Awake() => ServiceLocator.Register(this);

    public void CreatePool<T>(string key, T prefab, int initial = 10)
        where T : Component
    {
        if (_pools.ContainsKey(key)) return;
        _pools[key] = new ObjectPool<T>(prefab, initial);
    }

    public T Get<T>(string key, Vector3 pos = default) where T : Component
    {
        if (_pools.TryGetValue(key, out var pool))
            return ((ObjectPool<T>)pool).Get(pos);
        Debug.LogError($"Pool '{key}' not found");
        return null;
    }

    public void Return<T>(string key, T obj) where T : Component
    {
        if (_pools.TryGetValue(key, out var pool))
            ((ObjectPool<T>)pool).Return(obj);
    }
}
```

## 자동 리턴 컴포넌트

```csharp
public class AutoReturnToPool : MonoBehaviour
{
    public string poolKey;
    public float lifetime = 3f;

    void OnEnable() => Invoke(nameof(ReturnSelf), lifetime);
    void OnDisable() => CancelInvoke();

    void ReturnSelf()
    {
        ServiceLocator.Get<PoolManager>().Return(poolKey, this);
    }
}
```

## JSON 기반 풀 설정

```json
// pool_config.json
{
    "pools": [
        { "key": "bullet_player", "prefabPath": "Prefabs/Generated/Bullets/player_bullet", "initialSize": 30 },
        { "key": "bullet_enemy", "prefabPath": "Prefabs/Generated/Bullets/enemy_bullet", "initialSize": 20 },
        { "key": "vfx_hit", "prefabPath": "Prefabs/VFX/hit_spark", "initialSize": 15 },
        { "key": "vfx_death", "prefabPath": "Prefabs/VFX/death_poof", "initialSize": 10 }
    ]
}
```
