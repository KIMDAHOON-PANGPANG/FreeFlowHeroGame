using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 시 머티리얼 플래시 효과.
    /// 적/플레이어 공용 — 모든 Renderer 타입 지원.
    ///
    /// ★ 동작 방식:
    ///   1순위 — _FlashAmount 프로퍼티 있는 셰이더 (Sprite-Flash): MPB 방식으로 부드러운 페이드
    ///   2순위 — 그 외 셰이더 (UnityToon 등 3D): 흰색 Unlit 머티리얼로 순간 교체 → 원본 복원
    ///
    /// 사용법:
    ///   hitFlash.Play();   // 플래시 시작 (재호출 시 타이머 리셋)
    ///   hitFlash.Stop();   // 즉시 중단
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
        private Renderer[] targetRenderers;
        private MaterialPropertyBlock mpb;
        private float flashTimer;
        private float currentDuration;
        private float currentIntensity;

        // ─── 모드 판별 ───
        private bool useMPBMode; // true: MPB(_FlashAmount), false: 머티리얼 스왑

        // ─── 3D 폴백: 머티리얼 스왑 방식 ───
        private Material[][] originalMaterials; // 렌더러별 원본 머티리얼 배열
        private Material flashMaterial;         // 공용 흰색 Unlit 머티리얼
        private bool isSwapped;                 // 현재 스왑 상태인지

        // 셰이더 프로퍼티 ID 캐시
        private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

        /// <summary>현재 플래시 중인지</summary>
        public bool IsFlashing => flashTimer > 0f;

        private void Awake()
        {
            targetRenderers = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();

            // 모드 결정: 첫 번째 렌더러의 셰이더로 판별
            useMPBMode = false;
            if (targetRenderers != null && targetRenderers.Length > 0)
            {
                var mat = targetRenderers[0].sharedMaterial;
                if (mat != null && mat.HasProperty(FlashAmountID))
                    useMPBMode = true;
            }

            if (!useMPBMode)
                InitSwapFallback();
        }

        /// <summary>3D 폴백: 원본 머티리얼 캐시 + 플래시 머티리얼 생성</summary>
        private void InitSwapFallback()
        {
            if (targetRenderers == null || targetRenderers.Length == 0) return;

            // 원본 머티리얼 저장
            originalMaterials = new Material[targetRenderers.Length][];
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                var shared = targetRenderers[i].sharedMaterials;
                originalMaterials[i] = new Material[shared.Length];
                System.Array.Copy(shared, originalMaterials[i], shared.Length);
            }

            // 흰색 Unlit 머티리얼 생성
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Unlit/Color");

            if (unlitShader != null)
            {
                flashMaterial = new Material(unlitShader);
                flashMaterial.color = flashColor;
                flashMaterial.name = "HitFlash_Runtime";
            }
        }

        /// <summary>플래시 시작. 이미 플래시 중이면 타이머 리셋.</summary>
        public void Play()
        {
            if (targetRenderers == null || targetRenderers.Length == 0) return;

            currentDuration = durationOverride > 0f
                ? durationOverride
                : BattleSettings.GetHitFlashDuration();

            currentIntensity = intensityOverride > 0f
                ? intensityOverride
                : BattleSettings.GetHitFlashIntensity();

            flashTimer = currentDuration;

            if (useMPBMode)
            {
                ApplyFlashMPB(currentIntensity);
            }
            else
            {
                // 머티리얼 스왑 → 흰색
                SwapToFlash();
            }
        }

        /// <summary>플래시를 지정 색상으로 시작</summary>
        public void Play(Color color)
        {
            flashColor = color;
            if (flashMaterial != null)
                flashMaterial.color = flashColor;
            Play();
        }

        /// <summary>즉시 중단</summary>
        public void Stop()
        {
            flashTimer = 0f;
            if (useMPBMode)
                ApplyFlashMPB(0f);
            else
                RestoreOriginal();
        }

        private void Update()
        {
            if (flashTimer <= 0f) return;

            flashTimer -= Time.deltaTime;

            if (useMPBMode)
            {
                // MPB 모드: 부드러운 페이드아웃
                if (flashTimer <= 0f)
                {
                    flashTimer = 0f;
                    ApplyFlashMPB(0f);
                }
                else
                {
                    float t = flashTimer / currentDuration;
                    ApplyFlashMPB(t * currentIntensity);
                }
            }
            else
            {
                // 스왑 모드: duration의 절반이 지나면 원본 복원 (짧은 번쩍임)
                float peakRatio = 0.4f; // 전체 시간의 40%까지 흰색 유지
                float elapsed = currentDuration - flashTimer;

                if (elapsed >= currentDuration * peakRatio && isSwapped)
                {
                    RestoreOriginal();
                }

                if (flashTimer <= 0f)
                {
                    flashTimer = 0f;
                    RestoreOriginal(); // 안전장치
                }
            }
        }

        // ═══════════════════════════════════════════
        //  1순위: MPB — _FlashAmount (Sprite-Flash 셰이더)
        // ═══════════════════════════════════════════

        private void ApplyFlashMPB(float amount)
        {
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                var rend = targetRenderers[i];
                if (rend == null) continue;

                rend.GetPropertyBlock(mpb);
                mpb.SetColor(FlashColorID, flashColor);
                mpb.SetFloat(FlashAmountID, amount);
                rend.SetPropertyBlock(mpb);
            }
        }

        // ═══════════════════════════════════════════
        //  2순위: 머티리얼 스왑 (3D 셰이더 — 흰색 Unlit)
        // ═══════════════════════════════════════════

        private void SwapToFlash()
        {
            if (flashMaterial == null || originalMaterials == null || isSwapped) return;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                var rend = targetRenderers[i];
                if (rend == null) continue;

                // 모든 서브 머티리얼을 flashMaterial로 교체
                var flashArray = new Material[originalMaterials[i].Length];
                for (int m = 0; m < flashArray.Length; m++)
                    flashArray[m] = flashMaterial;
                rend.materials = flashArray;
            }
            isSwapped = true;
        }

        private void RestoreOriginal()
        {
            if (originalMaterials == null || !isSwapped) return;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                var rend = targetRenderers[i];
                if (rend == null) continue;

                rend.sharedMaterials = originalMaterials[i];
            }
            isSwapped = false;
        }

        private void OnDestroy()
        {
            // 스왑 상태에서 파괴되면 복원
            RestoreOriginal();

            if (flashMaterial != null)
            {
                Destroy(flashMaterial);
                flashMaterial = null;
            }
        }
    }
}
