using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.HitReaction;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Dodge 상태: 회피 대시.
    /// ★ 애니메이션 클립 기반 이동: Dodge_B 루트모션이 캐릭터를 이동시킨다.
    ///   - 클립 재생 시작 = 이동 시작
    ///   - 클립 재생 종료 = 상태 종료
    ///   - 이동 거리/속도는 클립 루트모션이 결정 (하드코딩 없음)
    ///
    /// REPLACED 원작의 🔴빨간 인디케이터 대응 액션.
    /// </summary>
    public class DodgeState : CombatState
    {
        public override string StateName => "Dodge";

        // ─── 상태 변수 ───
        private Vector2 dodgeDirection;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private bool dodgeAttackUsed;
        private float stateElapsedTime;
        private RootMotionCanceller rootMotionCanceller;
        private bool animStarted;
        private bool isForwardDodge;       // true: 전방 대시, false: 백대시
        private string animStateName;      // "Dodge" 또는 "DodgeForward"

        // ─── 시각 피드백 ───
        private static readonly Color DodgeColor = new Color(0.3f, 1f, 0.3f, 0.6f);

        public override void Enter()
        {
            base.Enter();
            dodgeAttackUsed = false;
            stateElapsedTime = 0f;
            animStarted = false;

            // 무적 활성화
            context.isInvulnerable = true;

            // 방향 결정 + 전방/후방 판별
            float inputX = context.lastInputDirection.x;
            float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

            if (Mathf.Abs(inputX) < 0.1f)
            {
                // 방향 입력 없음 → 뒤로 회피
                inputX = -facing;
                isForwardDodge = false;
            }
            else
            {
                // 입력 방향이 facing과 같으면 전방, 반대면 후방
                isForwardDodge = Mathf.Sign(inputX) == Mathf.Sign(facing);
            }
            dodgeDirection = new Vector2(Mathf.Sign(inputX), 0f);
            animStateName = isForwardDodge ? "DodgeForward" : "Dodge";

            // 루트모션 활성화 → RootMotionCanceller가 클립의 delta를 rb에 적용
            rootMotionCanceller = context.playerAnimator.GetComponent<RootMotionCanceller>();
            if (rootMotionCanceller != null)
                rootMotionCanceller.UseRootMotion = true;

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = DodgeColor;
            }

            // 이벤트
            CombatEventBus.Publish(new OnDodge { Direction = dodgeDirection });

            // 애니메이션 트리거: 전방/후방에 따라 다른 클립
            SafeSetTrigger(animStateName);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            stateElapsedTime += deltaTime;

            // Animator가 대시 상태에 진입했는지 추적
            var stateInfo = context.playerAnimator.GetCurrentAnimatorStateInfo(0);
            bool isInDodgeAnim = stateInfo.IsName(animStateName);

            // [DEBUG] 매 프레임 상태 추적
            if (!animStarted || !context.canCancel)
            {
                Debug.Log($"[Dodge][DEBUG] elapsed={stateElapsedTime:F3} animStarted={animStarted} " +
                    $"IsName({animStateName})={isInDodgeAnim} " +
                    $"nTime={stateInfo.normalizedTime:F3} " +
                    $"canCancel={context.canCancel} " +
                    $"curState={stateInfo.shortNameHash}");
            }

            if (!animStarted && isInDodgeAnim)
            {
                animStarted = true;
                Debug.Log($"[Dodge] animStarted = true (nTime={stateInfo.normalizedTime:F3})");
            }

            // ★ 캔슬 윈도우 — normalizedTime 비율 기반
            if (!context.canCancel && animStarted
                && stateInfo.normalizedTime >= context.dodgeCancelDelay)
            {
                context.canCancel = true;
                context.isInvulnerable = false;
                Debug.Log($"[Dodge] ★ 캔슬 OPEN (nTime={stateInfo.normalizedTime:F3} >= {context.dodgeCancelDelay})");

                // 색상 복원
                if (spriteRenderer != null)
                    spriteRenderer.color = originalColor;
            }

            // ★ 이동 캔슬: 캔슬 윈도우 중 방향키 입력 → 즉시 Idle(→Locomotion)
            if (context.canCancel && context.lastInputDirection.sqrMagnitude > 0.01f)
            {
                Debug.Log($"[Dodge] 이동 캔슬 → Idle (dir={context.lastInputDirection})");
                fsm.TransitionTo<IdleState>();
                return;
            }

            // 캔슬 입력 처리 (공격 등 버퍼)
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInput(buffered);
                return;
            }

            // ★ 애니메이션 완료 → 상태 종료
            if (animStarted)
            {
                bool clipDone = isInDodgeAnim && stateInfo.normalizedTime >= 0.95f;
                bool transitioned = !isInDodgeAnim;

                if (clipDone || transitioned)
                {
                    Debug.Log($"[Dodge] 종료 → Idle (clipDone={clipDone} transitioned={transitioned} nTime={stateInfo.normalizedTime:F3})");
                    fsm.TransitionTo<IdleState>();
                }
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isInvulnerable = false;

            // 루트모션 비활성화
            if (rootMotionCanceller != null)
            {
                rootMotionCanceller.UseRootMotion = false;
                rootMotionCanceller = null;
            }

            // 색상 복원
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }

        public override void HandleInput(InputData input)
        {
            if (!context.canCancel)
            {
                // ★ 닷지 중 재닷지 입력은 버퍼링하지 않음 → 광클해도 1회만 실행
                //   공격 입력만 버퍼링 (닷지 후 반격용)
                if (input.Type != InputType.Dodge)
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
