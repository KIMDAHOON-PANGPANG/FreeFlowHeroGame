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
        public override string StateName => "Idle";

        // ─── 이동 파라미터 ───
        private const float MoveSpeed = 5f;

        // ─── Animator 해시 (성능) ───
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        public override void Enter()
        {
            base.Enter();
            context.isWarping = false;
            context.isInvulnerable = false;
            context.canCancel = true;
            // comboChainIndex: 여기서 리셋하지 않음!
            // 콤보 윈도우 내에서 재공격 시 다음 타수로 이어져야 하므로
            // ResetCombo() (콤보 윈도우 만료)에서만 리셋

            // 타겟 클리어
            fsm.TargetSelector.ClearTarget();

            // Kinematic: velocity 리셋은 base.Enter()에서 처리됨

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
                    // 프리플로우: 타겟 선택 → 워핑 필요하면 Warp, 아니면 직접 Strike
                    ResolveAttack(input);
                    break;

                case InputType.Dodge:
                    fsm.TransitionTo<DodgeState>();
                    break;

                case InputType.Counter:
                    fsm.TransitionTo<CounterState>();
                    break;

                case InputType.Heavy:
                    // Phase 2: Heavy → 일단 Strike로 전환 (Phase 4에서 별도 HeavyAttackState)
                    ResolveAttack(input);
                    break;

                case InputType.Huxley:
                    // Phase 4에서 구현
                    if (context.huxleyGauge >= 33f)
                    {
                        Debug.Log("[Idle] 헉슬리 발사 (Phase 4에서 구현)");
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
        /// 프리플로우 공격 분기:
        /// 1. 타겟 선택
        /// 2. 워핑 필요 → WarpState
        /// 3. 근접 거리 → 바로 StrikeState
        /// 4. 타겟 없음 → 헛스윙 StrikeState
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
                // 타겟 발견 → 워핑 필요 여부
                context.currentTarget = target.GetTransform();

                if (fsm.TargetSelector.NeedsWarp(playerPos, target))
                {
                    // 워핑 필요 → WarpState → 자동으로 StrikeState 전환
                    fsm.TransitionTo<WarpState>();
                }
                else
                {
                    // 근접 → 바로 Strike
                    float dir = Mathf.Sign(target.GetTransform().position.x - playerPos.x);
                    Vector3 scale = context.playerTransform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                    context.playerTransform.localScale = scale;

                    fsm.TransitionTo<StrikeState>();
                }
            }
            else
            {
                // 타겟 없음 → 헛스윙 Strike
                fsm.TransitionTo<StrikeState>();
            }
        }
    }
}
