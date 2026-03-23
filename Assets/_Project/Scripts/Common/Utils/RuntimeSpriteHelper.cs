using UnityEngine;

namespace FreeFlowHero.Common
{
    /// <summary>
    /// SpriteRenderer에 Sprite가 없을 때 런타임에 자동 생성하는 헬퍼.
    /// 에디터 스크립트 실행 없이도 최소한의 시각화를 보장한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class RuntimeSpriteHelper : MonoBehaviour
    {
        [SerializeField] private Vector2 size = new Vector2(1f, 2f);

        private static Texture2D sharedTexture;

        private void Awake()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr.sprite == null)
            {
                sr.sprite = CreateBoxSprite();
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = size;
            }
        }

        /// <summary>4x4 흰색 박스 스프라이트를 생성 (공유 텍스처)</summary>
        public static Sprite CreateBoxSprite()
        {
            if (sharedTexture == null)
            {
                sharedTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                sharedTexture.filterMode = FilterMode.Point;
                Color[] pixels = new Color[16];
                for (int i = 0; i < 16; i++) pixels[i] = Color.white;
                sharedTexture.SetPixels(pixels);
                sharedTexture.Apply();
            }

            return Sprite.Create(
                sharedTexture,
                new Rect(0, 0, 4, 4),
                new Vector2(0.5f, 0f), // 피벗: 하단 중앙
                4f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(1, 1, 1, 1) // 9-slice 보더
            );
        }
    }
}
