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
        private float hitStopRemaining; // 히트스탑 잔여 시간 (초)

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

        // (hadCancelWindowActive 제거됨 — 체인 전진은 캔슬 성공 시에만 HandleBufferedInput에서 처리)

        // ─── 인라인 워핑 상태 (WARP 노티파이로 트리거) ───
        private bool isWarpActive;
        private bool warpExecuted;  // ★ 이 액션에서 워핑이 이미 실행되었는지 (중복 발동 방지)
        private Vector2 warpStartPos;
        private Vector2 warpEndPos;
        private float warpTimer;
        private float activeWarpDuration;
        private int activeWarpEaseType;
        private Color warpOriginalColor;
        private static readonly Color WarpColor = new Color(0.5f, 0.8f, 1f, 0.7f);

        public override void Enter()
        {
            base.Enter();
            hitConnected = false;
            hitStopRemaining = 0f;
            stateElapsedTime = 0f;
            hitboxSubscribed = false;
            isAttacking = true;             // ★ 공격 시작 → 공격 입력 잠금
            context.canCancel = false;      // ★ 이전 상태의 canCancel 잔류값 초기화
            isWarpActive = false;           // 인라인 워핑 초기화
            warpExecuted = false;           // 워핑 실행 플래그 초기화
            // (hadCancelWindowActive 제거됨)

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

                // ★ 노티파이 구간 요약 (런타임에서 실제 사용 중인 값 확인용)
                string notifySummary = "";
                if (currentAction.notifies != null)
                {
                    foreach (var n in currentAction.notifies)
                    {
                        if (n.disabled) continue;
                        notifySummary += $" {n.type}[{n.startFrame}-{n.endFrame}]";
                    }
                }
                Debug.Log($"[Strike] Enter — Action:{currentAction.id} Chain:{context.comboChainIndex} " +
                    $"Wall:{totalFrames} Rate:{currentPlaybackRate:F2} Notifies:{notifySummary}");
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

            // 워핑 중 Exit 시 플래그 정리
            if (isWarpActive)
            {
                isWarpActive = false;
                context.isWarping = false;
                context.isInvulnerable = false;
            }

            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // ── 재생 배율 복원 ──
            if (context.playerAnimator != null)
                context.playerAnimator.speed = 1.0f;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // ★ 히트스탑: 잔여 시간 동안 프레임 카운터/애니메이션 정지
            if (hitStopRemaining > 0f)
            {
                hitStopRemaining -= deltaTime;
                if (context.playerAnimator != null)
                    context.playerAnimator.speed = 0f;
                UpdateFlashTimer(deltaTime); // 플래시는 계속 진행
                return; // 프레임 진행 스킵
            }
            else if (context.playerAnimator != null && context.playerAnimator.speed < 0.01f)
            {
                // 히트스탑 종료 → 애니메이션 속도 복원
                context.playerAnimator.speed = 1f;
            }

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

            // ── 인라인 워핑 진행 중이면 워핑만 업데이트 ──
            if (isWarpActive)
            {
                UpdateInlineWarp(deltaTime);
                // 워핑 중에는 COLLISION/CANCEL 처리 스킵, 프레임 카운터는 계속 진행
                // 프레임 완료 체크만 수행
                if (isWarpActive) return;
                // 워핑 완료 → 아래로 계속 진행
            }

            // ── WARP 노티파이 트리거 감지 (액션당 1회만) ──
            if (notifyProcessor.IsWarpTriggered && !warpExecuted)
            {
                warpExecuted = true;  // ★ 중복 발동 방지
                StartInlineWarp(notifyProcessor.WarpNotify);
                if (isWarpActive) return; // 워핑 시작됨 → 이번 프레임은 여기서 종료
            }

            // ── ROOT_MOTION: 커브 기반 이동 (우선순위 최상) ──
            if (notifyProcessor.IsRootMotionActive && Mathf.Abs(notifyProcessor.RootMotionSpeed) > 0.01f)
            {
                MoveHorizontal(facing * notifyProcessor.RootMotionSpeed * deltaTime);
            }
            // ── STARTUP: 이동 속도 적용 (ROOT_MOTION이 없을 때 폴백) ──
            else if (notifyProcessor.IsStartupActive && notifyProcessor.StartupMoveSpeed > 0f)
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

            // ── PENDING_WINDOW: 입력 수집 구간 (CANCEL_WINDOW 직전) ──
            if (notifyProcessor.PendingWindowJustStarted)
            {
                // ★ 펜딩 윈도우 시작 → 이전 입력 폐기
                //   광클 시 이 시점 이전에 쌓인 버퍼 입력을 폐기하고,
                //   펜딩 윈도우 동안 새로 들어오는 입력만 버퍼에 저장한다.
                //   → CANCEL_WINDOW가 열릴 때 펜딩 입력이 즉시 소비됨.
                Debug.Log($"[Strike][FLOW] PENDING START — Action:{currentAction?.id} animF:{animFrame} wallF:{frame} buffer:{(fsm.InputBuffer.HasInput ? "有" : "無")} → Clear");
                fsm.InputBuffer.Clear();
            }
            if (notifyProcessor.PendingWindowJustEnded)
            {
                Debug.Log($"[Strike][FLOW] PENDING END — Action:{currentAction?.id} animF:{animFrame} buffer:{(fsm.InputBuffer.HasInput ? "有→캔슬대기" : "無")}");
            }

            // ── CANCEL_WINDOW: 캔슬 플래그 + isAttacking 해제 ──
            bool prevCanCancel = context.canCancel;
            context.canCancel = notifyProcessor.AnyCancelActive;

            // ★ CANCEL_WINDOW 최초 진입 → isAttacking 해제
            if (context.canCancel && !prevCanCancel)
            {
                isAttacking = false;

                // ★ 펜딩 윈도우가 있는 액션: 버퍼 클리어 안 함 (펜딩에서 수집한 입력 보존)
                //   펜딩 윈도우가 없는 액션: 기존대로 버퍼 클리어 (하위 호환)
                if (!notifyProcessor.HasPendingWindow)
                {
                    fsm.InputBuffer.Clear();
                }

                Debug.Log($"[Strike][FLOW] CANCEL_WINDOW OPEN — Action:{currentAction?.id} animF:{animFrame} wallF:{frame} buffer:{(fsm.InputBuffer.HasInput ? "有→즉시소비" : "無→입력대기")}");
            }

            // 캔슬 가능하고 버퍼에 입력이 있으면 처리
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                string inputKey = InputTypeToString(buffered.Type);
                bool canCancelWith = notifyProcessor.CanCancelWith(inputKey);

                if (canCancelWith)
                {
                    // ★ isAttacking 중이면 공격 계열은 소비하지 않음 (아직 모션 진행 중)
                    if (IsAttackInput(buffered.Type) && isAttacking)
                    {
                        fsm.InputBuffer.BufferInput(buffered);
                    }
                    else
                    {
                        Debug.Log($"[Strike][FLOW] CANCEL! — Action:{currentAction?.id}→{inputKey} animF:{animFrame} wallF:{frame}");
                        HandleBufferedInput(buffered);
                        return;
                    }
                }
                else
                {
                    fsm.InputBuffer.BufferInput(buffered);
                }
            }

            // 프레임 완료 → Idle 전환
            if (frame >= totalFrames)
            {
                // ★ 자연 완료 시 comboChainIndex 전진 안 함
                //   체인 전진은 캔슬이 실제로 성공했을 때(HandleBufferedInput)만 발생.
                //   캔슬 윈도우가 열렸는데 입력이 없어서 자연 완료된 경우,
                //   IDLE에서 다시 공격하면 같은 타수부터 시작하는 것이 올바름.

                // ★ 콤보 윈도우 타이머 시작 (히트 여부 무관)
                //   히트 시에는 IncrementCombo()에서 이미 시작되지만,
                //   헛스윙(적 없이 공격) 시에도 comboWindowTimer를 시작해야
                //   시간 초과 후 comboChainIndex가 0으로 리셋됨
                if (context.comboWindowTimer <= 0f)
                {
                    context.comboWindowTimer = CombatConstants.ComboWindowDuration;
                }

                // ★ 자연 완료 시 버퍼 클리어
                //   캔슬 윈도우를 놓치고 모션이 끝까지 재생된 경우,
                //   광클로 쌓인 버퍼 입력이 Idle에서 즉시 소비되어
                //   캔슬 윈도우를 우회하는 것을 방지한다.
                //   → 캔슬 윈도우 내에서만 콤보 체인이 연결됨.
                fsm.InputBuffer.Clear();

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
                // ★ 자연 완료 시 버퍼 클리어 (캔슬 윈도우 우회 방지)
                fsm.InputBuffer.Clear();
                fsm.TransitionTo<IdleState>();
            }
        }

        // ═══════════════════════════════════════════════════════
        //  입력 처리 (공통)
        // ═══════════════════════════════════════════════════════

        public override void HandleInput(InputData input)
        {
            // 워핑 중 입력은 버퍼에만 저장
            if (isWarpActive)
            {
                Debug.Log($"[Strike][FLOW] INPUT {input.Type} → 버퍼(워핑중) wallF:{context.stateFrameCounter}");
                fsm.InputBuffer.BufferInput(input);
                return;
            }

            int frame = context.stateFrameCounter;
            int animFrame = Mathf.FloorToInt(frame * currentPlaybackRate);

            // ★ 공격 계열 입력: isAttacking 중이면 무조건 버퍼 (모션 완주 보장)
            if (IsAttackInput(input.Type) && isAttacking)
            {
                bool isPending = notifyProcessor != null && notifyProcessor.IsPendingWindowActive;
                Debug.Log($"[Strike][FLOW] INPUT {input.Type} → 버퍼(isAttacking) wallF:{frame} animF:{animFrame} pending:{(isPending ? "Y" : "N")}");
                fsm.InputBuffer.BufferInput(input);
                return;
            }

            // 캔슬 윈도우가 아직 안 열렸으면 버퍼
            if (!context.canCancel)
            {
                Debug.Log($"[Strike][FLOW] INPUT {input.Type} → 버퍼(캔슬불가) wallF:{frame} animF:{animFrame}");
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
            string inputKey = InputTypeToString(input.Type);

            // ─── 노티파이 기반 캔슬 라우팅 (C안: CANCEL_WINDOW에 통합) ───
            if (useNotifyMode && notifyProcessor != null)
            {
                string cancelTarget = notifyProcessor.GetCancelTarget(inputKey);
                if (cancelTarget != null)
                {
                    ResolveCancelTarget(cancelTarget, input);
                    return;
                }
            }

            // ─── 레거시 폴백: 노티파이 모드가 아닌 경우 cancels[] 사용 ───
            if (!useNotifyMode)
            {
                string legacyTarget = currentAction?.GetCancelTarget(inputKey);
                if (legacyTarget != null)
                {
                    ResolveCancelTarget(legacyTarget, input);
                    return;
                }
            }

            // ─── 라우팅 없으면 기본 분기 ───
            switch (input.Type)
            {
                case InputType.Attack:
                {
                    context.comboChainIndex = (context.comboChainIndex + 1) % MaxComboChain;
                    ResolveNextComboAttack(input);
                    break;
                }

                case InputType.Execute:
                {
                    // 처형 체크: 저HP 적이 근처에 있으면 처형 발동
                    Vector2 pos = GetPos();
                    float dir = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
                    var execTarget = ExecutionSystem.FindExecutionTarget(
                        pos, context.activeEnemies, context.comboCount, dir);
                    if (execTarget != null)
                    {
                        context.executionTarget = execTarget;
                        fsm.TransitionTo<ExecutionState>();
                    }
                    // 처형 대상 없으면 무시
                    break;
                }

                case InputType.Heavy:
                    fsm.TransitionTo<GuardState>();
                    break;

                case InputType.Dodge:
                    fsm.TransitionTo<DodgeState>();
                    break;

                default:
                    fsm.InputBuffer.BufferInput(input);
                    break;
            }
        }

        /// <summary>
        /// 캔슬 라우팅에서 결정된 다음 액션으로 전환.
        /// LightAtk 계열은 콤보 체인 인덱스를 갱신한 뒤 워핑 판정을 거침.
        /// Dodge 등은 직접 상태 전환.
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
                case "DodgeBack":
                case "DodgeFront":
                    fsm.TransitionTo<DodgeState>();
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
                case InputType.Huxley:  return "Huxley";
                case InputType.Execute: return "Execute";
                default:                return type.ToString();
            }
        }

        public override void OnHit(HitData hitData)
        {
            // 워핑 중 무적
            if (isWarpActive) return;
            base.OnHit(hitData);
        }

        /// <summary>
        /// 프리플로우 콤보 체인: 다음 공격 시 타겟 재선택.
        /// ★ comboChainIndex는 이 함수 호출 전에 반드시 세팅되어 있어야 함.
        ///   - ResolveCancelTarget에서 명시적으로 세팅하거나
        ///   - HandleBufferedInput 기본 분기에서 직접 증가
        /// ★ 워핑은 WARP 노티파이가 처리하므로 WarpState 분기 제거됨.
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
                context.currentTarget = target.GetTransform();
            else
                context.currentTarget = null;

            // 워핑은 WARP 노티파이가 처리 → 무조건 StrikeState 진입
            fsm.TransitionTo<StrikeState>();
        }

        // ═══════════════════════════════════════════════════════
        //  인라인 워핑 (WARP 노티파이 트리거)
        // ═══════════════════════════════════════════════════════

        /// <summary>WARP 노티파이로 인라인 워핑 시작</summary>
        private void StartInlineWarp(ActionNotify warpNotify)
        {
            // 자동 타겟 재선택
            if (warpNotify.warpAutoTarget)
            {
                Vector2 playerPos = GetPos();
                float inputDir = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
                var target = fsm.TargetSelector.SelectTarget(
                    playerPos, context.activeEnemies, inputDir);
                if (target != null)
                    context.currentTarget = target.GetTransform();
            }

            // 타겟 없으면 워핑 스킵 (헛스윙)
            if (context.currentTarget == null) return;

            Vector2 startPos = GetPos();
            Vector2 targetPos = (Vector2)context.currentTarget.position;
            float dist = Vector2.Distance(startPos, targetPos);

            // ★ 발동 거리 체크 (노티파이 파라미터)
            float minRange = warpNotify.warpMinRange > 0f
                ? warpNotify.warpMinRange : ActionNotify.DefaultWarpMinRange;
            float maxRange = warpNotify.warpMaxRange;

            Debug.Log($"[Strike] WARP 거리 체크 — dist:{dist:F3}m minRange:{minRange:F3}m({minRange*100f:F0}cm) " +
                $"maxRange:{maxRange:F3}m({maxRange*100f:F0}cm) JSON.warpMinRange:{warpNotify.warpMinRange:F4}");

            // 최소 거리 이내 → 워핑 스킵, 방향만 전환
            if (dist <= minRange)
            {
                float d = Mathf.Sign(targetPos.x - startPos.x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (d >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
                facing = d;
                return;
            }

            // 최대 거리 밖 → 워핑 스킵 (0=무제한)
            if (maxRange > 0f && dist > maxRange)
            {
                float d = Mathf.Sign(targetPos.x - startPos.x);
                Vector3 scale = context.playerTransform.localScale;
                scale.x = Mathf.Abs(scale.x) * (d >= 0 ? 1f : -1f);
                context.playerTransform.localScale = scale;
                facing = d;
                return;
            }

            // 워핑 시작
            isWarpActive = true;
            context.isWarping = true;
            context.isInvulnerable = warpNotify.warpInvincible;

            warpStartPos = startPos;

            // 도착 위치 계산 (노티파이 파라미터)
            float dir = Mathf.Sign(targetPos.x - startPos.x);
            float offsetX = warpNotify.warpOffsetX;
            // ★ offsetX가 0이면 기본값 사용 (JsonUtility 누락 필드 대응)
            if (Mathf.Approximately(offsetX, 0f))
                offsetX = ActionNotify.DefaultWarpOffsetX;
            warpEndPos = new Vector2(
                targetPos.x + dir * offsetX,
                startPos.y + warpNotify.warpOffsetY
            );

            // 방향 전환
            Vector3 sc = context.playerTransform.localScale;
            sc.x = Mathf.Abs(sc.x) * (dir >= 0 ? 1f : -1f);
            context.playerTransform.localScale = sc;
            facing = dir;

            // 워핑 시간 계산 (우선순위: Speed > Duration > 자동)
            float warpDist = Vector2.Distance(warpStartPos, warpEndPos);
            if (warpNotify.warpSpeed > 0f)
            {
                // ★ 속도 모드: 거리 / 속도 = 시간
                activeWarpDuration = warpDist / warpNotify.warpSpeed;
                float minD = warpNotify.warpMinDuration > 0f
                    ? warpNotify.warpMinDuration : ActionNotify.DefaultWarpMinDuration;
                activeWarpDuration = Mathf.Max(activeWarpDuration, minD);
            }
            else if (warpNotify.warpDuration > 0f)
            {
                // 고정 시간 모드
                activeWarpDuration = warpNotify.warpDuration;
            }
            else
            {
                // 자동 계산: 거리 비례
                float minD = warpNotify.warpMinDuration > 0f
                    ? warpNotify.warpMinDuration : ActionNotify.DefaultWarpMinDuration;
                float maxD = warpNotify.warpMaxDuration > 0f
                    ? warpNotify.warpMaxDuration : ActionNotify.DefaultWarpMaxDuration;
                activeWarpDuration = Mathf.Lerp(minD, maxD, warpDist / CombatConstants.MaxWarpDistance);
                activeWarpDuration = Mathf.Max(activeWarpDuration, minD);
            }

            activeWarpEaseType = warpNotify.warpEaseType;
            warpTimer = 0f;

            // 히트박스 비활성 (워핑 중 공격 판정 방지)
            hitbox?.Deactivate();
            UnsubscribeHitbox();

            // 시각 피드백: 시안색
            if (spriteRenderer != null)
            {
                warpOriginalColor = spriteRenderer.color;
                spriteRenderer.color = WarpColor;
            }

            // 속도 초기화 (Kinematic 직접 이동)
            if (context.playerRigidbody != null)
                context.playerRigidbody.linearVelocity = Vector2.zero;

            Debug.Log($"[Strike] WARP START — dist:{dist:F1} dur:{activeWarpDuration:F3} " +
                $"ease:{activeWarpEaseType} offset:({warpNotify.warpOffsetX:F1},{warpNotify.warpOffsetY:F1})");
        }

        /// <summary>인라인 워핑 매 프레임 업데이트</summary>
        private void UpdateInlineWarp(float deltaTime)
        {
            warpTimer += deltaTime;
            float t = Mathf.Clamp01(warpTimer / activeWarpDuration);
            float eased = ActionNotify.ApplyWarpEasing(t, activeWarpEaseType);

            Vector2 newPos = Vector2.Lerp(warpStartPos, warpEndPos, eased);
            MoveTo(newPos);

            if (t >= 1f)
            {
                // 워핑 완료
                isWarpActive = false;
                context.isWarping = false;
                context.isInvulnerable = false;

                // 색상 복원
                if (spriteRenderer != null)
                    spriteRenderer.color = warpOriginalColor;

                Debug.Log($"[Strike] WARP END");
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

            // 노티파이 모드: damageScale + 히트 리액션 데이터 조립
            if (useNotifyMode && notifyProcessor != null)
            {
                hitData.DamageMultiplier *= notifyProcessor.DamageScale;

                // ★ 히트 리액션 데이터 조립: 프리셋 기본값 + 노티파이 오프셋
                var cn = notifyProcessor.ActiveCollisionNotify;
                if (cn != null)
                {
                    var hitType = (HitType)cn.hitType;
                    var preset = (HitPreset)cn.hitPreset;
                    var facing = (HitFacing)cn.hitFacing;
                    bool flip = cn.forceFlip;
                    var knockDirType = (HitKnockDirection)cn.hitKnockDirection;

                    // ★ 넉백 방향 결정
                    if (knockDirType == HitKnockDirection.Attacker)
                    {
                        // 공격자가 바라보는 방향으로 넉백 (워핑 후 위치 기반 계산은 불안정)
                        float attackerFacing = Mathf.Sign(fsm.transform.localScale.x);
                        hitData.KnockbackDirection = new Vector2(attackerFacing, 0f);
                    }
                    else if (knockDirType == HitKnockDirection.Defender)
                    {
                        // 피격자가 바라보는 방향으로 넉백
                        float defenderFacing = Mathf.Sign(target.GetTransform().localScale.x);
                        hitData.KnockbackDirection = new Vector2(defenderFacing, 0f);
                    }

                    // ★ 그로기 타입 전달
                    hitData.GroggyType = cn.groggyType;

                    if (hitType == HitType.Knockdown)
                    {
                        var baseData = BattleSettings.GetKnockdownPreset(preset);
                        hitData.Reaction = HitReactionData.CreateKnockdown(
                            baseData.WithOffset(cn.knockLaunchOffset, cn.knockAirTimeOffset, cn.knockDistanceOffset, cn.knockDownTimeOffset),
                            facing, flip, knockDirType);
                    }
                    else
                    {
                        var baseData = BattleSettings.GetFlinchPreset(preset);
                        hitData.Reaction = HitReactionData.CreateFlinch(
                            baseData.WithOffset(cn.flinchPushOffset, cn.flinchFreezeOffset, cn.flinchHitStopOffset),
                            facing, flip, knockDirType);

                        // ★ 히트스탑 적용 (공격자 측 — Flinch만)
                        float hitStopFrames = hitData.Reaction.flinch.hitStop;
                        if (hitStopFrames > 0f)
                        {
                            hitStopRemaining = hitStopFrames * CombatConstants.FrameDuration;
                        }
                    }
                }
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
