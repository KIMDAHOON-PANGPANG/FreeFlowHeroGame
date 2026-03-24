using System.Collections.Generic;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 공격 조율자 (Singleton).
    /// 모든 적의 공격 스케줄을 중앙에서 관리하여
    /// 동시 공격자 수를 제한하고 호흡 시간을 보장한다.
    ///
    /// REPLACED 전투의 핵심: 적들이 순서대로 공격하여
    /// 플레이어가 프리플로우 전투의 흐름을 유지할 수 있게 한다.
    /// </summary>
    public class AttackCoordinator : MonoBehaviour
    {
        public static AttackCoordinator Instance { get; private set; }

        // ─── 설정 (Inspector) ───
        [Header("공격 슬롯")]
        [Tooltip("동시에 공격할 수 있는 최대 적 수")]
        [SerializeField] private int maxSimultaneousAttackers = CombatConstants.MaxSimultaneousAttackers;

        [Header("호흡 시간")]
        [Tooltip("공격 완료 후 다음 공격까지 최소 대기 시간")]
        [SerializeField] private float breathingTime = CombatConstants.BreathingTime;

        [Header("플레이어 콤보 디버프")]
        [Tooltip("플레이어 콤보 중 적 공격 빈도 감소 비율 (0.7 = 30% 감소)")]
        [SerializeField] private float comboDebuffMultiplier = 0.7f;

        // ─── 상태 ───
        private readonly List<EnemyAIController> activeAttackers = new List<EnemyAIController>();
        private float globalCooldownTimer;
        private bool playerInCombo;

        // ─── 대기열 (FIFO) ───
        private readonly Queue<EnemyAIController> attackQueue = new Queue<EnemyAIController>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void OnEnable()
        {
            // CombatEventBus 구독: 플레이어 콤보 상태 추적
            CombatEventBus.Subscribe<OnComboChanged>(OnComboChanged);
            CombatEventBus.Subscribe<OnComboBreak>(OnComboBreak);
        }

        private void OnDisable()
        {
            CombatEventBus.Unsubscribe<OnComboChanged>(OnComboChanged);
            CombatEventBus.Unsubscribe<OnComboBreak>(OnComboBreak);

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // 글로벌 쿨다운 틱
            if (globalCooldownTimer > 0f)
                globalCooldownTimer -= Time.deltaTime;

            // 죽은/비활성 공격자 정리
            CleanupDeadAttackers();

            // 대기열 처리: 슬롯 비면 다음 대기자에게 승인
            ProcessQueue();
        }

        // ────────────────────────────
        //  공개 API
        // ────────────────────────────

        /// <summary>
        /// 공격 슬롯 요청. 승인되면 true, 거부되면 대기열에 등록하고 false.
        /// 적 AI는 이 메서드의 결과에 따라 텔레그래프→공격 진행 여부를 결정한다.
        /// </summary>
        public bool RequestAttackSlot(EnemyAIController enemy)
        {
            if (enemy == null) return false;

            // 이미 활성 공격자이면 중복 요청 방지
            if (activeAttackers.Contains(enemy))
                return true;

            // 슬롯 + 쿨다운 체크
            if (activeAttackers.Count < maxSimultaneousAttackers && globalCooldownTimer <= 0f)
            {
                activeAttackers.Add(enemy);

                return true;
            }

            // 대기열에 등록 (중복 방지)
            if (!attackQueue.Contains(enemy))
            {
                attackQueue.Enqueue(enemy);

            }
            return false;
        }

        /// <summary>
        /// 공격 슬롯 반환. 공격이 끝나면 반드시 호출하여 슬롯을 해제한다.
        /// 호흡 시간 쿨다운을 시작한다.
        /// </summary>
        public void ReleaseAttackSlot(EnemyAIController enemy)
        {
            if (enemy == null) return;

            bool removed = activeAttackers.Remove(enemy);
            if (!removed) return;

            // 호흡 시간 설정 (플레이어 콤보 중이면 단축)
            float cooldown = playerInCombo
                ? breathingTime * comboDebuffMultiplier
                : breathingTime;
            globalCooldownTimer = cooldown;


        }

        /// <summary>특정 적이 현재 공격 중인지 확인</summary>
        public bool IsAttacking(EnemyAIController enemy)
        {
            return activeAttackers.Contains(enemy);
        }

        /// <summary>현재 활성 공격자 수</summary>
        public int ActiveAttackerCount => activeAttackers.Count;

        /// <summary>글로벌 쿨다운 남은 시간</summary>
        public float RemainingCooldown => globalCooldownTimer;

        // ────────────────────────────
        //  내부 로직
        // ────────────────────────────

        private void CleanupDeadAttackers()
        {
            for (int i = activeAttackers.Count - 1; i >= 0; i--)
            {
                if (activeAttackers[i] == null || !activeAttackers[i].gameObject.activeInHierarchy)
                {
                    activeAttackers.RemoveAt(i);
                }
            }
        }

        private void ProcessQueue()
        {
            while (attackQueue.Count > 0
                && activeAttackers.Count < maxSimultaneousAttackers
                && globalCooldownTimer <= 0f)
            {
                var next = attackQueue.Dequeue();

                // 유효성 체크
                if (next == null || !next.gameObject.activeInHierarchy)
                    continue;

                activeAttackers.Add(next);
                // 대기열에서 승인된 적에게 알림 → EnemyAI가 자체적으로 텔레그래프 진입

            }
        }

        // ────────────────────────────
        //  이벤트 핸들러
        // ────────────────────────────

        private void OnComboChanged(OnComboChanged evt)
        {
            playerInCombo = evt.ComboCount > 0;
        }

        private void OnComboBreak(OnComboBreak evt)
        {
            playerInCombo = false;
        }
    }
}
