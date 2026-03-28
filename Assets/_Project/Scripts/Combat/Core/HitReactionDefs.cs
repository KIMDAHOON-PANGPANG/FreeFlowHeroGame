namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 피격 리액션 타입.
    /// COLLISION 노티파이에서 선택하여 공격별 리액션을 데이터 드리븐으로 결정.
    /// </summary>
    public enum HitType
    {
        /// <summary>경직 — 가볍게 "윽!" 하며 밀림. 가장 기본적인 리액션.</summary>
        Flinch = 0,

        /// <summary>넉다운 — 공중으로 떠서 날아감. 강공격/피니셔용.</summary>
        Knockdown = 1
    }

    /// <summary>
    /// 리액션 강도 프리셋. BattleSettings에 정의된 기본값을 참조.
    /// 노티파이에서 프리셋 선택 후 Offset으로 미세 조정.
    /// </summary>
    public enum HitPreset
    {
        Light = 0,
        Medium = 1,
        Heavy = 2
    }

    /// <summary>
    /// 피격 시 피격자가 바라보는 방향.
    /// COLLISION 노티파이에서 설정.
    /// </summary>
    public enum HitFacing
    {
        /// <summary>공격자를 바라봄 (기본값)</summary>
        Attacker = 0,
        /// <summary>타격 접촉 지점을 바라봄</summary>
        HitPoint = 1,
        /// <summary>넉백 방향을 바라봄 (공격자 반대쪽)</summary>
        KnockDirection = 2
    }

    /// <summary>Flinch(경직) 리액션 파라미터.</summary>
    [System.Serializable]
    public struct FlinchData
    {
        /// <summary>밀림 거리 (cm). 즉시 텔레포트.</summary>
        public float pushDistance;

        /// <summary>피격 경직 시간 (초). 이 동안 AI/애니메이션 일시정지.</summary>
        public float freezeTime;

        /// <summary>히트스탑 (프레임, 소수점). 공격자 측에 부여. 예: 1.5 → 1.5프레임 정지.</summary>
        public float hitStop;

        public FlinchData(float push, float freeze, float stop)
        {
            pushDistance = push;
            freezeTime = freeze;
            hitStop = stop;
        }

        /// <summary>다른 FlinchData(오프셋)를 더함</summary>
        public FlinchData WithOffset(float pushOff, float freezeOff, float stopOff)
        {
            return new FlinchData(
                pushDistance + pushOff,
                freezeTime + freezeOff,
                hitStop + stopOff
            );
        }
    }

    /// <summary>Knockdown(넉다운) 리액션 파라미터.</summary>
    [System.Serializable]
    public struct KnockdownData
    {
        /// <summary>뜨는 최대 높이 (cm). sin 커브 정점.</summary>
        public float launchHeight;

        /// <summary>체공 시간 (초). 올라가기 + 내려오기 총 시간.</summary>
        public float airTime;

        /// <summary>수평 날아가는 거리 (cm).</summary>
        public float knockDistance;

        /// <summary>착지 후 누워있는 시간 (초). Down 상태 지속.</summary>
        public float downTime;

        public KnockdownData(float height, float air, float dist, float down = 0.5f)
        {
            launchHeight = height;
            airTime = air;
            knockDistance = dist;
            downTime = down;
        }

        /// <summary>오프셋 적용</summary>
        public KnockdownData WithOffset(float heightOff, float airOff, float distOff, float downOff = 0f)
        {
            return new KnockdownData(
                launchHeight + heightOff,
                airTime + airOff,
                knockDistance + distOff,
                downTime + downOff
            );
        }
    }

    /// <summary>
    /// 통합 피격 리액션 데이터. HitData에 포함되어 TakeHit()으로 전달.
    /// type에 따라 flinch 또는 knockdown 중 하나만 유효.
    /// </summary>
    [System.Serializable]
    public struct HitReactionData
    {
        public HitType type;
        public HitFacing facing;
        public bool forceFlip;       // true: 현재 방향과 무관하게 강제 플립
        public FlinchData flinch;
        public KnockdownData knockdown;

        public static HitReactionData CreateFlinch(FlinchData data, HitFacing facing = HitFacing.Attacker, bool forceFlip = true)
        {
            return new HitReactionData { type = HitType.Flinch, flinch = data, facing = facing, forceFlip = forceFlip };
        }

        public static HitReactionData CreateKnockdown(KnockdownData data, HitFacing facing = HitFacing.Attacker, bool forceFlip = true)
        {
            return new HitReactionData { type = HitType.Knockdown, knockdown = data, facing = facing, forceFlip = forceFlip };
        }
    }
}
