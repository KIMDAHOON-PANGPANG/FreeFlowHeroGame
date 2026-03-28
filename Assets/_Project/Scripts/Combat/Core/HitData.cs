using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 히트(공격 적중) 정보를 담는 데이터 구조체.
    /// CombatEventBus를 통해 전달된다.
    /// </summary>
    [System.Serializable]
    public struct HitData
    {
        // ─── 공격 정보 ───

        /// <summary>기본 공격력</summary>
        public float BaseDamage;

        /// <summary>공격 타입</summary>
        public AttackType AttackType;

        /// <summary>공격자 팀</summary>
        public CombatTeam AttackerTeam;

        // ─── 히트 리액션 힌트 ───

        /// <summary>넉백 방향 (정규화)</summary>
        public Vector2 KnockbackDirection;

        /// <summary>콤보 중 공격인지 여부</summary>
        public bool IsComboAttack;

        /// <summary>현재 콤보 카운트</summary>
        public int ComboCount;

        /// <summary>처형 킬 여부</summary>
        public bool IsExecutionKill;

        /// <summary>띄우기 공격 여부</summary>
        public bool IsLaunchAttack;

        /// <summary>넉다운 공격 여부</summary>
        public bool IsKnockdown;

        // ─── 피격 리액션 ───

        /// <summary>피격 리액션 데이터. COLLISION 노티파이의 HitType/Preset/Offset에서 조립.</summary>
        public HitReactionData Reaction;

        // ─── 데미지 배율 ───

        /// <summary>최종 데미지 배율 (노티파이 damageScale 등). 기본 1.0</summary>
        public float DamageMultiplier;

        // ─── 위치 정보 ───

        /// <summary>히트 접촉 지점 (VFX 스폰용)</summary>
        public Vector2 ContactPoint;

        /// <summary>공격자 위치</summary>
        public Vector2 AttackerPosition;

        // ─── 팩토리 메서드 ───

        /// <summary>Light Attack용 HitData 생성</summary>
        public static HitData CreateLightAttack(
            Vector2 attackerPos, Vector2 targetPos, int comboCount)
        {
            Vector2 direction = (targetPos - attackerPos).normalized;
            return new HitData
            {
                BaseDamage = 10f,
                DamageMultiplier = 1f,
                AttackType = AttackType.Light,
                AttackerTeam = CombatTeam.Player,
                KnockbackDirection = direction,
                IsComboAttack = comboCount > 1,
                ComboCount = comboCount,
                IsExecutionKill = false,
                IsLaunchAttack = false,
                IsKnockdown = false,
                ContactPoint = Vector2.Lerp(attackerPos, targetPos, 0.5f),
                AttackerPosition = attackerPos
            };
        }

        /// <summary>Heavy Attack용 HitData 생성</summary>
        public static HitData CreateHeavyAttack(
            Vector2 attackerPos, Vector2 targetPos, int comboCount)
        {
            Vector2 direction = (targetPos - attackerPos).normalized;
            return new HitData
            {
                BaseDamage = 20f,
                DamageMultiplier = 1f,
                AttackType = AttackType.Heavy,
                AttackerTeam = CombatTeam.Player,
                KnockbackDirection = direction,
                IsComboAttack = comboCount > 0,
                ComboCount = comboCount,
                IsExecutionKill = false,
                IsLaunchAttack = false,
                IsKnockdown = true,
                ContactPoint = Vector2.Lerp(attackerPos, targetPos, 0.5f),
                AttackerPosition = attackerPos
            };
        }

        /// <summary>Counter Attack용 HitData 생성</summary>
        public static HitData CreateCounterAttack(
            Vector2 attackerPos, Vector2 targetPos, int comboCount,
            bool isPerfect)
        {
            Vector2 direction = (targetPos - attackerPos).normalized;
            return new HitData
            {
                BaseDamage = isPerfect ? 25f : 15f,
                DamageMultiplier = 1f,
                AttackType = AttackType.Counter,
                AttackerTeam = CombatTeam.Player,
                KnockbackDirection = direction,
                IsComboAttack = comboCount > 0,
                ComboCount = comboCount,
                IsExecutionKill = false,
                IsLaunchAttack = false,
                IsKnockdown = isPerfect,
                ContactPoint = Vector2.Lerp(attackerPos, targetPos, 0.5f),
                AttackerPosition = attackerPos
            };
        }

        /// <summary>Dodge Attack용 HitData 생성 (회피 직후 반격)</summary>
        public static HitData CreateDodgeAttack(
            Vector2 attackerPos, Vector2 targetPos, int comboCount)
        {
            Vector2 direction = (targetPos - attackerPos).normalized;
            return new HitData
            {
                BaseDamage = 12f,
                DamageMultiplier = 1f,
                AttackType = AttackType.DodgeAttack,
                AttackerTeam = CombatTeam.Player,
                KnockbackDirection = direction,
                IsComboAttack = true,
                ComboCount = comboCount,
                IsExecutionKill = false,
                IsLaunchAttack = false,
                IsKnockdown = false,
                ContactPoint = Vector2.Lerp(attackerPos, targetPos, 0.5f),
                AttackerPosition = attackerPos
            };
        }
    }
}
