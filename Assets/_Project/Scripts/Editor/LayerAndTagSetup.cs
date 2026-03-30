using UnityEngine;
using UnityEditor;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 전투 시스템에 필요한 레이어, 태그, 소팅 레이어를 자동 등록한다.
    /// 메뉴: REPLACED > Setup > 1. Layers & Tags
    /// </summary>
    public static class LayerAndTagSetup
    {
        // ─── 등록할 레이어 ───
        private static readonly string[] RequiredLayers = new[]
        {
            "Player",       // 8
            "Enemy",        // 9
            "Hitbox",       // 10
            "Hurtbox",      // 11
            "Ground",       // 12
            "Wall",         // 13
        };

        // ─── 등록할 태그 ───
        private static readonly string[] RequiredTags = new[]
        {
            "Player",
            "Enemy",
            "Hitbox",
            "Hurtbox",
            "Ground",
            "Wall",
            "DummyEnemy",
        };

        // ─── 등록할 소팅 레이어 ───
        private static readonly string[] RequiredSortingLayers = new[]
        {
            "Background",
            "Ground",
            "Characters",
            "VFX",
            "UI",
        };

        [MenuItem("REPLACED/Advanced/1. Layers & Tags", priority = 1)]
        public static void Execute()
        {
            int layerCount = 0, tagCount = 0, sortCount = 0;

            // 레이어 등록
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            SerializedProperty layersProp = tagManager.FindProperty("layers");
            foreach (string layerName in RequiredLayers)
            {
                if (AddLayer(layersProp, layerName))
                    layerCount++;
            }

            // 태그 등록
            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            foreach (string tagName in RequiredTags)
            {
                if (AddTag(tagsProp, tagName))
                    tagCount++;
            }

            // 소팅 레이어 등록
            SerializedProperty sortingLayersProp = tagManager.FindProperty("m_SortingLayers");
            foreach (string sortName in RequiredSortingLayers)
            {
                if (AddSortingLayer(sortingLayersProp, sortName))
                    sortCount++;
            }

            tagManager.ApplyModifiedProperties();

            // 물리 충돌 매트릭스 설정
            SetupCollisionMatrix();

            Debug.Log($"[REPLACED] Layers & Tags 설정 완료 — " +
                $"레이어 {layerCount}개 추가, 태그 {tagCount}개 추가, 소팅레이어 {sortCount}개 추가");
        }

        /// <summary>레이어 추가 (빈 슬롯 8~31에 배치)</summary>
        private static bool AddLayer(SerializedProperty layersProp, string layerName)
        {
            // 이미 존재하는지 확인
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName)
                    return false;
            }

            // 빈 슬롯 찾기 (8번부터 — 0~7은 Unity 예약)
            for (int i = 8; i < layersProp.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layersProp.GetArrayElementAtIndex(i).stringValue))
                {
                    layersProp.GetArrayElementAtIndex(i).stringValue = layerName;
                    Debug.Log($"  Layer [{i}] = \"{layerName}\"");
                    return true;
                }
            }

            Debug.LogWarning($"  레이어 슬롯 부족: \"{layerName}\" 추가 실패");
            return false;
        }

        /// <summary>태그 추가</summary>
        private static bool AddTag(SerializedProperty tagsProp, string tagName)
        {
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tagName)
                    return false;
            }

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
            Debug.Log($"  Tag = \"{tagName}\"");
            return true;
        }

        /// <summary>소팅 레이어 추가</summary>
        private static bool AddSortingLayer(SerializedProperty sortProp, string sortName)
        {
            for (int i = 0; i < sortProp.arraySize; i++)
            {
                if (sortProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == sortName)
                    return false;
            }

            sortProp.InsertArrayElementAtIndex(sortProp.arraySize);
            var newEntry = sortProp.GetArrayElementAtIndex(sortProp.arraySize - 1);
            newEntry.FindPropertyRelative("name").stringValue = sortName;
            newEntry.FindPropertyRelative("uniqueID").intValue =
                (int)(sortName.GetHashCode() & 0x7FFFFFFF);
            Debug.Log($"  SortingLayer = \"{sortName}\"");
            return true;
        }

        /// <summary>
        /// Physics2D 충돌 매트릭스 설정.
        /// ┌──────────┬────────┬──────┬────────┬────────┬────────┬──────┐
        /// │          │ Player │ Enemy│ Hitbox │Hurtbox │ Ground │ Wall │
        /// ├──────────┼────────┼──────┼────────┼────────┼────────┼──────┤
        /// │ Player   │   —    │  ✗   │   ✗    │   ✗    │   ✓    │  ✓   │
        /// │ Enemy    │   ✗    │  ✓   │   ✗    │   ✗    │   ✓    │  ✓   │
        /// │ Hitbox   │   ✗    │  ✗   │   ✗    │   ✓    │   ✗    │  ✗   │
        /// │ Hurtbox  │   ✗    │  ✗   │   ✓    │   ✗    │   ✗    │  ✗   │
        /// │ Ground   │   ✓    │  ✓   │   ✗    │   ✗    │   —    │  —   │
        /// │ Wall     │   ✓    │  ✓   │   ✗    │   ✗    │   —    │  —   │
        /// └──────────┴────────┴──────┴────────┴────────┴────────┴──────┘
        /// Player↔Enemy 통과: 프리플로우 전투에서 플레이어가 적 사이를 자유롭게 워핑
        /// Enemy↔Enemy 충돌: 넉백 시 서로 겹치지 않도록
        /// </summary>
        private static void SetupCollisionMatrix()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int hitboxLayer = LayerMask.NameToLayer("Hitbox");
            int hurtboxLayer = LayerMask.NameToLayer("Hurtbox");
            int groundLayer = LayerMask.NameToLayer("Ground");
            int wallLayer = LayerMask.NameToLayer("Wall");

            // 레이어가 아직 없으면 스킵
            if (hitboxLayer < 0 || hurtboxLayer < 0 || playerLayer < 0 || enemyLayer < 0)
            {
                Debug.Log("  충돌 매트릭스: 레이어 등록 후 Unity 재시작 → 다시 실행 필요");
                return;
            }

            // ── 1. Hitbox / Hurtbox: 서로만 충돌 ──
            for (int i = 0; i < 32; i++)
            {
                Physics2D.IgnoreLayerCollision(hitboxLayer, i, true);
                Physics2D.IgnoreLayerCollision(hurtboxLayer, i, true);
            }
            Physics2D.IgnoreLayerCollision(hitboxLayer, hurtboxLayer, false);

            // ── 2. Player ↔ Enemy: 통과 (프리플로우 워핑) ──
            Physics2D.IgnoreLayerCollision(playerLayer, enemyLayer, true);

            // ── 3. Enemy ↔ Enemy: 충돌 (서로 겹침 방지) ──
            Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, false);

            // ── 4. Player/Enemy ↔ Ground/Wall: 충돌 (벽 막힘, 바닥 착지) ──
            if (groundLayer >= 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayer, groundLayer, false);
                Physics2D.IgnoreLayerCollision(enemyLayer, groundLayer, false);
            }
            if (wallLayer >= 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayer, wallLayer, false);
                Physics2D.IgnoreLayerCollision(enemyLayer, wallLayer, false);
            }

            // ── 5. Player ↔ Hitbox/Hurtbox 무시 (자기 자신 판정 방지) ──
            Physics2D.IgnoreLayerCollision(playerLayer, hitboxLayer, true);
            Physics2D.IgnoreLayerCollision(playerLayer, hurtboxLayer, true);
            Physics2D.IgnoreLayerCollision(enemyLayer, hitboxLayer, true);
            Physics2D.IgnoreLayerCollision(enemyLayer, hurtboxLayer, true);

            Debug.Log("  충돌 매트릭스 설정 완료:" +
                "\n    Hitbox↔Hurtbox: 충돌" +
                "\n    Player↔Enemy: 통과 (프리플로우)" +
                "\n    Enemy↔Enemy: 충돌 (겹침 방지)" +
                "\n    Player/Enemy↔Ground/Wall: 충돌 (벽 막힘)");
        }
    }
}
