# 세이브/로드 시스템 (JSON + AES)

> GUI 없이 코드로만 구현하는 세이브 시스템. JSON 직렬화 + AES 암호화 + 자동 저장.

## 아키텍처

```
SaveManager (ServiceLocator 등록)
├── ISaveSerializer       ← JSON 직렬화/역직렬화
├── ISaveEncryptor        ← AES 암호화 (선택)
├── ISaveStorage          ← 파일 I/O
└── SaveData              ← 게임 상태 데이터 컨테이너
```

## 핵심 코드

### SaveData 컨테이너

```csharp
[System.Serializable]
public class SaveData
{
    public string version = "1.0";
    public long timestamp;
    
    // 플레이어
    public PlayerSaveData player = new();
    
    // 인벤토리
    public List<ItemSaveEntry> inventory = new();
    
    // 퀘스트 진행
    public List<QuestSaveEntry> quests = new();
    
    // 맵/레벨 상태
    public Dictionary<string, bool> unlockedAreas = new();
    
    // 설정
    public SettingsSaveData settings = new();
}

[System.Serializable]
public class PlayerSaveData
{
    public float posX, posY;
    public int hp, maxHp;
    public int level, exp;
    public string currentScene;
}

[System.Serializable]
public class ItemSaveEntry
{
    public string itemId;
    public int count;
    public int slotIndex;
}

[System.Serializable]
public class QuestSaveEntry
{
    public string questId;
    public string state; // "NotStarted", "InProgress", "Completed"
    public int progress;
}

[System.Serializable]
public class SettingsSaveData
{
    public float masterVolume = 1f;
    public float bgmVolume = 0.8f;
    public float sfxVolume = 1f;
    public int resolutionIndex = 0;
    public bool fullscreen = true;
}
```

### SaveManager

```csharp
public class SaveManager : MonoBehaviour
{
    private const string SAVE_FILE = "save_{0}.json";
    private const string SETTINGS_FILE = "settings.json";
    private const string ENCRYPTION_KEY = "YourGameKey12345"; // 16/24/32 chars
    
    private SaveData _currentData;
    public SaveData CurrentData => _currentData ??= new SaveData();
    
    [SerializeField] private bool useEncryption = true;
    [SerializeField] private float autoSaveInterval = 300f; // 5분
    
    private float _autoSaveTimer;

    void Awake()
    {
        ServiceLocator.Register<SaveManager>(this);
    }

    void Update()
    {
        if (autoSaveInterval <= 0) return;
        _autoSaveTimer += Time.deltaTime;
        if (_autoSaveTimer >= autoSaveInterval)
        {
            _autoSaveTimer = 0;
            Save(0);
        }
    }

    // ── 저장 ──
    public void Save(int slot = 0)
    {
        _currentData.timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // 현재 게임 상태 수집
        CollectGameState();
        
        var json = JsonUtility.ToJson(_currentData, true);
        
        if (useEncryption)
            json = AESEncryptor.Encrypt(json, ENCRYPTION_KEY);
        
        var path = GetSavePath(slot);
        System.IO.File.WriteAllText(path, json);
        
        Debug.Log($"[Save] 슬롯 {slot} 저장 완료 → {path}");
    }

    // ── 로드 ──
    public bool Load(int slot = 0)
    {
        var path = GetSavePath(slot);
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[Save] 슬롯 {slot} 파일 없음");
            return false;
        }
        
        var json = System.IO.File.ReadAllText(path);
        
        if (useEncryption)
            json = AESEncryptor.Decrypt(json, ENCRYPTION_KEY);
        
        _currentData = JsonUtility.FromJson<SaveData>(json);
        
        // 게임 상태 복원
        ApplyGameState();
        
        Debug.Log($"[Save] 슬롯 {slot} 로드 완료");
        return true;
    }

    // ── 삭제 ──
    public void Delete(int slot)
    {
        var path = GetSavePath(slot);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    // ── 슬롯 존재 확인 ──
    public bool SlotExists(int slot)
        => System.IO.File.Exists(GetSavePath(slot));

    // ── 경로 ──
    private string GetSavePath(int slot)
        => System.IO.Path.Combine(
            Application.persistentDataPath,
            string.Format(SAVE_FILE, slot));

    // ── 게임 상태 수집/복원 (프로젝트별 커스텀) ──
    private void CollectGameState()
    {
        // 각 시스템에서 데이터를 수집하는 이벤트 발행
        // 또는 ISaveable 인터페이스를 구현한 오브젝트를 순회
    }

    private void ApplyGameState()
    {
        // 수집의 역순으로 데이터 복원
    }
}
```

### AES 암호화

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class AESEncryptor
{
    public static string Encrypt(string plainText, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV + 암호문을 Base64로
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string cipherText, string key)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));

        var iv = new byte[16];
        var cipher = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        Buffer.BlockCopy(fullCipher, 16, cipher, 0, cipher.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(decrypted);
    }
}
```

### ISaveable 인터페이스 패턴

```csharp
public interface ISaveable
{
    string SaveId { get; }
    object CaptureState();
    void RestoreState(object state);
}

// 사용 예시
public class PlayerController : MonoBehaviour, ISaveable
{
    public string SaveId => "player";

    public object CaptureState()
    {
        return new PlayerSaveData
        {
            posX = transform.position.x,
            posY = transform.position.y,
            hp = currentHp,
            maxHp = maxHp,
            level = level,
            exp = exp,
            currentScene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name
        };
    }

    public void RestoreState(object state)
    {
        if (state is PlayerSaveData data)
        {
            transform.position = new Vector3(data.posX, data.posY, 0);
            currentHp = data.hp;
            maxHp = data.maxHp;
            level = data.level;
            exp = data.exp;
        }
    }
}
```

## JSON 파이프라인 연동

세이브 시스템의 초기 설정도 JSON으로:

```json
// Assets/_Project/Data/JSON/save_config.json
{
    "useEncryption": true,
    "autoSaveIntervalSeconds": 300,
    "maxSlots": 3,
    "saveVersion": "1.0"
}
```

```csharp
[MenuItem("Tools/Import/Save Config")]
public static void ImportSaveConfig()
{
    // JSON → SO Config 생성
    // SaveManager가 이 Config SO를 참조
}
```
