using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 전투 테스트 씬을 원클릭으로 구성한다.
    /// Phase 2: 바닥 + 플레이어 + 더미 적 5체(좌우 분산) + 카메라 + 라이트.
    /// 메뉴: REPLACED > Setup > 4. Build Test Scene
    /// </summary>
    public static class CombatSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/CombatTestScene.unity";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string EnemyPrefab = "Assets/_Project/Prefabs/Enemies/DummyEnemy.prefab";

        [MenuItem("REPLACED/Setup/4. Build Test Scene", priority = 4)]
        public static void Execute()
        {
            // 새 씬 생성
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ─── 카메라 ───
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f; // Phase 2: 넓은 시야
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camObj.transform.position = new Vector3(3, 3, -10);
            camObj.AddComponent<AudioListener>();

            // ─── 라이트 ───
            GameObject lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);

            // ─── 바닥 (3D Quad — 흰색 평면) ───
            GameObject ground = new GameObject("Ground");
            ground.tag = "Ground";
            ground.layer = LayerMask.NameToLayer("Ground") >= 0
                ? LayerMask.NameToLayer("Ground") : 0;
            ground.transform.position = new Vector3(0, -0.5f, 0);

            // 3D Cube 바닥 비주얼 (3D 모델과 동일 렌더 파이프라인)
            // 큐브 상단 = 콜라이더 상단 (y=0)에 맞춤
            GameObject groundVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundVisual.name = "GroundVisual";
            groundVisual.transform.SetParent(ground.transform);
            groundVisual.transform.localPosition = new Vector3(0, 0f, 0); // 큐브 상단(Y=0) = 콜라이더 상단(Y=0)
            groundVisual.transform.localScale = new Vector3(50f, 1f, 10f);
            // 3D 콜라이더 제거 (2D 콜라이더 사용)
            Object.DestroyImmediate(groundVisual.GetComponent<Collider>());
            // 밝은 회색 머티리얼
            var groundRenderer = groundVisual.GetComponent<MeshRenderer>();
            if (groundRenderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader.name == "Hidden/InternalErrorShader")
                    mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.85f, 0.85f, 0.9f);
                groundRenderer.sharedMaterial = mat;
            }

            // 2D 물리 콜라이더
            var groundCol = ground.AddComponent<BoxCollider2D>();
            groundCol.size = new Vector2(50f, 1f);

            // ─── 벽 (좌/우) ───
            CreateWall("Wall_Left", new Vector3(-20f, 5f, 0f));
            CreateWall("Wall_Right", new Vector3(20f, 5f, 0f));

            // ─── 플레이어 배치 ───
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (playerPrefab != null)
            {
                GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = new Vector3(0, 0.5f, 0);
                player.name = "Player";

                // 레이어 강제 할당 (프리팹이 레이어 등록 전에 만들어졌을 경우 대비)
                SetLayerRecursiveByName(player, "Player", "Hitbox", "Hurtbox");

                var sr = player.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite == null)
                    sr.sprite = CreateWhiteSprite();

                // ★ AnimatorClipOverrider: JSON clipPath 기반 런타임 클립 교체
                if (player.GetComponent<FreeFlowHero.Combat.Core.AnimatorClipOverrider>() == null)
                    player.AddComponent<FreeFlowHero.Combat.Core.AnimatorClipOverrider>();
            }
            else
            {
                Debug.LogWarning("[REPLACED] Player 프리팹이 없습니다. " +
                    "먼저 REPLACED > Setup > 2. Create Prefabs 실행");
            }

            // ─── AttackCoordinator (공격 조율자 — Singleton) ───
            GameObject coordinatorObj = new GameObject("AttackCoordinator");
            coordinatorObj.AddComponent<FreeFlowHero.Combat.Enemy.AttackCoordinator>();
            Debug.Log("  [씬] AttackCoordinator 생성 (동시 공격 2명 제한 + 호흡 타이머)");

            // ─── ActionTableManager (액션 테이블 매니저 — Singleton) ───
            GameObject actionTableObj = new GameObject("[ActionTableManager]");
            actionTableObj.AddComponent<FreeFlowHero.Combat.Core.ActionTableManager>();
            Debug.Log("  [씬] ActionTableManager 생성 (JSON 액션 테이블 로더)");

            // ─── 더미 적 5체 배치 (좌우 분산) ───
            // Phase 2: 워핑 테스트를 위해 다양한 거리에 배치
            GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (enemyPrefab != null)
            {
                float[] xPositions = { -5f, -3f, 3f, 6f, 9f };
                Color[] enemyColors =
                {
                    new Color(1f, 0.3f, 0.2f), // 빨강
                    new Color(1f, 0.5f, 0.1f), // 주황
                    new Color(0.9f, 0.2f, 0.5f), // 분홍
                    new Color(0.8f, 0.2f, 0.2f), // 진빨강
                    new Color(1f, 0.4f, 0.3f), // 산호
                };

                for (int i = 0; i < xPositions.Length; i++)
                {
                    GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
                    enemy.transform.position = new Vector3(xPositions[i], 0.5f, 0);
                    enemy.name = $"DummyEnemy_{i + 1}";

                    var sr = enemy.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        if (sr.sprite == null) sr.sprite = CreateWhiteSprite();
                        sr.color = enemyColors[i];
                    }

                    // 레이어 강제 할당
                    SetLayerRecursiveByName(enemy, "Enemy", "Hitbox", "Hurtbox");

                    // EnemyCombatUI 자동 부착 (기존 프리팹에 없으면 추가)
                    if (enemy.GetComponent<FreeFlowHero.Combat.Enemy.EnemyCombatUI>() == null)
                        enemy.AddComponent<FreeFlowHero.Combat.Enemy.EnemyCombatUI>();

                    // EnemyAIController 자동 부착 (기존 프리팹에 없으면 추가)
                    if (enemy.GetComponent<FreeFlowHero.Combat.Enemy.EnemyAIController>() == null)
                        enemy.AddComponent<FreeFlowHero.Combat.Enemy.EnemyAIController>();
                }
            }
            else
            {
                Debug.LogWarning("[REPLACED] DummyEnemy 프리팹이 없습니다. " +
                    "먼저 REPLACED > Setup > 2. Create Prefabs 실행");
            }

            // ─── 씬 저장 ───
            EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[REPLACED] Phase 2 테스트 씬 생성 완료: {ScenePath}" +
                "\n  구성: 카메라 + 바닥(40유닛) + 벽2 + 플레이어 + 더미적 5체(좌우 분산)" +
                "\n  3D 모델이 부착된 경우 SpriteRenderer는 자동 비활성화됩니다." +
                "\n  조작: J=공격 | Space=회피 | L=카운터 | K=강공격 | U=헉슬리" +
                "\n  Play 버튼을 눌러 테스트하세요.");
        }

        /// <summary>원클릭 전체 셋업: 레이어 → 프리팹 → FBX → 애니메이터 → 3D모델 → 씬</summary>
        [MenuItem("REPLACED/Setup/0. Full Setup (All Steps)", priority = 0)]
        public static void FullSetup()
        {
            Debug.Log("[REPLACED] ===== 전체 자동 셋업 시작 =====");

            LayerAndTagSetup.Execute();         // 1. 레이어/태그/충돌 매트릭스
            PrefabFactory.CreateAllPrefabs();   // 2. 프리팹 생성
            FBXImportSetup.Execute();           // 6. FBX Humanoid 설정
            AnimatorControllerBuilder.Execute(); // 3. AnimatorController
            ModelSetup.Execute();               // 5. 3D 모델 부착
            Execute();                          // 4. 테스트 씬

            Debug.Log("[REPLACED] ===== 전체 자동 셋업 완료 =====" +
                "\n  Unity 메뉴: REPLACED > Setup 에서 개별 실행도 가능" +
                "\n  Play 버튼을 눌러 테스트하세요!");
        }

        // ─── 유틸리티 ───

        /// <summary>
        /// 오브젝트와 자식들에 레이어를 할당한다.
        /// 자식 중 Hitbox/Hurtbox 이름이 포함된 것은 해당 전용 레이어로 분리한다.
        /// </summary>
        private static void SetLayerRecursiveByName(
            GameObject obj, string defaultLayer,
            string hitboxLayerName, string hurtboxLayerName)
        {
            int mainLayer = LayerMask.NameToLayer(defaultLayer);
            int hitboxLayer = LayerMask.NameToLayer(hitboxLayerName);
            int hurtboxLayer = LayerMask.NameToLayer(hurtboxLayerName);

            if (mainLayer < 0)
            {
                Debug.LogWarning($"[REPLACED] 레이어 '{defaultLayer}'을 찾을 수 없습니다.");
                return;
            }

            // 루트는 기본 레이어
            obj.layer = mainLayer;

            // 자식들: 이름에 따라 적절한 레이어 할당
            foreach (Transform child in obj.GetComponentsInChildren<Transform>(true))
            {
                if (child == obj.transform) continue;

                string childName = child.gameObject.name.ToLower();
                if (childName.Contains("hitbox") && hitboxLayer >= 0)
                    child.gameObject.layer = hitboxLayer;
                else if (childName.Contains("hurtbox") && hurtboxLayer >= 0)
                    child.gameObject.layer = hurtboxLayer;
                else
                    child.gameObject.layer = mainLayer;
            }

            Debug.Log($"  [레이어] {obj.name} → {defaultLayer} (자식 포함 할당 완료)");
        }

        private static void CreateWall(string name, Vector3 position)
        {
            GameObject wall = new GameObject(name);
            wall.tag = "Wall";
            wall.layer = LayerMask.NameToLayer("Wall") >= 0
                ? LayerMask.NameToLayer("Wall") : 0;
            wall.transform.position = position;

            var col = wall.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, 12f);
        }

        /// <summary>런타임에서도 보이는 1x1 흰색 스프라이트 생성</summary>
        private static Sprite CreateWhiteSprite()
        {
            string spritePath = "Assets/_Project/Sprites/WhitePixel.asset";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (existing != null) return existing;

            EnsureFolder("Assets/_Project/Sprites");

            Texture2D tex = new Texture2D(4, 4);
            Color[] colors = new Color[16];
            for (int i = 0; i < 16; i++) colors[i] = Color.white;
            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Point;

            string texPath = "Assets/_Project/Sprites/WhitePixel.png";
            System.IO.File.WriteAllBytes(
                System.IO.Path.GetFullPath(texPath),
                tex.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);

            TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 4;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
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
    }
}
