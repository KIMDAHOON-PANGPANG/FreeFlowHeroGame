using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.HitReaction;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 테스트용 더미 적. ICombatTarget을 구현하여 Phase 1 히트 테스트에 사용한다.
    /// 맞으면 HitFlash(머티리얼 플래시) + 스케일 펀치 + 넉백 연출을 수행한다.
    /// </summary>
    public class DummyEnemyTarget : MonoBehaviour, ICombatTarget
    {
        [Header("스탯")]
        [SerializeField] private float maxHP = 100f;
        [SerializeField] private float currentHP = 100f;

        // ★ 데이터 튜닝: 스케일 펀치 지속 시간
        [Header("시각 피드백")]
        [SerializeField] private float scalePunchDuration = 0.15f;

        private SpriteRenderer spriteRenderer;
        private HitFlash hitFlash;
        private HitReactionHandler reactionHandler;
        private float scalePunchTimer;
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

            // 시각 피드백: 머티리얼 플래시 + 스케일 펀치
            hitFlash?.Play();
            scalePunchTimer = scalePunchDuration;
            transform.localScale = originalScale * 1.15f;

            // ★ 피격 리액션: HitReactionHandler로 Flinch/Knockdown 분기
            if (reactionHandler != null)
            {
                reactionHandler.ApplyReaction(hitData.Reaction, hitData);
            }
            else
            {
                // 폴백: HitReactionHandler 없으면 기존 고정값 넉백
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    float fallbackDir = hitData.KnockbackDirection.x;
                    if (Mathf.Approximately(fallbackDir, 0f)) fallbackDir = 1f;
                    Vector2 knockPos = rb.position + new Vector2(Mathf.Sign(fallbackDir) * 0.3f, 0f);
                    rb.position = knockPos;
                }
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
            hitFlash = GetComponent<HitFlash>();
            reactionHandler = GetComponent<HitReactionHandler>();
            originalScale = transform.localScale;
        }

        private void Update()
        {
            // ── 사망 페이드아웃 → Destroy ──
            if (isDying)
            {
                deathTimer += Time.deltaTime;

                float deathDelay = BattleSettings.GetEnemyDeathDelay();
                float fadeDur = BattleSettings.GetEnemyDeathFadeDuration();

                if (deathTimer >= deathDelay && spriteRenderer != null)
                {
                    float fadeT = Mathf.Clamp01((deathTimer - deathDelay) / fadeDur);
                    Color c = spriteRenderer.color;
                    c.a = Mathf.Lerp(1f, 0f, fadeT);
                    spriteRenderer.color = c;
                }

                if (deathTimer >= deathDelay + fadeDur)
                {
                    Destroy(gameObject);
                }
                return; // 사망 중에는 스케일 펀치 스킵
            }

            // 스케일 펀치 복원
            if (scalePunchTimer > 0f)
            {
                scalePunchTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(scalePunchTimer / scalePunchDuration);
                transform.localScale = Vector3.Lerp(originalScale, originalScale * 1.15f, t);

                if (scalePunchTimer <= 0f)
                {
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
