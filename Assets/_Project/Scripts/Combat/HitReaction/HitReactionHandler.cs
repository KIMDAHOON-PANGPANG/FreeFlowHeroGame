using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 리액션 실행기. 적/플레이어 공용 MonoBehaviour.
    /// HitReactionData를 받아 Flinch(즉시 밀림+경직) 또는 Knockdown(커브 체공)을 실행한다.
    /// facing/forceFlip으로 피격 시 바라보는 방향을 제어한다.
    ///
    /// ★ Knockdown 중에는 IsKnockdownActive=true → AI/중력 시스템에서 이동을 스킵해야 함.
    /// ★ 재피격 시: Flinch→Flinch 리셋 가능, Knockdown 중 Flinch는 무시.
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
        private float knockdownLaunchHeight;
        private float knockdownDistance;
        private float knockdownDir;
        private float knockdownBaseY;

        /// <summary>넉다운 체공 중인지. true이면 외부 중력/AI 이동을 스킵할 것.</summary>
        public bool IsKnockdownActive => knockdownActive;

        /// <summary>경직(Flinch) 중인지</summary>
        public bool IsFlinchActive { get; private set; }
        private float flinchTimer;

        /// <summary>현재 freezeTime 잔여 (EnemyAIController가 참조)</summary>
        public float FreezeTimeRemaining => flinchTimer;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();

            // ★ PlayerCombatFSM과 동일한 패턴: enabled + Controller 있는 Animator 우선 탐색
            //   루트에 비활성/컨트롤러 없는 Animator가 있으면 그걸 잡아서 트리거가 안 먹는 버그 방지
            foreach (var anim in GetComponentsInChildren<Animator>(true))
            {
                if (anim.enabled && anim.runtimeAnimatorController != null)
                {
                    animator = anim;
                    break;
                }
            }
            // 폴백: Controller 없어도 활성 Animator
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            // ★ 루트 모션 강제 비활성화 (메쉬 이탈 방지 런타임 안전장치)
            if (animator != null)
                animator.applyRootMotion = false;
        }

        /// <summary>
        /// HitReactionData 기반으로 리액션 실행.
        /// hitData: 공격자 위치, 접촉점 등 방향 계산에 사용.
        /// </summary>
        public void ApplyReaction(HitReactionData reaction, HitData hitData)
        {
            // ★ Knockdown 중 Flinch는 무시 (체공 중 경직 불가)
            if (knockdownActive && reaction.type == HitType.Flinch)
                return;

            // 방향 계산
            float knockDir = CalculateKnockDir(hitData);

            // 페이싱 적용
            ApplyFacing(reaction, hitData, knockDir);

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
        //  방향 & 페이싱
        // ═══════════════════════════════════════════

        private float CalculateKnockDir(HitData hitData)
        {
            float dir = hitData.KnockbackDirection.x;
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            return Mathf.Sign(dir);
        }

        private void ApplyFacing(HitReactionData reaction, HitData hitData, float knockDir)
        {
            float facingDir = 0f;

            switch (reaction.facing)
            {
                case HitFacing.Attacker:
                    // 공격자를 바라봄 = 공격자 방향으로 플립
                    float toAttacker = hitData.AttackerPosition.x - rb.position.x;
                    facingDir = Mathf.Approximately(toAttacker, 0f) ? -knockDir : Mathf.Sign(toAttacker);
                    break;

                case HitFacing.HitPoint:
                    // 타격 접촉 지점을 바라봄
                    float toHitPoint = hitData.ContactPoint.x - rb.position.x;
                    facingDir = Mathf.Approximately(toHitPoint, 0f) ? -knockDir : Mathf.Sign(toHitPoint);
                    break;

                case HitFacing.KnockDirection:
                    // 넉백 방향을 바라봄 (공격자 반대쪽)
                    facingDir = knockDir;
                    break;
            }

            if (Mathf.Approximately(facingDir, 0f)) return;

            // forceFlip=true면 무조건, false면 이미 맞는 방향이면 스킵
            if (reaction.forceFlip || !IsAlreadyFacing(facingDir))
            {
                FlipToFace(facingDir);
            }
        }

        private bool IsAlreadyFacing(float dir)
        {
            return Mathf.Sign(transform.localScale.x) == Mathf.Sign(dir);
        }

        private void FlipToFace(float dir)
        {
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
            transform.localScale = scale;
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

            float pushUnits = data.pushDistance * 0.01f;

            Vector2 pos = rb.position;
            pos.x += dir * pushUnits;
            rb.position = pos;

            // 경직 타이머 (재피격 시 리셋)
            flinchTimer = data.freezeTime;
            IsFlinchActive = true;

            PlayFlinchAnim();
        }

        // ═══════════════════════════════════════════
        //  Knockdown — sin 커브 체공
        // ═══════════════════════════════════════════

        private void ExecuteKnockdown(KnockdownData data, float dir)
        {
            if (rb == null) return;

            // 기존 Flinch 중단
            IsFlinchActive = false;
            flinchTimer = 0f;

            knockdownActive = true;
            knockdownTimer = 0f;
            knockdownAirTime = Mathf.Max(data.airTime, 0.1f);
            knockdownLaunchHeight = data.launchHeight * 0.01f;
            knockdownDistance = data.knockDistance * 0.01f;
            knockdownDir = dir;
            knockdownBaseY = rb.position.y;

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

            float y = knockdownBaseY + knockdownLaunchHeight * Mathf.Sin(Mathf.PI * t);
            float x = rb.position.x + knockdownDir * (knockdownDistance / knockdownAirTime) * Time.deltaTime;

            rb.position = new Vector2(x, y);

            if (t >= 1f)
            {
                knockdownActive = false;
                rb.position = new Vector2(rb.position.x, knockdownBaseY);
            }
        }
    }
}
