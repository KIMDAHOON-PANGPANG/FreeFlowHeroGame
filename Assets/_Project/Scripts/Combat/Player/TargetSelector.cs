using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Player
{
    /// <summary>
    /// 프리플로우 전투의 자동 타겟팅 시스템.
    /// 범위 내 적 중 방향 입력과 거리를 종합하여 최적의 타겟을 선택한다.
    /// 직전 타겟을 회피하여 적 사이를 넘나드는 프리플로우 흐름을 만든다.
    /// </summary>
    public class TargetSelector
    {
        // ─── 설정 ───
        private const float MaxAutoRange = 12f;           // 워핑 가능 최대 거리
        private const float DirectionWeight = 2.5f;       // 입력 방향 가중치
        public const float MeleeRange = 1.5f;             // 이 거리 안이면 워핑 불필요
        private const float LastTargetPenalty = 8f;        // 직전 타겟 페널티 (다른 적 우선)

        /// <summary>현재 선택된 타겟</summary>
        public ICombatTarget CurrentTarget { get; private set; }

        /// <summary>직전에 타격한 타겟 (프리플로우: 다른 적 우선 선택)</summary>
        public ICombatTarget LastHitTarget { get; private set; }

        /// <summary>
        /// 범위 내 적 목록에서 최적 타겟을 선택한다.
        /// 직전 타겟에는 페널티를 부여하여 적 사이를 넘나드는 흐름을 만든다.
        /// </summary>
        public ICombatTarget SelectTarget(
            Vector2 playerPos,
            List<ICombatTarget> enemies,
            float inputDir = 0f)
        {
            if (enemies == null || enemies.Count == 0)
            {
                CurrentTarget = null;
                return null;
            }

            ICombatTarget bestTarget = null;
            float bestScore = float.MaxValue;

            // 살아있는 적 수 먼저 카운트 (페널티 적용 조건)
            int aliveCount = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] != null && enemies[i].IsTargetable)
                    aliveCount++;
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsTargetable) continue;

                Transform enemyTransform = enemy.GetTransform();
                if (enemyTransform == null) continue;

                Vector2 enemyPos = enemyTransform.position;
                float dist = Vector2.Distance(playerPos, enemyPos);

                // 최대 범위 초과 스킵
                if (dist > MaxAutoRange) continue;

                // 점수 = 거리
                float score = dist;

                // 방향 입력 보너스
                float dirToEnemy = Mathf.Sign(enemyPos.x - playerPos.x);
                if (inputDir != 0f && Mathf.Sign(inputDir) == dirToEnemy)
                {
                    score -= MaxAutoRange * DirectionWeight;
                }

                // 직전 타겟 페널티 (다른 적이 있을 때만 적용)
                if (aliveCount > 1 && enemy == LastHitTarget)
                {
                    score += LastTargetPenalty;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = enemy;
                }
            }

            CurrentTarget = bestTarget;
            return bestTarget;
        }

        /// <summary>히트 성공 시 호출 — 직전 타겟 기록</summary>
        public void RegisterHit(ICombatTarget target)
        {
            LastHitTarget = target;
        }

        /// <summary>현재 타겟이 여전히 유효한지 검증</summary>
        public bool IsTargetValid()
        {
            if (CurrentTarget == null) return false;
            if (CurrentTarget.GetTransform() == null) return false;
            return CurrentTarget.IsTargetable;
        }

        // NeedsWarp, GetWarpDestination → WARP 노티파이(StrikeState.StartInlineWarp)로 이전됨

        /// <summary>타겟 클리어</summary>
        public void ClearTarget()
        {
            CurrentTarget = null;
        }

        /// <summary>전체 리셋 (콤보 끊겼을 때)</summary>
        public void Reset()
        {
            CurrentTarget = null;
            LastHitTarget = null;
        }
    }
}
