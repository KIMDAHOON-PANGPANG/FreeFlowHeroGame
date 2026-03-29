using UnityEngine;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// Animator 자식 오브젝트에 부착되어 루트모션을 상태별로 선택 적용하는 헬퍼.
    ///
    /// ★ 넉다운 중: 애니메이션 루트모션 deltaPosition을 부모 rb.position에 적용
    ///   → 루트(콜라이더)가 메쉬와 함께 이동.
    ///   → X축은 넉백 방향(knockdownDir)에 맞춰 플립.
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

        private void Awake()
        {
            anim = GetComponent<Animator>();
            handler = GetComponentInParent<HitReactionHandler>();
            if (handler != null)
                parentRb = handler.GetComponent<Rigidbody2D>();
        }

        private void OnAnimatorMove()
        {
            // ★ 넉다운 중: X 루트모션만 부모 rb에 적용 (Y는 적용하지 않음 — 시각적 체공은 포즈가 담당)
            if (handler != null && handler.IsKnockdownActive && parentRb != null)
            {
                Vector3 delta = anim.deltaPosition;
                // X축: 애니메이션 방향과 무관하게 넉백 방향으로 이동
                float xMove = Mathf.Abs(delta.x) * handler.KnockdownDir;
                parentRb.position += new Vector2(xMove, 0f);
                return;
            }
            // 그 외: 루트모션 차단 (아무것도 적용하지 않음)
        }
    }
}
