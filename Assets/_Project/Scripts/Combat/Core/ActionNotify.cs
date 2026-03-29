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

        /// <summary>워핑 트리거 — 타겟에게 보간 이동. 인스턴스 모드(1회 발화) 전용.</summary>
        WARP = 3,

        /// <summary>
        /// 펜딩 윈도우 — CANCEL_WINDOW 직전 입력 수집 구간.
        /// 이 구간이 시작되면 기존 입력 버퍼를 클리어하고,
        /// 구간 동안 새로 들어오는 입력만 버퍼에 저장한다.
        /// CANCEL_WINDOW는 PENDING_WINDOW 종료 후에만 활성화되어
        /// 펜딩 입력으로 즉시 캔슬한다.
        /// → "타격 팔로스루 강제 재생 + 입력 반응성 보존" 효과.
        /// </summary>
        PENDING_WINDOW = 4,

        /// <summary>
        /// 루트모션 — 커브 기반 이동 속도 제어.
        /// startFrame~endFrame 구간 동안 rootMotionKeys 커브를 평가하여
        /// 캐릭터를 facing 방향으로 이동시킨다.
        /// Unity 루트모션 대체: 2D Kinematic 환경에서 스크립트 기반 구현.
        /// </summary>
        ROOT_MOTION = 5,

        /// <summary>
        /// 호밍 — 공격 중 타겟 방향 회전 추적 허용 구간.
        /// startFrame~endFrame 동안 액터가 타겟을 향해 회전(플립) 가능.
        /// 기본 동작: HOMING 노티파이가 없으면 공격 중 회전 잠금.
        /// homingTurnRate: 회전 속도 제한 (도/초). 0 = 즉시 스냅.
        /// </summary>
        HOMING = 6,
    }

    /// <summary>
    /// 워핑 이징 커브 종류.
    /// JsonUtility에서는 int로 직렬화되므로 ActionNotify.warpEaseType과 매핑.
    /// </summary>
    public enum WarpEaseType
    {
        CubicOut = 0,    // 기본: 빠른 가속 후 부드러운 감속
        QuadOut = 1,     // 좀 더 부드러운 감속
        ExpoOut = 2,     // 급격한 가속 후 급감속
        Linear = 3,      // 일정 속도
        BackOut = 4,     // 약간 오버슈트 후 안착 (타격감 강화)
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

        // ─── COLLISION 히트박스 트랜스폼 (캐릭터 루트 기준 로컬 좌표) ───
        // ★ JsonUtility: 누락 float → 0으로 역직렬화됨. 에디터에서 0일 때 기본값 적용.
        public float hitboxOffsetX;  // 히트박스 X 오프셋 (좌우, 기본 0)
        public float hitboxOffsetY;  // 히트박스 Y 오프셋 (상하, 기본 0.8 = 캐릭터 중심)
        public float hitboxOffsetZ;  // 히트박스 Z 오프셋 (전후, 기본 0.5 = 전방)
        public float hitboxSizeX;    // 히트박스 X 크기 (폭, 기본 0.6)
        public float hitboxSizeY;    // 히트박스 Y 크기 (높이, 기본 0.8)
        public float hitboxSizeZ;    // 히트박스 Z 크기 (깊이, 기본 0.5)

        // ─── COLLISION 히트 리액션 ───
        // hitType/hitPreset/hitFacing은 int로 저장 (JSON enum 호환)
        public int hitType;              // (int)HitType: 0=Flinch, 1=Knockdown
        public int hitPreset;            // (int)HitPreset: 0=Light, 1=Medium, 2=Heavy
        public int hitFacing;            // (int)HitFacing: 0=Attacker, 1=HitPoint, 2=KnockDirection
        public bool forceFlip = true;    // 강제 플립 (기본 true)
        public int hitKnockDirection;    // (int)HitKnockDirection: 0=Attacker(기본), 1=Defender(피격자 방향)
        // Flinch 오프셋 (프리셋 기본값에 더함)
        public float flinchPushOffset;   // cm
        public float flinchFreezeOffset; // 초
        public float flinchHitStopOffset;// 프레임
        // Knockdown 오프셋
        public float knockLaunchOffset;  // cm
        public float knockAirTimeOffset; // 초
        public float knockDistanceOffset;// cm
        public float knockDownTimeOffset;// 초 (Down 상태 지속 시간 오프셋)

        // ─── CANCEL_WINDOW 파라미터 ───
        public bool skillCancel;    // 공격 캔슬 (콤보 연계) 허용
        public bool moveCancel;     // 이동 캔슬 허용
        public bool dodgeCancel;    // 회피 캔슬 허용
        public bool counterCancel;  // 카운터 캔슬 허용
        public string nextAction;   // Attack 입력 시 전이 대상 액션 ID (= 콤보 다음 타수)
        // ★ 입력별 캔슬 라우팅 (C안 통합): 각 입력 타입별 전이 대상 액션 ID
        //   비어있으면 해당 캔슬 플래그가 true여도 기본 액션("Dodge", "Counter" 등)으로 전이
        public string heavyNext;    // Heavy 입력 시 전이 대상 (기본: "HeavyAtk")
        public string dodgeNext;    // Dodge 입력 시 전이 대상 (기본: "Dodge")
        public string counterNext;  // Counter 입력 시 전이 대상 (기본: "Counter")

        // ─── WARP 파라미터 ───
        // ★ 데이터 튜닝: 액션별 워핑 느낌을 개별 조절 가능
        public float warpOffsetX;      // 적 기준 도착 오프셋 X (음수=적 앞쪽, 양수=적 뒤쪽, 기본 -0.5)
        public float warpOffsetY;      // 적 기준 도착 오프셋 Y (0=같은 높이)
        public float warpDuration;     // 워핑 시간 (초). 0=거리 비례 자동 계산
        public int warpEaseType;       // 이징 커브 (WarpEaseType enum 참조, 기본 0=CubicOut)
        public float warpMinDuration;  // 자동 계산 시 최소 시간 (초, 기본 0.04)
        public float warpMaxDuration;  // 자동 계산 시 최대 시간 (초, 기본 0.12)
        public bool warpInvincible;    // 워핑 중 무적 (기본 true)
        public bool warpAutoTarget;    // 자동 타겟 재선택 (기본 true)
        public float warpMinRange;     // 최소 발동 거리 (이 거리 이내면 워핑 스킵, 기본 1.5)
        public float warpMaxRange;     // 최대 발동 거리 (이 거리 밖이면 워핑 스킵, 0=무제한, 기본 0)
        public float warpSpeed;        // 워핑 속도 (유닛/초, 0=Duration 기반, 기본 0)

        // ─── ROOT_MOTION 파라미터 ───
        // ★ 커브 데이터: [time0, value0, time1, value1, ...] 쌍으로 저장
        //   time: 0.0~1.0 (normalized, 0=startFrame, 1=endFrame)
        //   value: 해당 시점의 이동 속도 (units/sec)
        //   런타임에서 AnimationCurve로 변환 → Evaluate(t)로 보간
        //   바이브 코딩: JSON 배열 직접 수정 / GUI: CurveField로 드래그 편집
        public float[] rootMotionKeys;      // 커브 키프레임 배열
        public float rootMotionScale = 1f;  // 전체 스케일 배율 (★ 데이터 튜닝)

        // ─── HOMING 파라미터 ───
        // ★ 공격 중 타겟 방향 회전 추적 허용 구간.
        //   homingTurnRate: 회전 속도 제한 (도/초). 0 = 즉시 스냅.
        //   2D 횡스크롤에서 "회전" = localScale.x 플립.
        //   turnRate > 0이면 turnRate × deltaTime ≥ 180° 일 때만 플립 허용 (지연 효과).
        public float homingTurnRate;  // ★ 데이터 튜닝: 0=즉시, 360=0.5초 지연, 720=0.25초 지연

        /// <summary>rootMotionKeys 배열을 AnimationCurve로 변환</summary>
        public AnimationCurve BuildRootMotionCurve()
        {
            var curve = new AnimationCurve();
            if (rootMotionKeys == null || rootMotionKeys.Length < 2) return curve;
            for (int i = 0; i + 1 < rootMotionKeys.Length; i += 2)
            {
                curve.AddKey(rootMotionKeys[i], rootMotionKeys[i + 1]);
            }
            return curve;
        }

        /// <summary>AnimationCurve를 rootMotionKeys 배열로 변환 (에디터 저장용)</summary>
        public void SetRootMotionCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                rootMotionKeys = System.Array.Empty<float>();
                return;
            }
            rootMotionKeys = new float[curve.length * 2];
            for (int i = 0; i < curve.length; i++)
            {
                var key = curve[i];
                rootMotionKeys[i * 2] = key.time;
                rootMotionKeys[i * 2 + 1] = key.value;
            }
        }

        // ─── 워핑 기본값 상수 ───
        public const float DefaultWarpOffsetX = -0.5f;
        public const float DefaultWarpMinDuration = 0.04f;
        public const float DefaultWarpMaxDuration = 0.12f;
        public const float DefaultWarpMinRange = 1.5f;

        // ─── 히트박스 기본값 상수 ───
        public const float DefaultHitboxOffsetY = 0.8f;
        public const float DefaultHitboxOffsetZ = 0.5f;
        public const float DefaultHitboxSizeX = 0.6f;
        public const float DefaultHitboxSizeY = 0.8f;
        public const float DefaultHitboxSizeZ = 0.5f;

        // ─── 계산 프로퍼티 ───

        /// <summary>히트박스 오프셋 (0이면 기본값 적용, Z는 2D 횡스크롤이므로 고정)</summary>
        public Vector3 GetHitboxOffset()
        {
            return new Vector3(
                hitboxOffsetX,
                hitboxOffsetY == 0f ? DefaultHitboxOffsetY : hitboxOffsetY,
                0f  // 2D 횡스크롤: Z 오프셋 사용 안 함
            );
        }

        /// <summary>히트박스 크기 (0이면 기본값 적용, Z는 2D 횡스크롤이므로 얇은 고정값)</summary>
        public Vector3 GetHitboxSize()
        {
            return new Vector3(
                hitboxSizeX == 0f ? DefaultHitboxSizeX : hitboxSizeX,
                hitboxSizeY == 0f ? DefaultHitboxSizeY : hitboxSizeY,
                0.1f  // 2D 횡스크롤: Z 크기 얇게 고정 (시각 확인용)
            );
        }

        /// <summary>히트박스 값을 기본값으로 리셋 (Z는 2D이므로 0 고정)</summary>
        public void ResetHitboxToDefaults()
        {
            hitboxOffsetX = 0f;
            hitboxOffsetY = DefaultHitboxOffsetY;
            hitboxOffsetZ = 0f;  // 2D: Z 사용 안 함
            hitboxSizeX = DefaultHitboxSizeX;
            hitboxSizeY = DefaultHitboxSizeY;
            hitboxSizeZ = 0f;    // 2D: Z 사용 안 함
        }

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

        /// <summary>COLLISION 노티파이 생성 (히트박스 기본값 포함)</summary>
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
                // 히트박스 트랜스폼 기본값 (2D: Z 제외)
                hitboxOffsetX = 0f,
                hitboxOffsetY = DefaultHitboxOffsetY,
                hitboxOffsetZ = 0f,
                hitboxSizeX = DefaultHitboxSizeX,
                hitboxSizeY = DefaultHitboxSizeY,
                hitboxSizeZ = 0f,
            };
        }

        /// <summary>CANCEL_WINDOW 노티파이 생성</summary>
        public static ActionNotify CreateCancelWindow(int start, int end,
            bool skill = true, bool move = true, bool dodge = true, bool counter = false,
            string nextAction = "", string heavyNext = "", string dodgeNext = "", string counterNext = "")
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
                heavyNext = heavyNext,
                dodgeNext = dodgeNext,
                counterNext = counterNext,
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
            };
        }

        /// <summary>WARP 노티파이 생성 (인스턴스 모드 전용)</summary>
        public static ActionNotify CreateWarp(int triggerFrame,
            float offsetX = DefaultWarpOffsetX, float offsetY = 0f,
            float duration = 0f, int easeType = 0,
            bool invincible = true, bool autoTarget = true)
        {
            return new ActionNotify
            {
                type = NotifyType.WARP.ToString(),
                startFrame = triggerFrame,
                endFrame = triggerFrame + 3,  // 인스턴스: endFrame은 타임라인 시각 표시용
                track = 3,
                disabled = false,
                isInstance = true,            // 포인트 이벤트 (1회 발화)
                // WARP 파라미터
                warpOffsetX = offsetX,
                warpOffsetY = offsetY,
                warpDuration = duration,
                warpEaseType = easeType,
                warpMinDuration = DefaultWarpMinDuration,
                warpMaxDuration = DefaultWarpMaxDuration,
                warpInvincible = invincible,
                warpAutoTarget = autoTarget,
                warpMinRange = DefaultWarpMinRange,
                warpMaxRange = 0f,      // 0=무제한
                warpSpeed = 0f,         // 0=Duration 기반
                // 다른 타입 파라미터 기본값
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
                nextAction = "",
            };
        }

        /// <summary>PENDING_WINDOW 노티파이 생성</summary>
        public static ActionNotify CreatePendingWindow(int start, int end)
        {
            return new ActionNotify
            {
                type = NotifyType.PENDING_WINDOW.ToString(),
                startFrame = start,
                endFrame = end,
                track = 4,
                disabled = false,
                isInstance = false,
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
                nextAction = "",
            };
        }

        /// <summary>ROOT_MOTION 노티파이 생성</summary>
        public static ActionNotify CreateRootMotion(int start, int end,
            float[] keys = null, float scale = 1f)
        {
            return new ActionNotify
            {
                type = NotifyType.ROOT_MOTION.ToString(),
                startFrame = start,
                endFrame = end,
                track = 5,
                disabled = false,
                isInstance = false,
                rootMotionKeys = keys ?? new float[] { 0f, 0f, 0.3f, 6f, 0.6f, 4f, 1f, 0f },
                rootMotionScale = scale,
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
                nextAction = "",
            };
        }

        /// <summary>HOMING 노티파이 생성</summary>
        public static ActionNotify CreateHoming(int start, int end, float turnRate = 0f)
        {
            return new ActionNotify
            {
                type = NotifyType.HOMING.ToString(),
                startFrame = start,
                endFrame = end,
                track = 6,
                disabled = false,
                isInstance = false,  // STATE 모드: 구간 동안 활성
                homingTurnRate = turnRate,
                damageScale = 1f,
                hitboxId = "",
                moveSpeed = 0f,
                nextAction = "",
            };
        }

        /// <summary>워핑 이징 커브 적용</summary>
        public static float ApplyWarpEasing(float t, int easeType)
        {
            switch (easeType)
            {
                case 0: // CubicOut
                    return 1f - Mathf.Pow(1f - t, 3f);
                case 1: // QuadOut
                    return 1f - (1f - t) * (1f - t);
                case 2: // ExpoOut
                    return Mathf.Approximately(t, 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case 3: // Linear
                    return t;
                case 4: // BackOut (오버슈트)
                    float c1 = 1.70158f;
                    float c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                default:
                    return 1f - Mathf.Pow(1f - t, 3f);
            }
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
                case NotifyType.WARP:           return new Color(0.2f, 0.9f, 0.5f, 0.8f);  // 초록
                case NotifyType.PENDING_WINDOW: return new Color(0.9f, 0.5f, 0.2f, 0.8f);  // 주황
                case NotifyType.ROOT_MOTION:    return new Color(0.7f, 0.3f, 0.9f, 0.8f);  // 보라
                case NotifyType.HOMING:         return new Color(0.2f, 0.8f, 0.9f, 0.8f);  // 시안
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
                case NotifyType.WARP:           return "WARP";
                case NotifyType.PENDING_WINDOW: return "PENDING";
                case NotifyType.ROOT_MOTION:    return "ROOT_MOTION";
                case NotifyType.HOMING:         return "HOMING";
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
                case NotifyType.WARP:           return 3;
                case NotifyType.PENDING_WINDOW: return 4;
                case NotifyType.ROOT_MOTION:    return 5;
                case NotifyType.HOMING:         return 6;
                default:                        return 7;
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
                case NotifyType.WARP:           return 3;  // 인스턴스: 시각 표시용 최소 길이
                case NotifyType.PENDING_WINDOW: return 5;  // 팔로스루 강제 재생 구간
                case NotifyType.ROOT_MOTION:    return 20; // 액션 전체 범위 (동기화 시 자동 설정)
                case NotifyType.HOMING:         return 10; // 추적 허용 구간
                default:                        return 5;
            }
        }
    }
}
