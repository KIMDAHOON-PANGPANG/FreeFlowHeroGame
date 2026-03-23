using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 적 AnimatorController를 자동 생성한다.
    /// EEJANAI 애니메이션 팩에서 적에게 사용할 클립을 매핑한다.
    /// 메뉴: REPLACED > Setup > 3b. Build Enemy Animator
    /// </summary>
    public static class EnemyAnimatorBuilder
    {
        private const string AnimatorPath = "Assets/_Project/Animations/Enemy/EnemyCombatAnimator.controller";
        private const string EEJANAIRoot = "Assets/EEJANAI_Team/FreeFighterAnimations";
        private const string FBXFolder = EEJANAIRoot + "/FBX";
        private const string AnimFolder = EEJANAIRoot + "/Animations";

        // Idle 클립 (Martial Art 또는 EEJANAI)
        private const string IdleFBX = "Assets/Martial Art Animations Sample/Animations/Fight_Idle.fbx";

        // 적 공격 애니메이션 매핑
        private static readonly (string fbxName, string stateName)[] EnemyAnimMap = new[]
        {
            ("5 inch punch",     "Attack_Jab"),      // 일반 졸개 공격
            ("back fist",        "Attack_BackFist"),  // 변형 공격
            ("charge fist",      "Attack_Heavy"),     // 아머 적 강공격
            ("low kick",         "Attack_LowKick"),   // 돌진형 공격
            ("knee strike",      "Attack_Knee"),      // 엘리트 공격
        };

        [MenuItem("REPLACED/Setup/3b. Build Enemy Animator", priority = 31)]
        public static void Execute()
        {
            EnsureFolder("Assets/_Project/Animations");
            EnsureFolder("Assets/_Project/Animations/Enemy");

            // 기존 삭제 후 재생성
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath) != null)
                AssetDatabase.DeleteAsset(AnimatorPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);

            // ─── 파라미터 ───
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Telegraph", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitStun", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // ─── Idle 상태 (기본) ───
            AnimationClip idleClip = LoadClipFromFBX(IdleFBX);
            AnimatorState idleState = sm.AddState("Idle", new Vector3(0, 0, 0));
            if (idleClip != null)
                idleState.motion = idleClip;
            sm.defaultState = idleState;

            // ─── 공격 상태들 ───
            int stateCount = 0;
            int clipCount = 0;
            foreach (var (fbxName, stateName) in EnemyAnimMap)
            {
                AnimationClip clip = FindClipByFBXName(fbxName);
                var state = sm.AddState(stateName, GetStatePosition(stateCount + 1));
                stateCount++;

                if (clip != null)
                {
                    state.motion = clip;
                    clipCount++;
                    Debug.Log($"[EnemyAnimBuilder] ✓ {stateName} 클립: {clip.name}");
                }
                else
                {
                    Debug.LogWarning($"[EnemyAnimBuilder] ❌ {stateName} 클립 미발견: {fbxName}");
                }

                // Attack 트리거 + AttackIndex로 분기
                var transition = sm.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                transition.AddCondition(AnimatorConditionMode.Equals, stateCount - 1, "AttackIndex");
                transition.hasExitTime = false;
                transition.duration = 0.05f;
                transition.canTransitionToSelf = false;

                // 공격 → Idle 복귀
                var toIdle = state.AddTransition(idleState);
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0.9f;
                toIdle.duration = 0.1f;
            }

            // ─── 텔레그래프 상태 (빈 상태 — 색상 변경으로 대체) ───
            var telegraphState = sm.AddState("Telegraph", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                var tr = sm.AddAnyStateTransition(telegraphState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Telegraph");
                tr.hasExitTime = false;
                tr.duration = 0.05f;
                tr.canTransitionToSelf = false;

                // Telegraph → Attack (트리거로)
                var toAttack = telegraphState.AddTransition(idleState);
                toAttack.hasExitTime = true;
                toAttack.exitTime = 1f;
                toAttack.duration = 0.1f;
            }

            // ─── HitStun 상태 ───
            var hitStunState = sm.AddState("HitStun", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                var tr = sm.AddAnyStateTransition(hitStunState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "HitStun");
                tr.hasExitTime = false;
                tr.duration = 0.05f;
                tr.canTransitionToSelf = false;

                var toIdle = hitStunState.AddTransition(idleState);
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0.85f;
                toIdle.duration = 0.15f;
            }

            // ─── Die 상태 ───
            var dieState = sm.AddState("Die", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                var tr = sm.AddAnyStateTransition(dieState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Die");
                tr.hasExitTime = false;
                tr.duration = 0.1f;
                tr.canTransitionToSelf = false;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[REPLACED] Enemy AnimatorController 생성 완료: {AnimatorPath}" +
                $"\n  상태 {stateCount}개, 클립 {clipCount}개 매핑됨");
        }

        // ─── 유틸리티 ───

        private static AnimationClip LoadClipFromFBX(string fbxPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            if (assets == null) return null;
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        private static AnimationClip FindClipByFBXName(string fbxName)
        {
            string[] searchFolders = { FBXFolder, AnimFolder, EEJANAIRoot };
            string[] guids = AssetDatabase.FindAssets(fbxName, searchFolders);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                        return clip;
                }
            }

            guids = AssetDatabase.FindAssets($"t:AnimationClip {fbxName}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }

        private static Vector3 GetStatePosition(int index)
        {
            int col = index % 3;
            int row = index / 3;
            return new Vector3(250 * col, 100 * row, 0);
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
