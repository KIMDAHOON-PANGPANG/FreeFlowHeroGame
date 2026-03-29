using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 전투 시스템 전역 이벤트 버스.
    /// 모든 전투 모듈 간 통신은 반드시 이 버스를 통해서만 이루어진다.
    /// </summary>
    public static class CombatEventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> subscribers = new();

        /// <summary>
        /// 이벤트 구독
        /// </summary>
        public static void Subscribe<T>(Action<T> handler) where T : ICombatEvent
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type))
                subscribers[type] = new List<Delegate>();

            subscribers[type].Add(handler);
        }

        /// <summary>
        /// 이벤트 구독 해제
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : ICombatEvent
        {
            var type = typeof(T);
            if (subscribers.ContainsKey(type))
                subscribers[type].Remove(handler);
        }

        /// <summary>
        /// 이벤트 발행
        /// </summary>
        public static void Publish<T>(T combatEvent) where T : ICombatEvent
        {
            var type = typeof(T);
            if (!subscribers.ContainsKey(type)) return;

            // 역순 순회로 구독 해제 안전 처리
            for (int i = subscribers[type].Count - 1; i >= 0; i--)
            {
                if (subscribers[type][i] is Action<T> handler)
                    handler.Invoke(combatEvent);
            }
        }

        /// <summary>
        /// 모든 구독 해제 (씬 전환 시 호출)
        /// </summary>
        public static void Clear()
        {
            subscribers.Clear();
        }
    }

    /// <summary>
    /// 모든 전투 이벤트의 마커 인터페이스
    /// </summary>
    public interface ICombatEvent { }

    // ─── 이벤트 정의 ───

    /// <summary>공격 적중</summary>
    public struct OnAttackHit : ICombatEvent
    {
        public HitData HitData;
        public ICombatTarget Attacker;
        public ICombatTarget Target;
    }

    /// <summary>회피 실행</summary>
    public struct OnDodge : ICombatEvent
    {
        public Vector2 Direction;
    }

    /// <summary>콤보 카운트 변경</summary>
    public struct OnComboChanged : ICombatEvent
    {
        public int ComboCount;
        public int PreviousCount;
    }

    /// <summary>콤보 끊김</summary>
    public struct OnComboBreak : ICombatEvent
    {
        public int FinalComboCount;
    }

    /// <summary>헉슬리 게이지 변경</summary>
    public struct OnHuxleyChargeChanged : ICombatEvent
    {
        public float ChargePercent;
    }

    /// <summary>헉슬리 발사</summary>
    public struct OnHuxleyShot : ICombatEvent
    {
        public ShotType Type;
    }

    /// <summary>처형 시작</summary>
    public struct OnExecutionStart : ICombatEvent
    {
        public ICombatTarget Target;
    }

    /// <summary>처형 완료</summary>
    public struct OnExecutionEnd : ICombatEvent { }

    /// <summary>적 공격 예고 (텔레그래프)</summary>
    public struct OnEnemyTelegraph : ICombatEvent
    {
        public ICombatTarget Enemy;
        public TelegraphType Type;
        public float TelegraphDuration;
    }

    /// <summary>히트 리액션 시작</summary>
    public struct OnHitReactionStart : ICombatEvent
    {
        public ICombatTarget Target;
        public HitReactionType ReactionType;
    }

    /// <summary>히트 리액션 종료</summary>
    public struct OnHitReactionEnd : ICombatEvent
    {
        public ICombatTarget Target;
    }

    /// <summary>적 스턴</summary>
    public struct OnEnemyStunned : ICombatEvent
    {
        public ICombatTarget Enemy;
        public float StunDuration;
    }

    /// <summary>적 사망</summary>
    public struct OnEnemyDeath : ICombatEvent
    {
        public ICombatTarget Enemy;
    }

    /// <summary>플레이어 피격</summary>
    public struct OnPlayerHit : ICombatEvent
    {
        public HitData HitData;
    }
}
