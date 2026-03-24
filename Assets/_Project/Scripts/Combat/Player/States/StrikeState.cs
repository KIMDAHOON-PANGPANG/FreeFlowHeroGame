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

        /// <summary>현재 실행 중인 액션 ID (디버그 UI용)</summary>
        public string CurrentActionId => currentAction?.id ?? "None";

        // ─── 액터 ID ───
        private const string ActorId = "PC_Hero";

        // ─── 콤보 → 액션 ID 매핑 ───
        // ★ 데이터 튜닝: 콤보 체인 순서. JSON의 Action ID와 일치해야 함.
        private static readonly string[] ComboActionIds = { "LightAtk1", "LightAtk2", "LightAtk3", "LightAtk4" };
        private int MaxComboChain => ComboActionIds.Length;

        // ─── 폴백 기본값 (JSON 로드 실패 시) ───
        private static readonly int[] FallbackStartup  = { 5, 4, 5, 5 };
        private static readonly int[] FallbackActive   = { 8, 7, 9, 8 };
        private static readonly int[] FallbackRecovery = { 12, 10, 14, 12 };
        private static readonly float[] FallbackCancelRatio = { 0f, 0f, 0.3f, 0.3f };
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
        private float currentPlaybackRate = 1f;  // ★ 애니메이션 재생 배율 (프레임 스케일링용)

        // 히트박스 활성 추적 (노티파이 모드에서 이벤트 구독 관리)
        private bool hitboxSubscribed;

        // ★ 공격 중 플래그: true인 동안 공격 계열 입력은 버퍼에만 쌓이고 소비되지 않음.
        //   Enter() → true, CANCEL_WINDOW 진입 → false.
        //   회피/카운터는 isAttacking과 무관하게 즉시 처리 (생존 우선).
        private bool isAttacking;

        // 현재 액션의 JSON 데이터 참조
        private ActionEntry currentAction;

        public override void Enter()
        {
            base.Enter();
            hitConnected = false;
            stateElapsedTime = 0f;
            hitboxSubscribed = false;
            isAttacking = true;             // ★ 공격 시작 → 공격 입력 잠금
            context.canCancel = false;      // ★ 이전 상태의 canCancel 잔류값 초기화

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
                moveSpeed = currentAction.moveSpeed;

                // ★ playbackRate에 따라 totalFrames를 스케일링
                //   playbackRate=0.4이면 effectiveFrames=25 → totalFrames=63 (실제 벽시계 프레임)
                //   이렇게 해야 애니메이션과 스테이트머신이 동기화됨
                currentPlaybackRate = (currentAction.playbackRate > 0f) ? currentAction.playbackRate : 1f;

                // ★ 스테이트 지속 프레임 결정: 세 값 중 최대
                //   1) NotifyTotalFrames: 노티파이 커버 범위
                //   2) Legacy TotalFrames: startup+active+recovery
                //   3) ClipFrames: 실제 애니메이션 클립 길이 (60fps 환산)
                //   클립이 가장 길면 클립 기준, 아니면 데이터 기준.
                int notifyTotalFrames = notifyProcessor.TotalFrames;
                int legacyTotalFrames = currentAction.TotalFrames;
                int clipFrames = GetClipFrames(currentAction.clip);
                int effectiveTotalFrames = Mathf.Max(notifyTotalFrames, Mathf.Max(legacyTotalFrames, clipFrames));
                totalFrames = Mathf.CeilToInt(effectiveTotalFrames / currentPlaybackRate);

                Debug.Log($"[Strike] Enter — Action:{currentAction.id} Chain:{context.comboChainIndex} " +
                    $"Notify:{notifyTotalFrames} Legacy:{legacyTotalFrames} Clip:{clipFrames} " +
                    $"Effective:{effectiveTotalFrames} Wall:{totalFrames} Rate:{currentPlaybackRate:F2}");
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

                Debug.Log($"[Strike] Enter — Action:{currentAction.id} Chain:{context.comboChainIndex} " +
                    $"Legacy:{startupFrames}/{activeFrames}/{recoveryFrames} Total:{totalFrames}");
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
            // ★ 벽시계 프레임 → 애니메이션 프레임 변환
            //   playbackRate=0.4이면 벽시계 15프레임 = 애니메이션 6프레임
            //   노티파이(COLLISION, CANCEL_WINDOW)는 애니메이션 프레임 기준으로 정의됨
            int animFrame = Mathf.FloorToInt(frame * currentPlaybackRate);
            notifyProcessor.Tick(animFrame);

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

            // ── CANCEL_WINDOW: 캔슬 플래그 + isAttacking 해제 ──
            bool prevCanCancel = context.canCancel;
            context.canCancel = notifyProcessor.AnyCancelActive;

            // ★ CANCEL_WINDOW 최초 진입 → isAttacking 해제 (공격 입력 잠금 풀림)
            if (context.canCancel && !prevCanCancel)
            {
                isAttacking = false;
                Debug.Log($"[Strike][DEBUG] CANCEL_WINDOW OPEN — wall:{frame} anim:{animFrame} isAttacking→false HasBuffer:{fsm.InputBuffer.HasInput} BufferPeek:{fsm.InputBuffer.Peek?.Type}");
            }

            // 캔슬 가능하고 버퍼에 입력이 있으면 처리
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                string inputKey = InputTypeToString(buffered.Type);
                bool canCancelWith = notifyProcessor.CanCancelWith(inputKey);

                Debug.Log($"[Strike][DEBUG] BUFFER CONSUME — wall:{frame} anim:{animFrame} Type:{buffered.Type} CanCancelWith({inputKey}):{canCancelWith} isAttacking:{isAttacking}");

                if (canCancelWith)
                {
                    // ★ isAttacking 중이면 공격 계열은 소비하지 않음 (아직 모션 진행 중)
                    if (IsAttackInput(buffered.Type) && isAttacking)
                    {
                        Debug.Log($"[Strike][DEBUG] BUFFER RE-QUEUE — isAttacking guard blocked");
                        fsm.InputBuffer.BufferInput(buffered);
                    }
                    else
                    {
                        Debug.Log($"[Strike][DEBUG] BUFFER → HandleBufferedInput — {buffered.Type}");
                        HandleBufferedInput(buffered);
                        return;
                    }
                }
                else
                {
                    Debug.Log($"[Strike][DEBUG] BUFFER RE-QUEUE — CanCancelWith false");
                    fsm.InputBuffer.BufferInput(buffered);
                }
            }
            else if (context.canCancel && !fsm.InputBuffer.HasInput)
            {
                // ★ 디버그: 캔슬 윈도우 열렸는데 버퍼가 비어있음 (만료 의심)
                var peek = fsm.InputBuffer.Peek;
                if (peek != null)
                    Debug.Log($"[Strike][DEBUG] CANCEL_WINDOW ACTIVE but HasInput=false (버퍼 만료!) wall:{frame} anim:{animFrame} Peek:{peek.Type}");
            }

            // 프레임 완료 → 다음 체인 인덱스 전진 후 Idle
            if (frame >= totalFrames)
            {
                // ★ 콤보 액션 정상 완료 → 다음 공격을 위해 체인 인덱스 전진
                // Idle 복귀 후 콤보 윈도우 내에 다시 공격하면 다음 타수(2타→3타 등)가 나감
                int currentIdx = System.Array.IndexOf(ComboActionIds, currentAction?.id);
                if (currentIdx >= 0)
                {
                    context.comboChainIndex = (currentIdx + 1) % MaxComboChain;
                }

                // ★ 콤보 윈도우 타이머 시작 (히트 여부 무관)
                //   히트 시에는 IncrementCombo()에서 이미 시작되지만,
                //   헛스윙(적 없이 공격) 시에도 comboWindowTimer를 시작해야
                //   시간 초과 후 comboChainIndex가 0으로 리셋됨
                if (context.comboWindowTimer <= 0f)
                {
                    context.comboWindowTimer = CombatConstants.ComboWindowDuration;
                }

                fsm.TransitionTo<IdleState>();
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
            int frame = context.stateFrameCounter;
            int animFrame = Mathf.FloorToInt(frame * currentPlaybackRate);

            // ★ 공격 계열 입력: isAttacking 중이면 무조건 버퍼 (모션 완주 보장)
            if (IsAttackInput(input.Type) && isAttacking)
            {
                Debug.Log($"[Strike][DEBUG] HandleInput BUFFER — {input.Type} wall:{frame} anim:{animFrame} isAttacking:true canCancel:{context.canCancel}");
                fsm.InputBuffer.BufferInput(input);
                return;
            }

            // 캔슬 윈도우가 아직 안 열렸으면 버퍼
            if (!context.canCancel)
            {
                Debug.Log($"[Strike][DEBUG] HandleInput BUFFER — {input.Type} wall:{frame} anim:{animFrame} canCancel:false");
                fsm.InputBuffer.BufferInput(input);
                return;
            }

            // 노티파이 모드: 입력 타입별 캔슬 허용 체크
            if (useNotifyMode && notifyProcessor != null)
            {
                string inputKey = InputTypeToString(input.Type);
                if (!notifyProcessor.CanCancelWith(inputKey))
                {
                    Debug.Log($"[Strike][DEBUG] HandleInput BUFFER — {input.Type} wall:{frame} anim:{animFrame} CanCancelWith({inputKey}):false");
                    fsm.InputBuffer.BufferInput(input);
                    return;
                }
            }

            Debug.Log($"[Strike][DEBUG] HandleInput PASS → HandleBufferedInput — {input.Type} wall:{frame} anim:{animFrame}");
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
                Debug.Log($"[Strike][DEBUG] HandleBufferedInput — cancels[{inputKey}]→{cancelTarget}");
                ResolveCancelTarget(cancelTarget, input);
                return;
            }

            // ─── 노티파이 캔슬 윈도우의 nextAction 확인 ───
            if (useNotifyMode && !string.IsNullOrEmpty(notifyProcessor?.CancelNextAction))
            {
                Debug.Log($"[Strike][DEBUG] HandleBufferedInput — nextAction→{notifyProcessor.CancelNextAction}");
                ResolveCancelTarget(notifyProcessor.CancelNextAction, input);
                return;
            }

            Debug.Log($"[Strike][DEBUG] HandleBufferedInput — 기본 분기 (cancels 없음, nextAction 없음) input:{input.Type}");
            // ─── 캔슬 경로 없으면 기본 분기 ───
            switch (input.Type)
            {
                case InputType.Attack:
                case InputType.Heavy:
                    // cancels[]에도 nextAction에도 없는 경우 → 기본 순환 증가
                    context.comboChainIndex = (context.comboChainIndex + 1) % MaxComboChain;
                    ResolveNextComboAttack(input);
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

        /// <summary>공격 계열 입력인지 판별 (Attack, Heavy)</summary>
        private static bool IsAttackInput(InputType type)
        {
            return type == InputType.Attack || type == InputType.Heavy;
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
        /// 프리플로우 콤보 체인: 다음 공격 시 타겟 재선택 + 워핑 판정.
        /// ★ comboChainIndex는 이 함수 호출 전에 반드시 세팅되어 있어야 함.
        ///   - ResolveCancelTarget에서 명시적으로 세팅하거나
        ///   - HandleBufferedInput 기본 분기에서 직접 증가
        /// </summary>
        private void ResolveNextComboAttack(InputData input)
        {
            // 타겟 재선택 (방향 입력 없으면 현재 facing 사용)
            Vector2 playerPos = GetPos();
            float inputDir = (input != null) ? input.Direction.x : 0f;
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

            Debug.Log($"[Strike] HIT — {target.GetTransform().name} Combo:{context.comboCount}");

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
            // 히트 판정 시작
        }

        /// <summary>클립 이름으로 AnimatorController에서 실제 클립 길이(60fps 프레임 수)를 반환</summary>
        private int GetClipFrames(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return 0;
            var controller = context.playerAnimator?.runtimeAnimatorController;
            if (controller == null) return 0;

            foreach (var clip in controller.animationClips)
            {
                if (clip.name == clipName)
                    return Mathf.CeilToInt(clip.length * 60f);
            }
            return 0;
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
