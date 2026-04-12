using UnityEngine;
using FreeFlowHero.Combat.Core;
using FreeFlowHero.Combat.Player;

namespace FreeFlowHero.Combat.Enemy
{
    /// <summary>
    /// 적 전투 UI — HP바 + 토큰 마커 + 히트 게이지 + 텔레그래프/처형/그로기 + 플로팅 데미지.
    /// 플레이 모드: OnGUI + GUI.DrawTexture (GameObject 생성 없음).
    /// 에디터 모드: EnemyCombatUIEditor.OnSceneGUI()에서 Handles로 미리보기 + 기즈모 편집.
    /// </summary>
    [RequireComponent(typeof(DummyEnemyTarget))]
    public class EnemyCombatUI : MonoBehaviour
    {
        // ─── HP 바 설정 (에디터 기즈모 + 런타임 OnGUI 공유) ───
        [Header("HP 바")]
        [SerializeField] private float barOffsetY = 4.0f;
        [SerializeField] private float barWidth = 1.5f;
        [SerializeField] private float barHeight = 0.15f;

        [Header("토큰 마커 ◆")]
        [SerializeField] private float tokenOffsetY = 0.3f;
        [SerializeField] private float tokenSize = 0.25f;

        [Header("히트 게이지 바")]
        [SerializeField] private float gaugeOffsetY = -0.25f;
        [SerializeField] private float gaugeWidth = 1.2f;
        [SerializeField] private float gaugeHeight = 0.1f;

        // ★ 데이터 튜닝: UI 컬러 설정
        [Header("HP 바 컬러")]
        [SerializeField] private Color hpBarBgColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color hpFullColor = Color.green;
        [SerializeField] private Color hpLowColor = Color.red;

        [Header("토큰 마커 컬러")]
        [SerializeField] private Color tokenColor = new Color(1f, 0.84f, 0f); // 금색

        [Header("히트 게이지 컬러")]
        [SerializeField] private Color gaugeBgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        [SerializeField] private Color gaugeLowColor = new Color(0.2f, 0.8f, 0.2f);
        [SerializeField] private Color gaugeHighColor = Color.red;

        // ─── 참조 ───
        private DummyEnemyTarget enemyTarget;
        private PlayerCombatFSM playerFSM;
        private EnemyAIController aiController;

        // ─── 상태 ───
        private float lastHP;

        // ─── 플로팅 텍스트 ───
        private float floatingTextTimer;
        private string floatingText = "";
        private Vector3 floatingTextWorldPos;
        private Color floatingTextColor;

        // ─── GUI 스타일 ───
        private static GUIStyle hitNumberStyle;
        private static GUIStyle telegraphStyle;
        private static GUIStyle executionStyle;
        private static GUIStyle groggyStyle;
        private static GUIStyle tokenStyle;

        // ─── 텍스처 캐시 ───
        private static Texture2D _whiteTexture;
        private static Texture2D WhiteTexture
        {
            get
            {
                if (_whiteTexture == null)
                {
                    _whiteTexture = new Texture2D(1, 1);
                    _whiteTexture.SetPixel(0, 0, Color.white);
                    _whiteTexture.Apply();
                }
                return _whiteTexture;
            }
        }

        // ============================================================
        //  초기화
        // ============================================================

        // ─── 진단 플래그 (1회만 출력) ───
        private bool _diagLogged;
        private bool _onguiLogged;
        private int _updateCount;

        private void Awake()
        {
            enemyTarget = GetComponent<DummyEnemyTarget>();
            aiController = GetComponent<EnemyAIController>();
            if (enemyTarget != null)
                lastHP = enemyTarget.CurrentHP;

            Debug.Log($"[EnemyCombatUI] Awake — {gameObject.name} | " +
                $"enemyTarget={(enemyTarget != null ? "OK" : "NULL")} | " +
                $"aiController={(aiController != null ? "OK" : "NULL")} | " +
                $"enabled={enabled} | activeInHierarchy={gameObject.activeInHierarchy}");
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            playerFSM = FindAnyObjectByType<PlayerCombatFSM>();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // 진단: 10프레임째에 OnGUI 호출 여부 확인
            _updateCount++;
            if (_updateCount == 10 && !_onguiLogged)
            {
                Debug.LogWarning($"[EnemyCombatUI][DIAG] Update 10회 도달했지만 OnGUI 미호출! — {gameObject.name} | " +
                    $"enabled={enabled} | activeInHierarchy={gameObject.activeInHierarchy} | " +
                    $"activeSelf={gameObject.activeSelf}");
            }

            DetectHit();
            UpdateFloatingText();
        }

        // ============================================================
        //  OnGUI — 플레이 모드 전용 렌더링 (GameObject 생성 없음)
        // ============================================================

        private void OnGUI()
        {
            _onguiLogged = true; // OnGUI 호출 확인용

            if (!Application.isPlaying) return;
            if (Camera.main == null)
            {
                if (!_diagLogged) { Debug.LogWarning($"[EnemyCombatUI] OnGUI 중단 — Camera.main == null ({gameObject.name})"); _diagLogged = true; }
                return;
            }

            // 1회 진단 로그
            if (!_diagLogged)
            {
                _diagLogged = true;
                Debug.Log($"[EnemyCombatUI][DIAG] {gameObject.name} | " +
                    $"enemyTarget={(enemyTarget != null ? $"OK (HP={enemyTarget.CurrentHP}, Targetable={enemyTarget.IsTargetable})" : "NULL")} | " +
                    $"aiController={(aiController != null ? "OK" : "NULL")} | " +
                    $"AttackCoord={(AttackCoordinator.Instance != null ? "OK" : "NULL")} | " +
                    $"barOffsetY={barOffsetY} barWidth={barWidth} barHeight={barHeight} | " +
                    $"hpBarBgColor={hpBarBgColor} hpFullColor={hpFullColor} hpLowColor={hpLowColor} | " +
                    $"Camera={Camera.main.name} ortho={Camera.main.orthographic} orthoSize={Camera.main.orthographicSize}");

                // WorldToGUI 변환 테스트
                Vector3 testWorld = transform.position + Vector3.up * barOffsetY;
                bool guiOk = WorldToGUI(testWorld, out Vector2 testGui);
                float testScale = WorldToPixelScale();
                Debug.Log($"[EnemyCombatUI][DIAG] WorldToGUI 테스트 — worldPos={testWorld} → guiPos={testGui} ok={guiOk} | pixelScale={testScale} | pw={barWidth * testScale:F1} ph={barHeight * testScale:F1}");
            }

            DrawHPBar();
            DrawTokenMarker();
            DrawHitGaugeBar();
            DrawFloatingDamage();
            DrawTelegraphIndicator();
            DrawExecutionIndicator();
            DrawGroggyEffect();
        }

        // ─── 좌표 변환 헬퍼 ───

        /// <summary>월드 좌표 → GUI 좌표 변환. 카메라 뒤면 false.</summary>
        private bool WorldToGUI(Vector3 worldPos, out Vector2 guiPos)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(worldPos);
            if (sp.z < 0) { guiPos = Vector2.zero; return false; }
            guiPos = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        /// <summary>월드 단위 → 픽셀 스케일 (orthographic 카메라 전용).</summary>
        private float WorldToPixelScale()
        {
            if (Camera.main.orthographic)
                return Screen.height / (Camera.main.orthographicSize * 2f);
            // perspective 폴백: 대략적인 스케일
            float dist = (Camera.main.transform.position - transform.position).magnitude;
            return Screen.height / (2f * dist * Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad));
        }

        /// <summary>OnGUI용 바 그리기 공용 헬퍼 (배경 + 채움, 왼쪽 정렬).</summary>
        private void DrawBar(Vector2 center, float pixelW, float pixelH, float ratio, Color bgColor, Color fillColor)
        {
            // 배경
            Rect bgRect = new Rect(center.x - pixelW * 0.5f, center.y - pixelH * 0.5f, pixelW, pixelH);
            GUI.color = bgColor;
            GUI.DrawTexture(bgRect, WhiteTexture);

            // 채움 (왼쪽 정렬)
            float fillW = pixelW * Mathf.Clamp01(ratio);
            if (fillW > 0.5f)
            {
                Rect fillRect = new Rect(bgRect.x, bgRect.y, fillW, pixelH);
                GUI.color = fillColor;
                GUI.DrawTexture(fillRect, WhiteTexture);
            }

            GUI.color = Color.white;
        }

        // ─── HP 바 ───

        private void DrawHPBar()
        {
            if (enemyTarget == null || !enemyTarget.IsTargetable) return;

            float hpRatio = enemyTarget.HPRatio;

            Vector3 worldPos = transform.position + Vector3.up * barOffsetY;
            if (!WorldToGUI(worldPos, out Vector2 guiPos)) return;

            float scale = WorldToPixelScale();
            float pw = barWidth * scale;
            float ph = Mathf.Max(barHeight * scale, 4f); // 최소 4px
            Color fillColor = Color.Lerp(hpLowColor, hpFullColor, hpRatio);

            DrawBar(guiPos, pw, ph, hpRatio, hpBarBgColor, fillColor);
        }

        // ─── 토큰 마커 ◆ ───

        private void DrawTokenMarker()
        {
            if (aiController == null) return;
            if (AttackCoordinator.Instance == null) return;
            if (!AttackCoordinator.Instance.IsTokenHolder(aiController)) return;

            Vector3 worldPos = transform.position + Vector3.up * (barOffsetY + tokenOffsetY);
            if (!WorldToGUI(worldPos, out Vector2 guiPos)) return;

            if (tokenStyle == null)
            {
                tokenStyle = new GUIStyle(GUI.skin.label);
                tokenStyle.fontSize = 22;
                tokenStyle.fontStyle = FontStyle.Bold;
                tokenStyle.alignment = TextAnchor.MiddleCenter;
            }
            tokenStyle.normal.textColor = tokenColor;
            GUI.Label(new Rect(guiPos.x - 15, guiPos.y - 15, 30, 30), "◆", tokenStyle);
        }

        // ─── 히트 게이지 바 ───

        private void DrawHitGaugeBar()
        {
            if (aiController == null) return;
            if (AttackCoordinator.Instance == null) return;
            if (!AttackCoordinator.Instance.IsTokenHolder(aiController)) return;

            float gaugeRatio = AttackCoordinator.Instance.CurrentHolderGaugeRatio;

            Vector3 worldPos = transform.position + Vector3.up * (barOffsetY + gaugeOffsetY);
            if (!WorldToGUI(worldPos, out Vector2 guiPos)) return;

            float scale = WorldToPixelScale();
            float pw = gaugeWidth * scale;
            float ph = Mathf.Max(gaugeHeight * scale, 3f); // 최소 3px

            Color fillColor = Color.Lerp(gaugeLowColor, gaugeHighColor, gaugeRatio);
            DrawBar(guiPos, pw, ph, gaugeRatio, gaugeBgColor, fillColor);
        }

        // ============================================================
        //  히트 감지 (플로팅 데미지 텍스트용)
        // ============================================================

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
            floatingText = $"-{damage:F0}";
            floatingTextTimer = 0.8f;
            floatingTextWorldPos = transform.position + Vector3.up * (barOffsetY + 0.6f);
            floatingTextColor = new Color(1f, 1f, 0.2f);
        }

        // ============================================================
        //  플로팅 텍스트
        // ============================================================

        private void UpdateFloatingText()
        {
            if (floatingTextTimer > 0f)
            {
                floatingTextTimer -= Time.deltaTime;
                floatingTextWorldPos += Vector3.up * Time.deltaTime * 1.5f;
            }
        }

        // ============================================================
        //  기존 OnGUI 인디케이터 (텔레그래프/처형/그로기/플로팅 데미지)
        // ============================================================

        private void DrawFloatingDamage()
        {
            if (floatingTextTimer <= 0f || string.IsNullOrEmpty(floatingText)) return;

            if (hitNumberStyle == null)
            {
                hitNumberStyle = new GUIStyle(GUI.skin.label);
                hitNumberStyle.fontSize = 24;
                hitNumberStyle.fontStyle = FontStyle.Bold;
                hitNumberStyle.alignment = TextAnchor.MiddleCenter;
                hitNumberStyle.richText = true;
            }

            Vector3 screenPos = Camera.main.WorldToScreenPoint(floatingTextWorldPos);
            if (screenPos.z < 0) return;
            float guiY = Screen.height - screenPos.y;

            float alpha = Mathf.Clamp01(floatingTextTimer / 0.4f);
            hitNumberStyle.normal.textColor = new Color(
                floatingTextColor.r, floatingTextColor.g, floatingTextColor.b, alpha);

            float sizeT = Mathf.Clamp01(1f - (floatingTextTimer / 0.8f));
            hitNumberStyle.fontSize = (int)Mathf.Lerp(32, 20, sizeT);

            GUI.Label(new Rect(screenPos.x - 50, guiY - 20, 100, 40),
                floatingText, hitNumberStyle);
        }

        private void DrawTelegraphIndicator()
        {
            if (aiController == null || !aiController.IsTelegraphing) return;
            if (!enemyTarget.IsTargetable) return;

            if (telegraphStyle == null)
            {
                telegraphStyle = new GUIStyle(GUI.skin.box);
                telegraphStyle.fontSize = 20;
                telegraphStyle.fontStyle = FontStyle.Bold;
                telegraphStyle.alignment = TextAnchor.MiddleCenter;
            }

            string label;
            Color bgColor;
            if (aiController.CurrentAttackCategory == AttackCategory.Ranged)
            {
                label = "Shift";
                bgColor = new Color(0.9f, 0.2f, 0.2f, 0.95f);
            }
            else
            {
                label = "RB";
                bgColor = new Color(1f, 0.85f, 0.1f, 0.95f);
            }

            Vector3 worldPos = transform.position + new Vector3(0, barOffsetY + 1.0f, 0);
            worldPos.y += Mathf.Sin(Time.time * 5f) * 0.08f;
            Vector3 sp = Camera.main.WorldToScreenPoint(worldPos);
            if (sp.z < 0) return;
            float guiY = Screen.height - sp.y;

            GUI.backgroundColor = bgColor;
            telegraphStyle.normal.textColor = Color.black;
            GUI.Box(new Rect(sp.x - 30, guiY - 16, 60, 32), label, telegraphStyle);
            GUI.backgroundColor = Color.white;
        }

        private void DrawExecutionIndicator()
        {
            if (enemyTarget == null || !enemyTarget.IsTargetable) return;
            if (playerFSM == null) return;

            float hpRatio = enemyTarget.HPRatio;
            int comboCount = playerFSM.Context != null ? playerFSM.Context.comboCount : 0;
            float threshold = comboCount >= CombatConstants.ExecutionHighComboThreshold
                ? CombatConstants.ExecutionHPThresholdHighCombo
                : CombatConstants.ExecutionHPThreshold;

            if (hpRatio > threshold || hpRatio <= 0f) return;

            float dist = Vector2.Distance(
                (Vector2)transform.position,
                (Vector2)playerFSM.transform.position);
            if (dist > CombatConstants.ExecutionRange * 1.5f) return;

            if (executionStyle == null)
            {
                executionStyle = new GUIStyle(GUI.skin.box);
                executionStyle.fontSize = 22;
                executionStyle.fontStyle = FontStyle.Bold;
                executionStyle.alignment = TextAnchor.MiddleCenter;
            }

            Vector3 worldPos = transform.position + new Vector3(0, barOffsetY + 0.8f, 0);
            worldPos.y += Mathf.Sin(Time.time * 3f) * 0.1f;
            Vector3 sp = Camera.main.WorldToScreenPoint(worldPos);
            if (sp.z < 0) return;
            float guiY = Screen.height - sp.y;

            GUI.backgroundColor = new Color(0.85f, 0.1f, 0.1f, 0.95f);
            executionStyle.normal.textColor = Color.white;
            GUI.Box(new Rect(sp.x - 20, guiY - 16, 40, 32), "F", executionStyle);
            GUI.backgroundColor = Color.white;
        }

        private void DrawGroggyEffect()
        {
            if (aiController == null) return;
            if (!enemyTarget.IsTargetable) return;
            if (!aiController.IsGroggyActive) return;
            if (aiController.CurrentGroggyType != Core.GroggyType.Hard) return;

            if (groggyStyle == null)
            {
                groggyStyle = new GUIStyle(GUI.skin.label);
                groggyStyle.fontSize = 24;
                groggyStyle.fontStyle = FontStyle.Bold;
                groggyStyle.alignment = TextAnchor.MiddleCenter;
            }

            Vector3 worldPos = transform.position + new Vector3(0, barOffsetY + 1.2f, 0);
            Vector3 sp = Camera.main.WorldToScreenPoint(worldPos);
            if (sp.z < 0) return;
            float guiY = Screen.height - sp.y;

            float time = Time.time * 3f;
            groggyStyle.normal.textColor = new Color(1f, 1f, 0.2f);

            for (int i = 0; i < 3; i++)
            {
                float angle = time + i * (Mathf.PI * 2f / 3f);
                float ox = Mathf.Cos(angle) * 25f;
                float oy = Mathf.Sin(angle) * 10f;
                GUI.Label(new Rect(sp.x + ox - 10, guiY + oy - 12, 20, 24), "★", groggyStyle);
            }
        }
    }
}
