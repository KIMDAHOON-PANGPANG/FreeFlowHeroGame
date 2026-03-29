namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 전투 시스템 전역 상수.
    /// 프레임 데이터, 타이밍, 거리 등 모든 하드코딩 수치를 이곳에 모은다.
    /// ScriptableObject로 외부화되기 전의 기본값 역할.
    /// </summary>
    public static class CombatConstants
    {
        // ─── 프레임 ───
        public const int TargetFPS = 60;
        public const float FrameDuration = 1f / TargetFPS; // 0.0167초

        // ─── 콤보 ───
        public const float ComboWindowDuration = 0.8f;     // 마지막 히트부터 다음 입력까지
        public const int MaxComboCount = 999;

        // ─── 인풋 버퍼 ───
        public const float InputBufferDuration = 0.15f;    // 선입력 유효 시간

        // ─── 회피 ───
        public const int DodgeIFrames = 12;                // 무적 프레임 수
        public const float DodgeIFrameDuration = DodgeIFrames * FrameDuration; // 0.2초
        public const float DodgeSpeed = 15f;               // 유닛/초

        // ─── 워핑 ───
        // WarpDuration, WarpArrivalOffset → WARP 노티파이 파라미터로 이전됨
        public const float MaxWarpDistance = 20f;           // 거리 비례 워핑 시간 계산 기준

        // ─── 텔레그래프 ───
        public const float TelegraphMinDuration = 0.3f;
        public const float TelegraphMaxDuration = 0.5f;

        // ─── 공격 턴 관리 ───
        public const int MaxSimultaneousAttackers = 2;
        public const float BreathingTime = 0.5f;           // 연속 공격 사이 최소 간격

        // ─── 처형 ───
        public const float ExecutionHPThreshold = 0.2f;    // HP 20% 이하
        public const float ExecutionHPThresholdHighCombo = 0.3f; // 콤보 x50 시 30%
        public const float ExecutionRange = 2.0f;           // 처형 가능 거리

        // ─── 헉슬리 건 ───
        public const float HuxleyBaseChargePerHit = 5f;    // 히트당 5%
        public const float HuxleyMaxCharge = 100f;

        // ─── 콤보 보너스 임계치 ───
        public const int ComboThresholdGood = 5;
        public const int ComboThresholdGreat = 10;
        public const int ComboThresholdAwesome = 20;
        public const int ComboThresholdUnstoppable = 50;

        // ─── 적 사망 연출 ───
        public const float EnemyDeathDelay = 0.5f;       // 사망 후 페이드 시작까지 대기
        public const float EnemyDeathFadeDuration = 0.8f; // 페이드아웃 지속 시간

        // ─── 히트 플래시 ───
        public const float HitFlashDuration = 0.15f;     // 플래시 지속 시간
        public const float HitFlashIntensity = 1.0f;     // 플래시 강도 (0~1)

        // ─── 피격 리액션: 모션 클립 경로 ───
        public const string FlinchClipPath = "Assets/Martial Art Animations Sample/Animations/Hit_A.fbx";
        public const string KnockdownClipPath = "Assets/Martial Art Animations Sample/Animations/Knock_A.fbx";

        // ─── 피격 리액션: Flinch 프리셋 (cm / 초 / 프레임) ───
        public const float FlinchLightPush = 30f;
        public const float FlinchLightFreeze = 0.08f;
        public const float FlinchLightHitStop = 1.5f;

        public const float FlinchMediumPush = 60f;
        public const float FlinchMediumFreeze = 0.12f;
        public const float FlinchMediumHitStop = 2.5f;

        public const float FlinchHeavyPush = 100f;
        public const float FlinchHeavyFreeze = 0.18f;
        public const float FlinchHeavyHitStop = 4.0f;

        // ─── 피격 리액션: Knockdown 프리셋 (cm / 초 / cm) ───
        public const float KnockdownLightHeight = 80f;
        public const float KnockdownLightAirTime = 0.4f;
        public const float KnockdownLightDistance = 100f;

        public const float KnockdownMediumHeight = 150f;
        public const float KnockdownMediumAirTime = 0.6f;
        public const float KnockdownMediumDistance = 180f;

        public const float KnockdownHeavyHeight = 250f;
        public const float KnockdownHeavyAirTime = 0.85f;
        public const float KnockdownHeavyDistance = 300f;

        // ─── 피격 리액션: Knockdown Down 프리셋 (착지 후 누워있는 시간, 초) ───
        public const float KnockdownLightDownTime = 0.5f;
        public const float KnockdownMediumDownTime = 1.0f;
        public const float KnockdownHeavyDownTime = 1.5f;
    }
}
