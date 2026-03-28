using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Combat.HitReaction
{
    /// <summary>
    /// 피격 시 머티리얼 플래시 효과.
    /// 적/플레이어 공용 — 모든 Renderer 타입 지원.
    ///
    /// ★ 렌더러별 개별 모드 판별:
    ///   - _FlashAmount 있는 셰이더 (Sprite-Flash) → MPB 방식
    ///   - 그 외 (UnityToon 등 3D) → 흰색 Unlit 머티리얼 스왑
    ///   같은 오브젝트에 SpriteRenderer + SkinnedMeshRenderer가 공존해도 동작.
    ///
    /// ★ 지연 초기화: 첫 Play() 호출 시 렌더러를 수집하므로
    ///   자식 3D 모델이 나중에 추가되어도 문제 없음.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        [Header("오버라이드 (0 = BattleSettings 기본값)")]
        [Tooltip("플래시 지속 시간 오버라이드. 0이면 BattleSettings 값 사용")]
        [SerializeField] private float durationOverride = 0f;

        [Tooltip("플래시 강도 오버라이드. 0이면 BattleSettings 값 사용")]
        [SerializeField] private float intensityOverride = 0f;

        [Tooltip("플래시 색상 (기본: 흰색)")]
        [SerializeField] private Color flashColor = Color.white;

        // ─── 렌더러별 데이터 ───
        private struct FlashTarget
        {
            public Renderer renderer;
            public bool useMPB;              // true: MPB(_FlashAmount), false: 머티리얼 스왑
            public Material[] origMaterials; // 스왑 모드용 원본 머티리얼
        }

        private FlashTarget[] targets;
        private MaterialPropertyBlock mpb;
        private Material flashMaterial;       // 공용 흰색 Unlit
        private bool isInitialized;
        private bool isSwapped;

        // ─── 타이머 ───
        private float flashTimer;
        private float currentDuration;
        private float currentIntensity;

        // 셰이더 프로퍼티 ID
        private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorID = Shader.PropertyToID("_FlashColor");

        public bool IsFlashing => flashTimer > 0f;

        // ═══════════════════════════════════════════
        //  지연 초기화 — 첫 Play() 호출 시 실행
        // ═══════════════════════════════════════════

        private void EnsureInit()
        {
            if (isInitialized) return;
            isInitialized = true;

            mpb = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            targets = new FlashTarget[renderers.Length];
            bool needSwapMat = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                var rend = renderers[i];
                var mat = rend.sharedMaterial;
                bool hasProp = mat != null && mat.HasProperty(FlashAmountID);

                targets[i] = new FlashTarget
                {
                    renderer = rend,
                    useMPB = hasProp,
                    origMaterials = null
                };

                if (!hasProp)
                {
                    // 스왑 모드: 원본 머티리얼 캐시
                    var shared = rend.sharedMaterials;
                    targets[i].origMaterials = new Material[shared.Length];
                    System.Array.Copy(shared, targets[i].origMaterials, shared.Length);
                    needSwapMat = true;
                }
            }

            // 스왑용 흰색 Unlit 머티리얼 생성 (필요할 때만)
            if (needSwapMat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    flashMaterial = new Material(shader);
                    flashMaterial.color = flashColor;
                    flashMaterial.name = "HitFlash_Runtime";
                }
            }
        }

        /// <summary>렌더러 목록 강제 갱신 (자식 구조 변경 후 호출)</summary>
        public void RefreshRenderers()
        {
            // 복원 후 재초기화
            RestoreSwapped();
            isInitialized = false;
        }

        // ═══════════════════════════════════════════
        //  공개 API
        // ═══════════════════════════════════════════

        public void Play()
        {
            EnsureInit();
            if (targets == null || targets.Length == 0) return;

            currentDuration = durationOverride > 0f
                ? durationOverride
                : BattleSettings.GetHitFlashDuration();
            currentIntensity = intensityOverride > 0f
                ? intensityOverride
                : BattleSettings.GetHitFlashIntensity();

            flashTimer = currentDuration;

            // 즉시 최대 플래시 적용
            ApplyAll(currentIntensity);
            SwapToFlash();
        }

        public void Play(Color color)
        {
            flashColor = color;
            if (flashMaterial != null)
                flashMaterial.color = flashColor;
            Play();
        }

        public void Stop()
        {
            flashTimer = 0f;
            ApplyAll(0f);
            RestoreSwapped();
        }

        // ═══════════════════════════════════════════
        //  Update
        // ═══════════════════════════════════════════

        private void Update()
        {
            if (flashTimer <= 0f) return;

            flashTimer -= Time.deltaTime;

            if (flashTimer <= 0f)
            {
                flashTimer = 0f;
                ApplyAll(0f);
                RestoreSwapped();
            }
            else
            {
                float t = flashTimer / currentDuration;
                float amount = t * currentIntensity;

                // MPB 렌더러: 부드러운 페이드
                ApplyAll(amount);

                // 스왑 렌더러: 40% 지점에서 원본 복원
                float elapsed = currentDuration - flashTimer;
                if (elapsed >= currentDuration * 0.4f && isSwapped)
                    RestoreSwapped();
            }
        }

        // ═══════════════════════════════════════════
        //  내부 구현
        // ═══════════════════════════════════════════

        /// <summary>MPB 방식 렌더러에만 _FlashAmount 적용</summary>
        private void ApplyAll(float amount)
        {
            if (targets == null) return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i].useMPB) continue;
                var rend = targets[i].renderer;
                if (rend == null) continue;

                rend.GetPropertyBlock(mpb);
                mpb.SetColor(FlashColorID, flashColor);
                mpb.SetFloat(FlashAmountID, amount);
                rend.SetPropertyBlock(mpb);
            }
        }

        /// <summary>스왑 방식 렌더러의 머티리얼을 흰색 Unlit으로 교체</summary>
        private void SwapToFlash()
        {
            if (targets == null || flashMaterial == null || isSwapped) return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].useMPB) continue; // MPB 렌더러는 스킵
                var rend = targets[i].renderer;
                if (rend == null) continue;

                var flashArray = new Material[targets[i].origMaterials.Length];
                for (int m = 0; m < flashArray.Length; m++)
                    flashArray[m] = flashMaterial;
                rend.materials = flashArray;
            }
            isSwapped = true;
        }

        /// <summary>스왑된 렌더러를 원본 머티리얼로 복원</summary>
        private void RestoreSwapped()
        {
            if (targets == null || !isSwapped) return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].useMPB) continue;
                var rend = targets[i].renderer;
                if (rend == null || targets[i].origMaterials == null) continue;

                rend.sharedMaterials = targets[i].origMaterials;
            }
            isSwapped = false;
        }

        private void OnDestroy()
        {
            RestoreSwapped();
            if (flashMaterial != null)
            {
                Destroy(flashMaterial);
                flashMaterial = null;
            }
        }
    }
}
