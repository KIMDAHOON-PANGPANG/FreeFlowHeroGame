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

        // Martial Art Animations Sample 클립
        private const string IdleFBX = "Assets/Martial Art Animations Sample/Animations/Fight_Idle.fbx";
        private const string WalkFBX = "Assets/Martial Art Animations Sample/Animations/Walk_F.fbx";

        // 히트 리액션 클립
        private const string FlinchFBX = "Assets/Martial Art Animations Sample/Animations/Hit_A.fbx";
        private const string KnockdownFBX = "Assets/Martial Art Animations Sample/Animations/Knock_A.fbx";

        // 적 공격 애니메이션 매핑 (Martial Art Animations Sample)
        private const string AttackKickFBX = "Assets/Martial Art Animations Sample/Animations/Atk_K_1.fbx";
        private const string AttackPunchFBX = "Assets/Martial Art Animations Sample/Animations/Atk_P_1.fbx";

        private static readonly (string fbxPath, string stateName)[] EnemyAnimMap = new[]
        {
            (AttackPunchFBX,  "Attack_Punch"),  // 펀치
            (AttackKickFBX,   "Attack_Kick"),   // 킥
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
            controller.AddParameter("Flinch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Knockdown", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Idle", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // ─── Idle 상태 (기본) ───
            AnimationClip idleClip = LoadClipFromFBX(IdleFBX);
            AnimatorState idleState = sm.AddState("Idle", new Vector3(0, 0, 0));
            if (idleClip != null)
                idleState.motion = idleClip;
            sm.defaultState = idleState;

            // ★ Idle 트리거 → 강제 Idle 복귀 (넉다운/피격 후 Chase 진입 시 사용)
            {
                var toIdle = sm.AddAnyStateTransition(idleState);
                toIdle.AddCondition(AnimatorConditionMode.If, 0, "Idle");
                toIdle.hasExitTime = false;
                toIdle.duration = 0.15f;
                toIdle.canTransitionToSelf = true;
            }

            // ─── Walk 상태 ───
            AnimationClip walkClip = LoadClipFromFBX(WalkFBX);
            AnimatorState walkState = sm.AddState("Walk", new Vector3(250, 0, 0));
            if (walkClip != null)
            {
                walkState.motion = walkClip;
                clipCount++;
                Debug.Log($"[EnemyAnimBuilder] ✓ Walk 클립: {walkClip.name}");
            }

            // Idle → Walk: Speed > 0.1
            {
                var toWalk = idleState.AddTransition(walkState);
                toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                toWalk.hasExitTime = false;
                toWalk.duration = 0.15f;
            }
            // Walk → Idle: Speed < 0.1
            {
                var toIdle = walkState.AddTransition(idleState);
                toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                toIdle.hasExitTime = false;
                toIdle.duration = 0.15f;
            }

            // ─── 공격 상태들 ───
            int stateCount = 0;
            int clipCount = 0;
            foreach (var (fbxPath, stateName) in EnemyAnimMap)
            {
                AnimationClip clip = LoadClipFromFBX(fbxPath);
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
                    Debug.LogWarning($"[EnemyAnimBuilder] ❌ {stateName} 클립 미발견: {fbxPath}");
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

            // ─── Flinch 상태 (경직 피격) ───
            var flinchState = sm.AddState("Flinch", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                AnimationClip flinchClip = LoadClipFromFBX(FlinchFBX);
                if (flinchClip != null) flinchState.motion = flinchClip;

                var tr = sm.AddAnyStateTransition(flinchState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Flinch");
                tr.hasExitTime = false;
                tr.duration = 0.02f;
                tr.canTransitionToSelf = true; // 연속 피격 시 리셋

                var toIdle = flinchState.AddTransition(idleState);
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0.85f;
                toIdle.duration = 0.15f;
            }

            // ─── Knockdown 상태 (넉다운 에어본) ───
            // ★ Idle 자동 전환 제거: HitReactionHandler가 체공 제어 완료 후
            //   AI 상태머신이 SafeSetTrigger("Idle")로 직접 전환한다.
            //   exitTime 자동 전환이 있으면 체공 중 메쉬가 Idle 원점으로 스냅되는 버그 발생.
            var knockdownState = sm.AddState("Knockdown", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                AnimationClip knockClip = LoadClipFromFBX(KnockdownFBX);
                if (knockClip != null) knockdownState.motion = knockClip;

                var tr = sm.AddAnyStateTransition(knockdownState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Knockdown");
                tr.hasExitTime = false;
                tr.duration = 0.05f;
                tr.canTransitionToSelf = false;

                // ★ Knockdown → Idle 자동 전환 없음
                // AI가 HitStun 진입 시 SafeSetTrigger("Flinch") 또는 SafeSetTrigger("Idle")로 제어
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
