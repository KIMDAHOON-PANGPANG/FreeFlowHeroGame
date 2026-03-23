using System;
using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Common
{
    /// <summary>
    /// 히트박스 컨트롤러.
    /// 공격 판정 영역을 관리한다. 애니메이션 이벤트로 활성/비활성 제어.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HitboxController : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private CombatTeam ownerTeam = CombatTeam.Player;
        [SerializeField] private bool hitOnce = true; // 한 번의 활성화당 같은 대상 1회만 히트

        private Collider2D hitboxCollider;
        private HashSet<Collider2D> alreadyHit = new();
        private bool isActive;

        /// <summary>히트 발생 시 콜백</summary>
        public event Action<ICombatTarget, Vector2> OnHitDetected;

        /// <summary>히트박스 활성 여부</summary>
        public bool IsActive => isActive;

        private void Awake()
        {
            hitboxCollider = GetComponent<Collider2D>();
            hitboxCollider.isTrigger = true;
            Deactivate();
        }

        /// <summary>히트박스 활성화 (Active 프레임 시작 시)</summary>
        public void Activate()
        {
            isActive = true;
            hitboxCollider.enabled = true;
            alreadyHit.Clear();
        }

        /// <summary>히트박스 비활성화 (Active 프레임 종료 시)</summary>
        public void Deactivate()
        {
            isActive = false;
            hitboxCollider.enabled = false;
            alreadyHit.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            ProcessTriggerHit(other);
        }

        /// <summary>
        /// OnTriggerStay2D — 워핑 후 히트박스가 이미 겹친 상태에서
        /// Enable되는 경우 OnTriggerEnter2D가 불리지 않는 Unity 이슈 대응.
        /// alreadyHit으로 중복 방지되므로 성능 영향 없음.
        /// </summary>
        private void OnTriggerStay2D(Collider2D other)
        {
            ProcessTriggerHit(other);
        }

        /// <summary>공통 히트 판정 처리</summary>
        private void ProcessTriggerHit(Collider2D other)
        {
            if (!isActive) return;
            if (hitOnce && alreadyHit.Contains(other)) return;

            // Hurtbox 확인
            var hurtbox = other.GetComponent<HurtboxController>();
            if (hurtbox == null) return;

            // 같은 팀 무시
            if (hurtbox.OwnerTeam == ownerTeam) return;

            // 무적 상태 무시
            var target = hurtbox.GetCombatTarget();
            if (target != null && target.IsInvulnerable) return;

            alreadyHit.Add(other);

            // 접촉 지점 계산
            Vector2 contactPoint = other.ClosestPoint(transform.position);

            OnHitDetected?.Invoke(target, contactPoint);
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 히트박스 시각화</summary>
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Gizmos.color = isActive
                ? new Color(1f, 0f, 0f, 0.4f)   // 활성: 빨강
                : new Color(1f, 0.5f, 0f, 0.2f); // 비활성: 주황

            if (col is BoxCollider2D box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.offset, box.size);
                Gizmos.DrawWireCube(box.offset, box.size);
            }
            else if (col is CircleCollider2D circle)
            {
                Gizmos.DrawSphere(
                    transform.position + (Vector3)circle.offset,
                    circle.radius);
            }
        }
#endif
    }
}
