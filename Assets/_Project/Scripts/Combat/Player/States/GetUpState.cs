using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// GetUp 상태: 넉다운 후 기상 모션 재생.
    /// 모든 입력을 차단하여 기상 중 행동 불가.
    ///
    /// ★ 확장성 설계:
    ///   - 기상기 스킬(Wake-Up Attack 등) 구현 시:
    ///     DownState.TryWakeUpSkill()에서 이 상태를 거치지 않고 별도 State로 분기
    ///   - 기상 후 무적 프레임이 필요하면 Enter/Exit에서 context.isInvulnerable 조정
    ///
    /// 흐름: Down(누워있기) → GetUp(기상 모션) → Idle
    /// </summary>
    public class GetUpState : CombatState
    {
        public override string StateName => "GetUp";

        // ★ 데이터 튜닝: GetUp_A.fbx 모션 재생 시간 (초)
        // 실제 GetUp_A 클립 길이에 맞춰 조정. 애니메이터 exitTime으로도 제어 가능.
        private const float GetUpDuration = 1.2f;

        private float timer;

        public override void Enter()
        {
            base.Enter();
            timer = GetUpDuration;

            // 애니메이션: GetUp_A 모션
            if (context.playerAnimator != null)
                context.playerAnimator.SetTrigger("GetUp");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            timer -= deltaTime;
            if (timer <= 0f)
                fsm.TransitionTo<IdleState>();
        }

        public override void Exit()
        {
            base.Exit();
        }

        public override void HandleInput(InputData input)
        {
            // ★ 기상 모션 중 모든 입력 차단.
            // 미래 확장: 기상 직전(마지막 0.2초 등) 입력 버퍼 수집 허용 가능.
        }

        public override void OnHit(HitData hitData)
        {
            // 기상 중 피격: 현재는 무시.
            // 추후: 기상 모션 중 공격받으면 다시 HitState로 전환할 수 있음.
        }
    }
}
