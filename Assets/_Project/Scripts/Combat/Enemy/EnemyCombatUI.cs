using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.Player;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 적 전투 UI — HP 바 + 타겟 인디케이터 + 히트 넘버.
    /// Awake()에서 자식 오브젝트를 코드로 생성하므로 에디터 GUI 작업 불필요.
    /// DummyEnemyTarget이 있는 적 오브젝트에 부착한다.
    /// </summary>
    [RequireComponent(typeof(DummyEnemyTarget))]
    public class EnemyCombatUI : MonoBehaviour
    {
        // ─── 설정 ───
        [Header("HP 바")]
        [SerializeField] private float barWidth = 1.2f;
        [SerializeField] private float barHeight = 0.12f;
        [SerializeField] private float barOffsetY = 4.0f;
        [SerializeField] private Color barBgColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color barFullColor = new Color(0.2f, 0.9f, 0.2f);
        [SerializeField] private Color barLowColor = new Color(0.9f, 0.15f, 0.15f);

        [Header("타겟 인디케이터")]
        [SerializeField] private Color targetArrowColor = new Color(1f, 1f, 0f, 0.9f);
        [SerializeField] private float arrowSize = 0.6f;
        [SerializeField] private float arrowBobSpeed = 4f;
        [SerializeField] private float arrowBobAmount = 0.15f;

        [Header("히트 이펙트")]
        [SerializeField] private float hitScalePunch = 1.3f;
        [SerializeField] private float hitScaleDuration = 0.12f;

        // ─── 참조 ───
        private DummyEnemyTarget enemyTarget;
        private SpriteRenderer hpBarBg;
        private SpriteRenderer hpBarFill;
        private SpriteRenderer targetArrow;
        private Transform hpBarGroup;

        // ─── 상태 ───
        private float displayedHP = 1f;         // 부드러운 HP 보간용
        private float hitScaleTimer;
        private PlayerCombatFSM playerFSM;
        private float lastHP;

        // ─── 플로팅 텍스트 ───
        private float floatingTextTimer;
        private string floatingText = "";
        private Vector3 floatingTextWorldPos;
        private Color floatingTextColor;

        // ─── 히트 번호 스타일 ───
        private static GUIStyle hitNumberStyle;

        private void Awake()
        {
            enemyTarget = GetComponent<DummyEnemyTarget>();
            lastHP = enemyTarget.CurrentHP;

            CreateHPBar();
            CreateTargetArrow();
        }

        private void Start()
        {
            // 플레이어 FSM 참조 (타겟 인디케이터용) — 지연 검색 대비
            playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
            if (playerFSM == null)
                Debug.LogWarning($"[EnemyCombatUI] PlayerCombatFSM을 찾을 수 없습니다: {gameObject.name}");
        }

        private void Update()
        {
            UpdateHPBar();
            UpdateTargetArrow();
            UpdateHitScale();
            DetectHit();
            UpdateFloatingText();
        }

        private void OnDestroy()
        {
            // 씬 루트에 생성한 오브젝트 수동 정리
            if (hpBarGroup != null)
                Destroy(hpBarGroup.gameObject);
            if (targetArrow != null)
                Destroy(targetArrow.gameObject);
        }

        // ============================================================
        //  HP 바 생성 (코드 전용, GUI 작업 없음)
        // ============================================================

        private void CreateHPBar()
        {
            // 그룹 부모 — 부모 스케일 영향 방지 위해 씬 루트에 생성
            var groupObj = new GameObject($"HPBar_{gameObject.name}");
            hpBarGroup = groupObj.transform;
            // 씬 루트에 두고 Update에서 위치만 따라가게 함
            hpBarGroup.position = transform.position + new Vector3(0, barOffsetY, 0);
            hpBarGroup.localScale = Vector3.one;

            // 배경 바
            var bgObj = new GameObject("HPBar_BG");
            bgObj.transform.SetParent(hpBarGroup);
            bgObj.transform.localPosition = Vector3.zero;
            bgObj.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            hpBarBg = bgObj.AddComponent<SpriteRenderer>();
            hpBarBg.sprite = GetWhiteSprite();
            hpBarBg.color = barBgColor;
            hpBarBg.sortingOrder = 90;

            // 채움 바
            var fillObj = new GameObject("HPBar_Fill");
            fillObj.transform.SetParent(hpBarGroup);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localScale = new Vector3(barWidth, barHeight * 0.8f, 1f);
            hpBarFill = fillObj.AddComponent<SpriteRenderer>();
            hpBarFill.sprite = GetWhiteSprite();
            hpBarFill.color = barFullColor;
            hpBarFill.sortingOrder = 91;
        }

        private void UpdateHPBar()
        {
            if (enemyTarget == null || hpBarFill == null || hpBarGroup == null) return;

            // HP 바 위치를 적 머리 위로 따라감 (부모 스케일 무관)
            hpBarGroup.position = transform.position + new Vector3(0, barOffsetY, 0);

            float targetRatio = enemyTarget.HPRatio;

            // 부드러운 HP 보간
            displayedHP = Mathf.Lerp(displayedHP, targetRatio, Time.deltaTime * 10f);

            // 스케일 조정 (왼쪽 정렬)
            float fillW = barWidth * displayedHP;
            hpBarFill.transform.localScale = new Vector3(fillW, barHeight * 0.8f, 1f);

            // 왼쪽 정렬: 중심 이동
            float offset = (barWidth - fillW) * 0.5f;
            hpBarFill.transform.localPosition = new Vector3(-offset, 0, 0);

            // 색상: HP 비율에 따라 초록→빨강
            hpBarFill.color = Color.Lerp(barLowColor, barFullColor, displayedHP);

            // 사망 시 HP 바 숨김
            if (!enemyTarget.IsTargetable)
            {
                hpBarGroup.gameObject.SetActive(false);
            }
        }

        // ============================================================
        //  타겟 인디케이터 (▼ 화살표)
        // ============================================================

        private void CreateTargetArrow()
        {
            var arrowObj = new GameObject($"TargetArrow_{gameObject.name}");
            // 씬 루트에 배치 (부모 스케일 영향 방지)
            arrowObj.transform.position = transform.position + new Vector3(0, barOffsetY + 0.5f, 0);
            arrowObj.transform.localScale = new Vector3(arrowSize, arrowSize, 1f);
            arrowObj.transform.localRotation = Quaternion.Euler(0, 0, 180f); // ▼ 뒤집기

            targetArrow = arrowObj.AddComponent<SpriteRenderer>();
            targetArrow.sprite = CreateDiamondSprite();
            targetArrow.color = targetArrowColor;
            targetArrow.sortingOrder = 100; // 최상위 렌더링
            targetArrow.gameObject.SetActive(false);
        }

        private void UpdateTargetArrow()
        {
            if (targetArrow == null) return;

            // playerFSM 지연 검색
            if (playerFSM == null)
            {
                playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
                if (playerFSM == null) return;
            }

            var currentTarget = playerFSM.TargetSelector.CurrentTarget;
            bool isTarget = currentTarget != null &&
                            currentTarget.GetTransform() == transform;

            targetArrow.gameObject.SetActive(isTarget);

            if (isTarget)
            {
                // 위아래 흔들리는 애니메이션
                float bob = Mathf.Sin(Time.time * arrowBobSpeed) * arrowBobAmount;
                // 월드 좌표로 직접 배치 (부모 스케일 영향 제거)
                targetArrow.transform.position = transform.position +
                    new Vector3(0, barOffsetY + 0.5f + bob, 0);
            }
        }

        // ============================================================
        //  히트 이펙트 (스케일 펀치)
        // ============================================================

        /// <summary>DummyEnemyTarget에서 HP 변화 감지</summary>
        private void DetectHit()
        {
            if (enemyTarget == null) return;

            float curHP = enemyTarget.CurrentHP;
            if (curHP < lastHP)
            {
                float damage = lastHP - curHP;
                OnEnemyHit(damage);
            }
            lastHP = curHP;
        }

        private void OnEnemyHit(float damage)
        {
            // 스케일 펀치 시작
            hitScaleTimer = hitScaleDuration;

            // 플로팅 텍스트 생성
            floatingText = $"-{damage:F0}";
            floatingTextTimer = 0.8f;
            floatingTextWorldPos = transform.position + Vector3.up * (barOffsetY + 0.6f);
            floatingTextColor = new Color(1f, 1f, 0.2f);
        }

        private void UpdateHitScale()
        {
            if (hitScaleTimer <= 0f) return;

            hitScaleTimer -= Time.deltaTime;
            float t = hitScaleTimer / hitScaleDuration; // 1→0
            float scale = Mathf.Lerp(1f, hitScalePunch, t);

            if (hpBarGroup != null)
                hpBarGroup.localScale = new Vector3(scale, scale, 1f);

            if (hitScaleTimer <= 0f && hpBarGroup != null)
                hpBarGroup.localScale = Vector3.one;
        }

        // ============================================================
        //  플로팅 텍스트 (OnGUI — 월드좌표 기반)
        // ============================================================

        private void UpdateFloatingText()
        {
            if (floatingTextTimer > 0f)
            {
                floatingTextTimer -= Time.deltaTime;
                // 위로 떠오르는 효과
                floatingTextWorldPos += Vector3.up * Time.deltaTime * 1.5f;
            }
        }

        private void OnGUI()
        {
            if (floatingTextTimer <= 0f || string.IsNullOrEmpty(floatingText)) return;
            if (Camera.main == null) return;

            // 스타일 초기화
            if (hitNumberStyle == null)
            {
                hitNumberStyle = new GUIStyle(GUI.skin.label);
                hitNumberStyle.fontSize = 24;
                hitNumberStyle.fontStyle = FontStyle.Bold;
                hitNumberStyle.alignment = TextAnchor.MiddleCenter;
                hitNumberStyle.richText = true;
            }

            // 월드→스크린 변환
            Vector3 screenPos = Camera.main.WorldToScreenPoint(floatingTextWorldPos);
            if (screenPos.z < 0) return; // 카메라 뒤

            // Unity OnGUI Y축 반전
            float guiY = Screen.height - screenPos.y;

            // 페이드아웃
            float alpha = Mathf.Clamp01(floatingTextTimer / 0.4f);
            hitNumberStyle.normal.textColor = new Color(
                floatingTextColor.r, floatingTextColor.g, floatingTextColor.b, alpha);

            // 크기 애니메이션 (처음에 크게 → 줄어듦)
            float sizeT = Mathf.Clamp01(1f - (floatingTextTimer / 0.8f));
            hitNumberStyle.fontSize = (int)Mathf.Lerp(32, 20, sizeT);

            GUI.Label(new Rect(screenPos.x - 50, guiY - 20, 100, 40),
                floatingText, hitNumberStyle);
        }

        // ============================================================
        //  스프라이트 유틸
        // ============================================================

        /// <summary>런타임에서 1x1 흰색 스프라이트를 생성/캐시한다.</summary>
        private static Sprite cachedWhiteSprite;
        private static Sprite GetWhiteSprite()
        {
            if (cachedWhiteSprite != null) return cachedWhiteSprite;

            Texture2D tex = new Texture2D(4, 4);
            Color[] colors = new Color[16];
            for (int i = 0; i < 16; i++) colors[i] = Color.white;
            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Point;

            cachedWhiteSprite = Sprite.Create(tex,
                new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return cachedWhiteSprite;
        }

        /// <summary>런타임에서 다이아몬드(화살표) 스프라이트 생성</summary>
        private static Sprite cachedDiamondSprite;
        private static Sprite CreateDiamondSprite()
        {
            if (cachedDiamondSprite != null) return cachedDiamondSprite;

            int size = 16;
            Texture2D tex = new Texture2D(size, size);
            Color clear = new Color(0, 0, 0, 0);

            // 다이아몬드 형태 (▼ 화살표 역할)
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = size / 2;
                    int cy = size / 2;
                    float dist = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                    tex.SetPixel(x, y, dist <= size / 2 ? Color.white : clear);
                }
            }

            tex.Apply();
            tex.filterMode = FilterMode.Point;

            cachedDiamondSprite = Sprite.Create(tex,
                new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), (float)size);
            return cachedDiamondSprite;
        }
    }
}
