using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 처형 조건 판별 유틸리티.
    /// 저HP 적이 처형 범위 내에 있는지 확인한다.
    /// </summary>
    public static class ExecutionSystem
    {
        /// <summary>
        /// 처형 가능한 적을 찾아 반환한다.
        /// 조건: HP ≤ threshold + 거리 ≤ range + IsTargetable + !IsInvulnerable
        /// </summary>
        public static ICombatTarget FindExecutionTarget(
            Vector2 playerPos, List<ICombatTarget> activeEnemies,
            int comboCount, float inputDir)
        {
            float threshold = GetHPThreshold(comboCount);
            float range = CombatConstants.ExecutionRange;

            ICombatTarget best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (enemy == null || !enemy.IsTargetable || enemy.IsInvulnerable)
                    continue;

                // HP 임계치 확인
                if (enemy.HPRatio > threshold)
                    continue;

                // 거리 확인
                Vector2 enemyPos = (Vector2)enemy.GetTransform().position;
                float dist = Vector2.Distance(playerPos, enemyPos);
                if (dist > range)
                    continue;

                // 스코어: 거리 + 방향 보너스 (TargetSelector 패턴)
                float score = dist;
                if (!Mathf.Approximately(inputDir, 0f))
                {
                    float enemyDir = Mathf.Sign(enemyPos.x - playerPos.x);
                    if (Mathf.Sign(inputDir) == enemyDir)
                        score -= 30f; // 입력 방향 보너스
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }

            return best;
        }

        /// <summary>단일 적이 처형 가능한지 확인 (UI 인디케이터용)</summary>
        public static bool IsExecutable(ICombatTarget target, Vector2 playerPos, int comboCount)
        {
            if (target == null || !target.IsTargetable || target.IsInvulnerable)
                return false;

            if (target.HPRatio > GetHPThreshold(comboCount))
                return false;

            float dist = Vector2.Distance(playerPos, (Vector2)target.GetTransform().position);
            return dist <= CombatConstants.ExecutionRange;
        }

        /// <summary>콤보 수에 따른 HP 임계치 반환</summary>
        private static float GetHPThreshold(int comboCount)
        {
            return comboCount >= CombatConstants.ExecutionHighComboThreshold
                ? CombatConstants.ExecutionHPThresholdHighCombo
                : CombatConstants.ExecutionHPThreshold;
        }

        /// <summary>
        /// BattleSettings의 가중치 테이블에서 랜덤 처형 모션 ActionId를 반환한다.
        /// BattleSettings가 로드되지 않은 경우 "Execution1"~"Execution3" 중 랜덤 폴백.
        /// </summary>
        public static string GetRandomMotionActionId()
        {
            if (BattleSettings.Instance != null
                && BattleSettings.Instance.executionMotions != null
                && BattleSettings.Instance.executionMotions.Length > 0)
            {
                return BattleSettings.SelectWeightedRandom(BattleSettings.Instance.executionMotions);
            }

            // 폴백: BattleSettings 미로드 시 균등 랜덤
            return "Execution" + (Random.Range(0, 3) + 1);
        }

        /// <summary>콤보 수에 따른 처형 모션 인덱스 반환 (0, 1, 2)</summary>
        [System.Obsolete("GetRandomMotionActionId()를 사용하세요. 콤보 기반 선택은 가중치 랜덤으로 대체되었습니다.")]
        public static int GetMotionIndex(int comboCount)
        {
            if (comboCount >= CombatConstants.ExecutionMotionTier3Combo) return 2;
            if (comboCount >= CombatConstants.ExecutionMotionTier2Combo) return 1;
            return 0;
        }
    }
}
