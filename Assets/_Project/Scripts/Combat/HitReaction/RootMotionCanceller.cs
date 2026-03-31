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
        private Transform hipsTransform;
        private Quaternion initialLocalRotation;

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

        private void Start()
        {
            // 초기 회전 캐싱 (ModelSetup에서 설정한 Euler(0,90,0) 등)
            // 애니메이션 루트 회전이 본에 누출되어 방향이 틀어지는 것을 방지
            initialLocalRotation = transform.localRotation;
            CacheHipsTransform();
        }

        /// <summary>Hips 본을 캐싱한다. Humanoid 매핑 → 이름 검색 순으로 시도.</summary>
        private void CacheHipsTransform()
        {
            if (hipsTransform != null) return;

            // 1순위: Humanoid 본 매핑
            if (anim != null && anim.isHuman)
                hipsTransform = anim.GetBoneTransform(HumanBodyBones.Hips);

            // 2순위: 이름에 "Hips" 포함하는 본 검색 (EEJANAI: "EEJANAIBot:Hips")
            if (hipsTransform == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>())
                {
                    if (t != transform && t.name.IndexOf("Hips",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hipsTransform = t;
                        break;
                    }
                }
            }

            if (hipsTransform != null)
                Debug.Log($"<color=cyan>[RootMotionCanceller] Hips 본 캐싱: {hipsTransform.name}</color>");
            else
                Debug.LogWarning("[RootMotionCanceller] Hips 본 미발견 — XZ 보정 불가");
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

        /// <summary>
        /// ★ 루트모션 위치/회전 보정 (월드 좌표 기반)
        ///
        /// 1) Hips XZ 역상쇄: 루트모션 추출 실패 시 Hips가 XZ로 이탈 → 메쉬 밀림 방지
        /// 2) 루트 회전 복원: 애니메이션 루트 회전이 본에 누출 → 모델 방향 틀어짐 방지
        ///
        /// ※ 월드 좌표 사용 이유: 모델이 Y축 90도 회전(2D 횡스크롤 뷰)되어 있어
        ///   self-local과 parent-local 좌표계가 다르기 때문.
        /// ※ 캐릭터 좌우 방향(facing)은 부모의 localScale.x 플립으로 처리됨.
        /// </summary>
        private void LateUpdate()
        {
            if (UseRootMotion) return;
            if (handler != null && handler.IsKnockdownActive) return;

            // ★ 루트 회전 복원: 애니메이션이 모델 방향을 틀어도 초기 회전 강제 유지
            // (캐릭터 좌우 flip은 부모 localScale.x로 처리, 모델 자체 회전은 고정)
            transform.localRotation = initialLocalRotation;

            if (hipsTransform != null)
            {
                // 부모(플레이어 루트) 월드 위치 기준으로 Hips의 XZ 오프셋 계산
                Vector3 parentPos = transform.parent != null
                    ? transform.parent.position : Vector3.zero;
                float offsetX = hipsTransform.position.x - parentPos.x;
                float offsetZ = hipsTransform.position.z - parentPos.z;

                // Animator를 Hips 반대 방향으로 이동 → Hips가 부모 바로 위에 고정
                transform.position = new Vector3(
                    transform.position.x - offsetX,
                    transform.position.y,
                    transform.position.z - offsetZ
                );
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }
        }
    }
}
