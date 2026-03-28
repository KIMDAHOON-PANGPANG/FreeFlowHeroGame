using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// EEJANAIbot 3D 모델을 Player/Enemy 프리팹에 자식으로 배치하고
    /// AnimatorController를 연결한다.
    /// 기존 SpriteRenderer는 비활성화(적) 또는 유지(히트박스 시각화용).
    /// 메뉴: REPLACED > Setup > 5. Attach 3D Model
    /// </summary>
    public static class ModelSetup
    {
        private const string ModelPath = "Assets/EEJANAI_Team/Commons/Model/EEJANAIbot.fbx";
        private const string PlayerAnimatorPath = "Assets/_Project/Animations/Player/PlayerCombatAnimator.controller";
        private const string EnemyAnimatorPath = "Assets/_Project/Animations/Enemy/EnemyCombatAnimator.controller";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string DummyEnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/DummyEnemy.prefab";

        // 3D 모델 스케일 (FBX에 따라 조절 필요)
        private const float ModelScale = 1.0f;
        // 모델 Y 오프셋 (발 위치를 바닥에 맞추기 위해)
        private const float ModelYOffset = 0f;

        [MenuItem("REPLACED/Setup/5. Attach 3D Model", priority = 5)]
        public static void Execute()
        {
            // 모델 확인
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelPrefab == null)
            {
                Debug.LogError($"[REPLACED] EEJANAIbot 모델을 찾을 수 없습니다: {ModelPath}");
                return;
            }

            // AnimatorController 확인
            var playerAnim = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorPath);
            if (playerAnim == null)
                Debug.LogWarning("[REPLACED] PlayerCombatAnimator가 없습니다. 먼저 3a. Build Animator Controller 실행");

            var enemyAnim = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(EnemyAnimatorPath);
            if (enemyAnim == null)
                Debug.LogWarning("[REPLACED] EnemyCombatAnimator가 없습니다. 먼저 3b. Build Enemy Animator 실행");

            int count = 0;
            count += AttachModelToPrefab(PlayerPrefabPath, modelPrefab, playerAnim,
                "PlayerModel", new Color(0.3f, 0.6f, 1f));
            count += AttachModelToPrefab(DummyEnemyPrefabPath, modelPrefab, enemyAnim,
                "EnemyModel", new Color(1f, 0.3f, 0.2f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[REPLACED] 3D 모델 부착 완료 — {count}개 프리팹 업데이트" +
                "\n  모델: EEJANAIbot.fbx" +
                "\n  Player → PlayerCombatAnimator, Enemy → EnemyCombatAnimator" +
                "\n  기존 SpriteRenderer는 비활성화됨 (삭제 아님)" +
                "\n  REPLACED > Setup > 4. Build Test Scene 으로 씬 재생성 후 테스트");
        }

        /// <summary>프리팹을 열어 3D 모델 자식을 추가하고 Animator를 연결한다.</summary>
        private static int AttachModelToPrefab(
            string prefabPath, GameObject modelSource,
            RuntimeAnimatorController animController, string childName, Color tintColor)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[REPLACED] 프리팹을 찾을 수 없습니다: {prefabPath}");
                return 0;
            }

            // 프리팹 편집 모드
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

            // 기존 3D 모델 자식이 있으면 제거 (멱등성)
            Transform existingModel = prefabRoot.transform.Find(childName);
            if (existingModel != null)
            {
                Object.DestroyImmediate(existingModel.gameObject);
            }

            // 3D 모델 인스턴스 생성 (자식으로 배치)
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelSource);
            modelInstance.name = childName;
            modelInstance.transform.SetParent(prefabRoot.transform);
            modelInstance.transform.localPosition = new Vector3(0, ModelYOffset, 0);
            modelInstance.transform.localScale = Vector3.one * ModelScale;
            // 옆에서 보는 뷰: Y축 90도 회전 (모델이 카메라를 향하도록)
            modelInstance.transform.localRotation = Quaternion.Euler(0, 90, 0);

            // 머티리얼 색상 적용 (플레이어=파랑, 적=빨강 등 구분)
            ApplyTintColor(modelInstance, tintColor);

            // Animator 설정 — 모델의 Animator 사용 (FBX에 포함된 아바타)
            Animator modelAnimator = modelInstance.GetComponent<Animator>();
            if (modelAnimator == null)
                modelAnimator = modelInstance.AddComponent<Animator>();

            if (animController != null)
                modelAnimator.runtimeAnimatorController = animController;

            // ★ 루트모션 추출 모드: true + RootMotionCanceller로 루트 본 이탈 방지
            //   런타임에서 HitReactionHandler/PlayerCombatFSM이 RootMotionCanceller를 자동 부착.
            //   에디터 프리팹에서도 미리 true로 설정.
            modelAnimator.applyRootMotion = true;

            // Avatar 명시적 설정 (Humanoid)
            Avatar avatar = GetModelAvatar();
            if (avatar != null)
                modelAnimator.avatar = avatar;

            // 루트 오브젝트의 기존 Animator 비활성화 (3D 모델 Animator가 대체)
            Animator rootAnimator = prefabRoot.GetComponent<Animator>();
            if (rootAnimator != null)
                rootAnimator.enabled = false;

            // 기존 SpriteRenderer 비활성화 (3D 모델이 대체)
            SpriteRenderer sr = prefabRoot.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.enabled = false;

            // RuntimeSpriteHelper도 비활성화
            var spriteHelper = prefabRoot.GetComponent<MonoBehaviour>();
            // "RuntimeSpriteHelper" 타입을 이름으로 찾아 비활성화
            foreach (var comp in prefabRoot.GetComponents<MonoBehaviour>())
            {
                if (comp != null && comp.GetType().Name == "RuntimeSpriteHelper")
                    comp.enabled = false;
            }

            // 프리팹 저장
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            Debug.Log($"  {prefabPath} — 3D 모델 \"{childName}\" 부착 완료 (색상: {tintColor})");
            return 1;
        }

        /// <summary>EEJANAIbot FBX에서 Humanoid Avatar를 추출한다.</summary>
        private static Avatar GetModelAvatar()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            foreach (Object asset in assets)
            {
                if (asset is Avatar avatar && avatar.isHuman)
                    return avatar;
            }
            Debug.LogWarning("[REPLACED] EEJANAIbot Avatar를 찾을 수 없습니다. " +
                "REPLACED > Setup > 6. Setup FBX Import 먼저 실행 필요");
            return null;
        }

        /// <summary>
        /// 색상 머티리얼을 .mat 에셋으로 생성/로드하여 3D 모델에 적용한다.
        /// Shader.Find()가 실패해도 기존 프로젝트 머티리얼을 클론하거나
        /// RenderPipelineAsset에서 기본 머티리얼을 가져온다.
        /// </summary>
        private static void ApplyTintColor(GameObject model, Color color)
        {
            const string matFolder = "Assets/_Project/Materials";
            EnsureFolder(matFolder);

            // 색상 기반 머티리얼 에셋 이름
            string matName = $"Tint_{color.r:F1}_{color.g:F1}_{color.b:F1}".Replace(".", "_");
            string matPath = $"{matFolder}/{matName}.mat";

            // 이미 에셋이 있으면 재사용
            Material tintMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (tintMat == null)
            {
                // ── 전략 1: 현재 렌더 파이프라인의 기본 머티리얼을 클론 ──
                Material baseMat = null;
                var rpAsset = GraphicsSettings.currentRenderPipeline;
                if (rpAsset != null)
                {
                    baseMat = rpAsset.defaultMaterial;
                    Debug.Log($"  [머티리얼] RenderPipeline 기본 머티리얼: {baseMat?.name ?? "NULL"} " +
                        $"(셰이더: {baseMat?.shader?.name ?? "NULL"})");
                }

                // ── 전략 2: 프로젝트 내 기존 URP 머티리얼 검색 ──
                if (baseMat == null)
                {
                    string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Packages/com.unity.render-pipelines.universal" });
                    foreach (string guid in matGuids)
                    {
                        Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                        if (m != null && m.shader != null && m.shader.name.Contains("Universal"))
                        {
                            baseMat = m;
                            Debug.Log($"  [머티리얼] URP 패키지 머티리얼 발견: {m.name} (셰이더: {m.shader.name})");
                            break;
                        }
                    }
                }

                // ── 전략 3: Shader.Find 폴백 ──
                if (baseMat == null)
                {
                    string[] shaderNames = {
                        "Universal Render Pipeline/Lit",
                        "Universal Render Pipeline/Simple Lit",
                        "Unlit/Color",
                        "Standard"
                    };
                    foreach (string sName in shaderNames)
                    {
                        Shader s = Shader.Find(sName);
                        if (s != null && !s.name.Contains("Error") && !s.name.Contains("Hidden"))
                        {
                            baseMat = new Material(s);
                            Debug.Log($"  [머티리얼] Shader.Find 성공: {sName}");
                            break;
                        }
                    }
                }

                if (baseMat == null)
                {
                    Debug.LogError("[REPLACED] 사용 가능한 머티리얼/셰이더를 찾을 수 없습니다!");
                    return;
                }

                // 클론 → 색상 적용 → 에셋으로 저장
                tintMat = new Material(baseMat);
                tintMat.name = matName;

                // 다양한 색상 프로퍼티를 모두 설정
                if (tintMat.HasProperty("_BaseColor"))
                    tintMat.SetColor("_BaseColor", color);
                if (tintMat.HasProperty("_Color"))
                    tintMat.SetColor("_Color", color);
                if (tintMat.HasProperty("_Smoothness"))
                    tintMat.SetFloat("_Smoothness", 0.3f);
                if (tintMat.HasProperty("_Surface"))
                    tintMat.SetFloat("_Surface", 0f);

                AssetDatabase.CreateAsset(tintMat, matPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"  [머티리얼] 에셋 생성: {matPath} (셰이더: {tintMat.shader.name}, 색상: {color})");
            }
            else
            {
                Debug.Log($"  [머티리얼] 기존 에셋 재사용: {matPath}");
            }

            // 모든 Renderer에 머티리얼 적용
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var newMats = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < newMats.Length; i++)
                    newMats[i] = tintMat;
                renderer.sharedMaterials = newMats;
            }
        }

        /// <summary>폴더가 없으면 생성한다.</summary>
        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                if (!AssetDatabase.IsValidFolder(parent))
                    EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
