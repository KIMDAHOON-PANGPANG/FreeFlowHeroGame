namespace FreeFlowHero.Combat.Core
{
    /// <summary>적 텔레그래프 타입 (인디케이터 색상)</summary>
    public enum TelegraphType
    {
        None,           // 텔레그래프 없음
        Red_Dodge,      // 🔴 빨강: 회피만 가능
        Yellow_Counter  // 🟡 노랑: 카운터 가능
    }

    /// <summary>히트 리액션 타입 (우선순위 순)</summary>
    public enum HitReactionType
    {
        Flinch = 1,         // 약한 경직
        HitStun = 2,        // 콤보 연속 피격
        Stagger = 3,        // 강한 경직 + 밀림
        Knockback = 4,      // 넉백 (서기 유지)
        Knockdown = 5,      // 완전 쓰러짐
        Launch = 6,         // 공중 띄우기
        ExecutionKill = 7   // 처형 즉사
    }

    /// <summary>헉슬리 건 발사 타입</summary>
    public enum ShotType
    {
        Single,     // 33%: 단발
        Double,     // 66%: 2연발
        Finisher    // 100%: 피니셔 (시네마틱)
    }

    /// <summary>공격 타입</summary>
    public enum AttackType
    {
        Light,
        Heavy,
        HuxleyShot,
        Execution,
        DodgeAttack
    }

    /// <summary>팀 구분 (아군/적군 판별)</summary>
    public enum CombatTeam
    {
        Player,
        Enemy
    }
}
