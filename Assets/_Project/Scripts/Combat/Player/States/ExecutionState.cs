using UnityEngine;
using FreeFlowHero.Combat.Core;
namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// Execution 상태: 처형 시네마틱.
    /// 저HP 적 근처에서 Attack 입력 시 진입.
    /// 5페이즈 시네마틱: SlowMo → Warp → Attack → Finish → Recovery
    ///
    /// ★ Time.timeScale 조작 시 unscaledDeltaTime 사용
    /// </summary>
    public class ExecutionState : CombatState
    {
        public override string StateName => "Execution";

        // ─── 페이즈 ───
        private enum Phase { SlowMo, Warp, Attack, Finish, Recovery }
        private Phase currentPhase;

        // ─── 타이머 (unscaled) ───
        private float unscaledTimer;
        private int phaseFrame; // 현재 페이즈 내 프레임 (unscaled 기준)

        // ─── 처형 대상 ───
        private ICombatTarget executionTarget;
        private int motionIndex;

        // ─── 워프 ───
        private Vector2 warpStartPos;
        private Vector2 warpEndPos;

        // ─── 카메라 ───
        private float originalOrthoSize;
        private Vector3 originalCameraPos;
        private bool cameraStored;

        // ─── 데미지 적용 플래그 ───
        private bool damageApplied;
        private bool aoeApplied;


        // ─── 시각 피드백 ───
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private static readonly Color ExecutionColor = new Color(1f, 0f, 1f, 0.8f); // 마젠타

        // ─── Animator 해시 ───
        private static readonly int ExecutionIndexHash = Animator.StringToHash("ExecutionIndex");

        public override void Enter()
        {
            base.Enter();

            // 타겟 캐시
            executionTarget = context.executionTarget;
            if (executionTarget == null || !executionTarget.IsTargetable)
            {
                fsm.TransitionTo<IdleState>();
                return;
            }

            // 초기화
            currentPhase = Phase.SlowMo;
            unscaledTimer = 0f;
            phaseFrame = 0;
            damageApplied = false;
            aoeApplied = false;
            cameraStored = false;

            // 무적
            context.isInvulnerable = true;
            context.canCancel = false;

            // 방향 전환
            Vector2 targetPos = executionTarget.GetTransform().position;
            float dir = Mathf.Sign(targetPos.x - GetPos().x);
            Vector3 scale = context.playerTransform.localScale;
            scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
            context.playerTransform.localScale = scale;

            // 적 행동 중단
            executionTarget.InterruptAction();

            // 이벤트
            CombatEventBus.Publish(new OnExecutionStart { Target = executionTarget });

            // 모션 선택 (가중치 랜덤)
            string selectedActionId = ExecutionSystem.GetRandomMotionActionId();
            // ActionId에서 인덱스 추출: "Execution1" → 0, "Execution2" → 1, "Execution3" → 2
            motionIndex = 0;
            if (selectedActionId != null && selectedActionId.Length > 9)
            {
                int.TryParse(selectedActionId.Substring(9), out int tier);
                motionIndex = Mathf.Max(0, tier - 1);
            }

            // 애니메이터
            SafeSetInt(ExecutionIndexHash, motionIndex);
            SafeSetTrigger("Execution");

            // 시각 피드백
            spriteRenderer = context.playerTransform.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
                spriteRenderer.color = ExecutionColor;
            }

            // 카메라 저장
            if (Camera.main != null)
            {
                originalOrthoSize = Camera.main.orthographicSize;
                originalCameraPos = Camera.main.transform.position;
                cameraStored = true;
            }

            // 슬로우모션 시작
            Time.timeScale = CombatConstants.ExecutionSlowmoScale;
        }

        public override void Update(float deltaTime)
        {
            // ★ base.Update 호출 (프레임 카운터는 timeScale 영향 받지만 괜찮음)
            base.Update(deltaTime);

            // ★ unscaled 타이머로 페이즈 관리
            unscaledTimer += Time.unscaledDeltaTime;
            phaseFrame = Mathf.FloorToInt(unscaledTimer / CombatConstants.FrameDuration);

            // 타겟 소실 체크
            if (executionTarget == null || !executionTarget.IsTargetable)
            {
                if (currentPhase != Phase.Recovery && currentPhase != Phase.Finish)
                {
                    EnterPhase(Phase.Recovery);
                    return;
                }
            }

            switch (currentPhase)
            {
                case Phase.SlowMo:
                    UpdateSlowMo();
                    break;
                case Phase.Warp:
                    UpdateWarp();
                    break;
                case Phase.Attack:
                    UpdateAttack();
                    break;
                case Phase.Finish:
                    UpdateFinish();
                    break;
                case Phase.Recovery:
                    UpdateRecovery();
                    break;
            }

            // 카메라 줌 (SlowMo/Warp/Attack 중)
            UpdateCameraZoom();
        }

        public override void Exit()
        {
            base.Exit();
            context.isInvulnerable = false;
            context.isWarping = false;
            context.executionTarget = null;

            // timeScale 안전 복원
            Time.timeScale = 1.0f;

            // 카메라 복원
            RestoreCamera();

            // 색상 복원
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // ★ Idle 애니메이션으로 복귀 (처형 모션 루프 방지)
            SafeSetTrigger("Idle");

            // 이벤트
            CombatEventBus.Publish(new OnExecutionEnd());
        }

        /// <summary>처형 중 완전 무적</summary>
        public override void OnHit(HitData hitData)
        {
            // 무시 — 처형 중 피격 불가
        }

        public override void HandleInput(InputData input)
        {
            if (context.canCancel)
            {
                HandleBufferedInput(input);
                return;
            }
            fsm.InputBuffer.BufferInput(input);
        }

        // ═══════════════════════════════════════════════════════
        //  페이즈 업데이트
        // ═══════════════════════════════════════════════════════

        private void UpdateSlowMo()
        {
            if (phaseFrame >= CombatConstants.ExecutionSlowmoFrames)
            {
                EnterPhase(Phase.Warp);
            }
        }

        private void UpdateWarp()
        {
            int warpFrame = phaseFrame;
            int warpFrames = CombatConstants.ExecutionWarpFrames;

            if (warpFrame == 0)
            {
                // 워프 시작
                context.isWarping = true;
                warpStartPos = GetPos();
                if (executionTarget != null)
                {
                    Vector2 targetPos = executionTarget.GetTransform().position;
                    float facing = context.playerTransform.localScale.x >= 0 ? 1f : -1f;
                    warpEndPos = targetPos + new Vector2(-facing * 0.5f, 0f);
                }
                else
                {
                    warpEndPos = warpStartPos;
                }
            }

            // 보간 이동
            float t = Mathf.Clamp01((float)warpFrame / warpFrames);
            // CubicOut 이징
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Vector2 pos = Vector2.Lerp(warpStartPos, warpEndPos, eased);
            MoveTo(pos);

            if (warpFrame >= warpFrames)
            {
                context.isWarping = false;
                // timeScale 복원 (공격 모션은 정상 속도로)
                Time.timeScale = 1.0f;

                // ★ FBX 루트모션은 사용하지 않음 (EEJANAI 클립은 Bake Into Pose 미설정)
                //   대신 ROOT_MOTION 노티파이로 코드 제어 이동 사용.

                EnterPhase(Phase.Attack);
            }
        }

        private void UpdateAttack()
        {
            int attackFrame = phaseFrame;

            // 타격 시점: 공격 프레임의 60% 지점
            int hitFrame = Mathf.FloorToInt(CombatConstants.ExecutionAttackFrames * 0.6f);
            if (!damageApplied && attackFrame >= hitFrame)
            {
                damageApplied = true;
                if (executionTarget != null && executionTarget.IsTargetable)
                {
                    Vector2 attackerPos = GetPos();
                    Vector2 targetPos = executionTarget.GetTransform().position;
                    var hitData = HitData.CreateExecution(
                        attackerPos, targetPos, context.comboCount);
                    executionTarget.TakeHit(hitData);
                    fsm.TargetSelector.RegisterHit(executionTarget);
                }
            }

            if (attackFrame >= CombatConstants.ExecutionAttackFrames)
            {
                EnterPhase(Phase.Finish);
            }
        }

        private void UpdateFinish()
        {
            int finishFrame = phaseFrame;

            // AOE 데미지 (1회)
            if (!aoeApplied)
            {
                aoeApplied = true;
                ApplyAOEDamage();

                // 콤보 + 헉슬리 보너스
                context.IncrementCombo(5);
                context.ChargeHuxley(CombatConstants.ExecutionHuxleyCharge);
            }

            if (finishFrame >= CombatConstants.ExecutionFinishFrames)
            {
                EnterPhase(Phase.Recovery);
            }
        }

        private void UpdateRecovery()
        {
            context.canCancel = true;
            context.isInvulnerable = false;

            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;

            // 버퍼 소비
            if (fsm.InputBuffer.HasInput)
            {
                var buffered = fsm.InputBuffer.Consume();
                HandleBufferedInput(buffered);
                return;
            }

            if (phaseFrame >= CombatConstants.ExecutionRecoveryFrames)
            {
                fsm.TransitionTo<IdleState>();
            }
        }

        // ═══════════════════════════════════════════════════════
        //  내부 유틸리티
        // ═══════════════════════════════════════════════════════

        private void EnterPhase(Phase newPhase)
        {
            currentPhase = newPhase;
            unscaledTimer = 0f;
            phaseFrame = 0;
        }

        private void ApplyAOEDamage()
        {
            Vector2 center = GetPos();
            float radius = CombatConstants.ExecutionAOERadius;
            float damage = CombatConstants.ExecutionAOEDamage;

            for (int i = 0; i < context.activeEnemies.Count; i++)
            {
                var enemy = context.activeEnemies[i];
                if (enemy == null || !enemy.IsTargetable) continue;
                if (enemy == executionTarget) continue; // 처형 대상 제외 (이미 처리됨)

                Vector2 enemyPos = enemy.GetTransform().position;
                if (Vector2.Distance(center, enemyPos) <= radius)
                {
                    var hitData = HitData.CreateAOE(center, enemyPos, damage);
                    enemy.TakeHit(hitData);
                }
            }
        }

        private void UpdateCameraZoom()
        {
            if (!cameraStored || Camera.main == null) return;

            float targetSize;
            if (currentPhase == Phase.Recovery)
            {
                // 카메라 복원
                targetSize = originalOrthoSize;
            }
            else if (currentPhase == Phase.SlowMo || currentPhase == Phase.Warp
                     || currentPhase == Phase.Attack)
            {
                targetSize = originalOrthoSize - CombatConstants.ExecutionCameraZoom;
            }
            else
            {
                targetSize = originalOrthoSize;
            }

            Camera.main.orthographicSize = Mathf.Lerp(
                Camera.main.orthographicSize, targetSize,
                8f * Time.unscaledDeltaTime);

            // 처형 대상 중심으로 카메라 이동
            if (executionTarget != null && currentPhase != Phase.Recovery)
            {
                Vector2 midPoint = Vector2.Lerp(
                    GetPos(), (Vector2)executionTarget.GetTransform().position, 0.5f);
                Vector3 camTarget = new Vector3(midPoint.x, midPoint.y,
                    Camera.main.transform.position.z);
                Camera.main.transform.position = Vector3.Lerp(
                    Camera.main.transform.position, camTarget,
                    6f * Time.unscaledDeltaTime);
            }
        }

        private void RestoreCamera()
        {
            if (!cameraStored || Camera.main == null) return;
            Camera.main.orthographicSize = originalOrthoSize;
            Camera.main.transform.position = originalCameraPos;
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

        private void SafeSetTrigger(string triggerName)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetTrigger(triggerName); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Execution] Animator 오류 무시: {e.Message}");
            }
        }

        private void SafeSetInt(int hash, int value)
        {
            if (context.playerAnimator == null) return;
            if (context.playerAnimator.runtimeAnimatorController == null) return;
            try { context.playerAnimator.SetInteger(hash, value); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Execution] Animator 오류 무시: {e.Message}");
            }
        }
    }
}
