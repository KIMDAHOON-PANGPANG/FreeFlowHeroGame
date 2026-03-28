using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Hit 상태: 플레이어 피격.
    /// HitReactionHandler를 통해 Flinch/Knockdown 리액션을 실행한다.
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

            // ★ 머티리얼 플래시
            context.hitFlash?.Play();

            // ★ 히트 리액션 실행 (Flinch/Knockdown 모션 + 넉백 + 방향)
            var hitData = context.lastHitData;
            if (context.hitReactionHandler != null)
            {
                context.hitReactionHandler.ApplyReaction(hitData.Reaction, hitData);
            }
            else
            {
                // 폴백: HitReactionHandler 없으면 기존 애니메이션 트리거만
                if (context.playerAnimator != null)
                    context.playerAnimator.SetTrigger("Hit");
            }

            // 이벤트 발행
            CombatEventBus.Publish(new OnPlayerHit
            {
                HitData = hitData
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

            // ★ Knockdown 중에는 경직 프레임 무시 — HitReactionHandler가 제어
            if (context.hitReactionHandler != null && context.hitReactionHandler.IsKnockdownActive)
                return;

            // 무적 프레임 종료
            if (context.stateFrameCounter >= InvulnerableFrames)
            {
                context.isInvulnerable = false;
            }

            // ★ Flinch 종료 대기: HitReactionHandler가 있으면 freezeTime 기반
            if (context.hitReactionHandler != null)
            {
                bool flinchDone = !context.hitReactionHandler.IsFlinchActive;
                bool knockdownDone = !context.hitReactionHandler.IsKnockdownActive;
                if (flinchDone && knockdownDone && context.stateFrameCounter >= InvulnerableFrames)
                {
                    // Hard Hit(Knockdown) → Down → GetUp 흐름
                    // Soft Hit(Flinch) → 바로 Idle 복귀
                    bool wasKnockdown = context.lastHitData.Reaction.type == HitType.Knockdown;
                    if (wasKnockdown)
                        fsm.TransitionTo<DownState>();
                    else
                        fsm.TransitionTo<IdleState>();
                }
            }
            else
            {
                // 폴백: 고정 경직 프레임
                if (context.stateFrameCounter >= StunFrames)
                {
                    fsm.TransitionTo<IdleState>();
                }
            }
        }

        public override void HandleInput(InputData input)
        {
            // 피격 중 모든 입력 무시
        }

        /// <summary>피격 중 추가 피격 (슈퍼아머 없으면 리셋)</summary>
        public override void OnHit(HitData hitData)
        {
            // 무적 상태면 무시
            if (context.isInvulnerable) return;

            // HitData 갱신 + 프레임 카운터 리셋 (경직 재시작)
            context.lastHitData = hitData;
            context.ResetStateFrame();
            context.isInvulnerable = true;

            // ★ 재피격 리액션 실행
            if (context.hitReactionHandler != null)
            {
                context.hitReactionHandler.ApplyReaction(hitData.Reaction, hitData);
            }

            context.hitFlash?.Play();
        }
    }
}
