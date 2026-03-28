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

                // ★ 루트 모션 Bake Into Pose — 애니메이션이 메쉬를 이동/회전시키지 않도록
                // 이 설정 없으면: 킥/펀치 모션 시 스켈레톤이 앞으로 이동하거나 Y축으로 내려가 땅에 파묻힘
                needsReimport |= BakeRootMotionIntoPose(importer);
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

        /// <summary>
        /// 애니메이션 클립의 루트 트랜스폼을 Bake Into Pose로 설정한다.
        /// 이렇게 하면 애니메이션 재생 시 루트 본이 이동/회전하지 않아
        /// 메쉬가 제자리에서 애니메이션된다.
        /// </summary>
        private static bool BakeRootMotionIntoPose(ModelImporter importer)
        {
            // 기존 클립 설정 가져오기 (없으면 기본값)
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning($"  [BakeRoot] 클립 없음: {importer.assetPath}");
                return false;
            }

            // ★ 피격 모션(Knock_, Hit_) 판별: 루트 Y를 Original 기준으로 고정
            //   발 기준(heightFromFeet)이면 넉다운 중 발 위치 변화로 루트 Y가 흔들림
            string fileName = System.IO.Path.GetFileNameWithoutExtension(importer.assetPath);
            bool isHitReactionClip = fileName.StartsWith("Knock_") || fileName.StartsWith("Hit_")
                || fileName.StartsWith("GetUp_");

            for (int i = 0; i < clips.Length; i++)
            {
                // Root Transform Rotation → Bake Into Pose (Based Upon: Original)
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = true;

                // Root Transform Position (Y) → Bake Into Pose
                clips[i].lockRootHeightY = true;
                if (isHitReactionClip)
                {
                    // ★ 피격 모션: Based Upon = Original (원점 고정)
                    //   넉다운/피격 모션은 체공/넘어짐 중 발 위치가 크게 변하므로
                    //   Feet 기준이면 루트 Y가 흔들린다. Original로 원점 고정.
                    clips[i].keepOriginalPositionY = true;
                    clips[i].heightFromFeet = false;
                }
                else
                {
                    // 일반 전투 모션: Based Upon = Feet (지면 기준)
                    clips[i].keepOriginalPositionY = false;
                    clips[i].heightFromFeet = true;
                }

                // Root Transform Position (XZ) → Bake Into Pose (Based Upon: Original)
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            string yBasis = isHitReactionClip ? "Original" : "Feet";
            Debug.Log($"  [BakeRoot] ✓ {clips.Length}개 클립 Bake Into Pose 적용 (Y={yBasis} 기준): {fileName}");
            return true;
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
