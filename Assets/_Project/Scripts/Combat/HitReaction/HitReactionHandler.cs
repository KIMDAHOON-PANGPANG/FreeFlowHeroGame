using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 리액션 실행기. 적/플레이어 공용 MonoBehaviour.
    /// HitReactionData를 받아 Flinch(즉시 밀림+경직) 또는 Knockdown(애니메이션 루트모션 체공)을 실행한다.
    /// facing/forceFlip으로 피격 시 바라보는 방향을 제어한다.
    ///
    /// ★ Knockdown 중에는 IsKnockdownActive=true → AI/중력 시스템에서 이동을 스킵해야 함.
    /// ★ Knockdown 이동은 코드 드리븐 궤적이 전담 (AnimationCurve 포물선 Y + 선형 X).
    ///   루트모션은 전면 차단되고, rb.position을 Update()에서 직접 제어.
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
        private float knockdownAirTime;  // 넉다운 체공 시간 (종료 판정용)
        private float knockdownDir;      // 넉백 방향 (+1 또는 -1)
        private float knockdownBaseY;    // 넉다운 시작 Y (착지 보정용)
        private float knockdownLaunchHeight;  // 최대 Y 오프셋 (Unity 단위)
        private float knockdownKnockDistance; // 총 X 이동 거리 (Unity 단위)

        // ★ 데이터 튜닝: 넉다운 궤적 커브
        //   X축: 시간 비율 (0=시작, 1=착지), Y축: 높이 비율 (0=지면, 1=최고점)
        //   기본값: 빠르게 상승 → 천천히 낙하 (피크 t=0.35 지점)
        //   Inspector에서 커브 에디터로 자유롭게 조절 가능
        [SerializeField]
        private AnimationCurve knockdownArcCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f),      // 시작: 급상승
            new Keyframe(0.35f, 1f, 0f, 0f),    // 최고점: t=0.35 (비대칭 — 빠르게 올라감)
            new Keyframe(1f, 0f, -2f, 0f)        // 착지: 자연 낙하
        );

        /// <summary>넉다운 체공 중인지. true이면 외부 중력/AI 이동을 스킵할 것.</summary>
        public bool IsKnockdownActive => knockdownActive;

        /// <summary>넉다운 넉백 방향 (+1 또는 -1). RootMotionCanceller에서 참조.</summary>
        public float KnockdownDir => knockdownDir;

        /// <summary>경직(Flinch) 중인지</summary>
        public bool IsFlinchActive { get; private set; }
        private float flinchTimer;

        /// <summary>현재 freezeTime 잔여 (EnemyAIController가 참조)</summary>
        public float FreezeTimeRemaining => flinchTimer;

        /// <summary>마지막 Knockdown의 Down 지속 시간 (EnemyAIController가 참조)</summary>
        public float LastDownTime { get; private set; }

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

            // ★ applyRootMotion=true: Unity가 Hips 본에서 루트모션을 추출.
            //   넉다운 중: 루트모션 전면 차단. 궤적은 Update()에서 AnimationCurve 포물선으로 제어.
            //   그 외: RootMotionCanceller가 차단 → 제자리 재생.
            if (animator != null)
            {
                animator.applyRootMotion = true;
                // Animator가 자식 GO에 있으면 RootMotionCanceller 부착
                if (animator.gameObject != gameObject)
                {
                    if (animator.gameObject.GetComponent<RootMotionCanceller>() == null)
                        animator.gameObject.AddComponent<RootMotionCanceller>();
                }
            }
        }

        /// <summary>
        /// Animator가 같은 GO에 있을 때 루트모션 차단.
        /// 넉다운 궤적은 Update()에서 AnimationCurve로 제어.
        /// Animator가 자식에 있으면 RootMotionCanceller가 대신 차단.
        /// </summary>
        private void OnAnimatorMove()
        {
            // 넉다운 중: 궤적은 Update()에서 코드로 제어. 루트모션 전면 차단.
            // 그 외: 루트모션 차단 (아무것도 적용하지 않음 = 제자리 재생)
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

            float prevScaleX = transform.localScale.x;

            // forceFlip=true면 무조건, false면 이미 맞는 방향이면 스킵
            if (reaction.forceFlip || !IsAlreadyFacing(facingDir))
            {
                FlipToFace(facingDir);
            }

            Debug.Log($"[HitReaction][Facing][{gameObject.name}] " +
                $"mode={reaction.facing} facingDir={facingDir:F1} " +
                $"prevScaleX={prevScaleX:F2} → newScaleX={transform.localScale.x:F2} " +
                $"forceFlip={reaction.forceFlip} knockDir={knockDir:F1} " +
                $"attackerX={hitData.AttackerPosition.x:F2} myX={rb.position.x:F2}");
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
        //  Knockdown — 애니메이션 루트모션 체공
        // ═══════════════════════════════════════════

        private void ExecuteKnockdown(KnockdownData data, float dir)
        {
            if (rb == null) return;

            // 기존 Flinch 중단
            IsFlinchActive = false;
            flinchTimer = 0f;

            knockdownActive = true;
            knockdownTimer = 0f;
            LastDownTime = data.downTime;
            knockdownAirTime = Mathf.Max(data.airTime, 0.1f);
            knockdownDir = dir;
            knockdownBaseY = rb.position.y;
            knockdownLaunchHeight = data.launchHeight * 0.01f;  // cm → Unity 단위
            knockdownKnockDistance = data.knockDistance * 0.01f; // cm → Unity 단위

            Debug.Log($"[Knockdown][START][{gameObject.name}] baseY={knockdownBaseY:F3} " +
                $"airTime={knockdownAirTime:F3} dir={knockdownDir:F1} " +
                $"rb.pos={rb.position} applyRoot={animator?.applyRootMotion}");

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

            // ── Knockdown 궤적 (코드 드리븐) ──
            // ★ AnimationCurve로 Y 궤적, 선형으로 X 이동. 루트모션은 차단됨.
            if (!knockdownActive) return;

            knockdownTimer += Time.deltaTime;
            float t = Mathf.Clamp01(knockdownTimer / knockdownAirTime);

            // Y: AnimationCurve로 비대칭 포물선 (빠르게 상승 → 천천히 낙하)
            float arcY = knockdownBaseY + knockdownLaunchHeight * knockdownArcCurve.Evaluate(t);

            // X: knockDistance를 airTime에 걸쳐 균등 분배
            float xSpeed = knockdownKnockDistance / knockdownAirTime;
            float xDelta = xSpeed * Time.deltaTime * knockdownDir;

            rb.position = new Vector2(rb.position.x + xDelta, arcY);

            if (t >= 1f)
            {
                knockdownActive = false;
                rb.position = new Vector2(rb.position.x, knockdownBaseY);
            }
        }

        private void LateUpdate()
        {
            if (animator == null) return;
            var meshT = animator.transform;
            if (meshT == transform) return; // Animator가 루트 자체면 스킵

            // ★ 메쉬 컨테이너를 항상 루트 위치(0,0,0)에 고정
            // 루트모션 deltaPosition은 rb.position에 적용됨 (OnAnimatorMove/RootMotionCanceller).
            // meshT는 rb 자식이므로 localPosition=zero면 rb와 동일 위치.
            meshT.localPosition = Vector3.zero;
        }
    }
}
