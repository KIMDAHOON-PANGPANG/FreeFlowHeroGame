using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Editor
{
    /// <summary>
    /// 기존 액션 테이블에 WARP 노티파이를 자동 추가하는 마이그레이션 도구.
    /// 콤보/공격 계열 액션에 WARP 노티파이가 없으면 프레임 0에 기본 워핑을 추가한다.
    /// </summary>
    public static class WarpNotifyMigrator
    {
        [MenuItem("REPLACED/Advanced/마이그레이션/기존 액션에 WARP 노티파이 추가")]
        public static void MigrateAllActions()
        {
            string folderPath = Path.Combine(Application.dataPath, "_Project/Tool/ActionTables");
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError("[WarpMigrator] ActionTables 폴더를 찾을 수 없습니다: " + folderPath);
                return;
            }

            string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");
            int totalAdded = 0;
            int totalSkipped = 0;

            foreach (string filePath in jsonFiles)
            {
                string json = File.ReadAllText(filePath);
                var table = JsonUtility.FromJson<ActorActionTable>(json);
                if (table?.actions == null) continue;

                bool modified = false;

                foreach (var action in table.actions)
                {
                    // 콤보/공격 태그가 있는 액션만 대상
                    if (!IsComboAction(action)) continue;

                    // 이미 WARP 노티파이가 있으면 스킵
                    if (HasWarpNotify(action))
                    {
                        totalSkipped++;
                        continue;
                    }

                    // WARP 노티파이 추가 (프레임 0, 기본 파라미터)
                    var warpNotify = ActionNotify.CreateWarp(0);
                    var notifyList = action.notifies != null
                        ? new List<ActionNotify>(action.notifies)
                        : new List<ActionNotify>();
                    notifyList.Insert(0, warpNotify); // 맨 앞에 삽입
                    action.notifies = notifyList.ToArray();

                    totalAdded++;
                    modified = true;
                    Debug.Log($"[WarpMigrator] {table.actorId}/{action.id} — WARP 노티파이 추가 (frame 0)");
                }

                if (modified)
                {
                    string output = JsonUtility.ToJson(table, true);
                    File.WriteAllText(filePath, output);
                    Debug.Log($"[WarpMigrator] 저장 완료: {Path.GetFileName(filePath)}");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[WarpMigrator] 마이그레이션 완료 — 추가: {totalAdded}개, 스킵(이미 존재): {totalSkipped}개");
        }

        /// <summary>콤보/공격 계열 액션인지 판별</summary>
        private static bool IsComboAction(ActionEntry action)
        {
            if (action.tags == null) return false;
            foreach (var tag in action.tags)
            {
                if (tag == "light" || tag == "combo" || tag == "heavy" || tag == "attack")
                    return true;
            }
            // 태그 없어도 ID가 공격 계열이면 포함
            if (action.id != null &&
                (action.id.StartsWith("LightAtk") || action.id.StartsWith("HeavyAtk")))
                return true;
            return false;
        }

        /// <summary>WARP 노티파이가 이미 있는지 확인</summary>
        private static bool HasWarpNotify(ActionEntry action)
        {
            if (action.notifies == null) return false;
            foreach (var n in action.notifies)
            {
                if (n.type == "WARP") return true;
            }
            return false;
        }
    }
}
