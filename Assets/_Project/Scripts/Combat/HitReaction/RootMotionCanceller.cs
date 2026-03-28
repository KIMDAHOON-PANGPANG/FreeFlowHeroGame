using UnityEngine;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// Animator 자식 오브젝트에 부착되어 루트 본 시각 이탈을 방지하는 헬퍼.
    ///
    /// applyRootMotion=true 상태에서 빈 OnAnimatorMove()를 제공하면
    /// Unity가 루트 본 이동량을 추출만 하고 transform에 적용하지 않는다.
    /// → 루트 본이 스켈레톤 원점에 고정되어 SkinnedMesh가 rb.position에서 벗어나지 않음.
    ///
    /// 실제 이동(넉다운 체공, Flinch 밀림 등)은 HitReactionHandler가
    /// sin-curve / 즉시 텔레포트로 rb.position을 직접 제어한다.
    ///
    /// HitReactionHandler.Awake()에서 자동 부착된다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RootMotionCanceller : MonoBehaviour
    {
        private void OnAnimatorMove()
        {
            // 의도적으로 비어있음: 루트모션 적용 차단
            // 모든 이동은 부모의 Rigidbody2D(rb.position)로 스크립트가 전담
        }
    }
}
