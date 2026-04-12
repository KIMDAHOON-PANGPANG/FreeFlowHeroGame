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

        // ─── ★ 데이터 튜닝: 공격 슬롯 ───
        [Header("공격 슬롯")]
        [Tooltip("동시에 공격할 수 있는 최대 적 수. 토큰 시스템에서는 1 고정.")]
        [SerializeField] private int maxSimultaneousAttackers = 1;

        [Header("호흡 시간")]
        [Tooltip("공격 완료 후 다음 공격까지 최소 대기 시간")]
        [SerializeField] private float breathingTime = CombatConstants.BreathingTime;

        [Header("플레이어 콤보 디버프")]
        [Tooltip("플레이어 콤보 중 적 공격 빈도 감소 비율 (0.7 = 30% 감소)")]
        [SerializeField] private float comboDebuffMultiplier = 0.7f;

        // ─── ★ 데이터 튜닝: 그룹 AI / 포메이션 ───
        [Header("그룹 AI — 거리 분기")]
        [Tooltip("비토큰 적 행동 분기 경계. 이 거리 이하이면 스탠드오프, 이상이면 포위 슬롯으로 이동")]
        [SerializeField] private float closeRangeThreshold = 2.0f;
        [Tooltip("토큰 보유자가 PC에게 바짝 붙는 목표 거리. 이 거리 이하에 도달해야 Telegraph → Attack 시작")]
        [SerializeField] private float holderEngageDistance = 0.9f;

        [Header("그룹 AI — 스탠드오프 (근접 대치)")]
        [Tooltip("플레이어와 유지할 대치 거리")]
        [SerializeField] private float standoffDistance = 2.2f;
        [Tooltip("스탠드오프 거리 이력 (빈번한 방향 전환 방지)")]
        [SerializeField] private float standoffHysteresis = 0.3f;
        [Tooltip("백스텝 속도 배율 (chaseSpeed 기준)")]
        [SerializeField] private float retreatSpeedMultiplier = 0.6f;

        [Header("그룹 AI — 서라운드 (포위 슬롯)")]
        [Tooltip("포위 슬롯이 플레이어로부터 떨어지는 기본 거리 (가장 가까운 좌/우 적의 위치)")]
        [SerializeField] private float surroundRadius = 2.5f;
        [Tooltip("포위 슬롯 이동 속도 (PC 관통 허용, 충돌 무시)")]
        [SerializeField] private float surroundApproachSpeed = 3.0f;
        [Tooltip("포위 슬롯 개수 (기본 2 = 좌/우). 초과 적은 후방 대기")]
        [SerializeField] private int formationSlotCount = 2;
        [Tooltip("같은 사이드(좌 또는 우)의 적들이 유지하는 최소 X 간격. REPLACED 스타일 도열")]
        [SerializeField] private float minEnemySpacing = 1.3f;

        // ─── 토큰 이전 튜닝 값은 BattleSettings에서 로드 ───
        // (AttackCoordinator 인스펙터에서는 조절 불가 — REPLACED > Battle Settings에서 중앙 관리)

        // ─── 상태 ───
        private readonly List<EnemyAIController> activeAttackers = new List<EnemyAIController>();
        private float globalCooldownTimer;
        private bool playerInCombo;

        // ─── 토큰 / 등록부 ───
        private readonly List<EnemyAIController> registeredEnemies = new List<EnemyAIController>();
        private EnemyAIController currentTokenHolder;
        private float lastTokenTransferTime;

        // ─── 토큰 보유자 히트 게이지 ───
        // 현재 토큰 보유자가 맞아서 쌓인 게이지 (0 ~ tokenHolderGaugeMax).
        // 이 값이 최대치에 도달하면 사각지대 적으로 토큰 이전.
        // 감쇠: 마지막 피격 후 decayDelay 경과 시 decayPerSecond 속도로 감소.
        // 새 토큰 부여 / Release / Transfer 시 0으로 리셋.
        private float currentHolderGauge;
        private float lastHolderHitTime;

        /// <summary>현재 토큰 보유자의 히트 게이지 값 (0~max). 시각 인디케이터용.</summary>
        public float CurrentHolderGauge => currentHolderGauge;

        /// <summary>현재 토큰 보유자의 히트 게이지 비율 (0~1). 시각 인디케이터용.</summary>
        public float CurrentHolderGaugeRatio
        {
            get
            {
                float max = BattleSettings.GetTokenHolderGaugeMax();
                return max > 0f ? Mathf.Clamp01(currentHolderGauge / max) : 0f;
            }
        }

        // ─── 포메이션 슬롯 (Godot ThreatLineManager 이식) ───
        // 고정 8 슬롯: 인덱스 0~3 = 좌측 (가까운→먼), 4~7 = 우측 (가까운→먼)
        // offset = playerX 기준 상대 좌표, surroundRadius + minEnemySpacing 에서 동적 생성
        private readonly float[] slotOffsets = new float[8];
        private readonly Dictionary<EnemyAIController, int> slotAssignments = new Dictionary<EnemyAIController, int>();
        private float slotRebalanceTimer;

        /// <summary>현재 활성 공격자 수</summary>
        public int ActiveAttackerCount => activeAttackers.Count;

        /// <summary>글로벌 쿨다운 남은 시간</summary>
        public float RemainingCooldown => globalCooldownTimer;

        /// <summary>현재 토큰 보유자 (없으면 null)</summary>
        public EnemyAIController CurrentTokenHolder => currentTokenHolder;

        /// <summary>등록된 적 수 (디버그/테스트용)</summary>
        public int RegisteredEnemyCount => registeredEnemies.Count;

        /// <summary>등록부에서 index로 적 조회 (범위 밖이면 null)</summary>
        public EnemyAIController GetRegisteredEnemyAt(int index)
        {
            if (index < 0 || index >= registeredEnemies.Count) return null;
            return registeredEnemies[index];
        }

        // ─── 튜닝 값 외부 노출 (비토큰 적 행동 모듈용) ───
        public float CloseRangeThreshold => closeRangeThreshold;
        public float HolderEngageDistance => holderEngageDistance;
        public float StandoffDistance => standoffDistance;
        public float StandoffHysteresis => standoffHysteresis;
        public float RetreatSpeedMultiplier => retreatSpeedMultiplier;
        public float SurroundRadius => surroundRadius;
        public float SurroundApproachSpeed => surroundApproachSpeed;
        public int FormationSlotCount => formationSlotCount;
        public float MinEnemySpacing => minEnemySpacing;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            RecomputeSlotOffsets();
        }

        private void OnValidate()
        {
            RecomputeSlotOffsets();
        }

        /// <summary>
        /// 슬롯 offset 재계산 (Awake + Inspector 값 변경 시 호출).
        /// 좌 4 슬롯: -radius, -(radius+s), -(radius+2s), -(radius+3s)
        /// 우 4 슬롯:  radius,  radius+s,    radius+2s,    radius+3s
        /// </summary>
        private void RecomputeSlotOffsets()
        {
            float r = surroundRadius;
            float s = minEnemySpacing;
            // 좌측 (인덱스 0 = 플레이어와 가장 가까움)
            slotOffsets[0] = -r;
            slotOffsets[1] = -(r + s);
            slotOffsets[2] = -(r + 2f * s);
            slotOffsets[3] = -(r + 3f * s);
            // 우측 (인덱스 4 = 플레이어와 가장 가까움)
            slotOffsets[4] = r;
            slotOffsets[5] = r + s;
            slotOffsets[6] = r + 2f * s;
            slotOffsets[7] = r + 3f * s;
        }

        private void OnEnable()
        {
            CombatEventBus.Subscribe<OnComboChanged>(OnComboChanged);
            CombatEventBus.Subscribe<OnComboBreak>(OnComboBreak);
            CombatEventBus.Subscribe<OnAttackHit>(HandleAttackHit);
            CombatEventBus.Subscribe<OnEnemyDeath>(HandleEnemyDeath);
        }

        private void OnDisable()
        {
            CombatEventBus.Unsubscribe<OnComboChanged>(OnComboChanged);
            CombatEventBus.Unsubscribe<OnComboBreak>(OnComboBreak);
            CombatEventBus.Unsubscribe<OnAttackHit>(HandleAttackHit);
            CombatEventBus.Unsubscribe<OnEnemyDeath>(HandleEnemyDeath);

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

            // 토큰 보유자 히트 게이지 감쇠 (콤보 끊긴 후 자연 회복)
            TickHolderGaugeDecay();

            // 슬롯 Rebalance (0.5초 간격) — 좌/우 극단적 불균형 자동 교정
            slotRebalanceTimer -= Time.deltaTime;
            if (slotRebalanceTimer <= 0f)
            {
                RebalanceSlots();
                slotRebalanceTimer = 0.5f;
            }

            // ★ 자동 토큰 부여: 토큰 홀더가 없고 쿨다운 끝났으면 가장 가까운 eligible 적에게 토큰을 준다.
            //   비토큰 적이 슬롯(>= surroundRadius)에 도열해서 attackRange 안으로 못 들어가는 경우
            //   영원히 공격이 시작되지 않는 현상 해결. (Godot CombatDirector 방식)
            if (currentTokenHolder == null && activeAttackers.Count == 0 && globalCooldownTimer <= 0f)
            {
                TryAutoGrantToken();
            }
        }

        /// <summary>
        /// 토큰 보유자 히트 게이지 감쇠 처리.
        /// 마지막 피격 후 decayDelay 시간이 지나면 decayPerSecond 속도로 게이지 감소.
        /// "콤보가 끊기면 적이 멘탈을 회복해서 다시 오래 버틸 수 있다"는 느낌.
        /// </summary>
        private void TickHolderGaugeDecay()
        {
            if (currentTokenHolder == null) return;
            if (currentHolderGauge <= 0f) return;

            float delay = BattleSettings.GetTokenHolderGaugeDecayDelay();
            if (Time.time - lastHolderHitTime < delay) return;

            float decayRate = BattleSettings.GetTokenHolderGaugeDecayPerSecond();
            currentHolderGauge -= decayRate * Time.deltaTime;
            if (currentHolderGauge < 0f)
                currentHolderGauge = 0f;
        }

        /// <summary>
        /// 토큰 홀더 부재 시 가장 가까운 eligible 적을 선정하여 토큰을 즉시 부여한다.
        /// Chase/Surround/Idle 등 어떤 상태여도 동작하며, 다음 프레임부터 해당 적이
        /// UpdateChase의 isHolder 분기로 진입해 holderEngageDistance까지 접근한다.
        /// </summary>
        private void TryAutoGrantToken()
        {
            Transform playerRef = GetPlayerReference();
            if (playerRef == null) return;
            Vector2 playerPos = playerRef.position;

            EnemyAIController best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < registeredEnemies.Count; i++)
            {
                var e = registeredEnemies[i];
                if (e == null) continue;
                if (!e.IsTokenEligible()) continue;

                float d = Vector2.Distance(e.transform.position, playerPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }

            if (best == null) return;

            // 슬롯에서 빼고 활성 공격자로 등록 + 토큰 부여
            activeAttackers.Add(best);
            currentTokenHolder = best;
            lastTokenTransferTime = Time.time;
            // 게이지는 리셋하지 않음 — Release에서 유지된 값을 이어받는다.
            // (같은 적이 Release→AutoGrant로 다시 돌아오면 누적 게이지 유지)
            lastHolderHitTime = Time.time;
            ReleaseSlot(best);

            Debug.Log($"[Token] AutoGrant → {best.name} (gauge={currentHolderGauge:F0})");
            PublishTokenTransferred(null, best, TokenTransferReason.InitialGrant);
        }

        // ────────────────────────────
        //  공개 API
        // ────────────────────────────

        /// <summary>
        /// 공격 슬롯 요청. 승인되면 true, 거부되면 false.
        /// 토큰 시스템에서는 "슬롯 획득 = 토큰 부여"로 통합됨.
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

                // ─── 토큰 부여 ───
                currentTokenHolder = enemy;
                lastTokenTransferTime = Time.time;
                currentHolderGauge = 0f;
                lastHolderHitTime = Time.time;

                // 공격하러 나가므로 포메이션 슬롯 해제
                ReleaseSlot(enemy);

                Debug.Log($"[Token] Grant → {enemy.name}");

                return true;
            }

            return false;
        }

        /// <summary>
        /// 공격 슬롯 반환. 공격이 끝나면 반드시 호출하여 슬롯을 해제한다.
        /// 토큰 시스템에서는 "슬롯 반환 = 토큰 반환"으로 통합됨.
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

            // ─── 토큰 반환 ───
            // 게이지는 유지한다 (AutoGrant로 같은 적이 다시 토큰을 받으면 이어서 누적).
            // 게이지 리셋은 TransferTokenToClosest / 새 적에게 토큰 부여 시에만 발생.
            if (currentTokenHolder == enemy)
            {
                currentTokenHolder = null;
                // currentHolderGauge 유지 — 리셋하지 않음
                Debug.Log($"[Token] Release ← {enemy.name} (gauge={currentHolderGauge:F0} 유지)");
            }
        }

        /// <summary>특정 적이 현재 공격 중인지 확인</summary>
        public bool IsAttacking(EnemyAIController enemy)
        {
            return activeAttackers.Contains(enemy);
        }

        /// <summary>
        /// 토큰 보유자가 행동 불능 (Groggy/Knockdown/Down) 시 강제 토큰 이전.
        /// ReleaseAttackSlot과 달리 호흡 시간(breathingTime) 쿨다운을 설정하지 않음.
        /// → 새 토큰 보유자가 즉시 활성화되어 공격 가능.
        /// EnemyAIController.TransitionTo에서 호출.
        /// </summary>
        public void ForceTransferToken(EnemyAIController from, TokenTransferReason reason)
        {
            if (from == null) return;
            if (!IsTokenHolder(from)) return;

            Debug.Log($"[Token] ForceTransfer from {from.name} ({reason})");
            TransferTokenToClosest(from, reason);
        }

        // ────────────────────────────
        //  토큰 / 등록부 API
        // ────────────────────────────

        /// <summary>
        /// 적을 그룹 AI 등록부에 추가한다. EnemyAIController.Start()에서 호출.
        /// 이미 등록되어 있으면 무시 (멱등).
        /// </summary>
        public void RegisterEnemy(EnemyAIController enemy)
        {
            if (enemy == null) return;
            if (registeredEnemies.Contains(enemy)) return;
            registeredEnemies.Add(enemy);
        }

        /// <summary>
        /// 적을 그룹 AI 등록부에서 제거한다. 사망/파괴 시 호출.
        /// 등록부에 없으면 무시 (멱등).
        /// </summary>
        public void UnregisterEnemy(EnemyAIController enemy)
        {
            if (enemy == null) return;
            registeredEnemies.Remove(enemy);

            // 포메이션 슬롯 해제
            ReleaseSlot(enemy);

            // 토큰 보유자가 등록 해제되면 토큰 클리어 (다음 프레임 재할당은 후속 Step에서 구현)
            if (currentTokenHolder == enemy)
                currentTokenHolder = null;
        }

        /// <summary>지정된 적이 현재 토큰 보유자인지 확인</summary>
        public bool IsTokenHolder(EnemyAIController enemy)
        {
            return enemy != null && currentTokenHolder == enemy;
        }

        // ────────────────────────────
        //  포메이션 슬롯 시스템 (Godot ThreatLineManager 이식)
        //  - 8개 고정 슬롯 (좌 4 + 우 4)
        //  - 각 적이 한 슬롯을 "영구 소유"하여 매 프레임 목표가 흔들리지 않음
        //  - 배정은 "최근접 빈 슬롯" 원칙 → 자연스러운 사이드 분산
        //  - Rebalance로 좌/우 극단적 불균형 교정
        // ────────────────────────────

        /// <summary>
        /// 비토큰 적의 목표 X 좌표를 반환한다.
        /// 슬롯 미배정 상태면 최근접 빈 슬롯을 자동 배정한다.
        /// </summary>
        public float GetDesiredX(EnemyAIController self, float playerX)
        {
            if (self == null) return playerX;
            if (IsTokenHolder(self)) return playerX;

            if (!slotAssignments.TryGetValue(self, out int slotIdx))
            {
                slotIdx = AssignNearestEmptySlot(self, playerX);
                if (slotIdx < 0) return self.transform.position.x; // 8명 초과 드문 케이스
            }

            return playerX + slotOffsets[slotIdx];
        }

        /// <summary>
        /// 적의 현재 위치에서 가장 가까운 빈 슬롯을 배정한다.
        /// </summary>
        private int AssignNearestEmptySlot(EnemyAIController enemy, float playerX)
        {
            float enemyX = enemy.transform.position.x;
            int bestSlot = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < slotOffsets.Length; i++)
            {
                // 이미 다른 적이 점유한 슬롯은 스킵
                bool occupied = false;
                foreach (var kv in slotAssignments)
                {
                    if (kv.Value == i) { occupied = true; break; }
                }
                if (occupied) continue;

                float slotX = playerX + slotOffsets[i];
                float d = Mathf.Abs(enemyX - slotX);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSlot = i;
                }
            }

            if (bestSlot >= 0)
                slotAssignments[enemy] = bestSlot;

            return bestSlot;
        }

        /// <summary>
        /// 적의 슬롯을 해제한다. 토큰 부여/사망/행동불능 시 호출.
        /// </summary>
        public void ReleaseSlot(EnemyAIController enemy)
        {
            if (enemy == null) return;
            slotAssignments.Remove(enemy);
        }

        /// <summary>
        /// 좌/우 슬롯 점유가 극단적으로 치우치면 가장 먼 적을 반대편으로 이동한다.
        /// - 좌 0명 && 우 2명+ → 가장 먼 우측 적을 좌측 최근접 슬롯으로
        /// - 우 0명 && 좌 2명+ → 그 반대
        /// </summary>
        private void RebalanceSlots()
        {
            int leftCount = 0, rightCount = 0;
            foreach (var kv in slotAssignments)
            {
                if (kv.Value < 4) leftCount++;
                else rightCount++;
            }

            if (leftCount == 0 && rightCount >= 2)
            {
                // 가장 먼 우측 적 (인덱스 큰 것) 을 왼쪽 최근접 빈 슬롯으로
                EnemyAIController farthest = null;
                int farthestSlot = -1;
                foreach (var kv in slotAssignments)
                {
                    if (kv.Value >= 4 && kv.Value > farthestSlot)
                    {
                        farthestSlot = kv.Value;
                        farthest = kv.Key;
                    }
                }
                if (farthest != null)
                {
                    slotAssignments[farthest] = 0; // 좌측 최근접
                }
            }
            else if (rightCount == 0 && leftCount >= 2)
            {
                EnemyAIController farthest = null;
                int farthestSlot = 999;
                foreach (var kv in slotAssignments)
                {
                    if (kv.Value < 4 && kv.Value < farthestSlot)
                    {
                        farthestSlot = kv.Value;
                        farthest = kv.Key;
                    }
                }
                if (farthest != null)
                {
                    slotAssignments[farthest] = 4; // 우측 최근접
                }
            }
        }

        // ────────────────────────────
        //  토큰 이전 로직 (Step 3)
        // ────────────────────────────

        /// <summary>
        /// 플레이어의 공격이 토큰 보유자에게 적중하면 히트 게이지에 충전하고,
        /// 게이지가 최대치에 도달하면 사각지대 적으로 토큰을 이전한다.
        /// 게이지는 BattleSettings.tokenHolderGaugeMax / FillPerHit / DecayDelay / DecayPerSecond 로 튜닝.
        /// 예시 기본값: 1000 max, 250 fill → 4히트에 가득 참. 콤보 끊기면 400/s로 감쇠.
        /// </summary>
        private void HandleAttackHit(OnAttackHit evt)
        {
            if (currentTokenHolder == null) return;

            // 토큰 보유자가 아닌 대상을 때렸으면 무시
            var holderTarget = currentTokenHolder.EnemyTarget as ICombatTarget;
            if (holderTarget == null || evt.Target != holderTarget) return;

            // 연속 이전 방지 (안전장치)
            if (Time.time - lastTokenTransferTime < BattleSettings.GetTokenTransferMinInterval()) return;

            float max = BattleSettings.GetTokenHolderGaugeMax();
            float fill = BattleSettings.GetTokenHolderGaugeFillPerHit();

            currentHolderGauge += fill;
            lastHolderHitTime = Time.time;

            float ratio = max > 0f ? (currentHolderGauge / max) : 1f;
            Debug.Log($"[Token] Gauge {currentHolderGauge:F0}/{max:F0} ({ratio:P0}) on {currentTokenHolder.name}");

            // 임계치 미달 → 현재 보유자가 계속 맞아준다
            if (currentHolderGauge < max) return;

            // 임계치 도달 → 토큰 이전
            TransferTokenToClosest(currentTokenHolder, TokenTransferReason.HolderHit);
        }

        /// <summary>
        /// 현재 토큰 보유자에게서 다음 적으로 토큰을 이전한다.
        /// 선정 기준:
        ///   - 기본: 플레이어와의 거리 최소
        ///   - 우선 보정: PC가 바라보지 않는 방향(사각지대)에 있는 적은 BattleSettings.backsideDistanceDiscount 를 곱해 유효거리를 줄임
        ///   - 예: 뒷쪽 적 실거리 3.0 × 0.6 = 1.8 → 앞쪽 적 실거리 2.5 보다 우선 선택
        ///   - 뒷쪽에 후보가 없거나 멀면 자연스럽게 앞쪽 최근접 적이 선택됨
        /// 이전 직후 새 보유자는 공격 슬롯에 즉시 등록되며, 호흡 시간 쿨다운은 건드리지 않는다.
        /// </summary>
        private void TransferTokenToClosest(EnemyAIController from, TokenTransferReason reason)
        {
            // 이전 보유자를 슬롯에서 제거 (쿨다운 설정하지 않음 — ReleaseAttackSlot과는 다른 경로)
            if (from != null)
                activeAttackers.Remove(from);

            // 플레이어 위치 / facing 방향 (localScale.x < 0 → 좌측, 그 외 → 우측)
            Transform playerRef = GetPlayerReference();
            Vector2 playerPos = playerRef != null
                ? (Vector2)playerRef.position
                : (from != null ? (Vector2)from.transform.position : Vector2.zero);
            float playerFacing = (playerRef != null && playerRef.localScale.x < 0f) ? -1f : 1f;

            // 후보 선정: 거리 우선이지만 뒷쪽(사각지대) 적에게 거리 할인 적용
            float discount = BattleSettings.GetBacksideDistanceDiscount();
            EnemyAIController next = null;
            float bestScore = float.MaxValue;
            bool nextIsBackside = false;
            for (int i = 0; i < registeredEnemies.Count; i++)
            {
                var candidate = registeredEnemies[i];
                if (candidate == null || candidate == from) continue;
                if (!candidate.IsTokenEligible()) continue;

                float d = Vector2.Distance(candidate.transform.position, playerPos);

                // 사각지대 판정: 후보의 relativeX 부호와 플레이어 facing 부호가 반대이면 "뒤"
                float relX = candidate.transform.position.x - playerPos.x;
                bool isBackside = (relX != 0f) && (Mathf.Sign(relX) != playerFacing);

                // 유효거리 = 실거리 × (뒷쪽이면 할인)
                float score = isBackside ? d * discount : d;

                if (score < bestScore)
                {
                    bestScore = score;
                    next = candidate;
                    nextIsBackside = isBackside;
                }
            }

            // 토큰 이전
            var prev = currentTokenHolder;
            if (next != null)
            {
                if (!activeAttackers.Contains(next))
                    activeAttackers.Add(next);
                currentTokenHolder = next;
                lastTokenTransferTime = Time.time;
                currentHolderGauge = 0f;
                lastHolderHitTime = Time.time;

                // 새 토큰 보유자는 공격하러 가므로 슬롯 해제
                ReleaseSlot(next);

                string sideTag = nextIsBackside ? "★뒷쪽" : "앞쪽";
                Debug.Log($"[Token] Transfer: {(prev != null ? prev.name : "<없음>")} → {next.name} [{sideTag}] ({reason})");
            }
            else
            {
                currentTokenHolder = null;
                currentHolderGauge = 0f;
                Debug.Log($"[Token] Transfer: {(prev != null ? prev.name : "<없음>")} → <후보 없음> ({reason})");
            }

            PublishTokenTransferred(prev, next, reason);
        }

        /// <summary>플레이어 Transform 참조를 등록된 적 중 하나에서 얻는다.</summary>
        private Transform GetPlayerReference()
        {
            for (int i = 0; i < registeredEnemies.Count; i++)
            {
                var e = registeredEnemies[i];
                if (e != null && e.PlayerRef != null)
                    return e.PlayerRef;
            }
            return null;
        }

        private void PublishTokenTransferred(EnemyAIController from, EnemyAIController to, TokenTransferReason reason)
        {
            CombatEventBus.Publish(new OnTokenTransferred
            {
                FromHolder = from,
                ToHolder = to,
                Reason = reason
            });
        }

        // ────────────────────────────
        //  디버그 유틸 (개발용)
        // ────────────────────────────

        /// <summary>
        /// Inspector 우클릭 → "토큰/등록부 상태 출력"으로 Console에 현재 상태를 찍는다.
        /// </summary>
        [ContextMenu("토큰/등록부 상태 출력")]
        private void DebugPrintTokenState()
        {
            string holder = currentTokenHolder != null ? currentTokenHolder.name : "<없음>";
            string list = "";
            for (int i = 0; i < registeredEnemies.Count; i++)
            {
                var e = registeredEnemies[i];
                if (e == null) { list += (i > 0 ? ", " : "") + "<null>"; continue; }
                float dx = 0f;
                if (e.PlayerRef != null) dx = Mathf.Abs(e.transform.position.x - e.PlayerRef.position.x);
                list += (i > 0 ? "\n  " : "\n  ") + $"{e.name} pos=({e.transform.position.x:F2},{e.transform.position.y:F2}) distToPlayer={dx:F2}";
            }
            Debug.Log($"[AttackCoordinator] 등록 {registeredEnemies.Count}명:{list}\n토큰 보유자: {holder} / 활성 공격자: {activeAttackers.Count}");
        }

        // ─── Scene 뷰 디버그 (플레이 모드: 토큰/슬롯 배정 라인) ───
        // 슬롯 구체 표시 + 드래그 조절은 Editor/AttackCoordinatorEditor.cs 에서 처리
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            Transform playerRef = GetPlayerReference();
            if (playerRef == null) return;
            Vector3 pivotPos = playerRef.position;

            // 토큰 보유자 → 플레이어 라인 (금색)
            if (currentTokenHolder != null)
            {
                Vector3 holderPos = currentTokenHolder.transform.position;
                Gizmos.color = new Color(1f, 0.84f, 0f, 1f);
                Gizmos.DrawLine(holderPos + Vector3.up * 0.5f, pivotPos + Vector3.up * 0.5f);

                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(holderPos + Vector3.up * 2.5f, 0.5f);
            }

            // 슬롯 배정된 적 → 슬롯 라인 (초록)
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.6f);
            foreach (var kv in slotAssignments)
            {
                if (kv.Key == null) continue;
                Vector3 slotPos = new Vector3(pivotPos.x + slotOffsets[kv.Value], pivotPos.y + 0.1f, pivotPos.z);
                Gizmos.DrawLine(kv.Key.transform.position + Vector3.up * 0.3f, slotPos);
            }
        }
#endif

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

            // ★ 안전장치: 토큰 보유자가 null/비활성이면 토큰 강제 해제 → 다음 프레임 AutoGrant
            if (currentTokenHolder != null
                && (currentTokenHolder.gameObject == null || !currentTokenHolder.gameObject.activeInHierarchy))
            {
                Debug.Log($"[Token] CleanupDeadAttackers — 토큰 보유자 사라짐, 강제 해제");
                currentTokenHolder = null;
                currentHolderGauge = 0f;
            }
        }

        // ────────────────────────────
        //  이벤트 핸들러
        // ────────────────────────────

        /// <summary>
        /// 적 사망 이벤트 핸들러.
        /// 사망한 적이 토큰 보유자이면 즉시 다음 적으로 이전.
        /// 사망한 적을 등록부에서 제거.
        /// </summary>
        private void HandleEnemyDeath(OnEnemyDeath evt)
        {
            if (evt.Enemy == null) return;

            // OnEnemyDeath는 ICombatTarget(DummyEnemyTarget)을 전달.
            // 등록부의 EnemyAIController와 매칭.
            EnemyAIController dead = null;
            for (int i = 0; i < registeredEnemies.Count; i++)
            {
                var e = registeredEnemies[i];
                if (e == null) continue;
                var target = e.EnemyTarget as ICombatTarget;
                if (target != null && target == evt.Enemy)
                {
                    dead = e;
                    break;
                }
            }

            if (dead == null) return;

            // 토큰 보유자가 사망 → 즉시 이전 (호흡 시간 없음)
            if (IsTokenHolder(dead))
            {
                Debug.Log($"[Token] EnemyDeath — {dead.name} 사망, 토큰 강제 이전");
                TransferTokenToClosest(dead, TokenTransferReason.HolderDied);
            }

            // 등록부에서 제거 (슬롯도 함께 해제됨)
            UnregisterEnemy(dead);
        }

        private void OnComboChanged(OnComboChanged evt)
        {
            playerInCombo = evt.ComboCount > 0;
        }

        private void OnComboBreak(OnComboBreak evt)
        {
            playerInCombo = false;
        }
    }

    // ────────────────────────────
    //  토큰 이벤트 (그룹 AI 전용)
    // ────────────────────────────

    /// <summary>토큰 이전 사유</summary>
    public enum TokenTransferReason
    {
        InitialGrant,
        HolderHit,
        HolderDied,
        HolderStunned,
        HolderAttackCompleted
    }

    /// <summary>토큰(공격권)이 부여됨</summary>
    public struct OnTokenGranted : ICombatEvent
    {
        public EnemyAIController Holder;
        public float GrantedTime;
    }

    /// <summary>토큰이 한 적에서 다른 적으로 이전됨</summary>
    public struct OnTokenTransferred : ICombatEvent
    {
        public EnemyAIController FromHolder;  // null 가능 (초기 부여)
        public EnemyAIController ToHolder;    // null 가능 (후보 없음)
        public TokenTransferReason Reason;
    }
}
