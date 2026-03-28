using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 리액션 실행기. 적/플레이어 공용 MonoBehaviour.
    /// HitReactionData를 받아 Flinch(즉시 밀림+경직) 또는 Knockdown(커브 체공)을 실행한다.
    /// 리액션 타입별 애니메이션 클립도 재생 (BattleSettings에서 경로 참조).
    ///
    /// ★ Knockdown 중에는 IsKnockdownActive=true → AI/중력 시스템에서 이동을 스킵해야 함.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class HitReactionHandler : MonoBehaviour
    {
        private Rigidbody2D rb;
        private Animator animator;

        // Animator 트리거 이름
        private static readonly int FlinchTrigger = Animator.StringToHash("Flinch");
        private static readonly int KnockdownTrigger = Animator.StringToHash("Knockdown");

        // ─── Knockdown 상태 ───
        private bool knockdownActive;
        private float knockdownTimer;
        private float knockdownAirTime;
        private float knockdownLaunchHeight; // 유닛 (cm→유닛 변환 후)
        private float knockdownDistance;     // 유닛
        private float knockdownDir;          // +1 또는 -1
        private float knockdownBaseY;        // 발사 시점 Y 위치

        /// <summary>넉다운 체공 중인지. true이면 외부 중력/AI 이동을 스킵할 것.</summary>
        public bool IsKnockdownActive => knockdownActive;

        /// <summary>경직(Flinch) 중인지</summary>
        public bool IsFlinchActive { get; private set; }
        private float flinchTimer;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            animator = GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// HitReactionData 기반으로 리액션 실행.
        /// knockDir: 밀리는/날아가는 방향 (+1 또는 -1).
        /// </summary>
        public void ApplyReaction(HitReactionData reaction, float knockDir)
        {
            switch (reaction.type)
            {
                case HitType.Flinch:
                    ExecuteFlinch(reaction.flinch, knockDir);
                    break;
                case HitType.Knockdown:
                    ExecuteKnockdown(reaction.knockdown, knockDir);
                    break;
            }
        }

        // ═══════════════════════════════════════════
        //  애니메이션 트리거
        // ═══════════════════════════════════════════

        private void PlayFlinchAnim()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.SetTrigger(FlinchTrigger);
        }

        private void PlayKnockdownAnim()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.SetTrigger(KnockdownTrigger);
        }

        // ═══════════════════════════════════════════
        //  Flinch — 즉시 밀림 + 경직
        // ═══════════════════════════════════════════

        private void ExecuteFlinch(FlinchData data, float dir)
        {
            if (rb == null) return;

            // cm → 유닛 변환 (100cm = 1유닛)
            float pushUnits = data.pushDistance * 0.01f;

            // 즉시 텔레포트 밀림 (수평만)
            Vector2 pos = rb.position;
            pos.x += dir * pushUnits;
            rb.position = pos;

            // 경직 시간 설정
            flinchTimer = data.freezeTime;
            IsFlinchActive = true;

            // 피격 모션 재생
            PlayFlinchAnim();
        }

        // ═══════════════════════════════════════════
        //  Knockdown — sin 커브 체공
        // ═══════════════════════════════════════════

        private void ExecuteKnockdown(KnockdownData data, float dir)
        {
            if (rb == null) return;

            knockdownActive = true;
            knockdownTimer = 0f;
            knockdownAirTime = Mathf.Max(data.airTime, 0.1f);
            knockdownLaunchHeight = data.launchHeight * 0.01f; // cm → 유닛
            knockdownDistance = data.knockDistance * 0.01f;     // cm → 유닛
            knockdownDir = dir;
            knockdownBaseY = rb.position.y;

            // 넉다운 모션 재생
            PlayKnockdownAnim();
        }

        private void Update()
        {
            // ── Flinch 타이머 ──
            if (IsFlinchActive)
            {
                flinchTimer -= Time.deltaTime;
                if (flinchTimer <= 0f)
                    IsFlinchActive = false;
            }

            // ── Knockdown 체공 ──
            if (!knockdownActive) return;

            knockdownTimer += Time.deltaTime;
            float t = Mathf.Clamp01(knockdownTimer / knockdownAirTime);

            // Y축: sin(π * t) 커브 — 부드럽게 올라갔다 내려옴
            float y = knockdownBaseY + knockdownLaunchHeight * Mathf.Sin(Mathf.PI * t);

            // X축: 선형 이동
            float x = rb.position.x + knockdownDir * (knockdownDistance / knockdownAirTime) * Time.deltaTime;

            rb.position = new Vector2(x, y);

            // 체공 종료
            if (t >= 1f)
            {
                knockdownActive = false;
                rb.position = new Vector2(rb.position.x, knockdownBaseY);
            }
        }
    }
}
