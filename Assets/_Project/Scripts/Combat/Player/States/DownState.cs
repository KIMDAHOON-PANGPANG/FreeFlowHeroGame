using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Down 상태: Hard Hit(Knockdown) 피격 후 착지하여 누워있는 상태.
    /// downTime 동안 모든 일반 입력을 차단한다.
    ///
    /// ★ 확장성 설계: 기상기(Wake-Up Skill) 추가 시
    ///   - TryWakeUpSkill() 오버라이드: 특정 입력 감지 → GetUpState 스킵 후 기상기 발동
    ///   - HandleInput: 현재는 차단, 추후 기상기 입력만 허용하도록 확장 가능
    ///
    /// 흐름: Hit(Knockdown 체공) → Down(누워있기) → GetUp(기상 모션) → Idle
    /// </summary>
    public class DownState : CombatState
    {
        public override string StateName => "Down";

        private float downTimer;

        public override void Enter()
        {
            base.Enter();

            // HitReactionHandler에서 데이터 드리븐 downTime 가져오기
            float downTime = (context.hitReactionHandler != null)
                ? context.hitReactionHandler.LastDownTime
                : 0.5f;
            downTimer = Mathf.Max(downTime, 0f);

            // 애니메이션: Down 포즈 (Knockdown 클립 마지막 프레임 유지)
            if (context.playerAnimator != null)
                context.playerAnimator.SetTrigger("Down");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            downTimer -= deltaTime;

            // ★ 확장 훅: 기상기 스킬 체크 (추후 override로 구현)
            if (TryWakeUpSkill()) return;

            if (downTimer <= 0f)
                fsm.TransitionTo<GetUpState>();
        }

        public override void Exit()
        {
            base.Exit();
        }

        /// <summary>
        /// 기상기 스킬 훅. 현재는 아무것도 하지 않는다.
        /// 추후 파생 클래스 또는 이 메서드 내부에서:
        ///   특정 입력(예: 회피 입력) 감지 시 즉시 GetUp 혹은 기상기 State로 전환.
        /// </summary>
        /// <returns>true이면 Down 타이머 전환을 중단하고 이미 전환이 처리됨을 의미</returns>
        protected virtual bool TryWakeUpSkill() => false;

        public override void HandleInput(InputData input)
        {
            // ★ 누워있는 중 일반 조작 전체 차단.
            // 추후 기상기 구현 시: 특정 input.Type에 대해서만 TryWakeUpSkill 경유 처리.
        }

        public override void OnHit(HitData hitData)
        {
            // 누워있는 중 추가 피격은 무시 (지면 콤보 공격 구현 시 여기서 처리)
        }
    }
}
