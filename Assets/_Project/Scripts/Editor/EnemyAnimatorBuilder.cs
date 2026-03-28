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
        private const string WalkForwardFBX = "Assets/Martial Art Animations Sample/Animations/Walk_F.fbx";
        private const string WalkBackFBX = "Assets/Martial Art Animations Sample/Animations/Walk_B.fbx";

        // 히트 리액션 클립
        private const string FlinchFBX = "Assets/Martial Art Animations Sample/Animations/Hit_A.fbx";
        private const string KnockdownFBX = "Assets/Martial Art Animations Sample/Animations/Knock_A.fbx";
        private const string GetUpFBX = "Assets/Martial Art Animations Sample/Animations/GetUp_A.fbx";

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
            controller.AddParameter("Down", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("GetUp", AnimatorControllerParameterType.Trigger);
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

            int stateCount = 0;
            int clipCount = 0;

            // ─── WalkForward 상태 (전진) ───
            AnimationClip walkFClip = LoadClipFromFBX(WalkForwardFBX);
            AnimatorState walkForwardState = sm.AddState("WalkForward", new Vector3(250, 0, 0));
            if (walkFClip != null)
            {
                walkForwardState.motion = walkFClip;
                clipCount++;
                Debug.Log($"[EnemyAnimBuilder] ✓ WalkForward 클립: {walkFClip.name}");
            }

            // ─── WalkBack 상태 (후퇴 — PC를 바라보며 뒤로 이동) ───
            AnimationClip walkBClip = LoadClipFromFBX(WalkBackFBX);
            AnimatorState walkBackState = sm.AddState("WalkBack", new Vector3(500, 0, 0));
            if (walkBClip != null)
            {
                walkBackState.motion = walkBClip;
                clipCount++;
                Debug.Log($"[EnemyAnimBuilder] ✓ WalkBack 클립: {walkBClip.name}");
            }

            // Speed > 0 = 전진, Speed < 0 = 후퇴
            // Idle → WalkForward: Speed > 0.1
            {
                var tr = idleState.AddTransition(walkForwardState);
                tr.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.15f;
            }
            // Idle → WalkBack: Speed < -0.1
            {
                var tr = idleState.AddTransition(walkBackState);
                tr.AddCondition(AnimatorConditionMode.Less, -0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.15f;
            }
            // WalkForward → Idle: Speed < 0.1
            {
                var tr = walkForwardState.AddTransition(idleState);
                tr.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.15f;
            }
            // WalkBack → Idle: Speed > -0.1
            {
                var tr = walkBackState.AddTransition(idleState);
                tr.AddCondition(AnimatorConditionMode.Greater, -0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.15f;
            }
            // WalkForward ↔ WalkBack 직접 전환 (Idle 경유 없이 즉시)
            {
                var tr = walkForwardState.AddTransition(walkBackState);
                tr.AddCondition(AnimatorConditionMode.Less, -0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.1f;
            }
            {
                var tr = walkBackState.AddTransition(walkForwardState);
                tr.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                tr.hasExitTime = false;
                tr.duration = 0.1f;
            }

            // ─── 공격 상태들 ───
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

            // ─── 텔레그래프 상태 (Idle 포즈 유지 + 색상 변경으로 시각 피드백) ───
            var telegraphState = sm.AddState("Telegraph", GetStatePosition(stateCount + 1));
            if (idleClip != null) telegraphState.motion = idleClip;
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

            // ─── HitStun 상태 (Idle 포즈 유지) ───
            var hitStunState = sm.AddState("HitStun", GetStatePosition(stateCount + 1));
            if (idleClip != null) hitStunState.motion = idleClip;
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

                // ★ Knockdown → Idle/Down 자동 전환 없음
                // AI가 Down 상태로 직접 전환
            }

            // ─── Down 상태 (넉다운 후 누워있기) ───
            // ★ speed=0: 클립이 진행하지 않고 고정.
            //   AI가 animator.Play("Down", 0, 1.0f)로 마지막 프레임(누운 포즈)에 직접 진입.
            var downState = sm.AddState("Down", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                AnimationClip knockClip = LoadClipFromFBX(KnockdownFBX);
                if (knockClip != null) downState.motion = knockClip;
                downState.speed = 0f; // ★ 애니메이션 정지 — 누운 포즈 고정

                var tr = sm.AddAnyStateTransition(downState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Down");
                tr.hasExitTime = false;
                tr.duration = 0.05f;
                tr.canTransitionToSelf = false;
                // ★ Down → 자동 전환 없음 (AI가 GetUp 트리거로 직접 전환)
            }

            // ─── GetUp 상태 (기상 모션) ───
            var getUpState = sm.AddState("GetUp", GetStatePosition(stateCount + 1));
            stateCount++;
            {
                AnimationClip getUpClip = LoadClipFromFBX(GetUpFBX);
                if (getUpClip != null)
                {
                    getUpState.motion = getUpClip;
                    clipCount++;
                    Debug.Log($"[EnemyAnimBuilder] ✓ GetUp 클립: {getUpClip.name}");
                }

                var tr = sm.AddAnyStateTransition(getUpState);
                tr.AddCondition(AnimatorConditionMode.If, 0, "GetUp");
                tr.hasExitTime = false;
                tr.duration = 0.1f;
                tr.canTransitionToSelf = false;

                // GetUp → Idle: 모션 종료 후 자동 복귀
                var toIdle = getUpState.AddTransition(idleState);
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0.9f;
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
