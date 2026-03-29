using UnityEngine;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// Animator 자식 오브젝트에 부착되어 루트모션을 상태별로 선택 적용하는 헬퍼.
    ///
    /// ★ UseRootMotion=true: 루트모션 delta를 Rigidbody2D에 적용 (Dodge 등 애니메이션 기반 이동)
    ///   → 2D 스케일 플립(localScale.x &lt; 0) 시 X축 자동 반전
    ///
    /// ★ 넉다운 중: 루트모션 전면 차단 (궤적은 HitReactionHandler.Update()에서 코드로 제어)
    ///
    /// ★ 그 외: 루트모션 차단 (아무것도 적용하지 않음)
    ///   → 일반 Idle/Attack 등은 제자리 재생.
    ///
    /// HitReactionHandler.Awake()에서 자동 부착된다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RootMotionCanceller : MonoBehaviour
    {
        private Animator anim;
        private HitReactionHandler handler;
        private Rigidbody2D parentRb;

        /// <summary>
        /// true면 루트모션 delta를 Rigidbody2D에 적용한다.
        /// DodgeState 등 애니메이션 기반 이동 상태에서 사용.
        /// </summary>
        public bool UseRootMotion { get; set; }

        private void Awake()
        {
            anim = GetComponent<Animator>();
            handler = GetComponentInParent<HitReactionHandler>();
            if (handler != null)
                parentRb = handler.GetComponent<Rigidbody2D>();
        }

        private void OnAnimatorMove()
        {
            // ★ 애니메이션 기반 이동 (Dodge 등): 루트모션 delta → Rigidbody2D
            if (UseRootMotion && parentRb != null && anim != null)
            {
                Vector2 delta = (Vector2)anim.deltaPosition;

                // 2D 스케일 플립 보정: localScale.x < 0이면 X축 반전
                // Humanoid 루트모션은 Transform 회전 기준이므로, scale 플립은 반영되지 않음
                if (parentRb.transform.localScale.x < 0f)
                    delta.x = -delta.x;

                parentRb.position += delta;
                return;
            }

            // ★ 넉다운 중: 궤적은 HitReactionHandler.Update()에서 코드로 제어. 루트모션 전면 차단.
            if (handler != null && handler.IsKnockdownActive && parentRb != null)
            {
                return;
            }
            // 그 외: 루트모션 차단 (아무것도 적용하지 않음)
        }
    }
}
