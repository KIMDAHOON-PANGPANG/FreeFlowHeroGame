using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Idle 상태: 전투 대기 + 이동.
    /// 입력을 기다리며 콤보 윈도우를 관리한다.
    /// Phase 2: WASD 이동 + 모든 전투 액션으로의 전환을 지원한다.
    /// </summary>
    public class IdleState : CombatState
    {
        // ★ StateName: 이동 입력이 있으면 "Walk", 없으면 "Idle" 표시
        public override string StateName => isMoving ? "Walk" : "Idle";

        // ─── 이동 파라미터 ───
        private const float MoveSpeed = 5f;

        // ─── 이동 상태 추적 ───
        private bool isMoving;

        // ─── Animator 해시 (성능) ───
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");

        public override void Enter()
        {
            base.Enter();
            context.isWarping = false;
            context.isInvulnerable = false;
            context.canCancel = true;
            isMoving = false;
            // comboChainIndex: 여기서 리셋하지 않음!
            // 콤보 윈도우 내에서 재공격 시 다음 타수로 이어져야 하므로
            // ResetCombo() (콤보 윈도우 만료)에서만 리셋

            // 타겟 클리어
            fsm.TargetSelector.ClearTarget();

            // Kinematic: velocity 리셋은 base.Enter()에서 처리됨

            // ★ Locomotion 상태로 즉시 전환 (공격 모션 잔류 방지)
            // Exit Time 기반 전이는 FSM이 먼저 상태를 전환하면 작동하지 않으므로
            // CrossFade로 Locomotion 블렌드 트리를 명시적으로 재생한다.
            ForceLocomotionState();

            // Locomotion: Speed = 0 (Idle)
            SetAnimSpeed(0f);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // ─── WASD 이동 (Kinematic: rb.position 직접 이동) ───
            Vector2 moveDir = context.lastInputDirection;
            if (moveDir.sqrMagnitude > 0.01f)
            {
                isMoving = true;

                // Kinematic 이동: CapsuleCast로 적 관통 방지
                MoveHorizontal(moveDir.x * MoveSpeed * deltaTime);

                // 방향 전환 (좌우 flip)
                if (Mathf.Abs(moveDir.x) > 0.1f)
                {
                    Vector3 scale = context.playerTransform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (moveDir.x >= 0 ? 1f : -1f);
                    context.playerTransform.localScale = scale;
                }

                // Locomotion: Speed = 1 (Run)
                SetAnimSpeed(1f);
            }
            else
            {
                isMoving = false;

                // 정지: Kinematic이므로 별도 처리 불필요
                // Locomotion: Speed = 0 (Idle)
                SetAnimSpeed(0f);
            }

            // 버퍼에 남은 입력이 있으면 즉시 처리
            if (fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleInput(buffered);
            }
        }

        public override void Exit()
        {
            base.Exit();
            // Kinematic: velocity 리셋은 base.Enter()에서 처리
        }

        public override void HandleInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Attack:
                {
                    // 처형 체크: 저HP 적이 근처에 있으면 처형 발동
                    Vector2 pos = GetPos();
                    float inputDir = input.Direction.x;
                    if (Mathf.Approximately(inputDir, 0f))
                        inputDir = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

                    var execTarget = ExecutionSystem.FindExecutionTarget(
                        pos, context.activeEnemies, context.comboCount, inputDir);
                    if (execTarget != null)
                    {
                        context.executionTarget = execTarget;
                        fsm.TransitionTo<ExecutionState>();
                    }
                    else
                    {
                        ResolveAttack(input);
                    }
                    break;
                }

                case InputType.Dodge:
                    fsm.TransitionTo<DodgeState>();
                    break;

                case InputType.Heavy:
                    // 가드 상태 진입
                    fsm.TransitionTo<GuardState>();
                    break;

                case InputType.Huxley:
                    // Phase 4에서 구현
                    if (context.huxleyGauge >= 33f)
                    {

                    }
                    break;

                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        /// <summary>Animator Speed 파라미터를 안전하게 설정한다.</summary>
        private void SetAnimSpeed(float speed)
        {
            if (context.playerAnimator != null
                && context.playerAnimator.runtimeAnimatorController != null)
            {
                try { context.playerAnimator.SetFloat(SpeedHash, speed); }
                catch { /* Animator 파라미터 없을 수 있음 — 무시 */ }
            }
        }

        /// <summary>
        /// Animator를 Locomotion 블렌드 트리 상태로 즉시 전환한다.
        /// StrikeState 등에서 Exit Time 기반 전이가 FSM보다 늦게 작동하므로
        /// CrossFade로 명시적으로 Locomotion 상태를 재생시킨다.
        /// 전환 시간 0.1초(짧은 블렌딩)로 자연스럽게 전환.
        /// </summary>
        private void ForceLocomotionState()
        {
            if (context.playerAnimator == null
                || context.playerAnimator.runtimeAnimatorController == null)
                return;

            try
            {
                // ★ CrossFade: 현재 어떤 상태에 있든 Locomotion으로 빠르게 전환
                //   transitionDuration 0.1초 = 약 6프레임에 걸쳐 블렌딩
                //   0으로 하면 즉시 스냅 (더 반응적이지만 시각적으로 딱딱할 수 있음)
                context.playerAnimator.CrossFade(LocomotionStateHash, 0.1f, 0);
            }
            catch { /* Locomotion 상태가 없을 수 있음 — 무시 */ }
        }

        /// <summary>
        /// 프리플로우 공격 분기:
        /// 1. 타겟 선택 + 방향 전환
        /// 2. 무조건 StrikeState 진입 (워핑은 WARP 노티파이가 처리)
        /// </summary>
        private void ResolveAttack(InputData input)
        {
            Vector2 playerPos = GetPos();
            float inputDir = input.Direction.x;

            // 방향 입력이 없으면 현재 바라보는 방향을 기본값으로 사용
            if (Mathf.Approximately(inputDir, 0f))
                inputDir = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

            // 타겟 선택
            var target = fsm.TargetSelector.SelectTarget(
                playerPos, context.activeEnemies, inputDir);

            if (target != null)
            {
                context.currentTarget = target.GetTransform();

                // 방향 전환
                float dir = Mathf.Sign(target.GetTransform().position.x - playerPos.x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
            }
            else
            {
                context.currentTarget = null;
            }

            // 워핑은 WARP 노티파이가 처리 → 무조건 StrikeState 진입
            fsm.TransitionTo<StrikeState>();
        }
    }
}
