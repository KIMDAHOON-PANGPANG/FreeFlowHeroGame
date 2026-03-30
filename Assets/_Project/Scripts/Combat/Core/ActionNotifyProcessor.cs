using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 액션 노티파이 런타임 프로세서.
    /// 매 프레임 ActionEntry의 notifies[]를 폴링하여
    /// COLLISION(히트박스), CANCEL_WINDOW(캔슬 플래그), STARTUP(이동속도) 상태를 제공한다.
    ///
    /// ★ 두 가지 실행 모드:
    ///   - 스테이트 모드 (isInstance=false): startFrame~endFrame 구간 동안 매 프레임 활성.
    ///     언리얼의 AnimNotifyState와 동일.
    ///   - 인스턴스 모드 (isInstance=true): startFrame에서 단 1회만 발화.
    ///     언리얼의 AnimNotify와 동일.
    ///
    /// 사용법:
    ///   var proc = new ActionNotifyProcessor(actionEntry);
    ///   // 매 프레임:
    ///   proc.Tick(frameCounter);
    ///   if (proc.IsCollisionActive) hitbox.Activate(); else hitbox.Deactivate();
    ///   if (proc.CanSkillCancel) { ... }
    ///   // 인스턴스 이벤트 소비:
    ///   foreach (var evt in proc.ConsumeInstanceEvents()) { ... }
    ///
    /// HasNotifies == false인 경우 이 프로세서를 사용하지 말고 레거시 로직으로 폴백할 것.
    /// </summary>
    public class ActionNotifyProcessor
    {
        // ─── 입력 데이터 ───
        private readonly ActionEntry action;

        // ─── COLLISION 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 COLLISION 노티파이(스테이트)가 활성인지</summary>
        public bool IsCollisionActive { get; private set; }

        /// <summary>활성 COLLISION 노티파이의 damageScale (비활성 시 1.0)</summary>
        public float DamageScale { get; private set; } = 1f;

        /// <summary>활성 COLLISION 노티파이의 hitboxId (비활성 시 "default")</summary>
        public string HitboxId { get; private set; } = "default";

        /// <summary>COLLISION이 이번 프레임에 새로 활성화되었는지 (진입 에지)</summary>
        public bool CollisionJustStarted { get; private set; }

        /// <summary>COLLISION이 이번 프레임에 비활성화되었는지 (퇴출 에지)</summary>
        public bool CollisionJustEnded { get; private set; }

        /// <summary>활성 COLLISION의 히트 리액션 노티파이 (리액션 데이터 조립용)</summary>
        public ActionNotify ActiveCollisionNotify { get; private set; }

        // ─── CANCEL_WINDOW 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 CANCEL_WINDOW 노티파이(스테이트)가 활성인지</summary>
        public bool IsCancelWindowActive { get; private set; }

        /// <summary>스킬(공격 콤보) 캔슬 허용</summary>
        public bool CanSkillCancel { get; private set; }

        /// <summary>이동 캔슬 허용</summary>
        public bool CanMoveCancel { get; private set; }

        /// <summary>회피 캔슬 허용</summary>
        public bool CanDodgeCancel { get; private set; }

        /// <summary>캔슬 윈도우의 Attack 전이 대상 (= nextAction)</summary>
        public string CancelNextAction { get; private set; } = "";

        /// <summary>캔슬 윈도우의 Heavy 전이 대상</summary>
        public string CancelHeavyNext { get; private set; } = "";

        /// <summary>캔슬 윈도우의 Dodge 전이 대상</summary>
        public string CancelDodgeNext { get; private set; } = "";

        // ─── PENDING_WINDOW 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 PENDING_WINDOW 노티파이(스테이트)가 활성인지</summary>
        public bool IsPendingWindowActive { get; private set; }

        /// <summary>PENDING_WINDOW가 이번 프레임에 새로 활성화되었는지 (진입 에지)</summary>
        public bool PendingWindowJustStarted { get; private set; }

        /// <summary>PENDING_WINDOW가 이번 프레임에 비활성화되었는지 (퇴출 에지)</summary>
        public bool PendingWindowJustEnded { get; private set; }

        /// <summary>이 액션에 PENDING_WINDOW 노티파이가 하나라도 존재하는지</summary>
        public bool HasPendingWindow { get; private set; }

        // ─── STARTUP 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 STARTUP 노티파이(스테이트)가 활성인지</summary>
        public bool IsStartupActive { get; private set; }

        /// <summary>활성 STARTUP 노티파이의 moveSpeed</summary>
        public float StartupMoveSpeed { get; private set; }

        // ─── ROOT_MOTION 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 ROOT_MOTION 노티파이가 활성인지</summary>
        public bool IsRootMotionActive { get; private set; }

        /// <summary>현재 프레임의 루트모션 이동 속도 (커브 보간값 * scale)</summary>
        public float RootMotionSpeed { get; private set; }

        // ─── ROOT_MOTION 내부 캐시 ───
        private AnimationCurve rootMotionCurve;
        private ActionNotify activeRootMotionNotify;

        // ─── HOMING 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 HOMING 노티파이가 활성인지</summary>
        public bool IsHomingActive { get; private set; }

        /// <summary>활성 HOMING의 회전 속도 제한 (도/초, 0=즉시 스냅)</summary>
        public float HomingTurnRate { get; private set; }

        // ─── GUARD_SUCCESS 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 GUARD_SUCCESS 노티파이(스테이트)가 활성인지</summary>
        public bool IsGuardSuccessActive { get; private set; }

        // ─── WARP 상태 (인스턴스 모드) ───
        /// <summary>이번 프레임에 WARP 노티파이가 발화되었는지</summary>
        public bool IsWarpTriggered { get; private set; }

        /// <summary>발화된 WARP 노티파이 (파라미터 참조용)</summary>
        public ActionNotify WarpNotify { get; private set; }

        // ─── 인스턴스 이벤트 (1회 발화) ───
        /// <summary>이번 프레임에 발화된 인스턴스 노티파이 목록</summary>
        public IReadOnlyList<ActionNotify> InstanceEvents => instanceEvents;
        private readonly List<ActionNotify> instanceEvents = new();

        /// <summary>이번 Tick에서 인스턴스 이벤트가 발생했는지</summary>
        public bool HasInstanceEvents => instanceEvents.Count > 0;

        // ─── 총 프레임 ───
        /// <summary>노티파이 기반 총 프레임 수 (모션 종료 판정용)</summary>
        public int TotalFrames => action.NotifyTotalFrames;

        // ─── 내부 상태 ───
        private bool prevCollisionActive;
        private bool prevPendingActive;
        private int lastFrame = -1;
        // 인스턴스 노티파이 발화 추적 (같은 노티파이 중복 발화 방지)
        private readonly HashSet<int> firedInstanceIndices = new();

        /// <summary>프로세서 생성. action은 반드시 HasNotifies == true인 것만 전달할 것.</summary>
        public ActionNotifyProcessor(ActionEntry action)
        {
            this.action = action;

            // ★ 이 액션에 PENDING_WINDOW가 존재하는지 미리 확인
            HasPendingWindow = false;
            if (action.notifies != null)
            {
                foreach (var n in action.notifies)
                {
                    if (!n.disabled && n.TypeEnum == NotifyType.PENDING_WINDOW)
                    {
                        HasPendingWindow = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 매 프레임 호출. 현재 프레임 번호를 기반으로 모든 노티파이 상태를 갱신한다.
        /// </summary>
        /// <param name="frame">현재 프레임 (0-based, 60fps 기준)</param>
        public void Tick(int frame)
        {
            // ★ 에지 플래그는 매 호출마다 리셋 (1프레임 전용 신호)
            //   playbackRate < 1일 때 여러 벽시계 프레임이 같은 animFrame에 매핑되므로,
            //   early return 전에 반드시 클리어해야 중복 발화를 방지함.
            CollisionJustStarted = false;
            CollisionJustEnded = false;
            PendingWindowJustStarted = false;
            PendingWindowJustEnded = false;

            // 같은 프레임 중복 호출 방지
            if (frame == lastFrame) return;
            lastFrame = frame;

            // 이전 상태 저장 (에지 감지)
            prevCollisionActive = IsCollisionActive;
            prevPendingActive = IsPendingWindowActive;

            // ── 스테이트 리셋 (매 프레임) ──
            IsCollisionActive = false;
            DamageScale = 1f;
            HitboxId = "default";
            ActiveCollisionNotify = null;
            IsPendingWindowActive = false;
            IsCancelWindowActive = false;
            CanSkillCancel = false;
            CanMoveCancel = false;
            CanDodgeCancel = false;
            CancelNextAction = "";
            CancelHeavyNext = "";
            CancelDodgeNext = "";
            IsStartupActive = false;
            StartupMoveSpeed = 0f;
            IsRootMotionActive = false;
            RootMotionSpeed = 0f;
            IsHomingActive = false;
            HomingTurnRate = 0f;
            IsGuardSuccessActive = false;
            IsWarpTriggered = false;
            WarpNotify = null;

            // ── 인스턴스 이벤트 초기화 ──
            instanceEvents.Clear();

            if (action.notifies == null) return;

            // ── 모든 노티파이 순회 ──
            for (int i = 0; i < action.notifies.Length; i++)
            {
                var n = action.notifies[i];
                if (n.disabled) continue;

                // ★ 인스턴스 모드: startFrame에서 1회만 발화
                if (n.isInstance)
                {
                    if (n.IsInstanceFrame(frame) && !firedInstanceIndices.Contains(i))
                    {
                        firedInstanceIndices.Add(i);
                        instanceEvents.Add(n);

                        // 인스턴스 모드라도 해당 프레임에 타입별 상태 적용 (1프레임짜리 스테이트처럼)
                        ApplyNotifyState(n);
                    }
                    continue;
                }

                // ★ 스테이트 모드: 구간 내 매 프레임 활성
                if (!n.ContainsFrame(frame)) continue;
                ApplyNotifyState(n);
            }

            // ── 에지 감지 ──
            CollisionJustStarted = IsCollisionActive && !prevCollisionActive;
            CollisionJustEnded = !IsCollisionActive && prevCollisionActive;
            PendingWindowJustStarted = IsPendingWindowActive && !prevPendingActive;
            PendingWindowJustEnded = !IsPendingWindowActive && prevPendingActive;
        }

        /// <summary>노티파이 타입에 따라 프로세서 상태 적용</summary>
        private void ApplyNotifyState(ActionNotify n)
        {
            switch (n.TypeEnum)
            {
                case NotifyType.STARTUP:
                    IsStartupActive = true;
                    StartupMoveSpeed = n.moveSpeed;
                    break;

                case NotifyType.COLLISION:
                    IsCollisionActive = true;
                    DamageScale = n.damageScale;
                    ActiveCollisionNotify = n;
                    if (!string.IsNullOrEmpty(n.hitboxId))
                        HitboxId = n.hitboxId;
                    break;

                case NotifyType.CANCEL_WINDOW:
                    IsCancelWindowActive = true;
                    if (n.skillCancel) CanSkillCancel = true;
                    if (n.moveCancel) CanMoveCancel = true;
                    if (n.dodgeCancel) CanDodgeCancel = true;
                    if (!string.IsNullOrEmpty(n.nextAction))
                        CancelNextAction = n.nextAction;
                    if (!string.IsNullOrEmpty(n.heavyNext))
                        CancelHeavyNext = n.heavyNext;
                    if (!string.IsNullOrEmpty(n.dodgeNext))
                        CancelDodgeNext = n.dodgeNext;
                    break;

                case NotifyType.PENDING_WINDOW:
                    IsPendingWindowActive = true;
                    break;

                case NotifyType.ROOT_MOTION:
                    IsRootMotionActive = true;
                    // 커브 캐시: 같은 노티파이면 재빌드 안 함
                    if (activeRootMotionNotify != n)
                    {
                        activeRootMotionNotify = n;
                        rootMotionCurve = n.BuildRootMotionCurve();
                    }
                    // normalized time 계산 → 커브 보간
                    if (rootMotionCurve != null && rootMotionCurve.length > 0 && n.Duration > 0)
                    {
                        float t = (float)(lastFrame - n.startFrame) / n.Duration;
                        t = Mathf.Clamp01(t);
                        RootMotionSpeed = rootMotionCurve.Evaluate(t) * n.rootMotionScale;
                    }
                    break;

                case NotifyType.WARP:
                    IsWarpTriggered = true;
                    WarpNotify = n;
                    break;

                case NotifyType.HOMING:
                    IsHomingActive = true;
                    HomingTurnRate = n.homingTurnRate;
                    break;

                case NotifyType.GUARD_SUCCESS:
                    IsGuardSuccessActive = true;
                    break;
            }
        }

        /// <summary>
        /// 특정 입력 타입에 대한 캔슬 가능 여부 확인.
        /// CANCEL_WINDOW가 활성이고 해당 플래그가 true면 캔슬 허용.
        /// </summary>
        public bool CanCancelWith(string inputType)
        {
            if (!IsCancelWindowActive) return false;

            switch (inputType)
            {
                case "Attack":  return CanSkillCancel;
                case "Heavy":   return CanSkillCancel;
                case "Dodge":   return CanDodgeCancel;
                case "Move":    return CanMoveCancel;
                default:        return false;
            }
        }

        /// <summary>
        /// 특정 입력 타입에 대한 캔슬 전이 대상 액션 ID 반환.
        /// CANCEL_WINDOW의 입력별 라우팅 필드를 조회한다.
        /// 해당 입력이 캔슬 불가하면 null 반환.
        /// 캔슬 가능하지만 전이 대상이 비어있으면 기본 액션 ID 반환.
        /// </summary>
        public string GetCancelTarget(string inputType)
        {
            if (!IsCancelWindowActive) return null;

            switch (inputType)
            {
                case "Attack":
                    if (!CanSkillCancel) return null;
                    return !string.IsNullOrEmpty(CancelNextAction) ? CancelNextAction : null;
                case "Heavy":
                    if (!CanSkillCancel) return null;
                    return !string.IsNullOrEmpty(CancelHeavyNext) ? CancelHeavyNext : "HeavyAtk";
                case "Dodge":
                    if (!CanDodgeCancel) return null;
                    return !string.IsNullOrEmpty(CancelDodgeNext) ? CancelDodgeNext : "Dodge";
                case "Move":
                    return CanMoveCancel ? null : null;  // 이동 캔슬은 전이 대상 없음 (Idle 복귀)
                default:
                    return null;
            }
        }

        /// <summary>
        /// 현재 프레임에서 어떤 캔슬이든 허용되는지 (OR 합산).
        /// </summary>
        public bool AnyCancelActive =>
            IsCancelWindowActive && (CanSkillCancel || CanMoveCancel || CanDodgeCancel);
    }
}
