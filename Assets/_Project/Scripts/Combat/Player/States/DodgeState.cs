using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.HitReaction;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Dodge 상태: 회피 대시.
    /// ★ 노티파이 모드: 액션 테이블의 STARTUP + CANCEL_WINDOW 노티파이로 제어.
    ///   - I-Frame: ActionEntry.startup~startup+active 구간 (코드가 해석)
    ///   - 캔슬 윈도우: CANCEL_WINDOW 노티파이의 startFrame~endFrame
    ///   - 재생 배율: ActionEntry.playbackRate (AnimatorController와 동기화)
    ///
    /// ★ 레거시 모드: 노티파이 없으면 기존 normalizedTime 기반 동작 (폴백).
    ///
    /// REPLACED 원작의 🔴빨간 인디케이터 대응 액션.
    /// </summary>
    public class DodgeState : CombatState
    {
        public override string StateName => "Dodge";

        // ─── 액션 테이블 ───
        private const string ActorId = "PC_Hero";
        private ActionEntry currentAction;
        private ActionNotifyProcessor notifyProcessor;
        private bool useNotifyMode;
        private float currentPlaybackRate = 1f;
        private int iframeStartFrame;   // = startup
        private int iframeEndFrame;     // = startup + active
        private int totalWallFrames;

        // ─── 상태 변수 ───
        private Vector2 dodgeDirection;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private bool dodgeAttackUsed;
        private float stateElapsedTime;
        private RootMotionCanceller rootMotionCanceller;
        private bool isForwardDodge;       // true: 전방 대시, false: 백대시
        private string animStateName;      // "Dodge" 또는 "DodgeForward"

        // ─── 레거시 모드 전용 ───
        private bool animStarted;

        // ─── 시각 피드백 ───
        private static readonly Color DodgeColor = new Color(0.3f, 1f, 0.3f, 0.6f);

        public override void Enter()
        {
            base.Enter();
            dodgeAttackUsed = false;
            stateElapsedTime = 0f;
            animStarted = false;

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

            // ─── 액션 테이블 로드 ───
            string actionId = isForwardDodge ? "DodgeFront" : "DodgeBack";
            currentAction = ActionTableManager.Instance?.GetAction(ActorId, actionId);

            if (currentAction != null && currentAction.HasNotifies)
            {
                // ★ 노티파이 모드
                useNotifyMode = true;
                notifyProcessor = new ActionNotifyProcessor(currentAction);
                currentPlaybackRate = currentAction.playbackRate > 0f ? currentAction.playbackRate : 1f;
                iframeStartFrame = currentAction.startup;
                iframeEndFrame = currentAction.startup + currentAction.active;

                int notifyTotal = notifyProcessor.TotalFrames;
                int legacyTotal = currentAction.TotalFrames;
                totalWallFrames = Mathf.CeilToInt(Mathf.Max(notifyTotal, legacyTotal) / currentPlaybackRate);

                // STARTUP 중 비무적 → iframeStartFrame에서 무적 전환
                context.isInvulnerable = false;
                context.canCancel = false;

                Debug.Log($"[Dodge] Enter — {actionId} NotifyMode Wall:{totalWallFrames} Rate:{currentPlaybackRate:F1} " +
                    $"IFrame:[{iframeStartFrame},{iframeEndFrame})");
            }
            else
            {
                // ★ 레거시 모드 (노티파이 없음)
                useNotifyMode = false;
                notifyProcessor = null;
                context.isInvulnerable = true; // 즉시 무적
                context.canCancel = false;

                Debug.Log($"[Dodge] Enter — {actionId} LegacyMode");
            }

            // 루트모션 활성화 → RootMotionCanceller가 클립의 delta를 rb에 적용
            rootMotionCanceller = context.playerAnimator.GetComponent<RootMotionCanceller>();
            if (rootMotionCanceller != null)
                rootMotionCanceller.UseRootMotion = true;

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                // 노티파이 모드에서는 STARTUP 중 색상 미변경 (iFrame 진입 시 변경)
                if (!useNotifyMode)
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

            if (useNotifyMode)
                UpdateNotifyMode(deltaTime);
            else
                UpdateLegacyMode(deltaTime);
        }

        // ═══════════════════════════════════════════════════════
        //  노티파이 모드: 액션 테이블 데이터로 제어
        // ═══════════════════════════════════════════════════════

        private void UpdateNotifyMode(float deltaTime)
        {
            int wallFrame = context.stateFrameCounter;
            int animFrame = Mathf.FloorToInt(wallFrame * currentPlaybackRate);
            notifyProcessor.Tick(animFrame);

            // ── I-Frame on/off (startup/active 기반) ──
            if (!context.isInvulnerable && animFrame >= iframeStartFrame && animFrame < iframeEndFrame)
            {
                context.isInvulnerable = true;
                if (spriteRenderer != null) spriteRenderer.color = DodgeColor;
            }
            else if (context.isInvulnerable && animFrame >= iframeEndFrame)
            {
                context.isInvulnerable = false;
                if (spriteRenderer != null) spriteRenderer.color = originalColor;
            }

            // ── 캔슬 윈도우 (ActionNotifyProcessor) ──
            context.canCancel = notifyProcessor.AnyCancelActive;

            // ── 이동 캔슬 ──
            if (context.canCancel && notifyProcessor.CanMoveCancel
                && context.lastInputDirection.sqrMagnitude > 0.01f)
            {
                fsm.TransitionTo<IdleState>();
                return;
            }

            // ── 버퍼 입력 처리 ──
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInputNotify(buffered);
                return;
            }

            // ── 프레임 완료 ──
            if (wallFrame >= totalWallFrames)
                fsm.TransitionTo<IdleState>();
        }

        /// <summary>노티파이 모드 버퍼 입력 처리 — cancel route 참조</summary>
        private void HandleBufferedInputNotify(InputData input)
        {
            string inputKey = InputTypeToString(input.Type);
            string cancelTarget = notifyProcessor.GetCancelTarget(inputKey);

            if (cancelTarget != null)
            {
                // 캔슬 경로에 따라 전환
                switch (cancelTarget)
                {
                    case "DodgeCounterAtk":
                        if (!dodgeAttackUsed)
                        {
                            dodgeAttackUsed = true;
                            context.IncrementCombo(2);
                            fsm.TransitionTo<StrikeState>();
                        }
                        break;
                    case "DodgeBack":
                    case "DodgeFront":
                    case "Dodge":
                        fsm.TransitionTo<DodgeState>();
                        break;
                    default:
                        fsm.TransitionTo<IdleState>();
                        break;
                }
            }
            else if (input.Type == InputType.Attack && notifyProcessor.CanSkillCancel)
            {
                // skillCancel이 열려있지만 nextAction이 비어있으면 기본 공격 전환
                if (!dodgeAttackUsed)
                {
                    dodgeAttackUsed = true;
                    context.IncrementCombo(2);
                    fsm.TransitionTo<StrikeState>();
                }
            }
            else
            {
                fsm.InputBuffer.BufferInput(input);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  레거시 모드: normalizedTime 기반 (노티파이 없을 때 폴백)
        // ═══════════════════════════════════════════════════════

        private void UpdateLegacyMode(float deltaTime)
        {
            // Animator가 대시 상태에 진입했는지 추적
            var stateInfo = context.playerAnimator.GetCurrentAnimatorStateInfo(0);
            bool isInDodgeAnim = stateInfo.IsName(animStateName);

            if (!animStarted && isInDodgeAnim)
            {
                animStarted = true;
            }

            // ★ 캔슬 윈도우 — normalizedTime 비율 기반 (레거시 폴백용 의도적 사용)
            #pragma warning disable 0618
            if (!context.canCancel && animStarted
                && stateInfo.normalizedTime >= context.dodgeCancelDelay)
            #pragma warning restore 0618
            {
                context.canCancel = true;
                context.isInvulnerable = false;

                // 색상 복원
                if (spriteRenderer != null)
                    spriteRenderer.color = originalColor;
            }

            // ★ 이동 캔슬: 캔슬 윈도우 중 방향키 입력 → 즉시 Idle(→Locomotion)
            if (context.canCancel && context.lastInputDirection.sqrMagnitude > 0.01f)
            {
                fsm.TransitionTo<IdleState>();
                return;
            }

            // 캔슬 입력 처리 (공격 등 버퍼)
            if (context.canCancel && fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInputLegacy(buffered);
                return;
            }

            // ★ 애니메이션 완료 → 상태 종료
            if (animStarted)
            {
                bool clipDone = isInDodgeAnim && stateInfo.normalizedTime >= 0.95f;
                bool transitioned = !isInDodgeAnim;

                if (clipDone || transitioned)
                    fsm.TransitionTo<IdleState>();
            }
        }

        /// <summary>레거시 모드 버퍼 입력 처리</summary>
        private void HandleBufferedInputLegacy(InputData input)
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

        // ═══════════════════════════════════════════════════════
        //  공통
        // ═══════════════════════════════════════════════════════

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

            // 정리
            notifyProcessor = null;
            currentAction = null;
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

            if (useNotifyMode)
                HandleBufferedInputNotify(input);
            else
                HandleBufferedInputLegacy(input);
        }

        public override void OnHit(HitData hitData)
        {
            // IFrame 중이면 회피 성공 → 피격 무시
            if (context.isInvulnerable) return;

            // Recovery 중 피격은 정상 처리
            base.OnHit(hitData);
        }

        // ─── 유틸 ───

        private static string InputTypeToString(InputType type)
        {
            switch (type)
            {
                case InputType.Attack:  return "Attack";
                case InputType.Heavy:   return "Heavy";
                case InputType.Dodge:   return "Dodge";
                case InputType.Huxley:  return "Huxley";
                default:                return type.ToString();
            }
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
