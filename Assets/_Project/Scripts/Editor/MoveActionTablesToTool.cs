#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// ActionTables 폴더를 Resources → Tool로 이동하는 원클릭 스크립트.
    /// 실행 후 이 스크립트 파일은 수동 삭제해도 됩니다.
    /// </summary>
    public static class MoveActionTablesToTool
    {
        private const string SrcFolder = "Assets/_Project/Resources/ActionTables";
        private const string DstParent = "Assets/_Project/Tool";
        private const string DstFolder = "Assets/_Project/Tool/ActionTables";

        [MenuItem("REPLACED/Advanced/유틸/ActionTables → Tool 폴더 이동")]
        public static void Execute()
        {
            // 원본 확인
            if (!AssetDatabase.IsValidFolder(SrcFolder))
            {
                EditorUtility.DisplayDialog("완료",
                    "Resources/ActionTables가 이미 비어있거나 없습니다.\n이동이 완료된 상태입니다.", "OK");
                return;
            }

            // Tool 상위 폴더 생성
            if (!AssetDatabase.IsValidFolder(DstParent))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Tool");
            }

            // Tool/ActionTables 폴더가 이미 존재하면 파일 단위 이동
            if (AssetDatabase.IsValidFolder(DstFolder))
            {
                // Resources/ActionTables 안의 에셋을 개별 이동
                var guids = AssetDatabase.FindAssets("", new[] { SrcFolder });
                int moved = 0;
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    // 폴더 자체는 스킵
                    if (AssetDatabase.IsValidFolder(assetPath)) continue;

                    string fileName = Path.GetFileName(assetPath);
                    string dstPath = $"{DstFolder}/{fileName}";

                    // 대상에 같은 파일이 있으면 먼저 삭제
                    if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
                    {
                        AssetDatabase.DeleteAsset(dstPath);
                    }

                    string result = AssetDatabase.MoveAsset(assetPath, dstPath);
                    if (string.IsNullOrEmpty(result))
                    {
                        moved++;
                        Debug.Log($"[MoveActionTables] 이동: {fileName}");
                    }
                    else
                    {
                        Debug.LogError($"[MoveActionTables] 이동 실패 ({fileName}): {result}");
                    }
                }
                Debug.Log($"[MoveActionTables] {moved}개 파일 이동 완료");
            }
            else
            {
                // Tool/ActionTables가 없으면 폴더째 이동
                string result = AssetDatabase.MoveAsset(SrcFolder, DstFolder);
                if (!string.IsNullOrEmpty(result))
                {
                    Debug.LogError($"[MoveActionTables] 이동 실패: {result}");
                    EditorUtility.DisplayDialog("오류", $"이동 실패:\n{result}", "OK");
                    return;
                }
            }

            // Resources/ActionTables 폴더 삭제
            if (AssetDatabase.IsValidFolder(SrcFolder))
            {
                var remaining = AssetDatabase.FindAssets("", new[] { SrcFolder });
                if (remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(SrcFolder);
                    Debug.Log("[MoveActionTables] 빈 Resources/ActionTables 폴더 삭제");
                }
            }

            // Resources 폴더가 비었으면 삭제
            string resourcesPath = "Assets/_Project/Resources";
            if (AssetDatabase.IsValidFolder(resourcesPath))
            {
                var remaining = AssetDatabase.FindAssets("", new[] { resourcesPath });
                if (remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(resourcesPath);
                    Debug.Log("[MoveActionTables] 빈 Resources 폴더 삭제");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("<color=cyan>[MoveActionTables] ActionTables → Tool 폴더 이동 완료!</color>");
            EditorUtility.DisplayDialog("완료",
                "ActionTables 파일이 Tool 폴더로 이동되었습니다.\n\n" +
                "Assets/_Project/Tool/ActionTables/", "OK");
        }
    }
}
#endif
