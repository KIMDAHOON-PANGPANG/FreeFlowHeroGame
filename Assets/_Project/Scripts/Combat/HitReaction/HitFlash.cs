using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 시 머티리얼 플래시 효과.
    /// 적/플레이어 공용 — 모든 Renderer 타입 지원 (SpriteRenderer, MeshRenderer, SkinnedMeshRenderer).
    ///
    /// ★ 동작 방식:
    ///   1순위 — _FlashAmount 프로퍼티가 있는 셰이더 (Sprite-Flash): MaterialPropertyBlock으로 제어
    ///   2순위 — _Color 프로퍼티가 있는 셰이더 (UnityToon 등 3D): 머티리얼 인스턴스의 _Color를 흰색으로 보간
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

        // ─── 3D 폴백: 머티리얼 인스턴스 _Color 보간 ───
        private bool useMPBMode;              // true=MPB(_FlashAmount), false=Color 보간
        private Material[] materialInstances;  // 인스턴스 머티리얼 (폴백 시에만 생성)
        private Color[] originalColors;        // 원래 _Color 값 저장

        // 셰이더 프로퍼티 ID 캐시
        private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");
        private static readonly int ColorID = Shader.PropertyToID("_Color");

        /// <summary>현재 플래시 중인지</summary>
        public bool IsFlashing => flashTimer > 0f;

        private void Awake()
        {
            targetRenderers = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();

            // 첫 번째 렌더러의 머티리얼로 모드 결정
            useMPBMode = false;
            if (targetRenderers != null && targetRenderers.Length > 0)
            {
                var mat = targetRenderers[0].sharedMaterial;
                if (mat != null && mat.HasProperty(FlashAmountID))
                {
                    // Sprite-Flash 셰이더 → MaterialPropertyBlock 방식
                    useMPBMode = true;
                }
                else
                {
                    // 3D 셰이더 → 머티리얼 인스턴스 _Color 보간 방식
                    InitColorFallback();
                }
            }
        }

        /// <summary>3D 폴백: 머티리얼 인스턴스 생성 + 원본 _Color 저장</summary>
        private void InitColorFallback()
        {
            // renderer.material 접근 시 자동으로 인스턴스 생성됨
            // 모든 렌더러의 모든 머티리얼에서 _Color 저장
            int totalMats = 0;
            for (int i = 0; i < targetRenderers.Length; i++)
                totalMats += targetRenderers[i].sharedMaterials.Length;

            materialInstances = new Material[totalMats];
            originalColors = new Color[totalMats];

            int idx = 0;
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                // .materials 접근으로 인스턴스 생성 (sharedMaterial 오염 방지)
                var mats = targetRenderers[i].materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    materialInstances[idx] = mats[m];
                    originalColors[idx] = mats[m].HasProperty(ColorID)
                        ? mats[m].GetColor(ColorID)
                        : Color.white;
                    idx++;
                }
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
            ApplyFlash(currentIntensity);
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
                float t = flashTimer / currentDuration;
                ApplyFlash(t * currentIntensity);
            }
        }

        private void ApplyFlash(float amount)
        {
            if (useMPBMode)
                ApplyFlashMPB(amount);
            else
                ApplyFlashColor(amount);
        }

        /// <summary>1순위: MaterialPropertyBlock — _FlashAmount 제어 (Sprite-Flash 셰이더)</summary>
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

        /// <summary>2순위: 머티리얼 인스턴스 _Color 보간 (UnityToon 등 3D 셰이더)</summary>
        private void ApplyFlashColor(float amount)
        {
            if (materialInstances == null) return;

            for (int i = 0; i < materialInstances.Length; i++)
            {
                var mat = materialInstances[i];
                if (mat == null || !mat.HasProperty(ColorID)) continue;

                Color blended = Color.Lerp(originalColors[i], flashColor, amount);
                mat.SetColor(ColorID, blended);
            }
        }

        private void OnDestroy()
        {
            // 머티리얼 인스턴스 정리 (메모리 누수 방지)
            if (materialInstances != null)
            {
                for (int i = 0; i < materialInstances.Length; i++)
                {
                    if (materialInstances[i] != null)
                        Destroy(materialInstances[i]);
                }
                materialInstances = null;
            }
        }
    }
}
