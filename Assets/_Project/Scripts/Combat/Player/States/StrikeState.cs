using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Common;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Strike 상태: 기본 공격 (Light Attack).
    ///
    /// ★ 2가지 모드:
    ///   1) 노티파이 모드 (우선): ActionEntry에 notifies[]가 있으면
    ///      ActionNotifyProcessor로 매 프레임 폴링.
    ///      COLLISION → 히트박스, CANCEL_WINDOW → 캔슬 플래그, STARTUP → 이동 속도.
    ///   2) 레거시 모드 (폴백): notifies[]가 없으면
    ///      startup/active/recovery 프레임 수로 Phase 전환.
    ///
    /// ★ 데이터 소스: ActionTableManager (PC_Hero.json)
    ///   에디터의 Action Table Editor에서 프레임 데이터, 노티파이, 캔슬 경로 등을 수정하면
    ///   다음 플레이 시 자동 반영됩니다.
    ///
    /// ★ 폴백: ActionTableManager가 없거나 JSON 로드에 실패하면 하드코딩 기본값을 사용합니다.
    /// </summary>
    public class StrikeState : CombatState
    {
        public override string StateName => "Strike";

        // ─── 액터 ID ───
        private const string ActorId = "PC_Hero";

        // ─── 콤보 → 액션 ID 매핑 ───
        // ★ 데이터 튜닝: 콤보 체인 순서. JSON의 Action ID와 일치해야 함.
        private static readonly string[] ComboActionIds = { "LightAtk1", "LightAtk2", "LightAtk3" };
        private int MaxComboChain => ComboActionIds.Length;

        // ─── 폴백 기본값 (JSON 로드 실패 시) ───
        private static readonly int[] FallbackStartup  = { 5, 4, 5 };
        private static readonly int[] FallbackActive   = { 8, 7, 9 };
        private static readonly int[] FallbackRecovery = { 12, 10, 14 };
        private static readonly float[] FallbackCancelRatio = { 0f, 0f, 0.3f };
        private const float FallbackMoveSpeed = 6f;

        // ─── 시각 피드백 ───
        private static readonly Color AttackFlashColor = Color.yellow;
        private static readonly Color HitConfirmColor  = Color.cyan;
        private const float FlashDuration = 0.2f;

        // ─── 상태 변수 (레거시 모드) ───
        private enum Phase { Startup, Active, Recovery }
        private Phase currentPhase;

        // ─── 공통 변수 ───
        private HitboxController hitbox;
        private bool hitConnected;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private float flashTimer;
        private float facing;
        private float stateElapsedTime;

        // 레거시 모드 프레임 데이터
        private int startupFrames;
        private int activeFrames;
        private int recoveryFrames;
        private int totalFrames;
        private float moveSpeed;
        private float cancelDelay;

        // 노티파이 모드
        private ActionNotifyProcessor notifyProcessor;
        private bool useNotifyMode;

        // 히트박스 활성 추적 (노티파이 모드에서 이벤트 구독 관리)
        private bool hitboxSubscribed;

        // 현재 액션의 JSON 데이터 참조
        private ActionEntry currentAction;

        public override void Enter()
        {
            base.Enter();
            hitConnected = false;
            stateElapsedTime = 0f;
            hitboxSubscribed = false;

            // ─── 콤보 인덱스에 따른 액션 데이터 결정 ───
            int idx = Mathf.Clamp(context.comboChainIndex, 0, MaxComboChain - 1);
            string actionId = ComboActionIds[idx];

            currentAction = ActionTableManager.Instance?.GetAction(ActorId, actionId);

            // ─── 모드 결정: 노티파이 vs 레거시 ───
            if (currentAction != null && currentAction.HasNotifies)
            {
                // ★ 노티파이 모드
                useNotifyMode = true;
                notifyProcessor = new ActionNotifyProcessor(currentAction);
                totalFrames = notifyProcessor.TotalFrames;
                moveSpeed = currentAction.moveSpeed; // 레거시 moveSpeed는 STARTUP 노티에서 오버라이드됨

                Debug.Log($"[Strike] Enter (NOTIFY) — Chain:{context.comboChainIndex} Action:{currentAction.id} " +
                    $"TotalFrames:{totalFrames} Notifies:{currentAction.notifies.Length}");
            }
            else if (currentAction != null)
            {
                // ★ 레거시 모드 (JSON 있지만 노티파이 없음)
                useNotifyMode = false;
                notifyProcessor = null;
                currentPhase = Phase.Startup;

                startupFrames = currentAction.startup;
                activeFrames = currentAction.active;
                recoveryFrames = currentAction.recovery;
                moveSpeed = currentAction.moveSpeed;
                cancelDelay = currentAction.CancelDelay;
                totalFrames = startupFrames + activeFrames + recoveryFrames;

                Debug.Log($"[Strike] Enter (LEGACY) — Chain:{context.comboChainIndex} Action:{currentAction.id} " +
                    $"Frames:{startupFrames}/{activeFrames}/{recoveryFrames} CancelDelay:{cancelDelay:F3}s");
            }
            else
            {
                // ★ 폴백: 하드코딩 기본값
                useNotifyMode = false;
                notifyProcessor = null;
                currentPhase = Phase.Startup;

                Debug.LogWarning($"[Strike] ActionTable '{ActorId}/{actionId}' 로드 실패 — 폴백 사용");
                startupFrames = FallbackStartup[idx];
                activeFrames = FallbackActive[idx];
                recoveryFrames = FallbackRecovery[idx];
                moveSpeed = FallbackMoveSpeed;
                float ratio = FallbackCancelRatio[idx];
                cancelDelay = (startupFrames + activeFrames + recoveryFrames * ratio)
                    * CombatConstants.FrameDuration;
                totalFrames = startupFrames + activeFrames + recoveryFrames;
            }

            // 히트박스 참조
            hitbox = context.playerTransform.GetComponentInChildren<HitboxController>();

            // 시각 피드백 준비
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;
            flashTimer = 0f;

            // 방향: 타겟이 있으면 타겟 방향, 없으면 현재 facing
            if (context.currentTarget != null)
            {
                float dir = Mathf.Sign(
                    context.currentTarget.position.x - GetPos().x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
            }
            facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

            // 애니메이션
            SetAttackAnimation();
        }

        public override void Exit()
        {
            base.Exit();
            hitbox?.Deactivate();
            UnsubscribeHitbox();

            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // ── 재생 배율 복원 ──
            if (context.playerAnimator != null)
                context.playerAnimator.speed = 1.0f;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            int frame = context.stateFrameCounter;
            stateElapsedTime += deltaTime;

            // 플래시 타이머
            UpdateFlashTimer(deltaTime);

            if (useNotifyMode)
                UpdateNotifyMode(frame, deltaTime);
            else
                UpdateLegacyMode(frame, deltaTime);
        }

        // ═══════════════════════════════════════════════════════
        //  노티파이 모드 업데이트
        // ═══════════════════════════════════════════════════════

        private void UpdateNotifyMode(int frame, float deltaTime)
        {
            notifyProcessor.Tick(frame);

            // ── STARTUP: 이동 속도 적용 ──
            if (notifyProcessor.IsStartupActive && notifyProcessor.StartupMoveSpeed > 0f)
            {
                MoveHorizontal(facing * notifyProcessor.StartupMoveSpeed * deltaTime);
            }

            // ── COLLISION: 히트박스 활성/비활성 ──
            if (notifyProcessor.CollisionJustStarted)
            {
                hitbox?.Activate();
                SubscribeHitbox();
                TriggerAttackFlash();
            }
            else if (notifyProcessor.CollisionJustEnded)
            {
                hitbox?.Deactivate();
                UnsubscribeHitbox();
            }

            // ── CANCEL_WINDOW: 캔슬 플래그 ──
            // 노티파이 모드에서는 context.canCancel을 캔슬 윈도우 활성 여부로 매 프레임 갱신
            context.canCancel = notifyProcessor.AnyCancelActive;

            // 캔슬 가능하고 버퍼에 입력이 있으면 처리
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();

                // 노티파이 모드: 입력 타입별 캔슬 허용 여부 체크
                string inputKey = InputTypeToString(buffered.Type);
                if (notifyProcessor.CanCancelWith(inputKey))
                {
                    HandleBufferedInput(buffered);
                    return;
                }
                else
                {
                    // 이 캔슬 타입은 현재 윈도우에서 허용되지 않음 → 다시 버퍼에 넣기
                    fsm.InputBuffer.BufferInput(buffered);
                }
            }

            // 프레임 완료 → Idle
            if (frame >= totalFrames)
            {
                // defaultNext가 있으면 해당 액션으로, 없으면 Idle
                if (!string.IsNullOrEmpty(currentAction?.defaultNext))
                {
                    ResolveCancelTarget(currentAction.defaultNext, default);
                }
                else
                {
                    fsm.TransitionTo<IdleState>();
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        //  레거시 모드 업데이트 (기존 로직 유지)
        // ═══════════════════════════════════════════════════════

        private void UpdateLegacyMode(int frame, float deltaTime)
        {
            // Startup ~ Active: 전방 이동
            if (currentPhase == Phase.Startup || currentPhase == Phase.Active)
            {
                if (moveSpeed > 0f)
                    MoveHorizontal(facing * moveSpeed * deltaTime);
            }

            // Startup → Active
            if (currentPhase == Phase.Startup && frame >= startupFrames)
            {
                currentPhase = Phase.Active;
                hitbox?.Activate();
                SubscribeHitbox();
                TriggerAttackFlash();
            }

            // Active → Recovery
            if (currentPhase == Phase.Active && frame >= startupFrames + activeFrames)
            {
                currentPhase = Phase.Recovery;
                hitbox?.Deactivate();
                UnsubscribeHitbox();
            }

            // ★ 캔슬 타이밍: cancelDelay 이후 캔슬 가능
            if (!context.canCancel && stateElapsedTime >= cancelDelay)
            {
                context.canCancel = true;
            }

            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInput(buffered);
                return;
            }

            // 프레임 완료 → defaultNext 또는 Idle
            if (frame >= totalFrames)
            {
                fsm.TransitionTo<IdleState>();
            }
        }

        // ═══════════════════════════════════════════════════════
        //  입력 처리 (공통)
        // ═══════════════════════════════════════════════════════

        public override void HandleInput(InputData input)
        {
            if (!context.canCancel)
            {
                fsm.InputBuffer.BufferInput(input);
                return;
            }

            // 노티파이 모드: 입력 타입별 캔슬 허용 체크
            if (useNotifyMode && notifyProcessor != null)
            {
                string inputKey = InputTypeToString(input.Type);
                if (!notifyProcessor.CanCancelWith(inputKey))
                {
                    fsm.InputBuffer.BufferInput(input);
                    return;
                }
            }

            HandleBufferedInput(input);
        }

        /// <summary>
        /// 버퍼/직접 입력 처리.
        /// JSON 캔슬 경로가 있으면 우선 사용, 없으면 기본 분기.
        /// </summary>
        private void HandleBufferedInput(InputData input)
        {
            // ─── JSON 캔슬 경로 조회 ───
            string inputKey = InputTypeToString(input.Type);
            string cancelTarget = currentAction?.GetCancelTarget(inputKey);

            // 캔슬 경로가 정의되어 있으면 해당 액션으로 분기
            if (cancelTarget != null)
            {
                ResolveCancelTarget(cancelTarget, input);
                return;
            }

            // ─── 노티파이 캔슬 윈도우의 nextAction 확인 ───
            if (useNotifyMode && !string.IsNullOrEmpty(notifyProcessor?.CancelNextAction))
            {
                ResolveCancelTarget(notifyProcessor.CancelNextAction, input);
                return;
            }

            // ─── 캔슬 경로 없으면 기본 분기 ───
            switch (input.Type)
            {
                case InputType.Attack:
                    ResolveNextComboAttack(input);
                    break;

                case InputType.Dodge:
                    fsm.TransitionTo<DodgeState>();
                    break;

                case InputType.Counter:
                    fsm.TransitionTo<CounterState>();
                    break;

                case InputType.Heavy:
                    ResolveNextComboAttack(input);
                    break;

                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        /// <summary>
        /// JSON 캔슬 경로에서 결정된 다음 액션으로 전환.
        /// LightAtk 계열은 콤보 체인 인덱스를 갱신한 뒤 워핑 판정을 거침.
        /// Dodge, Counter 등은 직접 상태 전환.
        /// </summary>
        private void ResolveCancelTarget(string targetActionId, InputData input)
        {
            // 콤보 액션이면 comboChainIndex 갱신 후 워핑 판정
            int comboIdx = System.Array.IndexOf(ComboActionIds, targetActionId);
            if (comboIdx >= 0)
            {
                context.comboChainIndex = comboIdx;
                ResolveNextComboAttack(input);
                return;
            }

            // 비콤보 액션: 이름으로 상태 직접 전환
            switch (targetActionId)
            {
                case "Dodge":
                    fsm.TransitionTo<DodgeState>();
                    break;
                case "Counter":
                    fsm.TransitionTo<CounterState>();
                    break;
                case "HeavyAtk":
                    // Phase 4: HeavyAttackState
                    ResolveNextComboAttack(input);
                    break;
                case "Idle":
                    fsm.TransitionTo<IdleState>();
                    break;
                default:
                    Debug.LogWarning($"[Strike] 알 수 없는 캔슬 대상: {targetActionId}");
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        /// <summary>InputType enum → JSON 캔슬 경로의 input 문자열 변환</summary>
        private static string InputTypeToString(InputType type)
        {
            switch (type)
            {
                case InputType.Attack:  return "Attack";
                case InputType.Heavy:   return "Heavy";
                case InputType.Dodge:   return "Dodge";
                case InputType.Counter: return "Counter";
                case InputType.Huxley:  return "Huxley";
                default:                return type.ToString();
            }
        }

        /// <summary>
        /// 프리플로우 콤보 체인: 다음 공격 시 타겟 재선택 + 워핑 판정
        /// </summary>
        private void ResolveNextComboAttack(InputData input)
        {
            // JSON 캔슬 경로에서 comboChainIndex가 이미 세팅된 경우가 아니면 순환 증가
            if (System.Array.IndexOf(ComboActionIds, currentAction?.GetCancelTarget(InputTypeToString(input.Type))) < 0)
            {
                context.comboChainIndex = (context.comboChainIndex + 1) % MaxComboChain;
            }

            // 타겟 재선택 (방향 입력 없으면 현재 facing 사용)
            Vector2 playerPos = GetPos();
            float inputDir = input.Direction.x;
            if (Mathf.Approximately(inputDir, 0f))
                inputDir = context.playerTransform.localScale.x >= 0 ? 1f : -1f;

            var target = fsm.TargetSelector.SelectTarget(
                playerPos, context.activeEnemies, inputDir);

            if (target != null)
            {
                context.currentTarget = target.GetTransform();

                if (fsm.TargetSelector.NeedsWarp(playerPos, target))
                {
                    fsm.TransitionTo<WarpState>();
                }
                else
                {
                    fsm.TransitionTo<StrikeState>();
                }
            }
            else
            {
                context.currentTarget = null;
                fsm.TransitionTo<StrikeState>();
            }
        }

        // ═══════════════════════════════════════════════════════
        //  히트박스 이벤트 구독 관리
        // ═══════════════════════════════════════════════════════

        private void SubscribeHitbox()
        {
            if (hitboxSubscribed || hitbox == null) return;
            hitbox.OnHitDetected += OnHitDetected;
            hitboxSubscribed = true;
        }

        private void UnsubscribeHitbox()
        {
            if (!hitboxSubscribed || hitbox == null) return;
            hitbox.OnHitDetected -= OnHitDetected;
            hitboxSubscribed = false;
        }

        /// <summary>히트 감지 콜백</summary>
        private void OnHitDetected(ICombatTarget target, Vector2 contactPoint)
        {
            if (hitConnected) return;
            hitConnected = true;

            // 프리플로우: 직전 타격 적 기록 (다음 콤보에서 다른 적 우선)
            fsm.TargetSelector.RegisterHit(target);

            // 콤보 증가
            context.IncrementCombo();

            // 헉슬리 충전
            float chargeAmount = CombatConstants.HuxleyBaseChargePerHit
                * (1f + context.comboCount * 0.05f);
            context.ChargeHuxley(chargeAmount);

            // HitData 생성 및 이벤트 발행
            Vector2 attackerPos = GetPos();
            Vector2 targetPos = target.GetTransform().position;

            var hitData = HitData.CreateLightAttack(attackerPos, targetPos, context.comboCount);
            hitData.ContactPoint = contactPoint;

            // 노티파이 모드: damageScale 적용
            if (useNotifyMode && notifyProcessor != null)
            {
                hitData.DamageMultiplier *= notifyProcessor.DamageScale;
            }

            CombatEventBus.Publish(new OnAttackHit
            {
                HitData = hitData,
                Attacker = null,
                Target = target
            });

            // 시안 플래시
            if (spriteRenderer != null)
            {
                spriteRenderer.color = HitConfirmColor;
                flashTimer = FlashDuration;
            }

            Debug.Log($"[Strike] HIT! — Target: {target.GetTransform().name}, Combo: {context.comboCount}" +
                (useNotifyMode ? $", DmgScale: {notifyProcessor.DamageScale:F2}" : ""));

            target.TakeHit(hitData);
        }

        // ═══════════════════════════════════════════════════════
        //  시각 피드백
        // ═══════════════════════════════════════════════════════

        private void UpdateFlashTimer(float deltaTime)
        {
            if (flashTimer <= 0f) return;
            flashTimer -= deltaTime;
            if (spriteRenderer != null)
            {
                float t = Mathf.Clamp01(flashTimer / FlashDuration);
                spriteRenderer.color = Color.Lerp(originalColor, spriteRenderer.color, t);
                if (flashTimer <= 0f)
                    spriteRenderer.color = originalColor;
            }
        }

        /// <summary>공격 시 색상 플래시</summary>
        private void TriggerAttackFlash()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = AttackFlashColor;
                flashTimer = FlashDuration;
            }
            Debug.Log("[Strike] Active! — 히트 판정 시작");
        }

        /// <summary>콤보 인덱스에 따른 애니메이션 설정</summary>
        private void SetAttackAnimation()
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null)
            {
                Debug.Log("[Strike] Animator에 Controller 미할당 — 애니메이션 스킵");
                return;
            }
            try
            {
                context.playerAnimator.SetInteger("ComboIndex", context.comboChainIndex);
                context.playerAnimator.SetTrigger("Strike");

                // ── 액션별 재생 배율 적용 ──
                float rate = currentAction != null ? currentAction.playbackRate : 1.0f;
                if (rate <= 0f) rate = 1.0f;
                context.playerAnimator.speed = rate;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Strike] Animator 오류 무시: {e.Message}");
            }
        }
    }
}
