using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 점프 상태.
    /// Space 키로 진입, 공중 좌우 이동 가능, 착지 시 Idle 복귀.
    /// 공중에서 Shift + 벽 감지 → WallClimbState 전환.
    /// </summary>
    public class JumpState : CombatState
    {
        public override string StateName => "Jump";

        // ★ 데이터 튜닝
        private const float JumpForce = 12f;       // 점프 초기 속도
        private const float AirMoveSpeed = 5f;     // 공중 좌우 이동 속도
        private const float CoyoteTime = 0.08f;    // 코요테 타임 (플랫폼 가장자리 유예)

        private bool hasLanded;

        public override void Enter()
        {
            base.Enter();
            hasLanded = false;

            // 수직 속도 설정 (상승)
            context.verticalVelocity = JumpForce;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 착지 체크
            if (fsm.IsGrounded && context.verticalVelocity <= 0f && context.stateTimeAccumulator > 0.05f)
            {
                hasLanded = true;
                fsm.TransitionTo<IdleState>();
                return;
            }

            // 공중 좌우 이동
            float inputX = context.lastInputDirection.x;
            if (Mathf.Abs(inputX) > 0.1f)
            {
                MoveHorizontal(inputX * AirMoveSpeed * deltaTime);

                // facing 전환
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (inputX >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
            }
        }

        public override void HandleInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Dodge: // Shift → 벽타기 시도
                    if (fsm.DetectWall(out _))
                    {
                        fsm.TransitionTo<WallClimbState>();
                    }
                    break;

                case InputType.Attack:
                    // 공중 공격 (StrikeState로 전환)
                    fsm.TransitionTo<StrikeState>();
                    break;
            }
        }
    }
}
