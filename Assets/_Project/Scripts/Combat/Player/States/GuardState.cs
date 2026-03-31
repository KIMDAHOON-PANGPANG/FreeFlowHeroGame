using UnityEngine;
using FreeFlowHero.Combat.Core;
namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Guard 상태: 방어 자세.
    /// Heavy(우클릭/RB) 입력으로 진입, N초간 가드 포즈 유지.
    ///
    /// ★ Phase 6 개편:
    ///   1) 가드 워프: 진입 시 텔레그래프 중인 근접 적에게 워핑
    ///   2) 타이밍 패리: GUARD_SUCCESS 노티파이 윈도우 내 피격 → 퍼펙트 가드(반격 발동)
    ///      윈도우 밖 피격 → 노멀 블록(감소 데미지, 반격 없음)
    ///   3) 카운터 변형: BattleSettings.guardCounterMotions 가중 랜덤 선택
    ///
    /// ★ isInvulnerable = false 유지: 적 AI의 히트 판정이 도달해야 OnHit이 호출됨
    ///   OnHit 내부에서 데미지를 무시하거나 감소 처리
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
        private string counterActionId = "GuardCounter"; // 선택된 카운터 액션 ID

        // ─── 가드 노티파이 프로세서 (GUARD_SUCCESS 윈도우 판정) ───
        private ActionNotifyProcessor guardNotifyProcessor;
        private int guardFrameCounter;    // 가드 액션 프레임 카운터

        // ─── 카운터 노티파이 프로세서 (COLLISION 윈도우 판정) ───
        private ActionNotifyProcessor counterNotifyProcessor;
        private int counterFrameCounter;

        // ─── 가드 워프 ───
        // ★ 데이터 튜닝: CombatConstants.GuardWarpOffsetX, GuardWarpMaxRange
        private bool guardWarpActive;
        private Vector2 guardWarpStartPos;
        private Vector2 guardWarpEndPos;
        private float guardWarpTimer;
        private const float GuardWarpDuration = 0.1f; // 빠른 워프 (초)
        private ICombatTarget guardWarpTarget;

        // ─── 시각 피드백 ───
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private static readonly Color GuardColor = new Color(0.4f, 0.6f, 1f, 0.8f);   // 파란빛
        private static readonly Color CounterColor = new Color(1f, 0.8f, 0.2f, 1f);    // 노란빛
        private static readonly Color BlockFlashColor = new Color(0.8f, 0.9f, 1f, 1f); // 블로킹 플래시
        private static readonly Color PerfectGuardColor = new Color(1f, 1f, 0.5f, 1f); // 퍼펙트 가드 플래시

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
            guardFrameCounter = 0;

            // ★ isInvulnerable = false: 적 AI 히트 판정이 통과해야 OnHit 호출됨
            //   데미지 무시/감소는 OnHit 내부에서 처리
            context.isInvulnerable = false;
            context.canCancel = false;

            // ─── [A] 가드 액션 노티파이 프로세서 셋업 ───
            guardNotifyProcessor = null;
            var guardAction = ActionTableManager.Instance?.GetAction("PC_Hero", "Guard");
            if (guardAction != null && guardAction.HasNotifies)
            {
                guardNotifyProcessor = new ActionNotifyProcessor(guardAction);
            }

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = GuardColor;
            }

            // 가드 애니메이션
            SafeSetTrigger("Guard");

            // ─── [B] 가드 워프: 텔레그래프 중인 근접 적에게 워핑 ───
            guardWarpActive = false;
            guardWarpTarget = null;
            ICombatTarget warpCandidate = FindTelegraphingMeleeEnemy();
            if (warpCandidate != null)
            {
                guardWarpTarget = warpCandidate;
                Vector2 targetPos = (Vector2)warpCandidate.GetTransform().position;
                Vector2 myPos = GetPos();

                // 적 기준 반대편(플레이어 쪽)에 도착
                float facingDir = Mathf.Sign(targetPos.x - myPos.x);
                Vector2 destination = targetPos + new Vector2(
                    CombatConstants.GuardWarpOffsetX * facingDir, 0f);

                guardWarpStartPos = myPos;
                guardWarpEndPos = destination;
                guardWarpTimer = 0f;
                guardWarpActive = true;
                context.isWarping = true;

                // 적 방향으로 전환
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (facingDir >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;

                Debug.Log($"[Guard] WARP — 텔레그래프 적 {warpCandidate.GetTransform().name}에게 워핑");
            }

            Debug.Log("[Guard] Enter — 가드 자세 진입" +
                (guardNotifyProcessor != null ? " (노티파이 모드)" : " (레거시 모드)"));
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            stateElapsedTime += deltaTime;

            // ─── [B] 가드 워프 보간 ───
            if (guardWarpActive)
            {
                guardWarpTimer += deltaTime;
                float t = Mathf.Clamp01(guardWarpTimer / GuardWarpDuration);

                // CubicOut 이징: 1 - (1-t)^3
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                Vector2 pos = Vector2.Lerp(guardWarpStartPos, guardWarpEndPos, eased);
                MoveTo(pos);

                if (t >= 1f)
                {
                    guardWarpActive = false;
                    context.isWarping = false;
                }
            }

            // ─── 블로킹 넉백 보간 ───
            if (knockbackTimer > 0f)
            {
                knockbackTimer -= deltaTime;
                float speed = BlockKnockbackDistance / BlockKnockbackDuration;
                MoveHorizontal(knockbackDir.x * speed * deltaTime);
            }

            // ─── [E] 가드 노티파이 프로세서 틱 ───
            if (guardNotifyProcessor != null && currentPhase == Phase.Guard)
            {
                guardFrameCounter = context.stateFrameCounter;
                guardNotifyProcessor.Tick(guardFrameCounter);
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

                    // ─── 카운터 노티파이 프로세서 틱 ───
                    counterFrameCounter = counterFrame;
                    counterNotifyProcessor?.Tick(counterFrameCounter);

                    // ─── ROOT_MOTION 노티파이 기반 이동 (FBX 루트모션 대체) ───
                    if (counterNotifyProcessor != null
                        && counterNotifyProcessor.IsRootMotionActive
                        && Mathf.Abs(counterNotifyProcessor.RootMotionSpeed) > 0.01f)
                    {
                        float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
                        MoveHorizontal(facing * counterNotifyProcessor.RootMotionSpeed * deltaTime);
                    }

                    // ─── 반격 타격 판정: 노티파이 COLLISION 기반 ───
                    if (counterNotifyProcessor != null)
                    {
                        // 노티파이 모드: COLLISION 윈도우 진입 시 히트
                        if (counterNotifyProcessor.CollisionJustStarted
                            && counterTarget != null && counterTarget.IsTargetable)
                        {
                            ApplyCounterHit();
                            counterTarget = null; // 1회만 적용
                        }
                    }
                    else
                    {
                        // 레거시 폴백: 하드코딩 프레임 중반
                        if (counterFrame >= CounterStrikeFrames / 2
                            && counterTarget != null && counterTarget.IsTargetable)
                        {
                            ApplyCounterHit();
                            counterTarget = null;
                        }
                    }

                    // ─── 반격 모션 완료 → Recovery ───
                    int totalCounterFrames = counterNotifyProcessor != null
                        ? counterNotifyProcessor.TotalFrames
                        : CounterStrikeFrames;
                    if (counterFrame >= totalCounterFrames)
                    {
                        isCountering = false;
                        currentPhase = Phase.Recovery;
                        context.canCancel = true;
                        if (spriteRenderer != null)
                            spriteRenderer.color = originalColor;
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
            guardNotifyProcessor = null;
            counterNotifyProcessor = null;

            // 워프 중 상태 종료 시 정리
            if (guardWarpActive)
            {
                guardWarpActive = false;
                context.isWarping = false;
            }

            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }

        /// <summary>
        /// 가드 중 피격: 타이밍 기반 패리 시스템.
        ///   - GUARD_SUCCESS 윈도우 내 피격 → 퍼펙트 가드: 데미지 0 + 반격 발동
        ///   - GUARD_SUCCESS 윈도우 밖 피격 → 노멀 블록: 감소 데미지 + 넉백 (반격 없음)
        ///   - 노티파이 없으면 레거시 동작 (항상 퍼펙트 가드)
        /// 반격 모션 중 추가 피격은 무시.
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

            // ─── [C] 타이밍 패리 판정 ───
            bool isPerfectGuard = false;
            if (guardNotifyProcessor != null)
            {
                // 노티파이 모드: GUARD_SUCCESS 윈도우 체크
                isPerfectGuard = guardNotifyProcessor.IsGuardSuccessActive;
            }
            else
            {
                // 레거시 모드 (노티파이 없음): 항상 퍼펙트 가드 (기존 동작 유지)
                isPerfectGuard = true;
            }

            // ─── 공통: 블로킹 넉백 ───
            knockbackDir = -hitData.KnockbackDirection;
            knockbackTimer = BlockKnockbackDuration;

            if (isPerfectGuard)
            {
                // ════ 퍼펙트 가드: 100% 데미지 차단 + 반격 발동 ════
                if (context.hitFlash != null)
                    context.hitFlash.Play(PerfectGuardColor);

                Debug.Log($"[Guard] PERFECT GUARD — 퍼펙트 가드! 데미지 0 → 반격 발동 (frame:{guardFrameCounter})");

                // ─── 반격 발동 ───
                isCountering = true;
                currentPhase = Phase.Counter;
                counterTimer = 0f;
                context.isInvulnerable = true;

                counterTarget = FindAttacker(hitData.AttackerPosition);

                // 공격자 방향 전환
                if (counterTarget != null)
                {
                    float dir = Mathf.Sign(
                        counterTarget.GetTransform().position.x - GetPos().x);
                    Vector3 scale = context.playerTransform.localScale;
                    scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                    context.playerTransform.localScale = scale;

                    counterTarget.InterruptAction();
                }

                if (spriteRenderer != null)
                    spriteRenderer.color = CounterColor;

                // ─── [D] 카운터 변형: 가중 랜덤 선택 ───
                counterActionId = "GuardCounter"; // 폴백
                if (BattleSettings.IsLoaded && BattleSettings.Instance.guardCounterMotions != null
                    && BattleSettings.Instance.guardCounterMotions.Length > 0)
                {
                    string selected = BattleSettings.SelectWeightedRandom(
                        BattleSettings.Instance.guardCounterMotions);
                    if (!string.IsNullOrEmpty(selected))
                        counterActionId = selected;
                }

                // ─── 카운터 노티파이 프로세서 셋업 ───
                counterNotifyProcessor = null;
                counterFrameCounter = 0;
                var counterAction = ActionTableManager.Instance?.GetAction("PC_Hero", counterActionId);
                if (counterAction != null && counterAction.HasNotifies)
                {
                    counterNotifyProcessor = new ActionNotifyProcessor(counterAction);
                }

                // 카운터 인덱스 추출 (예: "GuardCounter2" → index 1)
                int counterIndex = ExtractCounterIndex(counterActionId);
                SafeSetInteger("GuardCounterIndex", counterIndex);

                Debug.Log($"[Guard] COUNTER — 선택: {counterActionId} (index:{counterIndex})" +
                    (counterNotifyProcessor != null ? " (노티파이 모드)" : " (레거시 모드)"));

                // ★ FBX 루트모션은 사용하지 않음 (EEJANAI 클립은 Bake Into Pose 미설정)
                //   대신 ROOT_MOTION 노티파이로 코드 제어 이동 사용.
                //   액션 테이블에서 GuardCounter에 ROOT_MOTION 노티파이 추가 필요.

                // 반격 애니메이션: CrossFade로 직접 상태 전환 (트리거보다 안정적)
                string counterStateName = counterIndex == 0 ? "GuardCounter" : $"GuardCounter{counterIndex + 1}";
                context.playerAnimator.CrossFadeInFixedTime(counterStateName, 0.05f);
            }
            else
            {
                // ════ 노멀 블록: 감소 데미지 + 넉백만 (반격 없음) ════
                float reducedDamage = hitData.BaseDamage *
                    (1f - CombatConstants.GuardNormalBlockDamageReduction);

                if (context.hitFlash != null)
                    context.hitFlash.Play(BlockFlashColor);

                Debug.Log($"[Guard] NORMAL BLOCK — 노멀 블록! 감소 데미지:{reducedDamage:F1} " +
                    $"(원본:{hitData.BaseDamage:F1}, 감소율:{CombatConstants.GuardNormalBlockDamageReduction:P0}) " +
                    $"(frame:{guardFrameCounter})");

                // TODO: 플레이어 HP 시스템 구현 후 실제 데미지 적용
                // context.ApplyDamage(reducedDamage);
            }
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

            // ★ 카운터 액션의 COLLISION notify에서 groggyType 가져오기
            var counterAction = ActionTableManager.Instance?.GetAction("PC_Hero", counterActionId);
            if (counterAction?.notifies != null)
            {
                foreach (var n in counterAction.notifies)
                {
                    if (!n.disabled && n.TypeEnum == NotifyType.COLLISION && n.groggyType > 0)
                    {
                        hitData.GroggyType = n.groggyType;
                        break;
                    }
                }
            }

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

        /// <summary>
        /// 텔레그래프 중인 근접 적 검색.
        /// 가드 워프 대상을 찾는다 (GuardWarpMaxRange 이내).
        /// </summary>
        private ICombatTarget FindTelegraphingMeleeEnemy()
        {
            ICombatTarget closest = null;
            float closestDist = CombatConstants.GuardWarpMaxRange;
            Vector2 myPos = GetPos();

            for (int i = 0; i < context.activeEnemies.Count; i++)
            {
                var enemy = context.activeEnemies[i];
                if (enemy == null || !enemy.IsTargetable) continue;

                // ITelegraphable 체크
                if (enemy is not ITelegraphable telegraph) continue;
                if (!telegraph.IsTelegraphing) continue;
                if (telegraph.CurrentAttackCategory != AttackCategory.Melee) continue;

                float dist = Vector2.Distance(
                    (Vector2)enemy.GetTransform().position, myPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = enemy;
                }
            }
            return closest;
        }

        /// <summary>
        /// 카운터 액션 ID에서 인덱스 추출.
        /// "GuardCounter" → 0, "GuardCounter2" → 1, "GuardCounter3" → 2, ...
        /// </summary>
        private int ExtractCounterIndex(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return 0;

            // 끝에서 숫자 추출
            int numStart = actionId.Length;
            while (numStart > 0 && char.IsDigit(actionId[numStart - 1]))
                numStart--;

            if (numStart >= actionId.Length) return 0; // 숫자 없음 → index 0

            if (int.TryParse(actionId.Substring(numStart), out int num))
                return Mathf.Max(0, num - 1); // 1-based → 0-based

            return 0;
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

        /// <summary>Animator Integer 파라미터 안전 설정</summary>
        private void SafeSetInteger(string paramName, int value)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetInteger(paramName, value); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Guard] Animator 파라미터 설정 오류 무시: {e.Message}");
            }
        }
    }
}
