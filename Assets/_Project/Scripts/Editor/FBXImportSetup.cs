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

            // ★ GetUp 클립: Translation DOF 활성화
            //   GetUp 모션은 Hips 본이 Y축으로 이동(눕기→서기)하는 데이터를 포함.
            //   Translation DOF 미활성 시 Humanoid 리타겟팅이 Hips translation을 삭제하여
            //   캐릭터가 지면에 파묻히는 현상 발생.
            string fileNameForDof = System.IO.Path.GetFileNameWithoutExtension(path);
            if (fileNameForDof.StartsWith("GetUp_") || fileNameForDof.StartsWith("Knock_"))
            {
                var hd = importer.humanDescription;
                if (!hd.hasTranslationDoF)
                {
                    hd.hasTranslationDoF = true;
                    importer.humanDescription = hd;
                    needsReimport = true;
                    Debug.Log($"  [TranslationDOF] ✓ 활성화: {fileNameForDof}");
                }
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

            // ★ 클립 타입 판별
            string fileName = System.IO.Path.GetFileNameWithoutExtension(importer.assetPath);

            // ★ 클립 타입 분류
            // 넉다운/피격: Bake Y ON + Original (체공 중 발 위치 불안정)
            bool isKnockdownClip = fileName.StartsWith("Knock_") || fileName.StartsWith("Hit_");

            // GetUp: Bake Y OFF → 루트모션 Y로 눕기→서기 높이 변화 보존
            //   + Translation DOF 활성화로 Hips 이동 데이터도 보존
            bool isGetUpClip = fileName.StartsWith("GetUp_");

            // 이동 + 회피 + 넉다운: Bake XZ OFF → 루트모션 delta 보존
            //   Knock_: 넉다운 넉백 이동을 루트모션 또는 코드에서 제어하므로 Bake OFF 필요
            bool isLocomotionClip = fileName.StartsWith("Walk_") || fileName.StartsWith("Run_")
                || fileName.StartsWith("Sprint_") || fileName.StartsWith("Jog_")
                || fileName.StartsWith("Dodge_") || fileName.StartsWith("Knock_");

            for (int i = 0; i < clips.Length; i++)
            {
                // Root Transform Rotation → Bake Into Pose (Based Upon: Original)
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = true;

                // Root Transform Position (Y)
                if (isGetUpClip)
                {
                    // ★ GetUp: Bake Y ON + Based Upon = Feet (지면 기준)
                    //   루트 Y는 발 기준으로 지면에 고정. Translation DOF가 Hips Y 이동을
                    //   보존하여 눕기→서기 메쉬 높이 변화를 정상 표현.
                    clips[i].lockRootHeightY = true;
                    clips[i].keepOriginalPositionY = false;
                    clips[i].heightFromFeet = true;
                }
                else if (isKnockdownClip)
                {
                    // ★ 넉다운/피격: Bake Y ON + Based Upon = Original (원점 고정)
                    //   체공/넘어짐 중 발 위치가 크게 변하므로 Feet 기준 사용 불가
                    clips[i].lockRootHeightY = true;
                    clips[i].keepOriginalPositionY = true;
                    clips[i].heightFromFeet = false;
                }
                else
                {
                    // 일반 전투 모션: Bake Y ON + Based Upon = Feet (지면 기준)
                    clips[i].lockRootHeightY = true;
                    clips[i].keepOriginalPositionY = false;
                    clips[i].heightFromFeet = true;
                }

                // Root Transform Position (XZ)
                // 이동/회피 클립: Bake OFF → 루트모션 delta 추출
                // 전투/피격 클립: Bake ON → 제자리 재생
                clips[i].lockRootPositionXZ = !isLocomotionClip;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            string yBasis = isGetUpClip ? "ON(Feet+TransDOF)" : (isKnockdownClip ? "ON(Original)" : "ON(Feet)");
            string xzBake = isLocomotionClip ? "OFF(이동)" : "ON(제자리)";
            Debug.Log($"  [BakeRoot] ✓ {clips.Length}개 클립 Bake Into Pose 적용 (Y={yBasis}, XZ={xzBake}): {fileName}");
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
