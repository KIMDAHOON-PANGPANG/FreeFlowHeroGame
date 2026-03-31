using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// Aggression System — 전투 리듬 곡선 제어.
    /// 전투의 긴장감을 일정하지 않게 조절하여
    /// 긴장(공격 러시)과 이완(호흡 구간)이 반복되는 리듬을 만든다.
    ///
    /// Aggression Level (0.0~1.0)에 따라:
    ///   - 동시 공격자 수 (maxSimultaneousAttackers)
    ///   - 공격 간격 (breathingTime)
    ///   - 적 행동 속도
    /// 가 동적으로 변한다.
    ///
    /// 입력 요소:
    ///   - 플레이어 콤보 카운트 → 높을수록 aggression 상승
    ///   - 잔여 적 수 → 적 줄면 aggression 상승 (최후의 발악)
    ///   - 콤보 끊김 → 잠시 aggression 하락 (회복 기회)
    ///   - 호흡 타이머 → 고강도 후 강제 휴식
    /// </summary>
    public class AggressionSystem : MonoBehaviour
    {
        public static AggressionSystem Instance { get; private set; }

        // ─── ★ 데이터 튜닝: Aggression 곡선 ───
        [Header("Aggression 범위")]
        [Tooltip("기본 Aggression (전투 시작 시)")]
        [SerializeField] private float baseAggression = 0.2f;

        [Header("콤보 영향")]
        [Tooltip("콤보 카운트당 Aggression 증가량")]
        [SerializeField] private float aggressionPerCombo = 0.02f;

        [Tooltip("콤보 끊김 시 Aggression 감소량")]
        [SerializeField] private float comboBreakPenalty = 0.15f;

        [Header("잔여 적 영향")]
        [Tooltip("초기 적 수 (이보다 줄면 Aggression 증가)")]
        [SerializeField] private int initialEnemyCount = 5;

        [Tooltip("적 감소당 Aggression 증가량")]
        [SerializeField] private float aggressionPerEnemyKill = 0.08f;

        [Header("호흡 타이머")]
        [Tooltip("고강도 웨이브 지속 시간 (초) — 이후 강제 휴식")]
        [SerializeField] private float highIntensityDuration = 5.0f;

        [Tooltip("강제 휴식 시간 (초)")]
        [SerializeField] private float breathingCooldown = 1.5f;

        [Header("Aggression → 전투 파라미터 매핑")]
        [Tooltip("Low 구간 동시 공격자")]
        [SerializeField] private int attackersLow = 1;
        [Tooltip("Medium 구간 동시 공격자")]
        [SerializeField] private int attackersMedium = 2;
        [Tooltip("High 구간 동시 공격자")]
        [SerializeField] private int attackersHigh = 2;
        [Tooltip("Frenzy 구간 동시 공격자")]
        [SerializeField] private int attackersFrenzy = 3;

        [Tooltip("Low 구간 호흡 시간")]
        [SerializeField] private float breathingLow = 2.0f;
        [Tooltip("Medium 구간 호흡 시간")]
        [SerializeField] private float breathingMedium = 1.2f;
        [Tooltip("High 구간 호흡 시간")]
        [SerializeField] private float breathingHigh = 0.8f;
        [Tooltip("Frenzy 구간 호흡 시간")]
        [SerializeField] private float breathingFrenzy = 0.5f;

        // ─── 상태 ───
        private float currentAggression;
        private float targetAggression;
        private float highIntensityTimer;
        private float breathingTimer;
        private bool isBreathing; // 강제 휴식 중
        private int currentEnemyCount;

        // ─── Public ───

        /// <summary>현재 Aggression Level (0.0~1.0)</summary>
        public float AggressionLevel => currentAggression;

        /// <summary>강제 휴식 중인지</summary>
        public bool IsBreathing => isBreathing;

        /// <summary>현재 Aggression에 따른 최대 동시 공격자 수</summary>
        public int GetMaxAttackers()
        {
            if (isBreathing) return 0; // 휴식 중 공격 금지
            if (currentAggression < 0.3f) return attackersLow;
            if (currentAggression < 0.6f) return attackersMedium;
            if (currentAggression < 0.8f) return attackersHigh;
            return attackersFrenzy;
        }

        /// <summary>현재 Aggression에 따른 호흡 시간</summary>
        public float GetBreathingTime()
        {
            if (currentAggression < 0.3f) return breathingLow;
            if (currentAggression < 0.6f) return breathingMedium;
            if (currentAggression < 0.8f) return breathingHigh;
            return breathingFrenzy;
        }

        /// <summary>Aggression 레벨 문자열 (디버그/UI용)</summary>
        public string GetAggressionTier()
        {
            if (currentAggression < 0.3f) return "Low";
            if (currentAggression < 0.6f) return "Medium";
            if (currentAggression < 0.8f) return "High";
            return "Frenzy";
        }

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
            CombatEventBus.Subscribe<OnEnemyDeath>(OnEnemyDeath);
        }

        private void OnDisable()
        {
            CombatEventBus.Unsubscribe<OnComboChanged>(OnComboChanged);
            CombatEventBus.Unsubscribe<OnComboBreak>(OnComboBreak);
            CombatEventBus.Unsubscribe<OnEnemyDeath>(OnEnemyDeath);

            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            currentAggression = baseAggression;
            targetAggression = baseAggression;
            currentEnemyCount = initialEnemyCount;

            // 씬의 적 수로 초기화
            var enemies = FindObjectsByType<EnemyAIController>(FindObjectsSortMode.None);
            if (enemies.Length > 0)
            {
                currentEnemyCount = enemies.Length;
                initialEnemyCount = enemies.Length;
            }
        }

        private void Update()
        {
            // Aggression 보간 (급격한 변화 방지)
            currentAggression = Mathf.MoveTowards(currentAggression, targetAggression,
                0.3f * Time.deltaTime);
            currentAggression = Mathf.Clamp01(currentAggression);

            // 고강도 지속 시 강제 휴식
            if (currentAggression >= 0.6f && !isBreathing)
            {
                highIntensityTimer += Time.deltaTime;
                if (highIntensityTimer >= highIntensityDuration)
                {
                    StartBreathing();
                }
            }
            else if (currentAggression < 0.6f)
            {
                highIntensityTimer = 0f;
            }

            // 휴식 타이머
            if (isBreathing)
            {
                breathingTimer -= Time.deltaTime;
                if (breathingTimer <= 0f)
                {
                    isBreathing = false;
                    // 휴식 후 aggression 살짝 낮춤
                    targetAggression = Mathf.Max(baseAggression, targetAggression - 0.1f);
                }
            }
        }

        // ─── 내부 ───

        private void RecalculateAggression()
        {
            float agg = baseAggression;

            // 잔여 적 감소 보너스
            int killed = Mathf.Max(0, initialEnemyCount - currentEnemyCount);
            agg += killed * aggressionPerEnemyKill;

            // "최후의 발악": 2명 이하면 추가 보너스
            if (currentEnemyCount <= 2 && currentEnemyCount > 0)
                agg += 0.2f;

            targetAggression = Mathf.Clamp01(agg);
        }

        private void StartBreathing()
        {
            isBreathing = true;
            breathingTimer = breathingCooldown;
            highIntensityTimer = 0f;
        }

        // ─── 이벤트 핸들러 ───

        private void OnComboChanged(OnComboChanged evt)
        {
            // 콤보 높을수록 aggression 상승
            float comboBonus = evt.ComboCount * aggressionPerCombo;
            targetAggression = Mathf.Clamp01(baseAggression + comboBonus +
                Mathf.Max(0, initialEnemyCount - currentEnemyCount) * aggressionPerEnemyKill);

            // "최후의 발악"
            if (currentEnemyCount <= 2 && currentEnemyCount > 0)
                targetAggression = Mathf.Min(1f, targetAggression + 0.2f);
        }

        private void OnComboBreak(OnComboBreak evt)
        {
            // 콤보 끊김 → aggression 하락 (회복 기회)
            targetAggression = Mathf.Max(baseAggression,
                targetAggression - comboBreakPenalty);
        }

        private void OnEnemyDeath(OnEnemyDeath evt)
        {
            currentEnemyCount = Mathf.Max(0, currentEnemyCount - 1);
            RecalculateAggression();
        }
    }
}
