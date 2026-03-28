using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Hit 상태: 플레이어 피격.
    /// 경직 시간 동안 입력 무효 → Idle 복귀.
    /// </summary>
    public class HitState : CombatState
    {
        public override string StateName => "Hit";

        // 피격 경직 프레임
        private const int StunFrames = 18; // 약 0.3초
        private const int InvulnerableFrames = 12; // 피격 후 무적 프레임

        public override void Enter()
        {
            base.Enter();

            // 콤보 리셋
            context.ResetCombo();

            // 헉슬리 게이지 감소 (-20%)
            context.ChargeHuxley(-20f);

            // 무적 설정 (연속 피격 방지)
            context.isInvulnerable = true;

            // 애니메이션
            if (context.playerAnimator != null)
                context.playerAnimator.SetTrigger("Hit");

            // ★ 머티리얼 플래시
            context.hitFlash?.Play();

            // 이벤트 발행
            CombatEventBus.Publish(new OnPlayerHit
            {
                HitData = default // TODO: 실제 HitData 전달
            });
        }

        public override void Exit()
        {
            base.Exit();
            // 무적 해제는 InvulnerableFrames 이후 자동 처리
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 무적 프레임 종료
            if (context.stateFrameCounter >= InvulnerableFrames)
            {
                context.isInvulnerable = false;
            }

            // 경직 종료 → Idle 복귀
            if (context.stateFrameCounter >= StunFrames)
            {
                fsm.TransitionTo<IdleState>();
            }
        }

        public override void HandleInput(InputData input)
        {
            // 피격 중 모든 입력 무시
            // Phase 2에서 카운터 빠른 반격 윈도우 추가 가능:
            // if (input.Type == InputType.Counter && IsInCounterRecoveryWindow())
            //     fsm.TransitionTo<CounterState>();
        }

        /// <summary>피격 중 추가 피격 (슈퍼아머 없으면 리셋)</summary>
        public override void OnHit(HitData hitData)
        {
            // 무적 상태면 무시
            if (context.isInvulnerable) return;

            // 프레임 카운터 리셋 (경직 재시작)
            context.ResetStateFrame();
            context.isInvulnerable = true;
        }
    }
}
