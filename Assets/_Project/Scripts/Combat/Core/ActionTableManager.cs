using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 액션 테이블 매니저 (싱글톤).
    /// Resources/ActionTables/ 폴더의 모든 JSON을 로드하여 캐싱한다.
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

        // ─── 경로 ───
        private const string ResourceFolder = "ActionTables";

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
        }

        /// <summary>Resources/ActionTables/ 하위 모든 JSON 로드</summary>
        public void LoadAll()
        {
            tables.Clear();

            var textAssets = Resources.LoadAll<TextAsset>(ResourceFolder);
            foreach (var asset in textAssets)
            {
                try
                {
                    var table = JsonUtility.FromJson<ActorActionTable>(asset.text);
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
                            }
                        }
                        table.BuildMap();
                        tables[table.actorId] = table;
                        Debug.Log($"[ActionTable] Loaded: {table.actorId} ({table.actorName}) — {table.actions.Length} actions");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[ActionTable] Failed to parse {asset.name}.json: {e.Message}");
                }
            }

            isLoaded = true;
            Debug.Log($"[ActionTable] Total {tables.Count} actor table(s) loaded.");
        }

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

        /// <summary>런타임 리로드 (핫 리로드용)</summary>
        public void Reload()
        {
            Debug.Log("[ActionTable] Reloading all tables...");
            LoadAll();
        }

        // ─── 에디터 전용: 파일 경로에서 직접 로드 ───
#if UNITY_EDITOR
        /// <summary>에디터 전용: 파일 경로로 단일 테이블 로드 (에디터 윈도우용)</summary>
        public static ActorActionTable LoadFromFile(string filePath)
        {
            try
            {
                string json = System.IO.File.ReadAllText(filePath);
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
                System.IO.File.WriteAllText(filePath, json);
                Debug.Log($"[ActionTable] Saved: {filePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ActionTable] File save error: {e.Message}");
            }
        }
#endif
    }
}
