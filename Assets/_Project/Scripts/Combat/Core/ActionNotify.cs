using System;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    // ═══════════════════════════════════════════════════════
    //  액션 노티파이 시스템
    //  타임라인 기반으로 액션의 각 구간(STARTUP, COLLISION, CANCEL_WINDOW)을
    //  자유롭게 배치하고 런타임에서 프레임 폴링으로 처리한다.
    //  JsonUtility 호환 (public 필드, enum은 string으로 직렬화).
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 노티파이 타입.
    /// 타임라인 트랙에 배치 가능한 구간형 이벤트.
    /// </summary>
    public enum NotifyType
    {
        /// <summary>선딜 구간 — 공격 전 준비 동작. 이동 속도 적용.</summary>
        STARTUP = 0,

        /// <summary>히트 판정 구간 — 히트박스 활성. HitboxController.Activate/Deactivate 연동.</summary>
        COLLISION = 1,

        /// <summary>캔슬 허용 구간 — 내부 플래그로 캔슬 종류별 허용/불허 제어.</summary>
        CANCEL_WINDOW = 2,
    }

    /// <summary>
    /// 단일 노티파이 엔트리.
    /// 타임라인 트랙에 배치되는 구간형 이벤트.
    /// startFrame ~ endFrame (exclusive) 사이에 활성.
    /// </summary>
    [Serializable]
    public class ActionNotify
    {
        // ─── 기본 정보 ───
        public string type;         // NotifyType 문자열 ("STARTUP", "COLLISION", "CANCEL_WINDOW")
        public int startFrame;      // 시작 프레임 (inclusive, 60fps 기준)
        public int endFrame;        // 종료 프레임 (exclusive)
        public int track;           // 타임라인 트랙 번호 (0~4)
        // ★ "disabled" 패턴: JsonUtility는 누락 bool → false로 역직렬화.
        //    disabled=false (기본) → 활성 상태. 기존 JSON에 필드 없어도 안전.
        //    disabled=true → 비활성 (런타임에서 무시, 에디터에서 흐리게 표시)
        public bool disabled;

        // ─── 인스턴스 모드 ───
        // true  = 인스턴스(포인트): startFrame에서 단 1회만 실행 (endFrame은 시각용 길이)
        // false = 스테이트(구간): startFrame~endFrame 사이 매 프레임 활성 (언리얼 NotifyState)
        public bool isInstance;

        // ─── STARTUP 파라미터 ───
        public float moveSpeed;     // 선딜 중 이동 속도

        // ─── COLLISION 파라미터 ───
        public string hitboxId;     // 히트박스 식별자 (기본: "default")
        public float damageScale;   // 데미지 배율 (1.0 = 기본)

        // ─── CANCEL_WINDOW 파라미터 ───
        public bool skillCancel;    // 공격 캔슬 (콤보 연계) 허용
        public bool moveCancel;     // 이동 캔슬 허용
        public bool dodgeCancel;    // 회피 캔슬 허용
        public bool counterCancel;  // 카운터 캔슬 허용
        public string nextAction;   // 스킬 캔슬 시 전이 대상 액션 ID (비어있으면 CancelRoute 참조)

        // ─── 계산 프로퍼티 ───

        /// <summary>구간 프레임 수</summary>
        public int Duration => Mathf.Max(endFrame - startFrame, 0);

        /// <summary>구간 시작 시간 (초)</summary>
        public float StartTime => startFrame * CombatConstants.FrameDuration;

        /// <summary>구간 종료 시간 (초)</summary>
        public float EndTime => endFrame * CombatConstants.FrameDuration;

        /// <summary>구간 지속 시간 (초)</summary>
        public float DurationTime => Duration * CombatConstants.FrameDuration;

        /// <summary>NotifyType enum 변환</summary>
        public NotifyType TypeEnum
        {
            get
            {
                if (Enum.TryParse<NotifyType>(type, true, out var result))
                    return result;
                return NotifyType.STARTUP;
            }
        }

        /// <summary>이 노티파이가 활성(enabled)인지</summary>
        public bool IsEnabled => !disabled;

        /// <summary>특정 프레임이 이 노티파이 구간 안에 있는지 확인 (disabled 고려)</summary>
        public bool ContainsFrame(int frame)
        {
            if (disabled) return false;
            return frame >= startFrame && frame < endFrame;
        }

        /// <summary>인스턴스 모드일 때 해당 프레임이 정확히 startFrame인지 확인</summary>
        public bool IsInstanceFrame(int frame)
        {
            if (disabled) return false;
            return isInstance && frame == startFrame;
        }

        /// <summary>스테이트 모드(구간)인지 — !isInstance && !disabled</summary>
        public bool IsStateMode => !isInstance && !disabled;

        // ─── 팩토리 메서드 ───

        /// <summary>STARTUP 노티파이 생성</summary>
        public static ActionNotify CreateStartup(int start, int end, float moveSpeed = 0f)
        {
            return new ActionNotify
            {
                type = NotifyType.STARTUP.ToString(),
                startFrame = start,
                endFrame = end,
                track = 0,
                disabled = false,
                isInstance = false,
                moveSpeed = moveSpeed,
                damageScale = 1f,
                hitboxId = "",
                nextAction = "",
            };
        }

        /// <summary>COLLISION 노티파이 생성</summary>
        public static ActionNotify CreateCollision(int start, int end, float damageScale = 1f, string hitboxId = "default")
        {
            return new ActionNotify
            {
                type = NotifyType.COLLISION.ToString(),
                startFrame = start,
                endFrame = end,
                track = 1,
                disabled = false,
                isInstance = false,
                damageScale = damageScale,
                hitboxId = hitboxId,
                moveSpeed = 0f,
                nextAction = "",
            };
        }

        /// <summary>CANCEL_WINDOW 노티파이 생성</summary>
        public static ActionNotify CreateCancelWindow(int start, int end,
            bool skill = true, bool move = true, bool dodge = true, bool counter = false,
            string nextAction = "")
        {
            return new ActionNotify
            {
                type = NotifyType.CANCEL_WINDOW.ToString(),
                startFrame = start,
                endFrame = end,
                track = 2,
                disabled = false,
                isInstance = false,
                skillCancel = skill,
                moveCancel = move,
                dodgeCancel = dodge,
                counterCancel = counter,
                nextAction = nextAction,
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
            };
        }
    }

    /// <summary>
    /// 노티파이 타입별 시각 설정 (에디터 + 디버그용).
    /// </summary>
    public static class NotifyTypeInfo
    {
        /// <summary>타입별 트랙 색상</summary>
        public static Color GetColor(NotifyType type)
        {
            switch (type)
            {
                case NotifyType.STARTUP:       return new Color(0.3f, 0.5f, 0.9f, 0.8f);  // 파랑
                case NotifyType.COLLISION:      return new Color(0.9f, 0.3f, 0.2f, 0.8f);  // 빨강
                case NotifyType.CANCEL_WINDOW:  return new Color(0.9f, 0.8f, 0.2f, 0.8f);  // 노랑
                default:                        return new Color(0.5f, 0.5f, 0.5f, 0.8f);  // 회색
            }
        }

        /// <summary>타입별 표시 이름</summary>
        public static string GetDisplayName(NotifyType type)
        {
            switch (type)
            {
                case NotifyType.STARTUP:       return "STARTUP";
                case NotifyType.COLLISION:      return "COLLISION";
                case NotifyType.CANCEL_WINDOW:  return "CANCEL";
                default:                        return type.ToString();
            }
        }

        /// <summary>타입별 기본 트랙 번호</summary>
        public static int GetDefaultTrack(NotifyType type)
        {
            switch (type)
            {
                case NotifyType.STARTUP:       return 0;
                case NotifyType.COLLISION:      return 1;
                case NotifyType.CANCEL_WINDOW:  return 2;
                default:                        return 3;
            }
        }

        /// <summary>타입별 기본 구간 길이 (프레임)</summary>
        public static int GetDefaultDuration(NotifyType type)
        {
            switch (type)
            {
                case NotifyType.STARTUP:       return 5;
                case NotifyType.COLLISION:      return 8;
                case NotifyType.CANCEL_WINDOW:  return 10;
                default:                        return 5;
            }
        }
    }
}
