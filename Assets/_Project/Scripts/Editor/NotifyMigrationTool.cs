#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 레거시 프레임 데이터(startup/active/recovery) → 노티파이 배열 자동 변환 도구.
    /// Tool/ActionTables/ 하위 모든 JSON을 스캔하여
    /// notifies[]가 비어있는 액션에 대해 STARTUP + COLLISION + CANCEL_WINDOW 노티파이를 생성한다.
    ///
    /// 메뉴: REPLACED > Migrate Legacy → Notifies
    /// </summary>
    public static class NotifyMigrationTool
    {
        private const string ActionTablesPath = "Assets/_Project/Tool/ActionTables";

        [MenuItem("REPLACED/Advanced/마이그레이션/Migrate Legacy → Notifies (전체 JSON)")]
        public static void MigrateAll()
        {
            // JSON 파일 수집
            if (!Directory.Exists(ActionTablesPath))
            {
                Debug.LogError($"[Migration] 경로 없음: {ActionTablesPath}");
                EditorUtility.DisplayDialog("Migration", $"경로가 존재하지 않습니다:\n{ActionTablesPath}", "OK");
                return;
            }

            var jsonFiles = Directory.GetFiles(ActionTablesPath, "*.json");
            if (jsonFiles.Length == 0)
            {
                Debug.LogWarning("[Migration] JSON 파일이 없습니다.");
                EditorUtility.DisplayDialog("Migration", "ActionTables 폴더에 JSON 파일이 없습니다.", "OK");
                return;
            }

            int totalActions = 0;
            int migratedActions = 0;
            int skippedActions = 0;
            int errorCount = 0;

            foreach (var filePath in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var table = JsonUtility.FromJson<ActorActionTable>(json);

                    if (table?.actions == null)
                    {
                        Debug.LogWarning($"[Migration] 스킵 (파싱 실패): {Path.GetFileName(filePath)}");
                        continue;
                    }

                    bool modified = false;

                    for (int i = 0; i < table.actions.Length; i++)
                    {
                        var action = table.actions[i];
                        totalActions++;

                        // 이미 노티파이가 있으면 스킵
                        if (action.notifies != null && action.notifies.Length > 0)
                        {
                            skippedActions++;
                            continue;
                        }

                        // 레거시 프레임 데이터가 없으면(0/0/0) 스킵
                        if (action.startup == 0 && action.active == 0 && action.recovery == 0)
                        {
                            skippedActions++;
                            continue;
                        }

                        // 마이그레이션 실행
                        action.notifies = CreateNotifiesFromLegacy(action);
                        modified = true;
                        migratedActions++;

                        Debug.Log($"[Migration] {table.actorId}/{action.id} — " +
                            $"S:{action.startup} A:{action.active} R:{action.recovery} → {action.notifies.Length} notifies");
                    }

                    if (modified)
                    {
                        string outputJson = JsonUtility.ToJson(table, true);
                        File.WriteAllText(filePath, outputJson);
                        Debug.Log($"[Migration] 저장: {Path.GetFileName(filePath)}");
                    }
                }
                catch (System.Exception e)
                {
                    errorCount++;
                    Debug.LogError($"[Migration] 오류 ({Path.GetFileName(filePath)}): {e.Message}");
                }
            }

            string summary = $"마이그레이션 완료!\n\n" +
                $"총 액션: {totalActions}\n" +
                $"변환됨: {migratedActions}\n" +
                $"스킵 (이미 노티파이 있음): {skippedActions}\n" +
                $"오류: {errorCount}\n\n" +
                $"처리된 JSON 파일: {jsonFiles.Length}개";

            Debug.Log($"[Migration] {summary.Replace("\n", " | ")}");
            EditorUtility.DisplayDialog("Migration Complete", summary, "OK");

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 레거시 프레임 데이터에서 3개의 노티파이 생성:
        /// 1) STARTUP: 0 ~ startup
        /// 2) COLLISION: startup ~ startup+active
        /// 3) CANCEL_WINDOW: startup+active+(recovery*cancelRatio) ~ startup+active+recovery
        /// </summary>
        private static ActionNotify[] CreateNotifiesFromLegacy(ActionEntry action)
        {
            var notifies = new System.Collections.Generic.List<ActionNotify>();

            int s = action.startup;
            int a = action.active;
            int r = action.recovery;

            // 1) STARTUP (선딜 구간)
            if (s > 0)
            {
                notifies.Add(ActionNotify.CreateStartup(0, s, action.moveSpeed));
            }

            // 2) COLLISION (히트 판정 구간)
            if (a > 0)
            {
                notifies.Add(ActionNotify.CreateCollision(s, s + a));
            }

            // 3) CANCEL_WINDOW (캔슬 허용 구간)
            //    cancelRatio 기반: Recovery의 일정 비율 이후부터 끝까지
            int cancelStart = s + a;
            if (action.cancelRatio > 0f)
            {
                cancelStart = s + a + Mathf.RoundToInt(r * action.cancelRatio);
            }
            int cancelEnd = s + a + r;

            if (cancelEnd > cancelStart)
            {
                notifies.Add(ActionNotify.CreateCancelWindow(
                    cancelStart, cancelEnd,
                    skill: true,    // 공격 콤보 캔슬
                    move: true,     // 이동 캔슬
                    dodge: true,    // 회피 캔슬
                    counter: false  // 카운터 캔슬은 기본 비활성
                ));
            }

            return notifies.ToArray();
        }

        [MenuItem("REPLACED/Advanced/마이그레이션/Migrate Legacy → Notifies (선택한 JSON만)")]
        public static void MigrateSelected()
        {
            // 프로젝트 뷰에서 선택한 TextAsset 대상
            var selected = Selection.objects
                .OfType<TextAsset>()
                .ToArray();

            if (selected.Length == 0)
            {
                EditorUtility.DisplayDialog("Migration",
                    "Project 뷰에서 JSON(TextAsset) 파일을 선택한 뒤 실행해주세요.", "OK");
                return;
            }

            int migrated = 0;

            foreach (var asset in selected)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (!path.EndsWith(".json")) continue;

                try
                {
                    var table = JsonUtility.FromJson<ActorActionTable>(asset.text);
                    if (table?.actions == null) continue;

                    bool modified = false;
                    for (int i = 0; i < table.actions.Length; i++)
                    {
                        var action = table.actions[i];
                        if (action.notifies != null && action.notifies.Length > 0) continue;
                        if (action.startup == 0 && action.active == 0 && action.recovery == 0) continue;

                        action.notifies = CreateNotifiesFromLegacy(action);
                        modified = true;
                        migrated++;
                    }

                    if (modified)
                    {
                        string fullPath = Path.Combine(Application.dataPath, "..", path);
                        string json = JsonUtility.ToJson(table, true);
                        File.WriteAllText(fullPath, json);
                        Debug.Log($"[Migration] 저장: {Path.GetFileName(path)}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Migration] {Path.GetFileName(path)}: {e.Message}");
                }
            }

            Debug.Log($"[Migration] 선택 마이그레이션 완료: {migrated}개 액션 변환");
            EditorUtility.DisplayDialog("Migration", $"{migrated}개 액션이 변환되었습니다.", "OK");
            AssetDatabase.Refresh();
        }
    }
}
#endif
