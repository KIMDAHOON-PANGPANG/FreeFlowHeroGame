using System;
using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>가중 랜덤 모션 엔트리 (가드 카운터/처형 등)</summary>
    [Serializable]
    public class WeightedMotionEntry
    {
        public string actionId;
        [Range(0.1f, 10f)]
        public float weight = 1.0f;
    }

    /// <summary>
    /// 전투 시스템 공용 설정 데이터 에셋.
    /// UE5의 DA_BATTLESETTINGS처럼 전투 규칙/수치를 한곳에서 관리한다.
    /// CombatConstants의 하드코딩 값을 런타임에서 오버라이드할 수 있다.
    ///
    /// 생성: REPLACED > Setup > 4. Generate BattleSettings Asset
    /// 위치: Assets/_Project/Data/CombatConfig/BattleSettings.asset
    /// </summary>
    [CreateAssetMenu(
        fileName = "BattleSettings",
        menuName = "REPLACED/Combat/BattleSettings",
        order = 0)]
    public class BattleSettings : ScriptableObject
    {
        // ════════════════════════════════════════════
        //  싱글톤 접근자
        // ════════════════════════════════════════════

        private static BattleSettings _instance;

        /// <summary>
        /// ScriptableObject 로드 시 자동 등록.
        /// Resources.LoadAll 또는 에셋 참조로 로드될 때 OnEnable이 호출된다.
        /// </summary>
        private void OnEnable()
        {
            _instance = this;
        }

        /// <summary>
        /// 런타임/에디터에서 BattleSettings에 접근한다.
        /// OnEnable에서 자동 등록되며, 없으면 Resources에서 검색한다.
        /// 할당되지 않으면 CombatConstants 기본값을 사용한다.
        /// </summary>
        public static BattleSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Resources 폴더에서 자동 검색
                    _instance = Resources.Load<BattleSettings>("BattleSettings");
                    if (_instance == null)
                    {
                        // 전체 에셋에서 검색 (에디터 전용은 아님 — SO는 빌드에 포함되면 FindObjectOfType 불가)
                        var all = Resources.LoadAll<BattleSettings>("");
                        if (all.Length > 0) _instance = all[0];
                    }
                    if (_instance != null)
                        Debug.Log($"<color=cyan>[BattleSettings] 자동 로드 성공</color>");
                }
                return _instance;
            }
            set => _instance = value;
        }

        /// <summary>인스턴스가 로드되어 있는지 확인</summary>
        public static bool IsLoaded => _instance != null;

        // ════════════════════════════════════════════
        //  프레임 기본
        // ════════════════════════════════════════════

        [Header("프레임 기본")]

        [Tooltip("목표 프레임 레이트. 전투 판정의 기준이 되는 FPS")]
        public int targetFPS = CombatConstants.TargetFPS;

        // ════════════════════════════════════════════
        //  콤보 시스템
        // ════════════════════════════════════════════

        [Header("콤보 시스템")]

        [Tooltip("마지막 히트 이후 다음 입력까지 콤보가 유지되는 시간 (초)")]
        [Range(0.1f, 3.0f)]
        public float comboWindowDuration = CombatConstants.ComboWindowDuration;

        [Tooltip("콤보 최대 카운트 (999 = 사실상 무제한)")]
        public int maxComboCount = CombatConstants.MaxComboCount;

        [Header("콤보 보너스 임계치")]

        [Tooltip("'Good' 등급 콤보 시작 히트 수")]
        public int comboThresholdGood = CombatConstants.ComboThresholdGood;

        [Tooltip("'Great' 등급 콤보 시작 히트 수")]
        public int comboThresholdGreat = CombatConstants.ComboThresholdGreat;

        [Tooltip("'Awesome' 등급 콤보 시작 히트 수")]
        public int comboThresholdAwesome = CombatConstants.ComboThresholdAwesome;

        [Tooltip("'Unstoppable' 등급 콤보 시작 히트 수")]
        public int comboThresholdUnstoppable = CombatConstants.ComboThresholdUnstoppable;

        // ════════════════════════════════════════════
        //  인풋 버퍼
        // ════════════════════════════════════════════

        [Header("인풋 버퍼")]

        [Tooltip("선입력이 유효한 시간 (초). 짧을수록 정밀한 입력 요구, 길수록 관대한 입력")]
        [Range(0.05f, 0.5f)]
        public float inputBufferDuration = CombatConstants.InputBufferDuration;

        // ════════════════════════════════════════════
        //  회피 (Dodge)
        // ════════════════════════════════════════════

        [Header("회피 (Dodge) — 액션 테이블로 이전됨, 레거시 폴백용")]

        [Tooltip("(Deprecated) 회피 시 무적 프레임 수 — 액션 테이블 startup/active 사용")]
        [Range(1, 30)]
        public int dodgeIFrames = CombatConstants.DodgeIFrames;

        [Tooltip("(Deprecated) 회피 이동 속도 — 액션 테이블 moveSpeed 사용")]
        public float dodgeSpeed = CombatConstants.DodgeSpeed;

        // ════════════════════════════════════════════
        //  워핑 (Warp)
        // ════════════════════════════════════════════

        [Header("워핑 (Warp)")]

        [Tooltip("워핑 시간 계산의 기준이 되는 최대 거리 (유닛)")]
        public float maxWarpDistance = CombatConstants.MaxWarpDistance;

        [Tooltip("★ 워핑 후 ROOT_MOTION으로 접근 가능한 적과의 최소 거리 (m). 이 거리 이내로는 전진 차단. 0.3 = 30cm")]
        [Range(0f, 2f)]
        public float warpMinContactDistance = CombatConstants.WarpMinContactDistance;

        // ════════════════════════════════════════════
        //  텔레그래프
        // ════════════════════════════════════════════

        [Header("텔레그래프")]

        [Tooltip("적 공격 예고 신호의 최소 표시 시간 (초)")]
        [Range(0.1f, 1.0f)]
        public float telegraphMinDuration = CombatConstants.TelegraphMinDuration;

        [Tooltip("적 공격 예고 신호의 최대 표시 시간 (초)")]
        [Range(0.2f, 2.0f)]
        public float telegraphMaxDuration = CombatConstants.TelegraphMaxDuration;

        // ════════════════════════════════════════════
        //  공격 턴 관리
        // ════════════════════════════════════════════

        [Header("공격 턴 관리")]

        [Tooltip("플레이어를 동시에 공격할 수 있는 최대 적 수")]
        [Range(1, 5)]
        public int maxSimultaneousAttackers = CombatConstants.MaxSimultaneousAttackers;

        [Tooltip("연속 공격 사이 최소 간격 (초). 플레이어에게 대응할 '숨 쉴 틈'을 준다")]
        [Range(0.1f, 2.0f)]
        public float breathingTime = CombatConstants.BreathingTime;

        // ════════════════════════════════════════════
        //  그룹 AI: 토큰 히트 게이지
        // ════════════════════════════════════════════

        [Header("그룹 AI — 토큰 히트 게이지")]

        [Tooltip("토큰 보유자가 맞는 히트 게이지 최대치. 이 값을 채우면 사각지대 적으로 토큰 이전. 1000 기준으로 비율 관리.")]
        [Range(100f, 5000f)]
        public float tokenHolderGaugeMax = CombatConstants.TokenHolderGaugeMax;

        [Tooltip("일반 히트 1회당 게이지 충전량. 기본 250이면 4히트에 가득 차서 토큰 이전. 값이 낮을수록 보유자가 더 오래 버팀 (REPLACED 느낌).")]
        [Range(50f, 1000f)]
        public float tokenHolderGaugeFillPerHit = CombatConstants.TokenHolderGaugeFillPerHit;

        [Tooltip("마지막 피격 후 감쇠가 시작되기까지의 대기 시간 (초). 이 시간 안에 새 히트가 들어오면 게이지 유지/증가.")]
        [Range(0f, 3f)]
        public float tokenHolderGaugeDecayDelay = CombatConstants.TokenHolderGaugeDecayDelay;

        [Tooltip("콤보 끊긴 후 게이지 초당 감쇠량. 400이면 2.5초 만에 만충된 게이지가 0이 됨. '멘탈 회복' 연출.")]
        [Range(0f, 2000f)]
        public float tokenHolderGaugeDecayPerSecond = CombatConstants.TokenHolderGaugeDecayPerSecond;

        [Tooltip("PC 사각지대(뒷쪽) 적의 거리 할인율. 0.6이면 뒷쪽 적의 유효거리가 실제의 60%로 계산되어 우선 선택됨. 1이면 방향 무시.")]
        [Range(0.1f, 1.0f)]
        public float backsideDistanceDiscount = CombatConstants.BacksideDistanceDiscount;

        [Tooltip("토큰 연속 이전 방지 최소 간격 (초). 안전장치.")]
        [Range(0.05f, 1f)]
        public float tokenTransferMinInterval = CombatConstants.TokenTransferMinInterval;

        // ════════════════════════════════════════════
        //  처형 (Execution)
        // ════════════════════════════════════════════

        [Header("처형 (Execution)")]

        [Tooltip("처형 가능 HP 비율 (0.2 = HP 20% 이하에서 처형 가능)")]
        [Range(0.05f, 0.5f)]
        public float executionHPThreshold = CombatConstants.ExecutionHPThreshold;

        [Tooltip("고콤보(x50+) 시 처형 HP 임계치 상향 (0.3 = 30%)")]
        [Range(0.05f, 0.5f)]
        public float executionHPThresholdHighCombo = CombatConstants.ExecutionHPThresholdHighCombo;

        [Tooltip("처형 가능 거리 (유닛)")]
        public float executionRange = CombatConstants.ExecutionRange;

        // ════════════════════════════════════════════
        //  헉슬리 건 (Huxley)
        // ════════════════════════════════════════════

        [Header("헉슬리 건 (Huxley)")]

        [Tooltip("히트 1회당 헉슬리 게이지 충전량 (%)")]
        public float huxleyBaseChargePerHit = CombatConstants.HuxleyBaseChargePerHit;

        [Tooltip("헉슬리 게이지 최대치 (%)")]
        public float huxleyMaxCharge = CombatConstants.HuxleyMaxCharge;

        // ════════════════════════════════════════════
        //  히트 플래시
        // ════════════════════════════════════════════

        [Header("히트 플래시")]

        [Tooltip("피격 시 플래시 지속 시간 (초)")]
        [Range(0.05f, 0.5f)]
        public float hitFlashDuration = CombatConstants.HitFlashDuration;

        [Tooltip("피격 시 플래시 강도 (0=없음, 1=최대)")]
        [Range(0f, 1f)]
        public float hitFlashIntensity = CombatConstants.HitFlashIntensity;

        // ════════════════════════════════════════════
        //  적 사망 연출
        // ════════════════════════════════════════════

        [Header("적 사망 연출")]

        [Tooltip("사망 후 페이드아웃 시작까지 대기 시간 (초)")]
        [Range(0f, 3f)]
        public float enemyDeathDelay = CombatConstants.EnemyDeathDelay;

        [Tooltip("페이드아웃 지속 시간 (초)")]
        [Range(0.1f, 3f)]
        public float enemyDeathFadeDuration = CombatConstants.EnemyDeathFadeDuration;

        // ════════════════════════════════════════════
        //  피격 리액션: Flinch 프리셋
        // ════════════════════════════════════════════

        [Header("Flinch — Light")]
        [Tooltip("밀림 거리 (cm)")] public float flinchLightPush = CombatConstants.FlinchLightPush;
        [Tooltip("경직 시간 (초)")] public float flinchLightFreeze = CombatConstants.FlinchLightFreeze;
        [Tooltip("히트스탑 (프레임)")] public float flinchLightHitStop = CombatConstants.FlinchLightHitStop;

        [Header("Flinch — Medium")]
        [Tooltip("밀림 거리 (cm)")] public float flinchMediumPush = CombatConstants.FlinchMediumPush;
        [Tooltip("경직 시간 (초)")] public float flinchMediumFreeze = CombatConstants.FlinchMediumFreeze;
        [Tooltip("히트스탑 (프레임)")] public float flinchMediumHitStop = CombatConstants.FlinchMediumHitStop;

        [Header("Flinch — Heavy")]
        [Tooltip("밀림 거리 (cm)")] public float flinchHeavyPush = CombatConstants.FlinchHeavyPush;
        [Tooltip("경직 시간 (초)")] public float flinchHeavyFreeze = CombatConstants.FlinchHeavyFreeze;
        [Tooltip("히트스탑 (프레임)")] public float flinchHeavyHitStop = CombatConstants.FlinchHeavyHitStop;

        // ════════════════════════════════════════════
        //  피격 리액션: Knockdown 프리셋
        // ════════════════════════════════════════════

        [Header("Knockdown — Light")]
        [Tooltip("뜨는 높이 (cm)")] public float knockdownLightHeight = CombatConstants.KnockdownLightHeight;
        [Tooltip("체공 시간 (초)")] public float knockdownLightAirTime = CombatConstants.KnockdownLightAirTime;
        [Tooltip("날아가는 거리 (cm)")] public float knockdownLightDistance = CombatConstants.KnockdownLightDistance;
        [Tooltip("착지 후 누워있는 시간 (초)")] public float knockdownLightDownTime = CombatConstants.KnockdownLightDownTime;

        [Header("Knockdown — Medium")]
        [Tooltip("뜨는 높이 (cm)")] public float knockdownMediumHeight = CombatConstants.KnockdownMediumHeight;
        [Tooltip("체공 시간 (초)")] public float knockdownMediumAirTime = CombatConstants.KnockdownMediumAirTime;
        [Tooltip("날아가는 거리 (cm)")] public float knockdownMediumDistance = CombatConstants.KnockdownMediumDistance;
        [Tooltip("착지 후 누워있는 시간 (초)")] public float knockdownMediumDownTime = CombatConstants.KnockdownMediumDownTime;

        [Header("Knockdown — Heavy")]
        [Tooltip("뜨는 높이 (cm)")] public float knockdownHeavyHeight = CombatConstants.KnockdownHeavyHeight;
        [Tooltip("체공 시간 (초)")] public float knockdownHeavyAirTime = CombatConstants.KnockdownHeavyAirTime;
        [Tooltip("날아가는 거리 (cm)")] public float knockdownHeavyDistance = CombatConstants.KnockdownHeavyDistance;
        [Tooltip("착지 후 누워있는 시간 (초)")] public float knockdownHeavyDownTime = CombatConstants.KnockdownHeavyDownTime;

        // ════════════════════════════════════════════
        //  피격 리액션: 모션 클립
        // ════════════════════════════════════════════

        [Header("히트 리액션 모션")]
        [Tooltip("Flinch 피격 모션 FBX 경로")]
        public string flinchClipPath = CombatConstants.FlinchClipPath;

        [Tooltip("Knockdown 피격 모션 FBX 경로")]
        public string knockdownClipPath = CombatConstants.KnockdownClipPath;

        // ════════════════════════════════════════════
        //  유틸리티
        // ════════════════════════════════════════════

        /// <summary>현재 설정의 프레임 지속 시간 (초)</summary>
        public float FrameDuration => 1f / Mathf.Max(targetFPS, 1);

        /// <summary>회피 무적 시간 (초)</summary>
        public float DodgeIFrameDuration => dodgeIFrames * FrameDuration;

        /// <summary>모든 값을 CombatConstants 기본값으로 리셋</summary>
        public void ResetToDefaults()
        {
            targetFPS = CombatConstants.TargetFPS;
            comboWindowDuration = CombatConstants.ComboWindowDuration;
            maxComboCount = CombatConstants.MaxComboCount;
            comboThresholdGood = CombatConstants.ComboThresholdGood;
            comboThresholdGreat = CombatConstants.ComboThresholdGreat;
            comboThresholdAwesome = CombatConstants.ComboThresholdAwesome;
            comboThresholdUnstoppable = CombatConstants.ComboThresholdUnstoppable;
            inputBufferDuration = CombatConstants.InputBufferDuration;
            dodgeIFrames = CombatConstants.DodgeIFrames;
            dodgeSpeed = CombatConstants.DodgeSpeed;
            maxWarpDistance = CombatConstants.MaxWarpDistance;
            warpMinContactDistance = CombatConstants.WarpMinContactDistance;
            telegraphMinDuration = CombatConstants.TelegraphMinDuration;
            telegraphMaxDuration = CombatConstants.TelegraphMaxDuration;
            maxSimultaneousAttackers = CombatConstants.MaxSimultaneousAttackers;
            breathingTime = CombatConstants.BreathingTime;
            executionHPThreshold = CombatConstants.ExecutionHPThreshold;
            executionHPThresholdHighCombo = CombatConstants.ExecutionHPThresholdHighCombo;
            executionRange = CombatConstants.ExecutionRange;
            huxleyBaseChargePerHit = CombatConstants.HuxleyBaseChargePerHit;
            huxleyMaxCharge = CombatConstants.HuxleyMaxCharge;
            hitFlashDuration = CombatConstants.HitFlashDuration;
            hitFlashIntensity = CombatConstants.HitFlashIntensity;
            enemyDeathDelay = CombatConstants.EnemyDeathDelay;
            enemyDeathFadeDuration = CombatConstants.EnemyDeathFadeDuration;
            // Flinch
            flinchLightPush = CombatConstants.FlinchLightPush;
            flinchLightFreeze = CombatConstants.FlinchLightFreeze;
            flinchLightHitStop = CombatConstants.FlinchLightHitStop;
            flinchMediumPush = CombatConstants.FlinchMediumPush;
            flinchMediumFreeze = CombatConstants.FlinchMediumFreeze;
            flinchMediumHitStop = CombatConstants.FlinchMediumHitStop;
            flinchHeavyPush = CombatConstants.FlinchHeavyPush;
            flinchHeavyFreeze = CombatConstants.FlinchHeavyFreeze;
            flinchHeavyHitStop = CombatConstants.FlinchHeavyHitStop;
            // Knockdown
            knockdownLightHeight = CombatConstants.KnockdownLightHeight;
            knockdownLightAirTime = CombatConstants.KnockdownLightAirTime;
            knockdownLightDistance = CombatConstants.KnockdownLightDistance;
            knockdownMediumHeight = CombatConstants.KnockdownMediumHeight;
            knockdownMediumAirTime = CombatConstants.KnockdownMediumAirTime;
            knockdownMediumDistance = CombatConstants.KnockdownMediumDistance;
            knockdownHeavyHeight = CombatConstants.KnockdownHeavyHeight;
            knockdownHeavyAirTime = CombatConstants.KnockdownHeavyAirTime;
            knockdownHeavyDistance = CombatConstants.KnockdownHeavyDistance;
            knockdownLightDownTime = CombatConstants.KnockdownLightDownTime;
            knockdownMediumDownTime = CombatConstants.KnockdownMediumDownTime;
            knockdownHeavyDownTime = CombatConstants.KnockdownHeavyDownTime;
            flinchClipPath = CombatConstants.FlinchClipPath;
            knockdownClipPath = CombatConstants.KnockdownClipPath;
            // 그룹 AI 토큰 게이지
            tokenHolderGaugeMax = CombatConstants.TokenHolderGaugeMax;
            tokenHolderGaugeFillPerHit = CombatConstants.TokenHolderGaugeFillPerHit;
            tokenHolderGaugeDecayDelay = CombatConstants.TokenHolderGaugeDecayDelay;
            tokenHolderGaugeDecayPerSecond = CombatConstants.TokenHolderGaugeDecayPerSecond;
            backsideDistanceDiscount = CombatConstants.BacksideDistanceDiscount;
            tokenTransferMinInterval = CombatConstants.TokenTransferMinInterval;
        }

        // ════════════════════════════════════════════
        //  런타임 접근 헬퍼 (BattleSettings → CombatConstants 폴백)
        // ════════════════════════════════════════════

        /// <summary>콤보 윈도우 시간. SO 없으면 CombatConstants 기본값.</summary>
        public static float GetComboWindowDuration()
            => IsLoaded ? _instance.comboWindowDuration : CombatConstants.ComboWindowDuration;

        /// <summary>인풋 버퍼 시간.</summary>
        public static float GetInputBufferDuration()
            => IsLoaded ? _instance.inputBufferDuration : CombatConstants.InputBufferDuration;

        /// <summary>회피 무적 프레임 수.</summary>
        public static int GetDodgeIFrames()
            => IsLoaded ? _instance.dodgeIFrames : CombatConstants.DodgeIFrames;

        /// <summary>회피 속도.</summary>
        public static float GetDodgeSpeed()
            => IsLoaded ? _instance.dodgeSpeed : CombatConstants.DodgeSpeed;

        /// <summary>최대 동시 공격자 수.</summary>
        public static int GetMaxSimultaneousAttackers()
            => IsLoaded ? _instance.maxSimultaneousAttackers : CombatConstants.MaxSimultaneousAttackers;

        /// <summary>호흡 시간.</summary>
        public static float GetBreathingTime()
            => IsLoaded ? _instance.breathingTime : CombatConstants.BreathingTime;

        // ─── 그룹 AI 토큰 게이지 접근자 ───

        /// <summary>토큰 히트 게이지 최대치 (기본 1000).</summary>
        public static float GetTokenHolderGaugeMax()
            => IsLoaded ? _instance.tokenHolderGaugeMax : CombatConstants.TokenHolderGaugeMax;

        /// <summary>히트 1회당 게이지 충전량 (기본 250).</summary>
        public static float GetTokenHolderGaugeFillPerHit()
            => IsLoaded ? _instance.tokenHolderGaugeFillPerHit : CombatConstants.TokenHolderGaugeFillPerHit;

        /// <summary>게이지 감쇠 시작 대기 시간 (초).</summary>
        public static float GetTokenHolderGaugeDecayDelay()
            => IsLoaded ? _instance.tokenHolderGaugeDecayDelay : CombatConstants.TokenHolderGaugeDecayDelay;

        /// <summary>게이지 초당 감쇠량.</summary>
        public static float GetTokenHolderGaugeDecayPerSecond()
            => IsLoaded ? _instance.tokenHolderGaugeDecayPerSecond : CombatConstants.TokenHolderGaugeDecayPerSecond;

        /// <summary>뒷쪽 적 거리 할인율.</summary>
        public static float GetBacksideDistanceDiscount()
            => IsLoaded ? _instance.backsideDistanceDiscount : CombatConstants.BacksideDistanceDiscount;

        /// <summary>토큰 연속 이전 방지 간격.</summary>
        public static float GetTokenTransferMinInterval()
            => IsLoaded ? _instance.tokenTransferMinInterval : CombatConstants.TokenTransferMinInterval;

        /// <summary>처형 HP 임계치.</summary>
        public static float GetExecutionHPThreshold()
            => IsLoaded ? _instance.executionHPThreshold : CombatConstants.ExecutionHPThreshold;

        /// <summary>고콤보 처형 HP 임계치.</summary>
        public static float GetExecutionHPThresholdHighCombo()
            => IsLoaded ? _instance.executionHPThresholdHighCombo : CombatConstants.ExecutionHPThresholdHighCombo;

        /// <summary>처형 거리.</summary>
        public static float GetExecutionRange()
            => IsLoaded ? _instance.executionRange : CombatConstants.ExecutionRange;

        /// <summary>헉슬리 히트당 충전량.</summary>
        public static float GetHuxleyBaseChargePerHit()
            => IsLoaded ? _instance.huxleyBaseChargePerHit : CombatConstants.HuxleyBaseChargePerHit;

        /// <summary>헉슬리 최대 충전량.</summary>
        public static float GetHuxleyMaxCharge()
            => IsLoaded ? _instance.huxleyMaxCharge : CombatConstants.HuxleyMaxCharge;

        /// <summary>텔레그래프 최소 시간.</summary>
        public static float GetTelegraphMinDuration()
            => IsLoaded ? _instance.telegraphMinDuration : CombatConstants.TelegraphMinDuration;

        /// <summary>텔레그래프 최대 시간.</summary>
        public static float GetTelegraphMaxDuration()
            => IsLoaded ? _instance.telegraphMaxDuration : CombatConstants.TelegraphMaxDuration;

        /// <summary>최대 워프 거리.</summary>
        public static float GetMaxWarpDistance()
            => IsLoaded ? _instance.maxWarpDistance : CombatConstants.MaxWarpDistance;

        /// <summary>워핑 후 적과의 최소 접근 거리.</summary>
        public static float GetWarpMinContactDistance()
            => IsLoaded ? _instance.warpMinContactDistance : CombatConstants.WarpMinContactDistance;

        /// <summary>콤보 임계치 Good.</summary>
        public static int GetComboThresholdGood()
            => IsLoaded ? _instance.comboThresholdGood : CombatConstants.ComboThresholdGood;

        /// <summary>콤보 임계치 Great.</summary>
        public static int GetComboThresholdGreat()
            => IsLoaded ? _instance.comboThresholdGreat : CombatConstants.ComboThresholdGreat;

        /// <summary>콤보 임계치 Awesome.</summary>
        public static int GetComboThresholdAwesome()
            => IsLoaded ? _instance.comboThresholdAwesome : CombatConstants.ComboThresholdAwesome;

        /// <summary>콤보 임계치 Unstoppable.</summary>
        public static int GetComboThresholdUnstoppable()
            => IsLoaded ? _instance.comboThresholdUnstoppable : CombatConstants.ComboThresholdUnstoppable;

        /// <summary>히트 플래시 지속 시간.</summary>
        public static float GetHitFlashDuration()
            => IsLoaded ? _instance.hitFlashDuration : CombatConstants.HitFlashDuration;

        /// <summary>히트 플래시 강도.</summary>
        public static float GetHitFlashIntensity()
            => IsLoaded ? _instance.hitFlashIntensity : CombatConstants.HitFlashIntensity;

        /// <summary>적 사망 후 페이드 대기 시간.</summary>
        public static float GetEnemyDeathDelay()
            => IsLoaded ? _instance.enemyDeathDelay : CombatConstants.EnemyDeathDelay;

        /// <summary>적 사망 페이드아웃 시간.</summary>
        public static float GetEnemyDeathFadeDuration()
            => IsLoaded ? _instance.enemyDeathFadeDuration : CombatConstants.EnemyDeathFadeDuration;

        // ─── 피격 리액션 프리셋 접근자 ───

        /// <summary>Flinch 프리셋 기본값 로드. Offset은 포함하지 않음.</summary>
        public static FlinchData GetFlinchPreset(HitPreset preset)
        {
            if (!IsLoaded)
            {
                return preset switch
                {
                    HitPreset.Light  => new FlinchData(CombatConstants.FlinchLightPush,  CombatConstants.FlinchLightFreeze,  CombatConstants.FlinchLightHitStop),
                    HitPreset.Medium => new FlinchData(CombatConstants.FlinchMediumPush, CombatConstants.FlinchMediumFreeze, CombatConstants.FlinchMediumHitStop),
                    HitPreset.Heavy  => new FlinchData(CombatConstants.FlinchHeavyPush,  CombatConstants.FlinchHeavyFreeze,  CombatConstants.FlinchHeavyHitStop),
                    _ => new FlinchData(CombatConstants.FlinchMediumPush, CombatConstants.FlinchMediumFreeze, CombatConstants.FlinchMediumHitStop)
                };
            }
            var i = _instance;
            return preset switch
            {
                HitPreset.Light  => new FlinchData(i.flinchLightPush,  i.flinchLightFreeze,  i.flinchLightHitStop),
                HitPreset.Medium => new FlinchData(i.flinchMediumPush, i.flinchMediumFreeze, i.flinchMediumHitStop),
                HitPreset.Heavy  => new FlinchData(i.flinchHeavyPush,  i.flinchHeavyFreeze,  i.flinchHeavyHitStop),
                _ => new FlinchData(i.flinchMediumPush, i.flinchMediumFreeze, i.flinchMediumHitStop)
            };
        }

        /// <summary>Knockdown 프리셋 기본값 로드. Offset은 포함하지 않음.</summary>
        public static KnockdownData GetKnockdownPreset(HitPreset preset)
        {
            if (!IsLoaded)
            {
                return preset switch
                {
                    HitPreset.Light  => new KnockdownData(CombatConstants.KnockdownLightHeight,  CombatConstants.KnockdownLightAirTime,  CombatConstants.KnockdownLightDistance,  CombatConstants.KnockdownLightDownTime),
                    HitPreset.Medium => new KnockdownData(CombatConstants.KnockdownMediumHeight, CombatConstants.KnockdownMediumAirTime, CombatConstants.KnockdownMediumDistance, CombatConstants.KnockdownMediumDownTime),
                    HitPreset.Heavy  => new KnockdownData(CombatConstants.KnockdownHeavyHeight,  CombatConstants.KnockdownHeavyAirTime,  CombatConstants.KnockdownHeavyDistance,  CombatConstants.KnockdownHeavyDownTime),
                    _ => new KnockdownData(CombatConstants.KnockdownMediumHeight, CombatConstants.KnockdownMediumAirTime, CombatConstants.KnockdownMediumDistance, CombatConstants.KnockdownMediumDownTime)
                };
            }
            var i = _instance;
            return preset switch
            {
                HitPreset.Light  => new KnockdownData(i.knockdownLightHeight,  i.knockdownLightAirTime,  i.knockdownLightDistance,  i.knockdownLightDownTime),
                HitPreset.Medium => new KnockdownData(i.knockdownMediumHeight, i.knockdownMediumAirTime, i.knockdownMediumDistance, i.knockdownMediumDownTime),
                HitPreset.Heavy  => new KnockdownData(i.knockdownHeavyHeight,  i.knockdownHeavyAirTime,  i.knockdownHeavyDistance, i.knockdownHeavyDownTime),
                _ => new KnockdownData(i.knockdownMediumHeight, i.knockdownMediumAirTime, i.knockdownMediumDistance, i.knockdownMediumDownTime)
            };
        }

        /// <summary>Flinch 피격 모션 클립 경로.</summary>
        public static string GetFlinchClipPath()
            => IsLoaded ? _instance.flinchClipPath : CombatConstants.FlinchClipPath;

        /// <summary>Knockdown 피격 모션 클립 경로.</summary>
        public static string GetKnockdownClipPath()
            => IsLoaded ? _instance.knockdownClipPath : CombatConstants.KnockdownClipPath;

        // ════════════════════════════════════════════
        //  ★ 그로기 프리셋
        // ════════════════════════════════════════════

        [Header("★ 그로기")]
        [Tooltip("약한 그로기 지속 시간 (초)")]
        [Range(0.3f, 3f)]
        public float groggySoftDuration = CombatConstants.GroggySoftDuration;

        [Tooltip("강한 그로기 지속 시간 (초)")]
        [Range(1f, 8f)]
        public float groggyHardDuration = CombatConstants.GroggyHardDuration;

        /// <summary>그로기 지속 시간 (타입별)</summary>
        public static float GetGroggyDuration(GroggyType type)
        {
            if (!IsLoaded)
                return type == GroggyType.Hard ? CombatConstants.GroggyHardDuration : CombatConstants.GroggySoftDuration;
            return type == GroggyType.Hard ? _instance.groggyHardDuration : _instance.groggySoftDuration;
        }

        // ════════════════════════════════════════════
        //  ★ 가드 카운터 모션 (가중 랜덤)
        // ════════════════════════════════════════════

        [Header("★ 가드 카운터 모션")]
        [Tooltip("가드 성공(퍼펙트 가드) 시 발동할 카운터 액션 목록. weight로 확률 조절.")]
        public WeightedMotionEntry[] guardCounterMotions = new[]
        {
            new WeightedMotionEntry { actionId = "GuardCounter", weight = 1f }
        };

        // ════════════════════════════════════════════
        //  ★ 처형 모션 (가중 랜덤)
        // ════════════════════════════════════════════

        [Header("★ 처형 모션")]
        [Tooltip("처형 시 발동할 모션 목록. weight로 확률 조절.")]
        public WeightedMotionEntry[] executionMotions = new[]
        {
            new WeightedMotionEntry { actionId = "Execution1", weight = 1f },
            new WeightedMotionEntry { actionId = "Execution2", weight = 1f },
            new WeightedMotionEntry { actionId = "Execution3", weight = 1f }
        };

        // ════════════════════════════════════════════
        //  가중 랜덤 선택 헬퍼
        // ════════════════════════════════════════════

        /// <summary>가중 랜덤으로 actionId 선택. 빈 배열이면 null 반환.</summary>
        public static string SelectWeightedRandom(WeightedMotionEntry[] entries)
        {
            if (entries == null || entries.Length == 0) return null;
            if (entries.Length == 1) return entries[0].actionId;

            float totalWeight = 0f;
            foreach (var e in entries)
                totalWeight += e.weight;

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var e in entries)
            {
                cumulative += e.weight;
                if (roll <= cumulative) return e.actionId;
            }
            return entries[entries.Length - 1].actionId;
        }
    }
}
