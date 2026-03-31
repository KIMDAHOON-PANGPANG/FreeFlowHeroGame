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
        private const string ModelFBX = "Assets/Resouces/EEJANAI_Team/Commons/Model/EEJANAIbot.fbx";
        private const string AnimFBXFolder = "Assets/Resouces/EEJANAI_Team/FreeFighterAnimations/FBX";
        private const string LocomotionFolder =
            "Assets/Resouces/ExplosiveLLC/Fighter Pack Bundle FREE/Fighters/" +
            "Female Fighter Mecanim Animation Pack FREE/Animations";
        private const string MartialArtFolder =
            "Assets/Resouces/Martial Art Animations Sample/Animations";

        // 강제 재임포트 플래그
        private static bool forceReimport;

        [MenuItem("REPLACED/Advanced/6. Setup FBX Import (Humanoid)", priority = 6)]
        public static void Execute()
        {
            forceReimport = false;
            ExecuteInternal();
        }

        /// <summary>모든 FBX를 강제 재임포트한다. 폴더 이동 후 임포트 에러 해결용.</summary>
        [MenuItem("REPLACED/Advanced/6x. Force Reimport All FBX", priority = 22)]
        public static void ForceReimportAll()
        {
            forceReimport = true;
            Debug.Log("[REPLACED] ===== FBX 강제 재임포트 시작 =====");
            ExecuteInternal();
            Debug.Log("[REPLACED] ===== FBX 강제 재임포트 완료 =====");
        }

        private static void ExecuteInternal()
        {
            AssetDatabase.Refresh();

            int count = 0;

            // ★ EEJANAI FBX: Humanoid 리그는 원본 보존, Bake Into Pose만 적용
            // EEJANAIbot의 Humanoid 본 매핑은 에셋 스토어 원본 .meta에 수동 저장되어 있음.
            // animationType/sourceAvatar를 코드로 건드리면 "No human bone found" 에러 발생.
            // → 리그 설정은 절대 건드리지 않고, BakeRootMotionIntoPose만 적용.
            Debug.Log("[REPLACED] 0단계: EEJANAI FBX Bake Into Pose 적용 (리그 보존)...");
            count += BakeOnlyAllFBXInFolder(AnimFBXFolder);

            // ── ExplosiveLLC Locomotion FBX → Humanoid 설정 ──
            Debug.Log("[REPLACED] 1단계: ExplosiveLLC Locomotion FBX Humanoid 설정...");
            count += SetHumanoidAllFBXInFolder(LocomotionFolder, null);

            // ── Martial Art Animations Sample FBX → Humanoid + Bake Into Pose 설정 ──
            Debug.Log("[REPLACED] 2단계: Martial Art Animations Sample FBX Humanoid 설정...");
            count += SetHumanoidAllFBXInFolder(MartialArtFolder, null);

            // Martial Art 모델도 Humanoid로 설정 (리타겟팅 소스)
            string martialArtModel = "Assets/Resouces/Martial Art Animations Sample/Models/Armature/Armature.fbx";
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

            // 강제 재임포트 시 무조건 재설정
            bool needsReimport = forceReimport;

            // Rig → Humanoid
            if (importer.animationType != ModelImporterAnimationType.Human || forceReimport)
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
                    if (importer.sourceAvatar != sourceAvatar || forceReimport)
                    {
                        importer.sourceAvatar = sourceAvatar;
                        needsReimport = true;
                    }
                }
                else
                {
                    // ExplosiveLLC 등 외부 팩: 자체 Avatar 사용 (sourceAvatar 클리어)
                    if (importer.sourceAvatar != null || forceReimport)
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

            // 이동 + 회피 + 넉다운 + 전투모션: Bake XZ OFF → 루트모션 delta 보존
            //   Knock_: 넉다운 넉백 이동을 루트모션 또는 코드에서 제어하므로 Bake OFF 필요
            //   Atk_/spinning/cressent/axe/back: 가드카운터/처형 모션의 루트모션 이동 보존
            bool isLocomotionClip = fileName.StartsWith("Walk_") || fileName.StartsWith("Run_")
                || fileName.StartsWith("Sprint_") || fileName.StartsWith("Jog_")
                || fileName.StartsWith("Dodge_") || fileName.StartsWith("Knock_")
                || fileName.StartsWith("Atk_")
                || fileName.StartsWith("spinning") || fileName.StartsWith("cressent")
                || fileName.StartsWith("axe") || fileName.StartsWith("back");

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

        /// <summary>
        /// 폴더 내 모든 FBX에 BakeRootMotionIntoPose만 적용한다 (리그 설정 미변경).
        /// EEJANAI처럼 Humanoid 리그를 코드로 건드릴 수 없는 에셋용.
        /// </summary>
        private static int BakeOnlyAllFBXInFolder(string folder)
        {
            string fullPath = System.IO.Path.GetFullPath(folder);
            if (!System.IO.Directory.Exists(fullPath))
            {
                Debug.LogWarning($"  [스킵] 폴더를 찾을 수 없습니다: {folder}");
                return 0;
            }

            int count = 0;
            var allFiles = new System.Collections.Generic.HashSet<string>();
            foreach (var f in System.IO.Directory.GetFiles(fullPath, "*.fbx", System.IO.SearchOption.AllDirectories))
                allFiles.Add(f);
            foreach (var f in System.IO.Directory.GetFiles(fullPath, "*.FBX", System.IO.SearchOption.AllDirectories))
                allFiles.Add(f);

            Debug.Log($"  [탐색] {folder} — {allFiles.Count}개 FBX 발견 (Bake Only)");

            foreach (string filePath in allFiles)
            {
                string assetPath = "Assets" + filePath
                    .Replace("\\", "/")
                    .Replace(Application.dataPath.Replace("\\", "/"), "");

                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null) continue;

                // ★ 리그(animationType, sourceAvatar)는 절대 건드리지 않음
                // EEJANAI 전용 Bake: Y ON(Feet) + XZ OFF (루트모션 추출 → RootMotionCanceller가 차단)
                if (importer.importAnimation && BakeRootMotionForEEJANAI(importer))
                {
                    importer.SaveAndReimport();
                    string fileName = System.IO.Path.GetFileName(assetPath);
                    Debug.Log($"  ✓ Bake Into Pose 적용: {fileName}");
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// EEJANAI 전용 Bake Into Pose 설정.
        /// ★ Y: Bake ON (Feet) — 지면 고정, 캐릭터 꺼짐 방지
        /// ★ XZ: Bake OFF — XZ 이동은 루트모션 델타로 추출되어 RootMotionCanceller가 차단.
        ///   Bake XZ ON으로 하면 이동이 뼈에 베이크되어 메쉬가 시각적으로 밀림.
        /// </summary>
        private static bool BakeRootMotionForEEJANAI(ModelImporter importer)
        {
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning($"  [BakeRoot] 클립 없음: {importer.assetPath}");
                return false;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                // Root Transform Rotation → Bake Into Pose (Based Upon: Body Orientation)
                // ★ keepOriginalOrientation=false: 신체 중심 방향(Body Orientation)으로 자동 정렬.
                //   Original(true)이면 EEJANAI 클립별 원본 방향이 달라 Hips가 돌아가는 버그 발생.
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = false;

                // Root Transform Position (Y) → Bake ON (Feet 기준)
                clips[i].lockRootHeightY = true;
                clips[i].keepOriginalPositionY = false;
                clips[i].heightFromFeet = true;

                // Root Transform Position (XZ) → Bake OFF
                // XZ 루트모션 델타를 추출 → RootMotionCanceller.OnAnimatorMove()가 차단
                clips[i].lockRootPositionXZ = false;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            Debug.Log($"  [BakeRoot] ✓ EEJANAI Bake 적용 (Y=ON(Feet), XZ=OFF, Rot=Body): " +
                System.IO.Path.GetFileNameWithoutExtension(importer.assetPath));
            return true;
        }

        /// <summary>
        /// 폴더 내 모든 FBX를 Humanoid로 설정한다.
        /// AssetDatabase.FindAssets 대신 System.IO로 직접 탐색 — GUID 변경에도 안전.
        /// </summary>
        private static int SetHumanoidAllFBXInFolder(string folder, Avatar sourceAvatar)
        {
            // Unity 에셋 경로 → 실제 파일 시스템 경로
            string fullPath = System.IO.Path.GetFullPath(folder);
            if (!System.IO.Directory.Exists(fullPath))
            {
                Debug.LogWarning($"  [스킵] 폴더를 찾을 수 없습니다: {folder}");
                return 0;
            }

            int count = 0;
            string[] fbxFiles = System.IO.Directory.GetFiles(fullPath, "*.fbx",
                System.IO.SearchOption.AllDirectories);
            // .FBX 확장자도 포함
            string[] fbxFilesUpper = System.IO.Directory.GetFiles(fullPath, "*.FBX",
                System.IO.SearchOption.AllDirectories);

            var allFiles = new System.Collections.Generic.HashSet<string>();
            foreach (var f in fbxFiles) allFiles.Add(f);
            foreach (var f in fbxFilesUpper) allFiles.Add(f);

            Debug.Log($"  [탐색] {folder} — {allFiles.Count}개 FBX 발견");

            foreach (string filePath in allFiles)
            {
                // 실제 경로 → Unity 에셋 경로로 변환
                string assetPath = "Assets" + filePath
                    .Replace("\\", "/")
                    .Replace(Application.dataPath.Replace("\\", "/"), "");

                if (SetHumanoid(assetPath, isModel: false, sourceAvatar: sourceAvatar))
                    count++;
            }
            return count;
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
