using UnityEngine;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 적 텔레그래프 시 시각 효과 제어.
    /// 3D 모델 + 2D 스프라이트 모두 지원.
    /// - Sprite-Outline 셰이더: MaterialPropertyBlock으로 _OutlineEnabled 제어
    /// - 일반 셰이더(3D): _Color/_BaseColor 틴트로 폴백
    /// </summary>
    public class TelegraphOutline : MonoBehaviour
    {
        // ─── 셰이더 프로퍼티 ID 캐시 ───
        private static readonly int PropOutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
        private static readonly int PropOutlineColor = Shader.PropertyToID("_OutlineColor");
        private static readonly int PropOutlineWidth = Shader.PropertyToID("_OutlineWidth");
        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");

        // ─── 내부 상태 ───
        private Renderer[] renderers;
        private MaterialPropertyBlock mpb;
        private bool initialized;
        private bool outlineActive;
        private Color outlineColor;
        private float baseWidth;
        private Color[] originalColors; // 3D 렌더러 원본 색상 백업

        /// <summary>아웃라인 활성 상태</summary>
        public bool IsOutlineActive => outlineActive;

        /// <summary>렌더러 초기화 (최초 호출 시 1회)</summary>
        private void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            renderers = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();

            // 원본 색상 백업
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                var mat = renderers[i].sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty(PropBaseColor))
                        originalColors[i] = mat.color;
                    else if (mat.HasProperty(PropColor))
                        originalColors[i] = mat.color;
                    else
                        originalColors[i] = Color.white;
                }
            }
        }

        /// <summary>아웃라인 활성화</summary>
        public void EnableOutline(Color color, float width = 2f)
        {
            EnsureInit();
            outlineActive = true;
            outlineColor = color;
            baseWidth = width;
            ApplyEffect(true, color, width);
        }

        /// <summary>아웃라인 비활성화</summary>
        public void DisableOutline()
        {
            if (!outlineActive) return;
            outlineActive = false;
            ApplyEffect(false, Color.clear, 0f);
        }

        private void Update()
        {
            if (!outlineActive) return;

            // 미세 펄스 효과
            float pulse = baseWidth * (1f + Mathf.Sin(Time.time * 6f) * 0.2f);
            ApplyEffect(true, outlineColor, pulse);
        }

        private void ApplyEffect(bool enabled, Color color, float width)
        {
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var mat = r.sharedMaterial;
                if (mat == null) continue;

                // Sprite-Outline 셰이더: MPB로 아웃라인 프로퍼티 제어
                if (mat.HasProperty(PropOutlineEnabled))
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(PropOutlineEnabled, enabled ? 1f : 0f);
                    mpb.SetColor(PropOutlineColor, color);
                    mpb.SetFloat(PropOutlineWidth, width);
                    r.SetPropertyBlock(mpb);
                }
                else
                {
                    // 3D 모델 폴백: MPB로 색상 틴트 (아웃라인 대신 색상 변경)
                    r.GetPropertyBlock(mpb);
                    if (enabled)
                    {
                        // 원본 색상에 아웃라인 색상을 블렌딩
                        Color tint = Color.Lerp(originalColors[i], color, 0.5f);
                        tint.a = originalColors[i].a;
                        if (mat.HasProperty(PropBaseColor))
                            mpb.SetColor(PropBaseColor, tint);
                        else if (mat.HasProperty(PropColor))
                            mpb.SetColor(PropColor, tint);
                    }
                    else
                    {
                        // 원본 색상 복원
                        if (mat.HasProperty(PropBaseColor))
                            mpb.SetColor(PropBaseColor, originalColors[i]);
                        else if (mat.HasProperty(PropColor))
                            mpb.SetColor(PropColor, originalColors[i]);
                    }
                    r.SetPropertyBlock(mpb);
                }
            }
        }
    }
}
