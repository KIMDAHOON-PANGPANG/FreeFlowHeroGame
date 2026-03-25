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

        // ─── 카운터 ───
        public const int PerfectCounterWindow = 3;         // ±3f
        public const int NormalCounterWindow = 8;           // ±8f

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
    }
}
