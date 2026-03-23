using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Common
{
    /// <summary>
    /// 허트박스 컨트롤러.
    /// 피격 판정 영역. ICombatTarget 구현체와 연결된다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class HurtboxController : MonoBehaviour
    {
        [SerializeField] private CombatTeam ownerTeam = CombatTeam.Player;

        private Collider2D hurtboxCollider;
        private ICombatTarget combatTarget;

        /// <summary>소속 팀</summary>
        public CombatTeam OwnerTeam => ownerTeam;

        /// <summary>외부에서 팀 설정 (에디터 스크립트용)</summary>
        public void SetOwnerTeam(CombatTeam team)
        {
            ownerTeam = team;
        }

        private void Awake()
        {
            hurtboxCollider = GetComponent<Collider2D>();
            hurtboxCollider.isTrigger = true;

            // 부모에서 ICombatTarget 검색
            combatTarget = GetComponentInParent<ICombatTarget>()
                ?? GetComponent<ICombatTarget>();

            // 안전장치: ICombatTarget의 팀 정보로 ownerTeam 자동 보정
            if (combatTarget != null && ownerTeam != combatTarget.Team)
            {
                Debug.LogWarning($"[Hurtbox] '{gameObject.name}' ownerTeam 불일치 감지: " +
                    $"{ownerTeam} → {combatTarget.Team} 자동 보정");
                ownerTeam = combatTarget.Team;
            }
        }

        /// <summary>연결된 ICombatTarget 반환</summary>
        public ICombatTarget GetCombatTarget()
        {
            return combatTarget;
        }

        /// <summary>허트박스 활성/비활성 (무적 시 비활성)</summary>
        public void SetActive(bool active)
        {
            hurtboxCollider.enabled = active;
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 허트박스 시각화</summary>
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Gizmos.color = new Color(0f, 1f, 0f, 0.2f); // 초록

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
