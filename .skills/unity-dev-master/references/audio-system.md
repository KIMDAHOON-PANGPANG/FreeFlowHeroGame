# 오디오 시스템 (코드 온리)

> SoundBank SO + AudioManager + JSON 설정. Inspector 없이 사운드 관리.

## 아키텍처

```
AudioManager (ServiceLocator)
├── BGM Player (AudioSource x1, 크로스페이드)
├── SFX Pool (AudioSource x8~16, 풀링)
├── UI SFX (AudioSource x2, 전용)
└── SoundBank (SO) ← JSON에서 생성
```

## SoundBank SO

```csharp
[CreateAssetMenu(menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    public SoundEntry[] sounds;
    
    private Dictionary<string, SoundEntry> _lookup;
    
    public SoundEntry Get(string id)
    {
        _lookup ??= sounds.ToDictionary(s => s.id);
        return _lookup.TryGetValue(id, out var entry) ? entry : null;
    }
}

[System.Serializable]
public class SoundEntry
{
    public string id;
    public AudioClip clip;
    public float volume = 1f;
    public float pitchMin = 1f;
    public float pitchMax = 1f;
    public bool loop;
    public string mixerGroup; // "BGM", "SFX", "UI"
}
```

## AudioManager

```csharp
public class AudioManager : MonoBehaviour
{
    [SerializeField] private SoundBank soundBank;
    [SerializeField] private AudioMixerGroup bgmGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    private AudioSource _bgmSource;
    private AudioSource _bgmFadeSource;
    private ObjectPool<AudioSource> _sfxPool;
    
    void Awake()
    {
        ServiceLocator.Register(this);
        SetupSources();
    }

    public void PlaySFX(string id, Vector3? position = null)
    {
        var entry = soundBank.Get(id);
        if (entry == null) { Debug.LogWarning($"Sound '{id}' not found"); return; }
        
        var src = GetAvailableSFXSource();
        src.clip = entry.clip;
        src.volume = entry.volume;
        src.pitch = Random.Range(entry.pitchMin, entry.pitchMax);
        src.outputAudioMixerGroup = sfxGroup;
        src.Play();
    }

    public void PlayBGM(string id, float fadeTime = 1f)
    {
        var entry = soundBank.Get(id);
        if (entry == null) return;
        StartCoroutine(CrossFadeBGM(entry, fadeTime));
    }

    public void StopBGM(float fadeTime = 1f)
    {
        StartCoroutine(FadeOut(_bgmSource, fadeTime));
    }

    // 볼륨 조절 (SaveManager 연동)
    public void SetMasterVolume(float v)
        => AudioListener.volume = v;
    
    public void SetBGMVolume(float v)
        => bgmGroup?.audioMixer?.SetFloat("BGMVolume", 
            Mathf.Log10(Mathf.Max(v, 0.001f)) * 20);

    public void SetSFXVolume(float v)
        => sfxGroup?.audioMixer?.SetFloat("SFXVolume", 
            Mathf.Log10(Mathf.Max(v, 0.001f)) * 20);
    
    private void SetupSources()
    {
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.loop = true;
        _bgmSource.playOnAwake = false;
        
        _bgmFadeSource = gameObject.AddComponent<AudioSource>();
        _bgmFadeSource.loop = true;
        _bgmFadeSource.playOnAwake = false;
    }
    
    private AudioSource GetAvailableSFXSource()
    {
        // 간단 버전: child AudioSource 풀에서 사용 가능한 것 반환
        foreach (Transform child in transform)
        {
            var src = child.GetComponent<AudioSource>();
            if (src != null && !src.isPlaying) return src;
        }
        // 풀 부족 시 새로 생성
        var go = new GameObject("SFX_Source");
        go.transform.SetParent(transform);
        return go.AddComponent<AudioSource>();
    }

    private IEnumerator CrossFadeBGM(SoundEntry entry, float duration)
    {
        _bgmFadeSource.clip = entry.clip;
        _bgmFadeSource.volume = 0;
        _bgmFadeSource.Play();
        
        float elapsed = 0;
        float startVol = _bgmSource.volume;
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            _bgmSource.volume = Mathf.Lerp(startVol, 0, t);
            _bgmFadeSource.volume = Mathf.Lerp(0, entry.volume, t);
            yield return null;
        }
        
        _bgmSource.Stop();
        // 스왑
        (_bgmSource, _bgmFadeSource) = (_bgmFadeSource, _bgmSource);
    }

    private IEnumerator FadeOut(AudioSource src, float duration)
    {
        float startVol = src.volume;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0, elapsed / duration);
            yield return null;
        }
        src.Stop();
    }
}
```

## JSON → SoundBank SO 임포터

```json
// sounds.json
{
    "sounds": [
        { "id": "sfx_hit_light", "clipPath": "Audio/SFX/hit_01", "volume": 0.8, "pitchMin": 0.95, "pitchMax": 1.05 },
        { "id": "sfx_hit_heavy", "clipPath": "Audio/SFX/hit_heavy_01", "volume": 1.0, "pitchMin": 0.9, "pitchMax": 1.0 },
        { "id": "bgm_stage1", "clipPath": "Audio/BGM/stage1", "volume": 0.7, "loop": true, "mixerGroup": "BGM" },
        { "id": "ui_click", "clipPath": "Audio/UI/click", "volume": 0.6, "mixerGroup": "UI" }
    ]
}
```

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Import/Sounds from JSON")]
public static void ImportSounds()
{
    var json = File.ReadAllText("Assets/_Project/Data/JSON/sounds.json");
    var raw = JsonUtility.FromJson<SoundListWrapper>(json);
    
    var bank = ScriptableObject.CreateInstance<SoundBank>();
    bank.sounds = raw.sounds.Select(s => new SoundEntry
    {
        id = s.id,
        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            $"Assets/_Project/{s.clipPath}.wav"), // or .mp3
        volume = s.volume,
        pitchMin = s.pitchMin > 0 ? s.pitchMin : 1f,
        pitchMax = s.pitchMax > 0 ? s.pitchMax : 1f,
        loop = s.loop,
        mixerGroup = s.mixerGroup ?? "SFX"
    }).ToArray();
    
    AssetDatabase.CreateAsset(bank, "Assets/_Project/Data/Generated/MainSoundBank.asset");
    AssetDatabase.SaveAssets();
    Debug.Log($"[Audio] SoundBank 생성 완료 ({bank.sounds.Length}개 사운드)");
}
#endif
```

## AudioMixer 코드 생성

```csharp
#if UNITY_EDITOR
[MenuItem("Tools/Setup/Create Audio Mixer")]
public static void CreateAudioMixer()
{
    // AudioMixer는 코드로 완전 생성이 어려워서 (Unity API 제한)
    // 대신 가이드를 출력한다
    Debug.Log(@"[Audio Mixer 셋업 가이드]
1. Assets/_Project/Audio/MainMixer.mixer 생성
2. Groups: Master → BGM, SFX, UI
3. Exposed Parameters: BGMVolume, SFXVolume, UIVolume
4. 각 AudioSource의 OutputAudioMixerGroup에 할당
※ 이 과정은 GUI가 필요한 몇 안 되는 예외 케이스입니다.");
}
#endif
```
