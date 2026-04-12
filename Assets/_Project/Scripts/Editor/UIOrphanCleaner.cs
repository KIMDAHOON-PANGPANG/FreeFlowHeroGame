using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 프리팹/씬에 남아있는 고아 UI 오브젝트를 정리한다.
    /// 메뉴: REPLACED > Advanced > Clean Orphan UI
    /// </summary>
    public static class UIOrphanCleaner
    {
        private static readonly string[] OrphanPrefixes = new[]
        {
            "HPBar_", "TargetArrow_", "TokenIndicator_"
        };

        [MenuItem("REPLACED/Advanced/Clean Orphan UI", priority = 100)]
        public static void CleanOrphanUI()
        {
            int cleaned = 0;

            // 1) 프리팹 정리
            string[] prefabPaths = new[]
            {
                "Assets/_Project/Prefabs/Enemies/DummyEnemy.prefab"
            };

            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                var children = contents.GetComponentsInChildren<Transform>(true);

                for (int i = children.Length - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (child == null || child == contents.transform) continue;

                    if (IsOrphanUI(child.name))
                    {
                        Debug.Log($"[UIOrphanCleaner] 프리팹에서 제거: {path} > {child.name}");
                        Object.DestroyImmediate(child.gameObject);
                        cleaned++;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // 2) 모든 로드된 씬 정리 (메인 씬 + 프리팹 스테이지 등)
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                cleaned += CleanScene(scene);
            }

            // 3) 프리팹 스테이지 씬 정리 (별도 씬이라 위 루프에 안 잡힘)
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                cleaned += CleanScene(prefabStage.scene);

            // 4) HideFlags.DontSave 오브젝트는 위 검색에 안 잡힘
            //    Resources.FindObjectsOfTypeAll만이 DontSave 포함 전수 검색 가능
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = allTransforms.Length - 1; i >= 0; i--)
            {
                var t = allTransforms[i];
                if (t == null) continue;
                if (!IsOrphanUI(t.name)) continue;
                // 에셋(프리팹 원본 등)은 건드리지 않음 — 씬 오브젝트만
                if (EditorUtility.IsPersistent(t.gameObject)) continue;
                // 루트 오브젝트만 삭제 (자식은 부모와 함께 삭제됨)
                if (t.parent == null || !IsOrphanUI(t.parent.name))
                {
                    Debug.Log($"[UIOrphanCleaner] DontSave 제거: {t.name} (hideFlags={t.gameObject.hideFlags})");
                    Object.DestroyImmediate(t.gameObject);
                    cleaned++;
                }
            }

            // 씬 변경 사항 저장 마킹
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (cleaned > 0 && activeScene.IsValid())
                EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log($"[UIOrphanCleaner] 정리 완료 — {cleaned}개 고아 오브젝트 제거. Ctrl+S로 씬 저장하세요.");
        }

        /// <summary>특정 씬의 루트 오브젝트에서 고아 UI 정리</summary>
        private static int CleanScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int count = 0;

            var rootObjects = scene.GetRootGameObjects();
            foreach (var go in rootObjects)
            {
                if (go == null) continue;

                if (IsOrphanUI(go.name))
                {
                    Debug.Log($"[UIOrphanCleaner] 씬에서 제거: {go.name}");
                    Object.DestroyImmediate(go);
                    count++;
                    continue;
                }

                // 자식 중 고아 검색
                var children = go.GetComponentsInChildren<Transform>(true);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    if (children[i] == null || children[i].gameObject == go) continue;
                    if (IsOrphanUI(children[i].name))
                    {
                        Debug.Log($"[UIOrphanCleaner] 씬에서 제거: {children[i].name}");
                        Object.DestroyImmediate(children[i].gameObject);
                        count++;
                    }
                }
            }
            return count;
        }

        private static bool IsOrphanUI(string name)
        {
            foreach (var prefix in OrphanPrefixes)
            {
                if (name.StartsWith(prefix)) return true;
            }
            return false;
        }
    }
}
