using UnityEngine;
using UnityEditor;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// EEJANAI FBX 파일들의 임포트 설정을 Humanoid로 변환하고
    /// 공용 Avatar를 설정한다.
    /// 메뉴: REPLACED > Setup > 6. Setup FBX Import (Humanoid)
    /// </summary>
    public static class FBXImportSetup
    {
        private const string ModelFBX = "Assets/EEJANAI_Team/Commons/Model/EEJANAIbot.fbx";
        private const string AnimFBXFolder = "Assets/EEJANAI_Team/FreeFighterAnimations/FBX";
        private const string LocomotionFolder =
            "Assets/ExplosiveLLC/Fighter Pack Bundle FREE/Fighters/" +
            "Female Fighter Mecanim Animation Pack FREE/Animations";
        private const string MartialArtFolder =
            "Assets/Martial Art Animations Sample/Animations";

        [MenuItem("REPLACED/Setup/6. Setup FBX Import (Humanoid)", priority = 6)]
        public static void Execute()
        {
            int count = 0;

            // ── 1단계: EEJANAIbot 모델을 Humanoid로 설정 ──
            Debug.Log("[REPLACED] 1단계: EEJANAIbot 모델 Humanoid 설정...");
            if (SetHumanoid(ModelFBX, isModel: true))
                count++;

            // 모델의 Avatar 가져오기
            Avatar sourceAvatar = GetAvatar(ModelFBX);
            if (sourceAvatar == null)
            {
                Debug.LogError("[REPLACED] EEJANAIbot Avatar를 찾을 수 없습니다. " +
                    "모델 FBX의 Rig 설정을 확인하세요.");
                // Avatar 없이도 각 FBX 자체 Avatar로 시도
            }
            else
            {
                Debug.Log($"[REPLACED] 소스 Avatar: {sourceAvatar.name} (isHuman={sourceAvatar.isHuman})");
            }

            // ── 2단계: 애니메이션 FBX를 Humanoid로 설정 + Avatar 소스 지정 ──
            Debug.Log("[REPLACED] 2단계: 애니메이션 FBX Humanoid 설정...");
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { AnimFBXFolder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (SetHumanoid(path, isModel: false, sourceAvatar: sourceAvatar))
                    count++;
            }

            // ── 3단계: ExplosiveLLC Locomotion FBX도 Humanoid로 설정 ──
            // 중요: ExplosiveLLC는 EEJANAIbot과 다른 스켈레톤이므로 sourceAvatar를 지정하지 않음
            // Humanoid 리타겟팅은 런타임에 자동으로 처리됨
            Debug.Log("[REPLACED] 3단계: ExplosiveLLC Locomotion FBX Humanoid 설정 (자체 Avatar 사용)...");
            if (AssetDatabase.IsValidFolder(LocomotionFolder))
            {
                string[] locoGuids = AssetDatabase.FindAssets("t:Model", new[] { LocomotionFolder });
                foreach (string guid in locoGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    // sourceAvatar: null → ExplosiveLLC 자체 Avatar 사용
                    if (SetHumanoid(path, isModel: false, sourceAvatar: null))
                        count++;
                }
            }
            else
            {
                Debug.LogWarning("[REPLACED] ExplosiveLLC Locomotion 폴더를 찾을 수 없습니다: " +
                    LocomotionFolder);
            }

            // ── 4단계: Martial Art Animations Sample FBX를 Humanoid로 설정 ──
            // Fight_Idle 등 Idle 대체 애니메이션용 — 자체 Avatar 사용
            Debug.Log("[REPLACED] 4단계: Martial Art Animations Sample FBX Humanoid 설정 (자체 Avatar 사용)...");
            if (AssetDatabase.IsValidFolder(MartialArtFolder))
            {
                string[] maGuids = AssetDatabase.FindAssets("t:Model", new[] { MartialArtFolder });
                foreach (string guid in maGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    // sourceAvatar: null → 자체 Avatar 사용
                    if (SetHumanoid(path, isModel: false, sourceAvatar: null))
                        count++;
                }
            }
            else
            {
                Debug.LogWarning("[REPLACED] Martial Art Animations 폴더를 찾을 수 없습니다: " +
                    MartialArtFolder);
            }

            // Martial Art 모델도 Humanoid로 설정 (리타겟팅 소스)
            string martialArtModel = "Assets/Martial Art Animations Sample/Models/Armature/Armature.fbx";
            if (System.IO.File.Exists(martialArtModel))
            {
                if (SetHumanoid(martialArtModel, isModel: true))
                    count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[REPLACED] FBX Humanoid 설정 완료 — {count}개 파일 처리" +
                "\n  EEJANAI + ExplosiveLLC + Martial Art 모두 Humanoid Rig 설정됨" +
                "\n  다음: REPLACED > Setup > 3. Build Animator Controller → 5. Attach 3D Model");
        }

        /// <summary>FBX를 Humanoid 리그로 설정한다.</summary>
        private static bool SetHumanoid(string path, bool isModel, Avatar sourceAvatar = null)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"  [스킵] ModelImporter 없음: {path}");
                return false;
            }

            bool needsReimport = false;

            // Rig → Humanoid
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                needsReimport = true;
            }

            // 애니메이션 FBX: 소스 Avatar 지정
            if (!isModel)
            {
                if (sourceAvatar != null)
                {
                    // EEJANAI 애니메이션: EEJANAIbot Avatar 공유
                    if (importer.sourceAvatar != sourceAvatar)
                    {
                        importer.sourceAvatar = sourceAvatar;
                        needsReimport = true;
                    }
                }
                else
                {
                    // ExplosiveLLC 등 외부 팩: 자체 Avatar 사용 (sourceAvatar 클리어)
                    if (importer.sourceAvatar != null)
                    {
                        importer.sourceAvatar = null;
                        needsReimport = true;
                    }
                }
            }

            // 애니메이션 설정
            if (!isModel)
            {
                // 애니메이션 임포트 활성화
                importer.importAnimation = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
                string fileName = System.IO.Path.GetFileName(path);
                Debug.Log($"  ✓ Humanoid 설정: {fileName}" +
                    (sourceAvatar != null && !isModel ? $" (Avatar: {sourceAvatar.name})" : ""));
                return true;
            }
            else
            {
                string fileName = System.IO.Path.GetFileName(path);
                Debug.Log($"  — 이미 Humanoid: {fileName}");
                return false;
            }
        }

        /// <summary>FBX에서 Avatar를 추출한다.</summary>
        private static Avatar GetAvatar(string fbxPath)
        {
            // 먼저 Humanoid로 설정되어 있는지 확인
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human)
                return null;

            // FBX 내부의 모든 에셋에서 Avatar 찾기
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (Object asset in assets)
            {
                if (asset is Avatar avatar && avatar.isHuman)
                    return avatar;
            }

            return null;
        }
    }
}
