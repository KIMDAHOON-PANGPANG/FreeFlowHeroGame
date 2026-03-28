using UnityEngine;

namespace FreeFlowHero.Combat.Core
{
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
        /// 런타임/에디터에서 BattleSettings에 접근한다.
        /// Resources 폴더가 아닌 직접 참조 방식 — CombatDirector 등에서 할당.
        /// 할당되지 않으면 CombatConstants 기본값을 사용한다.
        /// </summary>
        public static BattleSettings Instance
        {
            get => _instance;
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

        [Header("회피 (Dodge)")]

        [Tooltip("회피 시 무적 프레임 수 (60fps 기준)")]
        [Range(1, 30)]
        public int dodgeIFrames = CombatConstants.DodgeIFrames;

        [Tooltip("회피 이동 속도 (유닛/초)")]
        public float dodgeSpeed = CombatConstants.DodgeSpeed;

        // ════════════════════════════════════════════
        //  카운터 (Counter)
        // ════════════════════════════════════════════

        [Header("카운터 (Counter)")]

        [Tooltip("퍼펙트 카운터 판정 윈도우 (±프레임). 작을수록 엄격")]
        [Range(1, 10)]
        public int perfectCounterWindow = CombatConstants.PerfectCounterWindow;

        [Tooltip("일반 카운터 판정 윈도우 (±프레임)")]
        [Range(1, 20)]
        public int normalCounterWindow = CombatConstants.NormalCounterWindow;

        // ════════════════════════════════════════════
        //  워핑 (Warp)
        // ════════════════════════════════════════════

        [Header("워핑 (Warp)")]

        [Tooltip("워핑 시간 계산의 기준이 되는 최대 거리 (유닛)")]
        public float maxWarpDistance = CombatConstants.MaxWarpDistance;

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
            perfectCounterWindow = CombatConstants.PerfectCounterWindow;
            normalCounterWindow = CombatConstants.NormalCounterWindow;
            maxWarpDistance = CombatConstants.MaxWarpDistance;
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

        /// <summary>퍼펙트 카운터 윈도우.</summary>
        public static int GetPerfectCounterWindow()
            => IsLoaded ? _instance.perfectCounterWindow : CombatConstants.PerfectCounterWindow;

        /// <summary>일반 카운터 윈도우.</summary>
        public static int GetNormalCounterWindow()
            => IsLoaded ? _instance.normalCounterWindow : CombatConstants.NormalCounterWindow;

        /// <summary>최대 동시 공격자 수.</summary>
        public static int GetMaxSimultaneousAttackers()
            => IsLoaded ? _instance.maxSimultaneousAttackers : CombatConstants.MaxSimultaneousAttackers;

        /// <summary>호흡 시간.</summary>
        public static float GetBreathingTime()
            => IsLoaded ? _instance.breathingTime : CombatConstants.BreathingTime;

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
    }
}
