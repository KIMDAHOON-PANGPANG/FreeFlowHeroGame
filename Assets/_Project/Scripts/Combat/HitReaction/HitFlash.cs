using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 시 머티리얼 플래시 효과.
    /// 적/플레이어 공용 — SpriteRenderer가 있는 오브젝트에 부착.
    /// MaterialPropertyBlock으로 _FlashAmount를 제어하여 sharedMaterial 오염 방지.
    ///
    /// 사용법:
    ///   hitFlash.Play();   // 플래시 시작 (재호출 시 타이머 리셋)
    ///   hitFlash.Stop();   // 즉시 중단
    ///
    /// ★ SpriteRenderer의 Material이 "REPLACED/Sprite-Flash" 셰이더를 사용해야 효과가 보인다.
    ///   기본 Sprite-Unlit-Default에서는 에러 없이 무시됨.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        // ★ 데이터 튜닝: 개별 오버라이드 (0이면 BattleSettings 기본값 사용)
        [Header("오버라이드 (0 = BattleSettings 기본값)")]
        [Tooltip("플래시 지속 시간 오버라이드. 0이면 BattleSettings 값 사용")]
        [SerializeField] private float durationOverride = 0f;

        [Tooltip("플래시 강도 오버라이드. 0이면 BattleSettings 값 사용")]
        [SerializeField] private float intensityOverride = 0f;

        [Tooltip("플래시 색상 (기본: 흰색)")]
        [SerializeField] private Color flashColor = Color.white;

        // ─── 런타임 ───
        private SpriteRenderer spriteRenderer;
        private MaterialPropertyBlock mpb;
        private float flashTimer;
        private float currentDuration;
        private float currentIntensity;

        // 셰이더 프로퍼티 ID 캐시
        private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

        /// <summary>현재 플래시 중인지</summary>
        public bool IsFlashing => flashTimer > 0f;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            mpb = new MaterialPropertyBlock();
        }

        /// <summary>플래시 시작. 이미 플래시 중이면 타이머 리셋.</summary>
        public void Play()
        {
            if (spriteRenderer == null) return;

            currentDuration = durationOverride > 0f
                ? durationOverride
                : BattleSettings.GetHitFlashDuration();

            currentIntensity = intensityOverride > 0f
                ? intensityOverride
                : BattleSettings.GetHitFlashIntensity();

            flashTimer = currentDuration;

            // 색상 설정 (한 번만)
            spriteRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(FlashColorID, flashColor);
            mpb.SetFloat(FlashAmountID, currentIntensity);
            spriteRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>플래시를 지정 색상으로 시작</summary>
        public void Play(Color color)
        {
            flashColor = color;
            Play();
        }

        /// <summary>즉시 중단</summary>
        public void Stop()
        {
            flashTimer = 0f;
            ApplyFlash(0f);
        }

        private void Update()
        {
            if (flashTimer <= 0f) return;

            flashTimer -= Time.deltaTime;

            if (flashTimer <= 0f)
            {
                flashTimer = 0f;
                ApplyFlash(0f);
            }
            else
            {
                // 선형 페이드아웃: 시간이 지날수록 플래시가 약해짐
                float t = flashTimer / currentDuration;
                ApplyFlash(t * currentIntensity);
            }
        }

        private void ApplyFlash(float amount)
        {
            if (spriteRenderer == null) return;

            spriteRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(FlashAmountID, amount);
            spriteRenderer.SetPropertyBlock(mpb);
        }
    }
}
