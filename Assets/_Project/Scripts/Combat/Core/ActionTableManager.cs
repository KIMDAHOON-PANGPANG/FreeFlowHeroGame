using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 액션 테이블 매니저 (싱글톤).
    /// Tool/ActionTables/ 폴더의 모든 JSON을 디스크에서 직접 로드하여 캐싱한다.
    ///
    /// ★ 핫 리로드: 플레이 중 에디터에서 JSON을 저장하면 FileSystemWatcher가
    ///   파일 변경을 감지하고 자동으로 리로드한다. 다음 공격부터 새 데이터 적용.
    ///
    /// 사용법:
    ///   var action = ActionTableManager.Instance.GetAction("PC_Hero", "LightAtk1");
    ///   int startup = action.startup;
    ///   string nextOnAttack = action.GetCancelTarget("Attack"); // → "LightAtk2"
    /// </summary>
    public class ActionTableManager : MonoBehaviour
    {
        // ─── 싱글톤 ───
        private static ActionTableManager instance;
        public static ActionTableManager Instance
        {
            get
            {
                if (instance == null)
                {
                    // 씬에 없으면 자동 생성
                    var go = new GameObject("[ActionTableManager]");
                    instance = go.AddComponent<ActionTableManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        // ─── 데이터 ───
        private Dictionary<string, ActorActionTable> tables = new(System.StringComparer.OrdinalIgnoreCase);
        private bool isLoaded;

        /// <summary>테이블 로드/리로드 완료 시 발행. AnimatorClipOverrider 등이 구독.</summary>
        public event System.Action OnReloaded;

        // ─── 경로 ───
        private const string DiskSubPath = "_Project/Tool/ActionTables";

        // ─── 핫 리로드 ───
        private FileSystemWatcher fileWatcher;
        private volatile bool hotReloadPending;  // 메인 스레드에서 처리하기 위한 플래그
        private float hotReloadCooldown;         // 연속 변경 방지 쿨다운
        private const float HotReloadDelay = 0.3f;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);

            LoadAll();
            SetupFileWatcher();
        }

        private void OnDestroy()
        {
            CleanupFileWatcher();
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            // ★ 핫 리로드: FileSystemWatcher 콜백은 별도 스레드이므로
            //   메인 스레드(Update)에서 실제 리로드 수행
            if (hotReloadPending)
            {
                hotReloadCooldown -= Time.unscaledDeltaTime;
                if (hotReloadCooldown <= 0f)
                {
                    hotReloadPending = false;
                    LoadAllFromDisk();
                    Debug.Log($"<color=cyan>[ActionTable] ★ 핫 리로드 완료 — {tables.Count}개 액터 갱신</color>");
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        //  로드
        // ═══════════════════════════════════════════════════════

        /// <summary>Tool/ActionTables/ 하위 모든 JSON 로드 (초기 로드용)</summary>
        public void LoadAll()
        {
            LoadAllFromDisk();
        }

        /// <summary>디스크에서 직접 JSON 파일 로드 (핫 리로드용, Resources 캐시 우회)</summary>
        public void LoadAllFromDisk()
        {
            string folderPath = Path.Combine(Application.dataPath, DiskSubPath);
            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"[ActionTable] 핫 리로드 폴더 없음: {folderPath}");
                return;
            }

            tables.Clear();

            string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");
            foreach (string filePath in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    ParseAndAddTable(json, fileName);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ActionTable] 핫 리로드 파싱 오류 {filePath}: {e.Message}");
                }
            }

            isLoaded = true;
            OnReloaded?.Invoke();
        }

        /// <summary>JSON 문자열을 파싱하여 테이블에 등록</summary>
        private void ParseAndAddTable(string json, string sourceName)
        {
            try
            {
                var table = JsonUtility.FromJson<ActorActionTable>(json);
                if (table != null && !string.IsNullOrEmpty(table.actorId))
                {
                    // 필드 방어: playbackRate 미설정(0)→1.0, notifies null→빈배열
                    if (table.actions != null)
                    {
                        for (int i = 0; i < table.actions.Length; i++)
                        {
                            if (table.actions[i].playbackRate <= 0f)
                                table.actions[i].playbackRate = 1.0f;
                            if (table.actions[i].notifies == null)
                                table.actions[i].notifies = System.Array.Empty<ActionNotify>();
                            // rootMotionScale 방어: JSON 누락(0) → 1.0
                            foreach (var n in table.actions[i].notifies)
                            {
                                if (n.type == "ROOT_MOTION" && n.rootMotionScale <= 0f)
                                    n.rootMotionScale = 1f;
                            }
                        }
                    }
                    table.BuildMap();
                    tables[table.actorId] = table;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ActionTable] Failed to parse {sourceName}: {e.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════
        //  핫 리로드 (FileSystemWatcher)
        // ═══════════════════════════════════════════════════════

        private void SetupFileWatcher()
        {
            string folderPath = Path.Combine(Application.dataPath, DiskSubPath);
            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"[ActionTable] FileWatcher 대상 폴더 없음: {folderPath}");
                return;
            }

            try
            {
                fileWatcher = new FileSystemWatcher(folderPath, "*.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                fileWatcher.Changed += OnFileChanged;
                fileWatcher.Created += OnFileChanged;
                fileWatcher.Deleted += OnFileChanged;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ActionTable] FileWatcher 초기화 실패: {e.Message}");
            }
        }

        private void CleanupFileWatcher()
        {
            if (fileWatcher != null)
            {
                fileWatcher.EnableRaisingEvents = false;
                fileWatcher.Changed -= OnFileChanged;
                fileWatcher.Created -= OnFileChanged;
                fileWatcher.Deleted -= OnFileChanged;
                fileWatcher.Dispose();
                fileWatcher = null;
            }
        }

        /// <summary>파일 변경 콜백 (별도 스레드에서 호출됨)</summary>
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 쿨다운 리셋 (연속 저장 시 마지막 변경 후 0.3초 대기)
            hotReloadCooldown = HotReloadDelay;
            hotReloadPending = true;
        }

        // ═══════════════════════════════════════════════════════
        //  조회 API
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 특정 액터의 특정 액션 데이터 조회.
        /// 없으면 null 반환.
        /// </summary>
        public ActionEntry GetAction(string actorId, string actionId)
        {
            if (!isLoaded) LoadAll();

            if (tables.TryGetValue(actorId, out var table))
                return table.GetAction(actionId);

            Debug.LogWarning($"[ActionTable] Actor not found: {actorId}");
            return null;
        }

        /// <summary>액터 테이블 전체 반환. 없으면 null.</summary>
        public ActorActionTable GetActorTable(string actorId)
        {
            if (!isLoaded) LoadAll();

            tables.TryGetValue(actorId, out var table);
            return table;
        }

        /// <summary>로드된 모든 액터 ID 목록</summary>
        public IEnumerable<string> GetAllActorIds()
        {
            if (!isLoaded) LoadAll();
            return tables.Keys;
        }

        /// <summary>테이블 존재 여부 확인</summary>
        public bool HasActor(string actorId)
        {
            if (!isLoaded) LoadAll();
            return tables.ContainsKey(actorId);
        }

        /// <summary>런타임 리로드 (수동 호출용)</summary>
        public void Reload()
        {
            LoadAllFromDisk();
        }

        // ─── 에디터 전용: 파일 경로에서 직접 로드 ───
#if UNITY_EDITOR
        /// <summary>에디터 전용: 파일 경로로 단일 테이블 로드 (에디터 윈도우용)</summary>
        public static ActorActionTable LoadFromFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var table = JsonUtility.FromJson<ActorActionTable>(json);
                if (table?.actions != null)
                {
                    for (int i = 0; i < table.actions.Length; i++)
                    {
                        if (table.actions[i].playbackRate <= 0f)
                            table.actions[i].playbackRate = 1.0f;
                        if (table.actions[i].notifies == null)
                            table.actions[i].notifies = System.Array.Empty<ActionNotify>();
                    }
                }
                table?.BuildMap();
                return table;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ActionTable] File load error: {e.Message}");
                return null;
            }
        }

        /// <summary>에디터 전용: 테이블을 JSON 파일로 저장</summary>
        public static void SaveToFile(ActorActionTable table, string filePath)
        {
            try
            {
                string json = JsonUtility.ToJson(table, true);
                File.WriteAllText(filePath, json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ActionTable] File save error: {e.Message}");
            }
        }
#endif
    }
}
