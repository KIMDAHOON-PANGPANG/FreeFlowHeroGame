using UnityEngine;
using UnityEditor;
using FreeFlowHero.Combat.Player;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.Enemy;
using FreeFlowHero.Common;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 플레이어 및 더미 적 프리팹을 자동 생성한다.
    /// 메뉴: REPLACED > Setup > 2. Create Prefabs
    /// </summary>
    public static class PrefabFactory
    {
        private const string PrefabRoot = "Assets/_Project/Prefabs";
        private const string PlayerPrefabPath = PrefabRoot + "/Player/Player.prefab";
        private const string DummyEnemyPrefabPath = PrefabRoot + "/Enemies/DummyEnemy.prefab";

        [MenuItem("REPLACED/Setup/2. Create Prefabs", priority = 2)]
        public static void CreateAllPrefabs()
        {
            EnsureDirectories();
            CreatePlayerPrefab();
            CreateDummyEnemyPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[REPLACED] 프리팹 생성 완료");
        }

        [MenuItem("REPLACED/Setup/2a. Player Prefab Only")]
        public static void CreatePlayerPrefab()
        {
            if (AssetExists(PlayerPrefabPath))
            {
                Debug.Log("[REPLACED] Player 프리팹이 이미 존재합니다. 스킵.");
                return;
            }

            EnsureDirectories();

            // ─── 루트 오브젝트 ───
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            SetLayerRecursive(player, "Player");

            // Rigidbody2D (Kinematic — 프리플로우 전투는 모든 이동이 스크립트 제어)
            var rb = player.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true; // 트리거/콜리전 감지 유지
            rb.freezeRotation = true;

            // CapsuleCollider2D (바디)
            var bodyCol = player.AddComponent<CapsuleCollider2D>();
            bodyCol.size = new Vector2(0.6f, 1.8f);
            bodyCol.offset = new Vector2(0f, 0.9f);

            // SpriteRenderer (임시 시각화)
            var sr = player.AddComponent<SpriteRenderer>();
            sr.color = new Color(0.2f, 0.6f, 1f, 1f); // 파란색

            // RuntimeSpriteHelper — Sprite 없으면 런타임에 자동 생성
            var spriteHelper = player.AddComponent<RuntimeSpriteHelper>();

            // Animator
            player.AddComponent<Animator>();

            // ─── 전투 컴포넌트 ───
            player.AddComponent<PlayerCombatFSM>();
            player.AddComponent<CombatInputHandler>();

            // ─── Hitbox 자식 오브젝트 ───
            GameObject hitboxObj = new GameObject("Hitbox");
            hitboxObj.transform.SetParent(player.transform);
            hitboxObj.transform.localPosition = new Vector3(0.8f, 1.0f, 0f);
            SetLayerRecursive(hitboxObj, "Hitbox");

            var hitboxCol = hitboxObj.AddComponent<BoxCollider2D>();
            hitboxCol.size = new Vector2(1.2f, 1.0f);
            hitboxCol.isTrigger = true;

            var hitbox = hitboxObj.AddComponent<HitboxController>();

            // ─── Hurtbox 자식 오브젝트 ───
            GameObject hurtboxObj = new GameObject("Hurtbox");
            hurtboxObj.transform.SetParent(player.transform);
            hurtboxObj.transform.localPosition = Vector3.zero;
            SetLayerRecursive(hurtboxObj, "Hurtbox");

            var hurtboxCol = hurtboxObj.AddComponent<BoxCollider2D>();
            hurtboxCol.size = new Vector2(0.6f, 1.8f);
            hurtboxCol.offset = new Vector2(0f, 0.9f);
            hurtboxCol.isTrigger = true;

            var hurtbox = hurtboxObj.AddComponent<HurtboxController>();

            // ─── 프리팹 저장 ───
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);

            Debug.Log($"[REPLACED] Player 프리팹 생성: {PlayerPrefabPath}");
        }

        [MenuItem("REPLACED/Setup/2b. Dummy Enemy Prefab Only")]
        public static void CreateDummyEnemyPrefab()
        {
            if (AssetExists(DummyEnemyPrefabPath))
            {
                Debug.Log("[REPLACED] DummyEnemy 프리팹이 이미 존재합니다. 스킵.");
                return;
            }

            EnsureDirectories();

            // ─── 루트 오브젝트 ───
            GameObject enemy = new GameObject("DummyEnemy");
            enemy.tag = "Enemy";
            SetLayerRecursive(enemy, "Enemy");

            // Rigidbody2D (Kinematic — 플레이어와 동일하게 스크립트 제어 이동)
            var rb = enemy.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.freezeRotation = true;

            // CapsuleCollider2D
            var bodyCol = enemy.AddComponent<CapsuleCollider2D>();
            bodyCol.size = new Vector2(0.6f, 1.8f);
            bodyCol.offset = new Vector2(0f, 0.9f);

            // SpriteRenderer (빨간색)
            var sr = enemy.AddComponent<SpriteRenderer>();
            sr.color = new Color(1f, 0.3f, 0.2f, 1f);
            sr.sortingLayerName = "Characters";

            // RuntimeSpriteHelper — Sprite 없으면 런타임에 자동 생성
            enemy.AddComponent<RuntimeSpriteHelper>();

            // DummyEnemyTarget (ICombatTarget 구현)
            enemy.AddComponent<DummyEnemyTarget>();

            // EnemyCombatUI (HP 바 + 타겟 인디케이터 + 히트 넘버)
            enemy.AddComponent<EnemyCombatUI>();

            // EnemyAIController (기본 AI: 순찰/추적/텔레그래프/공격)
            enemy.AddComponent<EnemyAIController>();

            // ─── Hurtbox 자식 오브젝트 ───
            GameObject hurtboxObj = new GameObject("Hurtbox");
            hurtboxObj.transform.SetParent(enemy.transform);
            hurtboxObj.transform.localPosition = Vector3.zero;
            SetLayerRecursive(hurtboxObj, "Hurtbox");

            var hurtboxCol = hurtboxObj.AddComponent<BoxCollider2D>();
            hurtboxCol.size = new Vector2(0.6f, 1.8f);
            hurtboxCol.offset = new Vector2(0f, 0.9f);
            hurtboxCol.isTrigger = true;

            var hurtbox = hurtboxObj.AddComponent<HurtboxController>();
            // ★ 중요: 적 허트박스는 반드시 Enemy 팀으로 설정
            // (기본값이 Player이므로 플레이어 히트박스와 같은 팀 판정 → 히트 무시 버그 방지)
            hurtbox.SetOwnerTeam(CombatTeam.Enemy);

            // ─── 프리팹 저장 ───
            PrefabUtility.SaveAsPrefabAsset(enemy, DummyEnemyPrefabPath);
            Object.DestroyImmediate(enemy);

            Debug.Log($"[REPLACED] DummyEnemy 프리팹 생성: {DummyEnemyPrefabPath}");
        }

        /// <summary>기존 프리팹을 삭제하고 재생성 (Force Rebuild)</summary>
        [MenuItem("REPLACED/Setup/2x. Force Rebuild Prefabs", priority = 20)]
        public static void ForceRebuildAllPrefabs()
        {
            // 기존 프리팹 삭제
            if (AssetExists(PlayerPrefabPath))
            {
                AssetDatabase.DeleteAsset(PlayerPrefabPath);
                Debug.Log("[REPLACED] 기존 Player 프리팹 삭제");
            }
            if (AssetExists(DummyEnemyPrefabPath))
            {
                AssetDatabase.DeleteAsset(DummyEnemyPrefabPath);
                Debug.Log("[REPLACED] 기존 DummyEnemy 프리팹 삭제");
            }

            // 재생성
            CreateAllPrefabs();
            Debug.Log("[REPLACED] 프리팹 강제 재생성 완료!");
        }

        // ─── 유틸리티 ───

        private static void EnsureDirectories()
        {
            EnsureFolder("Assets/_Project");
            EnsureFolder("Assets/_Project/Prefabs");
            EnsureFolder("Assets/_Project/Prefabs/Player");
            EnsureFolder("Assets/_Project/Prefabs/Enemies");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        private static bool AssetExists(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Object>(path) != null;
        }

        private static void SetLayerRecursive(GameObject obj, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                obj.layer = layer;
                foreach (Transform child in obj.transform)
                    child.gameObject.layer = layer;
            }
        }
    }
}
