using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Guard 상태: 방어 자세.
    /// Heavy(우클릭) 입력으로 진입, N초간 가드 포즈 유지.
    /// 가드 중 적 공격 피격 시 자동 블록 + 블로킹 넉백 + 반격 모션 + 적 경직.
    ///
    /// ★ isInvulnerable = false 유지: 적 AI의 히트 판정이 도달해야 OnHit이 호출됨
    ///   OnHit 내부에서 데미지를 무시하고 반격 처리
    ///
    /// ★ 데이터 튜닝: context.guardDuration (Inspector에서 조절)
    /// </summary>
    public class GuardState : CombatState
    {
        public override string StateName => isCountering ? "GuardCounter" : "Guard";

        // ─── 페이즈 ───
        private enum Phase { Guard, Counter, Recovery }
        private Phase currentPhase;

        // ─── 타이머 ───
        private float guardTimer;         // 가드 잔여 시간
        private float counterTimer;       // 반격 모션 타이머
        private float stateElapsedTime;

        // ─── 반격 프레임 데이터 ───
        // ★ 데이터 튜닝: 반격 모션 프레임 수
        private const int CounterStrikeFrames = 15;
        private const int CounterRecoveryFrames = 10;

        // ─── 블로킹 넉백 ───
        // ★ 데이터 튜닝: 블로킹 시 밀리는 거리/시간
        private const float BlockKnockbackDistance = 0.4f;  // 유닛
        private const float BlockKnockbackDuration = 0.1f;  // 초
        private float knockbackTimer;
        private Vector2 knockbackDir;

        // ─── 상태 변수 ───
        private bool isCountering;        // 반격 모션 진행 중
        private ICombatTarget counterTarget; // 반격 대상 (공격해 온 적)

        // ─── 시각 피드백 ───
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private static readonly Color GuardColor = new Color(0.4f, 0.6f, 1f, 0.8f);   // 파란빛
        private static readonly Color CounterColor = new Color(1f, 0.8f, 0.2f, 1f);    // 노란빛
        private static readonly Color BlockFlashColor = new Color(0.8f, 0.9f, 1f, 1f); // 블로킹 플래시

        public override void Enter()
        {
            base.Enter();
            currentPhase = Phase.Guard;
            guardTimer = context.guardDuration;
            counterTimer = 0f;
            stateElapsedTime = 0f;
            isCountering = false;
            counterTarget = null;
            knockbackTimer = 0f;

            // ★ isInvulnerable = false: 적 AI 히트 판정이 통과해야 OnHit 호출됨
            //   데미지 무시는 OnHit 내부에서 HitState 전환을 차단하는 방식으로 처리
            context.isInvulnerable = false;
            context.canCancel = false;

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = GuardColor;
            }

            // 가드 애니메이션
            SafeSetTrigger("Guard");

            Debug.Log("[Guard] Enter — 가드 자세 진입");
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            stateElapsedTime += deltaTime;

            // ─── 블로킹 넉백 보간 ───
            if (knockbackTimer > 0f)
            {
                knockbackTimer -= deltaTime;
                float speed = BlockKnockbackDistance / BlockKnockbackDuration;
                MoveHorizontal(knockbackDir.x * speed * deltaTime);
            }

            switch (currentPhase)
            {
                case Phase.Guard:
                    guardTimer -= deltaTime;
                    if (guardTimer <= 0f)
                    {
                        // 가드 시간 종료 → Idle
                        fsm.TransitionTo<IdleState>();
                    }
                    break;

                case Phase.Counter:
                    counterTimer += deltaTime;
                    int counterFrame = Mathf.FloorToInt(counterTimer / CombatConstants.FrameDuration);

                    // 반격 타격 시점 (프레임 중반)
                    if (counterFrame >= CounterStrikeFrames / 2 && counterTarget != null
                        && counterTarget.IsTargetable)
                    {
                        ApplyCounterHit();
                        counterTarget = null; // 1회만 적용
                    }

                    // 반격 모션 완료 → 가드 잔여 시간이 있으면 Guard, 없으면 Recovery
                    if (counterFrame >= CounterStrikeFrames)
                    {
                        isCountering = false;
                        if (guardTimer > 0f)
                        {
                            currentPhase = Phase.Guard;
                            if (spriteRenderer != null)
                                spriteRenderer.color = GuardColor;
                            SafeSetTrigger("Guard");
                        }
                        else
                        {
                            currentPhase = Phase.Recovery;
                            context.canCancel = true;
                            if (spriteRenderer != null)
                                spriteRenderer.color = originalColor;
                        }
                    }
                    break;

                case Phase.Recovery:
                    // 버퍼 입력 처리
                    if (fsm.InputBuffer.HasInput)
                    {
                        var buffered = fsm.InputBuffer.Consume();
                        HandleBufferedInput(buffered);
                        return;
                    }

                    if (context.stateFrameCounter >= CounterStrikeFrames + CounterRecoveryFrames + 30)
                    {
                        fsm.TransitionTo<IdleState>();
                    }
                    break;
            }
        }

        public override void Exit()
        {
            base.Exit();
            context.isInvulnerable = false;
            isCountering = false;
            knockbackTimer = 0f;

            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }

        /// <summary>
        /// 가드 중 피격: 데미지 무시 + 블로킹 넉백 + 자동 반격 발동.
        /// 반격 모션 중 추가 피격은 무시.
        /// ★ base.OnHit()을 호출하지 않아 HitState로 전환되지 않음 = 데미지 무시
        /// </summary>
        public override void OnHit(HitData hitData)
        {
            Debug.Log($"[Guard] OnHit — phase:{currentPhase} isCountering:{isCountering} " +
                $"attacker:{hitData.AttackerPosition} damage:{hitData.BaseDamage}");

            // 반격 모션 중이면 추가 피격 무시
            if (isCountering) return;

            // 가드 상태가 아니면 기본 처리 (HitState로 전환)
            if (currentPhase != Phase.Guard)
            {
                base.OnHit(hitData);
                return;
            }

            // ─── 블로킹 넉백 (피격 방향으로 살짝 밀림) ───
            knockbackDir = -hitData.KnockbackDirection; // 공격 반대 방향으로 밀림
            knockbackTimer = BlockKnockbackDuration;

            // 블로킹 플래시 (잠깐 하얗게)
            if (context.hitFlash != null)
                context.hitFlash.Flash(BlockFlashColor, 0.08f);

            Debug.Log($"[Guard] BLOCK — 블로킹 성공! 넉백:{knockbackDir} → 반격 발동");

            // ─── 자동 반격 발동 ───
            isCountering = true;
            currentPhase = Phase.Counter;
            counterTimer = 0f;

            // 반격 중 무적 (추가 피격 방지)
            context.isInvulnerable = true;

            // 공격자 찾기 (AttackerPosition으로 가장 가까운 적 특정)
            counterTarget = FindAttacker(hitData.AttackerPosition);

            // 공격자 방향 전환
            if (counterTarget != null)
            {
                float dir = Mathf.Sign(
                    counterTarget.GetTransform().position.x - GetPos().x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;

                // 적 행동 중단
                counterTarget.InterruptAction();
            }

            // 시각 피드백
            if (spriteRenderer != null)
                spriteRenderer.color = CounterColor;

            // 반격 애니메이션 (spinning elbow 재사용)
            SafeSetTrigger("GuardCounter");
        }

        public override void HandleInput(InputData input)
        {
            if (!context.canCancel)
            {
                // 가드/반격 중에는 버퍼에 저장
                fsm.InputBuffer.BufferInput(input);
                return;
            }
            HandleBufferedInput(input);
        }

        // ─── 내부 로직 ───

        /// <summary>반격 데미지 적용</summary>
        private void ApplyCounterHit()
        {
            if (counterTarget == null || !counterTarget.IsTargetable) return;

            Vector2 attackerPos = GetPos();
            Vector2 targetPos = counterTarget.GetTransform().position;
            var hitData = HitData.CreateGuardCounter(
                attackerPos, targetPos, context.comboCount);
            counterTarget.TakeHit(hitData);

            Debug.Log($"[Guard] COUNTER HIT — 반격 적중! target:{counterTarget.GetTransform().name}");

            // 콤보 + 헉슬리 보너스
            context.IncrementCombo(CombatConstants.GuardCounterComboBonus);
            context.ChargeHuxley(CombatConstants.GuardCounterHuxleyCharge);

            // 히트 기록
            fsm.TargetSelector.RegisterHit(counterTarget);
        }

        /// <summary>공격자 위치로 가장 가까운 적 특정</summary>
        private ICombatTarget FindAttacker(Vector2 attackerPos)
        {
            ICombatTarget closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < context.activeEnemies.Count; i++)
            {
                var enemy = context.activeEnemies[i];
                if (enemy == null || !enemy.IsTargetable) continue;

                float dist = Vector2.Distance(
                    (Vector2)enemy.GetTransform().position, attackerPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = enemy;
                }
            }
            return closest;
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
                case InputType.Heavy:
                    fsm.TransitionTo<GuardState>();
                    break;
                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        /// <summary>Animator 트리거 안전 설정</summary>
        private void SafeSetTrigger(string triggerName)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetTrigger(triggerName); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Guard] Animator 오류 무시: {e.Message}");
            }
        }
    }
}
