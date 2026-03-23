using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Warp 상태: 프리플로우 워핑.
    /// 타겟에게 매우 빠르게 보간 이동한 뒤 즉시 Strike로 전환한다.
    /// EXPO EASE_OUT 커브로 처음에 빠르고 끝에 감속하는 연출.
    /// </summary>
    public class WarpState : CombatState
    {
        public override string StateName => "Warp";

        // ─── 워핑 파라미터 ───
        private Vector2 startPos;
        private Vector2 endPos;
        private float warpTimer;
        private float warpDuration;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private static readonly Color WarpColor = new Color(0.5f, 0.8f, 1f, 0.7f); // 반투명 시안

        public override void Enter()
        {
            base.Enter();

            context.isWarping = true;
            context.isInvulnerable = true; // 워핑 중 무적

            var selector = fsm.TargetSelector;
            var target = selector.CurrentTarget;

            if (target == null || !target.IsTargetable)
            {
                // 타겟 없으면 즉시 Idle 복귀
                fsm.TransitionTo<IdleState>();
                return;
            }

            startPos = GetPos();
            endPos = selector.GetWarpDestination(startPos, target);

            // 거리에 따라 워핑 시간 조절 (가까우면 더 빠르게)
            float distance = Vector2.Distance(startPos, endPos);
            warpDuration = Mathf.Lerp(0.06f, CombatConstants.WarpDuration, distance / CombatConstants.MaxWarpDistance);
            warpDuration = Mathf.Max(warpDuration, 0.04f); // 최소 0.04초
            warpTimer = 0f;

            // 방향 전환 (타겟을 향해)
            float dir = Mathf.Sign(endPos.x - startPos.x);
            Vector3 scale = context.playerTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
            context.playerTransform.localScale = scale;

            // 시각 피드백: 반투명 + 색상 변경
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = WarpColor;
            }

            Debug.Log($"[Warp] Enter — {startPos} → {endPos} (duration: {warpDuration:F3}s)");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            warpTimer += deltaTime;
            float t = Mathf.Clamp01(warpTimer / warpDuration);

            // Expo EaseOut 커브: 빠르게 출발, 천천히 도착
            float eased = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-out

            // Kinematic 워핑: rb.position 직접 설정 (MovePosition 사용 금지)
            Vector2 newPos = Vector2.Lerp(startPos, endPos, eased);
            MoveTo(newPos);

            // 워핑 완료
            if (t >= 1f)
            {
                // 즉시 Strike로 전환
                fsm.TransitionTo<StrikeState>();
            }
        }

        public override void Exit()
        {
            base.Exit();

            context.isWarping = false;
            context.isInvulnerable = false;

            // 색상 복원
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }

        public override void HandleInput(InputData input)
        {
            // 워핑 중 입력은 버퍼에 저장
            fsm.InputBuffer.BufferInput(input);
        }

        public override void OnHit(HitData hitData)
        {
            // 워핑 중 무적이므로 피격 무시
        }
    }
}
