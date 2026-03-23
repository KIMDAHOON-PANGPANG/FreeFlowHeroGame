using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Dodge 상태: 회피 대시.
    /// 입력 방향으로 빠르게 이동 + 12프레임 무적(i-frame).
    /// 회피 성공 시 콤보가 끊기지 않으며, Recovery 중 공격 입력으로 반격 가능.
    /// REPLACED 원작의 🔴빨간 인디케이터 대응 액션.
    /// </summary>
    public class DodgeState : CombatState
    {
        public override string StateName => "Dodge";

        // ─── 프레임 데이터 ───
        private const int StartupFrames = 2;                         // 극초반 선딜
        private const int IFrames = CombatConstants.DodgeIFrames;    // 12f (0.2초)
        private const int ActiveFrames = StartupFrames + IFrames;    // 14f
        private const int RecoveryFrames = 8;                        // 후딜
        private const int TotalFrames = ActiveFrames + RecoveryFrames; // 22f
        private const int CancelWindowStart = ActiveFrames + 3;      // 17f

        // ─── 이동 ───
        private const float DodgeDistance = 3.5f;   // 총 이동 거리 (유닛)

        // ─── 시각 피드백 ───
        private static readonly Color DodgeColor = new Color(0.3f, 1f, 0.3f, 0.6f); // 반투명 초록
        private static readonly Color DodgeAttackColor = Color.magenta;

        // ─── 상태 변수 ───
        private enum Phase { Startup, IFrame, Recovery }
        private Phase currentPhase;
        private Vector2 dodgeDirection;
        private float dodgeSpeed;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private bool dodgeAttackUsed;
        private float stateElapsedTime;

        public override void Enter()
        {
            base.Enter();
            currentPhase = Phase.Startup;
            dodgeAttackUsed = false;
            stateElapsedTime = 0f;

            // 무적 활성화
            context.isInvulnerable = true;

            // 방향 결정: 마지막 입력 방향 또는 현재 facing 반대편
            float inputX = context.lastInputDirection.x;

            // 방향이 없으면 현재 facing의 반대 (뒤로 구르기)
            if (Mathf.Abs(inputX) < 0.1f)
            {
                float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
                inputX = -facing; // 뒤로 회피
            }
            dodgeDirection = new Vector2(Mathf.Sign(inputX), 0f);

            // 이동 속도 계산 (총 거리 / IFrame 구간 시간)
            float iframeDuration = IFrames * CombatConstants.FrameDuration;
            dodgeSpeed = DodgeDistance / iframeDuration;

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = DodgeColor;
            }

            // 이벤트
            CombatEventBus.Publish(new OnDodge { Direction = dodgeDirection });

            // 애니메이션
            SafeSetTrigger("Dodge");

            Debug.Log($"[Dodge] Enter — direction: {dodgeDirection}");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            int frame = context.stateFrameCounter;
            stateElapsedTime += deltaTime;

            // Startup → IFrame 전환
            if (currentPhase == Phase.Startup && frame >= StartupFrames)
            {
                currentPhase = Phase.IFrame;
            }

            // IFrame 구간: 빠른 이동
            if (currentPhase == Phase.IFrame)
            {
                // Kinematic 회피: rb.position 직접 이동
                Vector2 pos = GetPos();
                pos += dodgeDirection * dodgeSpeed * deltaTime;
                MoveTo(pos);
            }

            // IFrame → Recovery 전환
            if (currentPhase == Phase.IFrame && frame >= ActiveFrames)
            {
                currentPhase = Phase.Recovery;
                context.isInvulnerable = false;

                // 색상을 원래대로
                if (spriteRenderer != null)
                    spriteRenderer.color = originalColor;
            }

            // ★ 시간 기반 캔슬 (Inspector: context.dodgeCancelDelay)
            if (!context.canCancel && stateElapsedTime >= context.dodgeCancelDelay)
            {
                context.canCancel = true;
            }

            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInput(buffered);
                return;
            }

            // 프레임 완료 → Idle
            if (frame >= TotalFrames)
            {
                fsm.TransitionTo<IdleState>();
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isInvulnerable = false;

            // 색상 복원
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // ★ 회피 종료 후 적과 겹침 해소
            // Dodge i-frame 중 적을 관통했을 수 있으므로,
            // 종료 시점에 겹침이 있으면 진입 방향(= 회피 반대쪽)으로 밀어냄
            ResolvePostDodgeOverlap();
        }

        /// <summary>
        /// 회피 종료 시 적 겹침 해소.
        /// 모든 겹침 적의 복합 바운드를 계산하고, 회피 진입 방향으로 캡슐을 밀어낸다.
        /// </summary>
        private void ResolvePostDodgeOverlap()
        {
            var capsule = context.playerRigidbody != null
                ? context.playerRigidbody.GetComponent<CapsuleCollider2D>()
                : null;
            if (capsule == null) return;

            int enemyMask = LayerMask.GetMask("Enemy");
            Vector2 origin = GetPos() + capsule.offset;

            var filter = new ContactFilter2D();
            filter.SetLayerMask(enemyMask);
            filter.useLayerMask = true;
            filter.useTriggers = false;

            var overlaps = new Collider2D[16];
            int count = Physics2D.OverlapCapsule(
                origin, capsule.size, capsule.direction, 0f, filter, overlaps);

            if (count == 0) return; // 겹침 없음

            // 복합 바운드
            float groupLeft = float.MaxValue;
            float groupRight = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (overlaps[i] == null) continue;
                groupLeft = Mathf.Min(groupLeft, overlaps[i].bounds.min.x);
                groupRight = Mathf.Max(groupRight, overlaps[i].bounds.max.x);
            }

            float halfW = capsule.size.x * 0.5f;
            const float pushBuffer = 0.08f; // 약간의 여유

            // 탈출 방향: 회피의 반대쪽 (왔던 곳으로 돌아감)
            // 이렇게 하면 Dodge가 적을 "뚫고 지나가는" 것이 아니라
            // "피하고 제자리로 복귀"하는 느낌이 됨
            float pushDir = -dodgeDirection.x;
            if (Mathf.Approximately(pushDir, 0f)) pushDir = -1f;

            float safeX;
            if (pushDir < 0f)
            {
                // 왼쪽으로 밀어냄: player.rightEdge가 groupLeft 바깥
                safeX = groupLeft - halfW - pushBuffer;
            }
            else
            {
                // 오른쪽으로 밀어냄: player.leftEdge가 groupRight 바깥
                safeX = groupRight + halfW + pushBuffer;
            }

            Vector2 safePos = GetPos();
            safePos.x = safeX;
            MoveTo(safePos);

            Debug.Log($"[Dodge] Post-dodge overlap resolved: pushed to X={safeX:F2} " +
                $"(dir={pushDir:F0}, group=[{groupLeft:F2},{groupRight:F2}])");
        }

        public override void HandleInput(InputData input)
        {
            if (!context.canCancel)
            {
                fsm.InputBuffer.BufferInput(input);
                return;
            }
            HandleBufferedInput(input);
        }

        /// <summary>회피 후속 입력 처리</summary>
        private void HandleBufferedInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Attack:
                    // 회피 후 반격 (DodgeAttack): 콤보 유지하면서 Strike
                    if (!dodgeAttackUsed)
                    {
                        dodgeAttackUsed = true;
                        // 회피 반격은 콤보 카운터 보너스 (+2)
                        context.IncrementCombo(2);
                        fsm.TransitionTo<StrikeState>();
                    }
                    break;

                case InputType.Dodge:
                    // 연속 회피
                    fsm.TransitionTo<DodgeState>();
                    break;

                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        public override void OnHit(HitData hitData)
        {
            // IFrame 중이면 회피 성공 → 피격 무시
            if (context.isInvulnerable) return;

            // Recovery 중 피격은 정상 처리
            base.OnHit(hitData);
        }

        /// <summary>애니메이터 안전 트리거</summary>
        private void SafeSetTrigger(string triggerName)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetTrigger(triggerName); }
            catch (System.Exception e) { Debug.LogWarning($"[Dodge] Animator 오류 무시: {e.Message}"); }
        }
    }
}
