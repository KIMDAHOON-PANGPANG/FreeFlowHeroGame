using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 테스트용 더미 적. ICombatTarget을 구현하여 Phase 1 히트 테스트에 사용한다.
    /// 맞으면 빨간색 플래시 + 넉백 연출만 수행한다.
    /// </summary>
    public class DummyEnemyTarget : MonoBehaviour, ICombatTarget
    {
        [Header("스탯")]
        [SerializeField] private float maxHP = 100f;
        [SerializeField] private float currentHP = 100f;

        [Header("시각 피드백")]
        [SerializeField] private float flashDuration = 0.15f;

        // ★ 데이터 튜닝: 사망 연출
        [Header("사망 연출")]
        [Tooltip("사망 후 페이드아웃 시작까지 대기 시간 (초)")]
        [SerializeField] private float deathDelay = 0.5f;
        [Tooltip("페이드아웃 지속 시간 (초)")]
        [SerializeField] private float fadeDuration = 0.8f;

        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private float flashTimer;
        private Vector3 originalScale;
        private bool isDying;
        private float deathTimer;

        // ─── ICombatTarget 구현 ───
        public bool IsTargetable => currentHP > 0f;
        public bool IsInvulnerable => false;
        public float CurrentHP => currentHP;
        public float MaxHP => maxHP;
        public float HPRatio => maxHP > 0 ? currentHP / maxHP : 0f;
        public CombatTeam Team => CombatTeam.Enemy;

        public Transform GetTransform() => transform;

        public void TakeHit(HitData hitData)
        {
            // 데미지 적용 (Phase 1: 단순 감산)
            currentHP = Mathf.Max(0f, currentHP - hitData.BaseDamage);

            // 시각 피드백: 흰색 플래시 + 스케일 펀치
            flashTimer = flashDuration;
            if (spriteRenderer != null)
                spriteRenderer.color = Color.white;
            transform.localScale = originalScale * 1.15f;

            // 넉백: Kinematic position-based (Impulse 방식은 반동/진동 유발 가능)
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                // ★ 즉시 위치 이동 방식 넉백 (수평 방향만 — Y 성분 포함 시 땅속 관통 유발)
                float knockbackDist = 0.3f;
                Vector2 knockDir = new Vector2(hitData.KnockbackDirection.x, 0f);
                if (knockDir == Vector2.zero) knockDir = Vector2.right;
                Vector2 knockPos = rb.position + knockDir.normalized * knockbackDist;
                rb.position = knockPos;
            }



            // 사망 처리
            if (currentHP <= 0f && !isDying)
            {
                isDying = true;
                deathTimer = 0f;
                CombatEventBus.Publish(new OnEnemyDeath { Enemy = this });
            }
        }

        public void InterruptAction()
        {
            // 더미 적은 공격 없음 → 빈 구현
        }

        // ─── MonoBehaviour ───

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;
            originalScale = transform.localScale;
        }

        private void Update()
        {
            // ── 사망 페이드아웃 → Destroy ──
            if (isDying)
            {
                deathTimer += Time.deltaTime;

                if (deathTimer >= deathDelay && spriteRenderer != null)
                {
                    float fadeT = Mathf.Clamp01((deathTimer - deathDelay) / fadeDuration);
                    Color c = spriteRenderer.color;
                    c.a = Mathf.Lerp(1f, 0f, fadeT);
                    spriteRenderer.color = c;
                }

                if (deathTimer >= deathDelay + fadeDuration)
                {
                    Destroy(gameObject);
                }
                return; // 사망 중에는 플래시 처리 스킵
            }

            // 플래시 타이머 + 스케일 복원
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(flashTimer / flashDuration);
                // 스케일: 펀치→원래 크기로 보간
                transform.localScale = Vector3.Lerp(originalScale, originalScale * 1.15f, t);

                if (flashTimer <= 0f)
                {
                    if (spriteRenderer != null)
                        spriteRenderer.color = originalColor;
                    transform.localScale = originalScale;
                }
            }
        }

        /// <summary>HP 리셋 (테스트용)</summary>
        public void ResetHP()
        {
            currentHP = maxHP;

        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // HP 바 시각화
            Vector3 pos = transform.position + Vector3.up * 2.2f;
            float width = 1f;
            float height = 0.1f;

            // 배경 (검정)
            Gizmos.color = Color.black;
            Gizmos.DrawCube(pos, new Vector3(width, height, 0));

            // HP (빨강→초록)
            float ratio = maxHP > 0 ? currentHP / maxHP : 0f;
            Gizmos.color = Color.Lerp(Color.red, Color.green, ratio);
            Gizmos.DrawCube(
                pos + Vector3.left * (width * (1 - ratio) * 0.5f),
                new Vector3(width * ratio, height, 0));
        }
#endif
    }
}
