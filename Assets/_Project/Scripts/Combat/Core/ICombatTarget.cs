using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 전투 대상 인터페이스.
    /// 플레이어와 적 모두 이 인터페이스를 구현한다.
    /// </summary>
    public interface ICombatTarget
    {
        /// <summary>타겟팅 가능 여부 (살아있고 접근 가능한지)</summary>
        bool IsTargetable { get; }

        /// <summary>무적 상태 여부 (i-frame, 처형 중 등)</summary>
        bool IsInvulnerable { get; }

        /// <summary>현재 HP (0~maxHP)</summary>
        float CurrentHP { get; }

        /// <summary>최대 HP</summary>
        float MaxHP { get; }

        /// <summary>HP 비율 (0~1)</summary>
        float HPRatio { get; }

        /// <summary>소속 팀</summary>
        CombatTeam Team { get; }

        /// <summary>Transform 참조</summary>
        Transform GetTransform();

        /// <summary>피격 처리</summary>
        void TakeHit(HitData hitData);

        /// <summary>현재 공격 중단 (스턴/피격 시)</summary>
        void InterruptAction();
    }

    /// <summary>
    /// 텔레그래프(공격 예고) 가능한 적 전용 인터페이스
    /// </summary>
    public interface ITelegraphable
    {
        /// <summary>현재 텔레그래프 타입</summary>
        TelegraphType CurrentTelegraph { get; }

        /// <summary>텔레그래프 시작 프레임</summary>
        int TelegraphStartFrame { get; }

        /// <summary>공격 예고 중인지 여부</summary>
        bool IsTelegraphing { get; }
    }
}
