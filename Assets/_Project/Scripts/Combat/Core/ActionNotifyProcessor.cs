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

        // ─── CANCEL_WINDOW 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 CANCEL_WINDOW 노티파이(스테이트)가 활성인지</summary>
        public bool IsCancelWindowActive { get; private set; }

        /// <summary>스킬(공격 콤보) 캔슬 허용</summary>
        public bool CanSkillCancel { get; private set; }

        /// <summary>이동 캔슬 허용</summary>
        public bool CanMoveCancel { get; private set; }

        /// <summary>회피 캔슬 허용</summary>
        public bool CanDodgeCancel { get; private set; }

        /// <summary>카운터 캔슬 허용</summary>
        public bool CanCounterCancel { get; private set; }

        /// <summary>캔슬 윈도우의 nextAction (비어있으면 CancelRoute 참조)</summary>
        public string CancelNextAction { get; private set; } = "";

        // ─── STARTUP 상태 (스테이트 모드) ───
        /// <summary>현재 프레임에 STARTUP 노티파이(스테이트)가 활성인지</summary>
        public bool IsStartupActive { get; private set; }

        /// <summary>활성 STARTUP 노티파이의 moveSpeed</summary>
        public float StartupMoveSpeed { get; private set; }

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
        private int lastFrame = -1;
        // 인스턴스 노티파이 발화 추적 (같은 노티파이 중복 발화 방지)
        private readonly HashSet<int> firedInstanceIndices = new();

        /// <summary>프로세서 생성. action은 반드시 HasNotifies == true인 것만 전달할 것.</summary>
        public ActionNotifyProcessor(ActionEntry action)
        {
            this.action = action;
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

            // 같은 프레임 중복 호출 방지
            if (frame == lastFrame) return;
            lastFrame = frame;

            // 이전 상태 저장 (에지 감지)
            prevCollisionActive = IsCollisionActive;

            // ── 스테이트 리셋 (매 프레임) ──
            IsCollisionActive = false;
            DamageScale = 1f;
            HitboxId = "default";
            IsCancelWindowActive = false;
            CanSkillCancel = false;
            CanMoveCancel = false;
            CanDodgeCancel = false;
            CanCounterCancel = false;
            CancelNextAction = "";
            IsStartupActive = false;
            StartupMoveSpeed = 0f;

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
                    if (!string.IsNullOrEmpty(n.hitboxId))
                        HitboxId = n.hitboxId;
                    break;

                case NotifyType.CANCEL_WINDOW:
                    IsCancelWindowActive = true;
                    if (n.skillCancel) CanSkillCancel = true;
                    if (n.moveCancel) CanMoveCancel = true;
                    if (n.dodgeCancel) CanDodgeCancel = true;
                    if (n.counterCancel) CanCounterCancel = true;
                    if (!string.IsNullOrEmpty(n.nextAction))
                        CancelNextAction = n.nextAction;
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
                case "Counter": return CanCounterCancel;
                case "Move":    return CanMoveCancel;
                default:        return false;
            }
        }

        /// <summary>
        /// 현재 프레임에서 어떤 캔슬이든 허용되는지 (OR 합산).
        /// </summary>
        public bool AnyCancelActive =>
            IsCancelWindowActive && (CanSkillCancel || CanMoveCancel || CanDodgeCancel || CanCounterCancel);
    }
}
