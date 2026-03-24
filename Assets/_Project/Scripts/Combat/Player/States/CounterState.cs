using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Counter 상태: 카운터/패리.
    /// 적의 🟡노란 인디케이터 공격에 타이밍 맞춰 L키로 반격.
    /// Perfect (±3f): 강화 반격 + 슬로우모션 연출 + 콤보 ×3
    /// Normal  (±8f): 일반 반격 + 콤보 ×2
    /// Miss:         카운터 실패 → 빈 동작 후 Idle 복귀
    ///
    /// Phase 2에서는 텔레그래프 적이 아직 구현 전이므로,
    /// 범위 내 아무 적이든 있으면 카운터가 성공하도록 간이 처리한다.
    /// Phase 3에서 ITelegraphable 연동 시 정식 타이밍 판정으로 교체.
    /// </summary>
    public class CounterState : CombatState
    {
        public override string StateName => "Counter";

        // ─── 프레임 데이터 ───
        private const int CounterWindowFrames = 10;
        private const int CounterStrikeFrames = 15;
        private const int RecoveryFrames = 10;
        private const int TotalFrames = CounterWindowFrames + CounterStrikeFrames + RecoveryFrames;
        private const int CancelWindowStart = CounterWindowFrames + CounterStrikeFrames + 4;

        // ─── 시각 피드백 ───
        private static readonly Color CounterReadyColor = new Color(1f, 1f, 0.2f, 0.8f);
        private static readonly Color PerfectCounterColor = new Color(1f, 0.5f, 0f, 1f);
        private static readonly Color NormalCounterColor = new Color(0.8f, 0.8f, 0.2f, 1f);
        private static readonly Color CounterMissColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        private const float ScalePulseDuration = 0.2f;

        // ─── 상태 변수 ───
        private enum Phase { Window, Strike, Recovery, Miss }
        private Phase currentPhase;
        private CounterType counterResult;
        private ICombatTarget counterTarget;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private float originalScaleX;    // Enter 시 기록 (방향 포함)
        private float stateElapsedTime;
        private float scalePulseTimer;   // 펄스 복원 타이머

        public override void Enter()
        {
            base.Enter();
            currentPhase = Phase.Window;
            counterResult = CounterType.Miss;
            counterTarget = null;
            scalePulseTimer = 0f;
            stateElapsedTime = 0f;

            // 스케일 기록 (방향 유지를 위해 부호 포함 x만 기록)
            originalScaleX = context.playerTransform.localScale.x;

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = CounterReadyColor;
            }

            SafeSetTrigger("Counter");


            // Phase 2 간이 판정
            TryResolveCounter();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            int frame = context.stateFrameCounter;
            stateElapsedTime += deltaTime;

            // 스케일 펄스 복원
            if (scalePulseTimer > 0f)
            {
                scalePulseTimer -= deltaTime;
                float t = Mathf.Clamp01(scalePulseTimer / ScalePulseDuration);
                float pulseScale = Mathf.Lerp(1f, 1.4f, t);
                Vector3 s = context.playerTransform.localScale;
                s.x = Mathf.Sign(s.x) * pulseScale;
                s.y = pulseScale;
                context.playerTransform.localScale = s;

                if (scalePulseTimer <= 0f)
                    RestoreScale();
            }

            switch (currentPhase)
            {
                case Phase.Window:
                    if (frame >= CounterWindowFrames)
                    {
                        if (counterResult != CounterType.Miss)
                            EnterCounterStrike();
                        else
                            EnterMiss();
                    }
                    break;

                case Phase.Strike:
                    // 타겟 방향으로 빠른 전진
                    if (counterTarget != null && counterTarget.IsTargetable)
                    {
                        Vector2 targetPos = counterTarget.GetTransform().position;
                        Vector2 playerPos = GetPos();
                        float dir = Mathf.Sign(targetPos.x - playerPos.x);
                        // Kinematic 돌진: rb.position 직접 이동
                        Vector2 pos = playerPos;
                        pos.x += dir * 10f * deltaTime;
                        MoveTo(pos);
                    }

                    if (frame >= CounterWindowFrames + CounterStrikeFrames)
                    {
                        currentPhase = Phase.Recovery;
                        if (spriteRenderer != null)
                            spriteRenderer.color = originalColor;
                    }
                    break;

                case Phase.Recovery:
                    // ★ 시간 기반 캔슬 (Inspector: context.counterCancelDelay)
                    if (!context.canCancel && stateElapsedTime >= context.counterCancelDelay)
                        context.canCancel = true;

                    if (context.canCancel && fsm.InputBuffer.HasInput)
                    {
                        var buffered = fsm.InputBuffer.Consume();
                        HandleBufferedInput(buffered);
                        return;
                    }
                    if (frame >= TotalFrames)
                        fsm.TransitionTo<IdleState>();
                    break;

                case Phase.Miss:
                    if (frame >= CounterWindowFrames + 10)
                        fsm.TransitionTo<IdleState>();
                    break;
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isInvulnerable = false;

            // 색상 복원
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // 스케일 확실히 복원 (방향 유지)
            RestoreScale();
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

        public override void OnHit(HitData hitData)
        {
            if (currentPhase == Phase.Window || currentPhase == Phase.Strike)
                return;
            base.OnHit(hitData);
        }

        // ─── 내부 로직 ───

        private void TryResolveCounter()
        {
            var selector = fsm.TargetSelector;
            Vector2 playerPos = GetPos();
            var target = selector.SelectTarget(playerPos, context.activeEnemies, 0f);

            if (target != null && target.IsTargetable)
            {
                counterTarget = target;
                counterResult = (context.globalFrameCounter % 4 == 0)
                    ? CounterType.Perfect
                    : CounterType.Normal;

            }
            else
            {
                counterResult = CounterType.Miss;

            }
        }

        private void EnterCounterStrike()
        {
            currentPhase = Phase.Strike;
            context.isInvulnerable = true;

            // 색상
            if (spriteRenderer != null)
            {
                spriteRenderer.color = counterResult == CounterType.Perfect
                    ? PerfectCounterColor
                    : NormalCounterColor;
            }

            // 타겟 방향 전환
            if (counterTarget != null)
            {
                float dir = Mathf.Sign(
                    counterTarget.GetTransform().position.x - GetPos().x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
                // 새 방향 기록
                originalScaleX = scale.x;

                counterTarget.InterruptAction();
            }

            // 이벤트
            CombatEventBus.Publish(new OnCounter
            {
                Type = counterResult,
                CounteredEnemy = counterTarget
            });

            // 콤보 + 헉슬리
            int comboBonus = counterResult == CounterType.Perfect ? 3 : 2;
            context.IncrementCombo(comboBonus);
            float chargeBonus = counterResult == CounterType.Perfect ? 15f : 10f;
            context.ChargeHuxley(chargeBonus);

            // 데미지
            if (counterTarget != null)
            {
                Vector2 attackerPos = GetPos();
                Vector2 targetPos = counterTarget.GetTransform().position;
                var hitData = HitData.CreateCounterAttack(
                    attackerPos, targetPos, context.comboCount,
                    counterResult == CounterType.Perfect);
                counterTarget.TakeHit(hitData);

                // 프리플로우: 직전 타격 적 기록
                fsm.TargetSelector.RegisterHit(counterTarget);
            }

            // 스케일 펄스 (타이머 기반으로 자동 복원)
            scalePulseTimer = ScalePulseDuration;

            SafeSetTrigger("CounterStrike");

        }

        private void EnterMiss()
        {
            currentPhase = Phase.Miss;
            if (spriteRenderer != null)
                spriteRenderer.color = CounterMissColor;

        }

        /// <summary>스케일을 원래 크기로 복원 (방향 유지)</summary>
        private void RestoreScale()
        {
            Vector3 s = context.playerTransform.localScale;
            s.x = Mathf.Sign(originalScaleX) * 1f;
            s.y = 1f;
            s.z = 1f;
            context.playerTransform.localScale = s;
            scalePulseTimer = 0f;
        }

        private void HandleBufferedInput(InputData input)
        {
            switch (input.Type)
            {
                case InputType.Attack:
                    fsm.TransitionTo<StrikeState>();
                    break;
                case InputType.Dodge:
                    fsm.TransitionTo<DodgeState>();
                    break;
                case InputType.Counter:
                    fsm.TransitionTo<CounterState>();
                    break;
                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        private void SafeSetTrigger(string triggerName)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetTrigger(triggerName); }
            catch (System.Exception e) { Debug.LogWarning($"[Counter] Animator 오류 무시: {e.Message}"); }
        }
    }
}
