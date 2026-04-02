using System.Collections.Generic;
using UnityEngine;

namespace FreeFlowHero.Level
{
    /// <summary>
    /// 절차적 맵 생성기.
    /// 2D 횡스크롤 청크 기반 무한 스크롤 레벨을 생성한다.
    /// 카메라 우측 끝 기준으로 미리 청크를 생성하고, 지나간 청크는 재활용한다.
    /// </summary>
    public class ProceduralLevelGenerator : MonoBehaviour
    {
        // ★ 데이터 튜닝
        [Header("생성 설정")]
        [Tooltip("seed 값 (같은 seed = 같은 맵)")]
        [SerializeField] private int seed = 42;

        [Tooltip("카메라 우측 기준 미리 생성할 거리")]
        [SerializeField] private float generateAhead = 30f;

        [Tooltip("카메라 좌측 기준 삭제할 거리")]
        [SerializeField] private float destroyBehind = 20f;

        [Header("청크 설정")]
        [Tooltip("청크 최소 폭")]
        [SerializeField] private float chunkMinWidth = 10f;

        [Tooltip("청크 최대 폭")]
        [SerializeField] private float chunkMaxWidth = 20f;

        [Header("플랫폼 설정")]
        [Tooltip("플랫폼 최소 높이")]
        [SerializeField] private float platformMinY = 1f;

        [Tooltip("플랫폼 최대 높이")]
        [SerializeField] private float platformMaxY = 6f;

        [Tooltip("갭 최소 폭")]
        [SerializeField] private float gapMinWidth = 2f;

        [Tooltip("갭 최대 폭")]
        [SerializeField] private float gapMaxWidth = 5f;

        [Header("벽 설정")]
        [Tooltip("벽타기 구간 벽 높이")]
        [SerializeField] private float wallHeight = 10f;

        [Tooltip("벽 간 간격")]
        [SerializeField] private float wallGap = 3f;

        [Header("난이도 곡선")]
        [Tooltip("청크 수에 따른 난이도 증가율")]
        [SerializeField] private float difficultyRamp = 0.05f;

        // ─── 상태 ───
        private float nextChunkX;           // 다음 청크 시작 X
        private int chunkCount;             // 생성된 청크 수
        private System.Random rng;          // seed 기반 랜덤
        private Transform playerTransform;
        private Camera mainCamera;

        // ─── 생성된 청크 관리 ───
        private readonly List<GeneratedChunk> activeChunks = new List<GeneratedChunk>();

        private void Awake()
        {
            rng = new System.Random(seed);
        }

        private void Start()
        {
            mainCamera = Camera.main;

            // 플레이어 찾기
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;

            // 시작 지점부터 생성
            nextChunkX = -5f;
            GenerateInitialChunks();
        }

        private void Update()
        {
            if (playerTransform == null) return;

            float playerX = playerTransform.position.x;

            // 우측으로 충분히 미리 생성
            while (nextChunkX < playerX + generateAhead)
            {
                GenerateNextChunk();
            }

            // 지나간 청크 제거
            for (int i = activeChunks.Count - 1; i >= 0; i--)
            {
                if (activeChunks[i].endX < playerX - destroyBehind)
                {
                    Destroy(activeChunks[i].root);
                    activeChunks.RemoveAt(i);
                }
            }
        }

        // ────────────────────────────
        //  청크 생성
        // ────────────────────────────

        private void GenerateInitialChunks()
        {
            // 시작 구역: 평지 (안전 지대)
            GenerateFlat(15f);

            // 추가 3청크 미리 생성
            for (int i = 0; i < 3; i++)
                GenerateNextChunk();
        }

        private void GenerateNextChunk()
        {
            float difficulty = Mathf.Clamp01(chunkCount * difficultyRamp);

            // 난이도에 따른 청크 타입 가중치
            float roll = (float)rng.NextDouble();

            if (difficulty < 0.2f)
            {
                // 초반: 평지 위주
                if (roll < 0.6f) GenerateFlat(RandomWidth());
                else GeneratePlatforming(RandomWidth());
            }
            else if (difficulty < 0.5f)
            {
                // 중반: 플랫폼 + 갭
                if (roll < 0.3f) GenerateFlat(RandomWidth());
                else if (roll < 0.7f) GeneratePlatforming(RandomWidth());
                else GenerateGap();
            }
            else
            {
                // 후반: 벽타기 + 갭 + 플랫폼 혼합
                if (roll < 0.15f) GenerateFlat(RandomWidth());
                else if (roll < 0.4f) GeneratePlatforming(RandomWidth());
                else if (roll < 0.65f) GenerateGap();
                else GenerateWallClimb();
            }

            chunkCount++;
        }

        // ────────────────────────────
        //  청크 타입별 생성
        // ────────────────────────────

        /// <summary>평지 청크 — 전투/이동 구간</summary>
        private void GenerateFlat(float width)
        {
            var chunk = CreateChunkRoot("Chunk_Flat");
            CreateGround(chunk.root.transform, 0f, width);
            chunk.endX = nextChunkX + width;
            nextChunkX += width;
            activeChunks.Add(chunk);
        }

        /// <summary>플랫폼 청크 — 높낮이 다른 발판</summary>
        private void GeneratePlatforming(float width)
        {
            var chunk = CreateChunkRoot("Chunk_Platform");

            // 바닥
            CreateGround(chunk.root.transform, 0f, width);

            // 플랫폼 2~4개 랜덤 배치
            int platCount = 2 + rng.Next(3);
            for (int i = 0; i < platCount; i++)
            {
                float px = (float)(rng.NextDouble() * (width - 3f)) + 1.5f;
                float py = platformMinY + (float)(rng.NextDouble() * (platformMaxY - platformMinY));
                float pw = 2f + (float)(rng.NextDouble() * 3f);
                CreatePlatform(chunk.root.transform, px, py, pw);
            }

            chunk.endX = nextChunkX + width;
            nextChunkX += width;
            activeChunks.Add(chunk);
        }

        /// <summary>갭 청크 — 낭떠러지 (점프 필수)</summary>
        private void GenerateGap()
        {
            float gapWidth = gapMinWidth + (float)(rng.NextDouble() * (gapMaxWidth - gapMinWidth));
            var chunk = CreateChunkRoot("Chunk_Gap");

            // 좌측 절벽 가장자리 (착지 지점 없음 — 순수 갭)
            chunk.endX = nextChunkX + gapWidth;
            nextChunkX += gapWidth;
            activeChunks.Add(chunk);

            // 갭 건너편에 착지 플랫폼
            var landing = CreateChunkRoot("Chunk_Landing");
            float landWidth = 5f + (float)(rng.NextDouble() * 5f);
            CreateGround(landing.root.transform, 0f, landWidth);
            landing.endX = nextChunkX + landWidth;
            nextChunkX += landWidth;
            activeChunks.Add(landing);
        }

        /// <summary>벽타기 청크 — 세로 벽 2개 (벽↔벽 점프 구간)</summary>
        private void GenerateWallClimb()
        {
            var chunk = CreateChunkRoot("Chunk_WallClimb");

            // 좌측 벽
            CreateWall(chunk.root.transform, 0f, wallHeight);
            // 우측 벽
            CreateWall(chunk.root.transform, wallGap, wallHeight);

            // 벽 사이 바닥 (추락 시 복귀용)
            CreateGround(chunk.root.transform, -1f, wallGap + 2f);

            // 벽 위 착지 플랫폼
            CreatePlatform(chunk.root.transform, wallGap * 0.5f, wallHeight + 0.5f, wallGap + 2f);

            float chunkWidth = wallGap + 2f;
            chunk.endX = nextChunkX + chunkWidth;
            nextChunkX += chunkWidth;
            activeChunks.Add(chunk);
        }

        // ────────────────────────────
        //  빌딩 블록
        // ────────────────────────────

        private GeneratedChunk CreateChunkRoot(string name)
        {
            var go = new GameObject($"{name}_{chunkCount}");
            go.transform.position = new Vector3(nextChunkX, 0f, 0f);

            return new GeneratedChunk
            {
                root = go,
                startX = nextChunkX
            };
        }

        private void CreateGround(Transform parent, float localX, float width)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(localX + width * 0.5f, -0.5f, 0f);
            go.layer = LayerMask.NameToLayer("Ground");
            go.tag = "Ground";

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 1f);

            // 시각화 — Z=1 (캐릭터 뒤)
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform);
            visual.transform.localPosition = new Vector3(0f, 0f, 1f);
            visual.transform.localScale = new Vector3(width, 1f, 10f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader.name == "Hidden/InternalErrorShader")
                    mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.85f, 0.85f, 0.9f);
                renderer.sharedMaterial = mat;
                renderer.sortingLayerName = "Environment";
            }
        }

        private void CreatePlatform(Transform parent, float localX, float localY, float width)
        {
            var go = new GameObject("Platform");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(localX, localY, 0f);
            go.layer = LayerMask.NameToLayer("Ground");

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 0.3f);

            // 시각화 — Z=1 (캐릭터 뒤)
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform);
            visual.transform.localPosition = new Vector3(0f, 0f, 1f);
            visual.transform.localScale = new Vector3(width, 0.3f, 3f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader.name == "Hidden/InternalErrorShader")
                    mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.6f, 0.75f, 0.6f);
                renderer.sharedMaterial = mat;
                renderer.sortingLayerName = "Environment";
            }
        }

        private void CreateWall(Transform parent, float localX, float height)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(localX, height * 0.5f, 0f);
            int wallLayer = LayerMask.NameToLayer("Wall");
            go.layer = wallLayer >= 0 ? wallLayer : 0;
            go.tag = "Wall";

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, height);

            // 시각화 — Z=1 (캐릭터 뒤)
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform);
            visual.transform.localPosition = new Vector3(0f, 0f, 1f);
            visual.transform.localScale = new Vector3(0.5f, height, 2f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader.name == "Hidden/InternalErrorShader")
                    mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.5f, 0.35f, 0.25f);
                renderer.sharedMaterial = mat;
                renderer.sortingLayerName = "Environment";
            }
        }

        private float RandomWidth()
        {
            return chunkMinWidth + (float)(rng.NextDouble() * (chunkMaxWidth - chunkMinWidth));
        }

        // ────────────────────────────
        //  데이터 클래스
        // ────────────────────────────

        private class GeneratedChunk
        {
            public GameObject root;
            public float startX;
            public float endX;
        }
    }
}
