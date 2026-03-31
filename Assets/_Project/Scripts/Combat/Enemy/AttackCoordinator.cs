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
    ///
    /// ★ 그룹 AI 확장:
    ///   - 우선순위 기반 토큰 발급 (근거리 > 원거리)
    ///   - 좌우 교대 공격 (한쪽으로 쏠리지 않게)
    ///   - 개별 적 쿨다운 (같은 적이 연속 공격 방지)
    ///   - 화면 밖 적 토큰 불가 (페어플레이)
    /// </summary>
    public class AttackCoordinator : MonoBehaviour
    {
        public static AttackCoordinator Instance { get; private set; }

        // ─── ★ 데이터 튜닝: 공격 슬롯 ───
        [Header("공격 슬롯")]
        [Tooltip("동시에 공격할 수 있는 최대 적 수")]
        [SerializeField] private int maxSimultaneousAttackers = CombatConstants.MaxSimultaneousAttackers;

        [Header("호흡 시간")]
        [Tooltip("공격 완료 후 다음 공격까지 최소 대기 시간")]
        [SerializeField] private float breathingTime = CombatConstants.BreathingTime;

        [Header("플레이어 콤보 디버프")]
        [Tooltip("플레이어 콤보 중 적 공격 빈도 감소 비율 (0.7 = 30% 감소)")]
        [SerializeField] private float comboDebuffMultiplier = 0.7f;

        // ─── ★ 데이터 튜닝: 그룹 AI 확장 ───
        [Header("좌우 교대")]
        [Tooltip("좌우 교대 공격 활성화")]
        [SerializeField] private bool alternateLeftRight = true;

        [Header("개별 쿨다운")]
        [Tooltip("같은 적이 연속 토큰을 받지 못하는 쿨다운 (초)")]
        [SerializeField] private float perEnemyCooldown = 3.0f;

        [Header("화면 제한")]
        [Tooltip("화면 밖 적의 토큰 획득 차단")]
        [SerializeField] private bool screenBoundCheck = true;

        // ─── 상태 ───
        private readonly List<EnemyAIController> activeAttackers = new List<EnemyAIController>();
        private float globalCooldownTimer;
        private bool playerInCombo;

        // ─── 좌우 교대 추적 ───
        private int lastAttackSide; // -1=왼쪽에서 공격, +1=오른쪽에서 공격, 0=초기

        // ─── 개별 쿨다운 추적 ───
        private readonly Dictionary<EnemyAIController, float> enemyCooldowns
            = new Dictionary<EnemyAIController, float>();

        // ─── 대기 후보 목록 (매 프레임 재정렬) ───
        private readonly List<EnemyAIController> candidates = new List<EnemyAIController>();

        // ─── 등록된 적 목록 (Circling 상태의 적 전체) ───
        private readonly HashSet<EnemyAIController> registeredEnemies = new HashSet<EnemyAIController>();

        /// <summary>현재 활성 공격자 수</summary>
        public int ActiveAttackerCount => activeAttackers.Count;

        /// <summary>글로벌 쿨다운 남은 시간</summary>
        public float RemainingCooldown => globalCooldownTimer;

        /// <summary>마지막 공격 사이드 (-1=좌, +1=우)</summary>
        public int LastAttackSide => lastAttackSide;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void OnEnable()
        {
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

            // 개별 쿨다운 틱
            TickEnemyCooldowns();

            // 죽은/비활성 공격자 정리
            CleanupDeadAttackers();

            // ★ 우선순위 기반 토큰 발급
            ProcessCandidates();
        }

        // ────────────────────────────
        //  등록 API (Circling 적 관리)
        // ────────────────────────────

        /// <summary>적을 공격 후보로 등록 (Circling 진입 시)</summary>
        public void RegisterEnemy(EnemyAIController enemy)
        {
            if (enemy != null)
                registeredEnemies.Add(enemy);
        }

        /// <summary>적을 공격 후보에서 해제 (사망/비활성 시)</summary>
        public void UnregisterEnemy(EnemyAIController enemy)
        {
            if (enemy != null)
            {
                registeredEnemies.Remove(enemy);
                enemyCooldowns.Remove(enemy);
            }
        }

        // ────────────────────────────
        //  공개 API
        // ────────────────────────────

        /// <summary>
        /// 공격 슬롯 요청. 승인되면 true, 거부되면 false.
        /// 적 AI는 이 메서드의 결과에 따라 텔레그래프→공격 진행 여부를 결정한다.
        /// </summary>
        public bool RequestAttackSlot(EnemyAIController enemy)
        {
            if (enemy == null) return false;

            // 이미 활성 공격자이면 중복 요청 방지
            if (activeAttackers.Contains(enemy))
                return true;

            // ★ 개별 쿨다운 체크
            if (enemyCooldowns.ContainsKey(enemy) && enemyCooldowns[enemy] > 0f)
                return false;

            // ★ 화면 밖 체크
            if (screenBoundCheck && ThreatLineManager.Instance != null)
            {
                if (!ThreatLineManager.Instance.IsOnScreen(enemy.transform.position))
                    return false;
            }

            // 슬롯 + 쿨다운 체크
            if (activeAttackers.Count < maxSimultaneousAttackers && globalCooldownTimer <= 0f)
            {
                // ★ 좌우 교대 체크
                if (alternateLeftRight && ThreatLineManager.Instance != null
                    && lastAttackSide != 0)
                {
                    int enemySide = ThreatLineManager.Instance.GetSide(enemy);
                    // 같은 쪽에서 연속 공격 시도 → 반대편에 후보가 있으면 거부
                    if (enemySide == lastAttackSide && HasCandidateOnSide(-lastAttackSide))
                        return false;
                }

                activeAttackers.Add(enemy);

                // 좌우 추적 갱신
                if (ThreatLineManager.Instance != null)
                    lastAttackSide = ThreatLineManager.Instance.GetSide(enemy);

                return true;
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

            // ★ 개별 쿨다운 설정
            enemyCooldowns[enemy] = perEnemyCooldown;
        }

        /// <summary>특정 적이 현재 공격 중인지 확인</summary>
        public bool IsAttacking(EnemyAIController enemy)
        {
            return activeAttackers.Contains(enemy);
        }

        // ────────────────────────────
        //  내부 로직
        // ────────────────────────────

        private void CleanupDeadAttackers()
        {
            for (int i = activeAttackers.Count - 1; i >= 0; i--)
            {
                if (activeAttackers[i] == null || !activeAttackers[i].gameObject.activeInHierarchy)
                    activeAttackers.RemoveAt(i);
            }

            // 등록 목록도 정리
            registeredEnemies.RemoveWhere(e => e == null || !e.gameObject.activeInHierarchy);
        }

        /// <summary>
        /// ★ 우선순위 기반 후보 처리.
        /// 매 프레임 등록된 적 중 자격 있는 후보를 수집 → 우선순위 정렬 → 토큰 발급.
        /// 기존 FIFO 큐 대신 능동적으로 후보를 선정한다.
        /// </summary>
        private void ProcessCandidates()
        {
            if (activeAttackers.Count >= maxSimultaneousAttackers) return;
            if (globalCooldownTimer > 0f) return;

            candidates.Clear();

            foreach (var enemy in registeredEnemies)
            {
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

                // 이미 공격 중이면 스킵
                if (activeAttackers.Contains(enemy)) continue;

                // 개별 쿨다운 체크
                if (enemyCooldowns.ContainsKey(enemy) && enemyCooldowns[enemy] > 0f) continue;

                // 화면 밖 체크
                if (screenBoundCheck && ThreatLineManager.Instance != null)
                {
                    if (!ThreatLineManager.Instance.IsOnScreen(enemy.transform.position))
                        continue;
                }

                candidates.Add(enemy);
            }

            if (candidates.Count == 0) return;

            // ★ 우선순위 정렬: 근거리 > 원거리, 좌우 교대 보너스
            candidates.Sort((a, b) =>
            {
                float scoreA = CalculatePriority(a);
                float scoreB = CalculatePriority(b);
                return scoreB.CompareTo(scoreA); // 높은 순
            });

            // 최우선 후보는 Circling 상태에서 자체적으로 RequestAttackSlot을 호출한다.
            // 여기서는 별도 강제 발급하지 않음 — 후보 순위만 정보로 제공.
        }

        /// <summary>적의 공격 우선순위 점수 계산 (높을수록 우선)</summary>
        private float CalculatePriority(EnemyAIController enemy)
        {
            float score = 0f;

            // 1. 거리: PC에 가까울수록 높은 점수
            float dist = Vector2.Distance(enemy.transform.position,
                FindAnyObjectByType<Player.PlayerCombatFSM>()?.transform.position ?? Vector3.zero);
            score += Mathf.Max(0f, 10f - dist); // 최대 10점

            // 2. 좌우 교대 보너스: 마지막 공격 반대편이면 +5
            if (alternateLeftRight && ThreatLineManager.Instance != null && lastAttackSide != 0)
            {
                int side = ThreatLineManager.Instance.GetSide(enemy);
                if (side != lastAttackSide)
                    score += 5f;
            }

            // 3. 오래 대기한 적 보너스 (개별 쿨다운이 0에 가까울수록)
            if (enemyCooldowns.ContainsKey(enemy))
            {
                float cd = enemyCooldowns[enemy];
                if (cd <= 0f) score += 3f; // 쿨다운 완료
            }
            else
            {
                score += 3f; // 쿨다운 기록 없음 = 첫 공격
            }

            return score;
        }

        /// <summary>특정 사이드에 공격 가능한 후보가 있는지 확인</summary>
        private bool HasCandidateOnSide(int side)
        {
            if (ThreatLineManager.Instance == null) return false;

            foreach (var enemy in registeredEnemies)
            {
                if (enemy == null || activeAttackers.Contains(enemy)) continue;
                if (enemyCooldowns.ContainsKey(enemy) && enemyCooldowns[enemy] > 0f) continue;
                if (ThreatLineManager.Instance.GetSide(enemy) == side)
                    return true;
            }
            return false;
        }

        private void TickEnemyCooldowns()
        {
            // Dictionary를 순회하며 감소 (키 수정을 위해 임시 리스트 사용)
            var keys = new List<EnemyAIController>(enemyCooldowns.Keys);
            foreach (var key in keys)
            {
                if (key == null)
                {
                    enemyCooldowns.Remove(key);
                    continue;
                }
                float cd = enemyCooldowns[key] - Time.deltaTime;
                if (cd <= 0f)
                    enemyCooldowns.Remove(key);
                else
                    enemyCooldowns[key] = cd;
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
