using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFlowHero.Combat.Core
{
    /// <summary>
    /// 키보드 [2] 토글로 캐릭터 방향 디버그 오버레이를 표시한다.
    ///
    /// 표시 항목:
    ///   - 플레이어: 초록색 화살표 (localScale.x 기반 facing)
    ///   - 적: 빨간색 화살표
    ///   - Hips 본 Y 회전값 (텍스트)
    ///   - 현재 재생 클립명
    ///
    /// CombatSceneSetup에서 자동 부착하거나, 플레이어에 수동 부착.
    /// </summary>
    public class FacingDebugOverlay : MonoBehaviour
    {
        private bool isEnabled;
        private Camera mainCam;

        // ★ 데이터 튜닝: 화살표 길이/색상
        private const float ArrowLength = 1.5f;
        private const float ArrowHeadSize = 0.3f;

        private static readonly Color PlayerColor = Color.green;
        private static readonly Color EnemyColor = new Color(1f, 0.3f, 0.3f);
        private static readonly Color HipsColor = Color.cyan;

        private GUIStyle labelStyle;

        private void Update()
        {
            // New Input System: 키보드 [2] 토글
            if (Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                isEnabled = !isEnabled;
                Debug.Log($"<color=cyan>[FacingDebug] {(isEnabled ? "ON" : "OFF")}</color>");
            }
        }

        private void OnGUI()
        {
            if (!isEnabled) return;
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam == null) return;

            // 스타일 초기화
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            // 토글 상태 표시
            GUI.Label(new Rect(10, 10, 200, 20), "<color=#00ff00>[2] Facing Debug ON</color>", labelStyle);

            // 플레이어 표시
            var playerFSM = FindAnyObjectByType<Player.PlayerCombatFSM>();
            if (playerFSM != null)
            {
                DrawFacingInfo(playerFSM.transform, "Player", PlayerColor);
            }

            // 적 표시
            var enemies = FindObjectsByType<Enemy.EnemyAIController>(FindObjectsSortMode.None);
            foreach (var enemy in enemies)
            {
                DrawFacingInfo(enemy.transform, enemy.gameObject.name, EnemyColor);
            }
        }

        private void DrawFacingInfo(Transform target, string label, Color color)
        {
            if (target == null || mainCam == null) return;

            float facing = Mathf.Sign(target.localScale.x);
            Vector3 worldPos = target.position + Vector3.up * 2.2f;
            Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);

            // 카메라 뒤에 있으면 스킵
            if (screenPos.z < 0) return;

            // Unity GUI는 좌상단 원점 → 변환
            float guiY = Screen.height - screenPos.y;

            // ── 화살표 (facing 방향) ──
            float arrowLen = 60f;
            float arrowX = screenPos.x;
            float arrowEndX = arrowX + facing * arrowLen;

            // 화살표 본체 (라인)
            DrawLine(
                new Vector2(arrowX, guiY),
                new Vector2(arrowEndX, guiY),
                color, 3f);

            // 화살표 머리
            DrawLine(
                new Vector2(arrowEndX, guiY),
                new Vector2(arrowEndX - facing * 12f, guiY - 8f),
                color, 3f);
            DrawLine(
                new Vector2(arrowEndX, guiY),
                new Vector2(arrowEndX - facing * 12f, guiY + 8f),
                color, 3f);

            // ── Hips 본 정보 ──
            string hipsInfo = "";
            string clipName = "";
            var animator = target.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // Hips 본 Y 회전
                if (animator.isHuman)
                {
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null)
                    {
                        Vector3 hipsEuler = hips.localRotation.eulerAngles;
                        hipsInfo = $"HipsY:{hipsEuler.y:F0}°";

                        // Hips 방향 화살표 (시안색, 작게)
                        float hipsYRad = hipsEuler.y * Mathf.Deg2Rad;
                        // 2D에서 Hips Y회전은 모델 로컬 Z축 방향 변화
                        // 모델이 Y축 90° 회전되어 있으므로 화면상 X방향에 매핑
                        float hipsArrowLen = 30f;
                        Vector2 hipsStart = new Vector2(arrowX, guiY + 15f);
                        Vector2 hipsEnd = new Vector2(
                            arrowX + Mathf.Sin(hipsYRad) * hipsArrowLen * facing,
                            guiY + 15f - Mathf.Cos(hipsYRad) * hipsArrowLen * 0.3f);
                        DrawLine(hipsStart, hipsEnd, HipsColor, 2f);
                    }
                }

                // 현재 재생 클립
                var clipInfo = animator.GetCurrentAnimatorClipInfo(0);
                if (clipInfo.Length > 0)
                    clipName = clipInfo[0].clip.name;
            }

            // ── 텍스트 라벨 ──
            string text = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>" +
                $"{label} F:{(facing > 0 ? "→" : "←")} {hipsInfo}" +
                $"\n{clipName}</color>";

            labelStyle.normal.textColor = color;
            GUI.Label(new Rect(screenPos.x - 80, guiY - 45, 160, 40), text, labelStyle);
        }

        // ── GL 라인 그리기 (OnGUI 내에서 사용) ──
        private static Material lineMat;

        private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
        {
            if (lineMat == null)
            {
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
                lineMat.hideFlags = HideFlags.HideAndDontSave;
                lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMat.SetInt("_ZWrite", 0);
            }

            GL.PushMatrix();
            lineMat.SetPass(0);
            GL.LoadPixelMatrix();

            // 라인 폭을 위해 여러 줄 그리기
            float halfW = width * 0.5f;
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * halfW;

            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(from.x + perp.x, from.y + perp.y, 0);
            GL.Vertex3(from.x - perp.x, from.y - perp.y, 0);
            GL.Vertex3(to.x - perp.x, to.y - perp.y, 0);
            GL.Vertex3(to.x + perp.x, to.y + perp.y, 0);
            GL.End();

            GL.PopMatrix();
        }
    }
}
