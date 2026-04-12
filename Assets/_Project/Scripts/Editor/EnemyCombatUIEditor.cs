using UnityEditor;
using UnityEngine;
using FreeFlowHero.Combat.Enemy;

namespace FreeFlowHero.Combat.Editor
{
    /// <summary>
    /// EnemyCombatUI Scene 뷰 핸들 + 인스펙터.
    /// 프리팹/씬 편집 모드에서 HP바, 토큰 마커, 히트 게이지를 Handles로 미리보기 + 드래그 편집.
    /// GameObject 생성 없음 — 순수 Handles 그리기만.
    /// </summary>
    [CustomEditor(typeof(EnemyCombatUI))]
    [CanEditMultipleObjects]
    public class EnemyCombatUIEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            // 인스펙터 변경 시 Scene 뷰 즉시 반영
            if (serializedObject.hasModifiedProperties)
            {
                serializedObject.ApplyModifiedProperties();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
            if (GUILayout.Button("UI 위치 기본값 초기화", GUILayout.Height(24)))
            {
                Undo.RecordObjects(targets, "Reset UI positions");
                foreach (var t in targets)
                {
                    var so = new SerializedObject(t);
                    so.Update();
                    so.FindProperty("barOffsetY").floatValue = 4.0f;
                    so.FindProperty("barWidth").floatValue = 1.5f;
                    so.FindProperty("barHeight").floatValue = 0.15f;
                    so.FindProperty("tokenOffsetY").floatValue = 0.3f;
                    so.FindProperty("tokenSize").floatValue = 0.25f;
                    so.FindProperty("gaugeOffsetY").floatValue = -0.25f;
                    so.FindProperty("gaugeWidth").floatValue = 1.2f;
                    so.FindProperty("gaugeHeight").floatValue = 0.1f;
                    // 컬러 기본값
                    so.FindProperty("hpBarBgColor").colorValue = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                    so.FindProperty("hpFullColor").colorValue = Color.green;
                    so.FindProperty("hpLowColor").colorValue = Color.red;
                    so.FindProperty("tokenColor").colorValue = new Color(1f, 0.84f, 0f);
                    so.FindProperty("gaugeBgColor").colorValue = new Color(0.15f, 0.15f, 0.15f, 0.9f);
                    so.FindProperty("gaugeLowColor").colorValue = new Color(0.2f, 0.8f, 0.2f);
                    so.FindProperty("gaugeHighColor").colorValue = Color.red;
                    so.ApplyModifiedProperties();
                }
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
        }

        private void OnSceneGUI()
        {
            var ui = (EnemyCombatUI)target;
            if (ui == null) return;

            var so = new SerializedObject(target);
            so.Update();

            Vector3 ownerPos = ui.transform.position;

            // SerializedProperty 읽기
            float offsetY = so.FindProperty("barOffsetY").floatValue;
            float bW = so.FindProperty("barWidth").floatValue;
            float bH = so.FindProperty("barHeight").floatValue;
            float tokOffY = so.FindProperty("tokenOffsetY").floatValue;
            float tokSz = so.FindProperty("tokenSize").floatValue;
            float gOffY = so.FindProperty("gaugeOffsetY").floatValue;
            float gW = so.FindProperty("gaugeWidth").floatValue;
            float gH = so.FindProperty("gaugeHeight").floatValue;

            // 컬러 필드 읽기
            Color hpBgCol = so.FindProperty("hpBarBgColor").colorValue;
            Color hpFullCol = so.FindProperty("hpFullColor").colorValue;
            Color hpLowCol = so.FindProperty("hpLowColor").colorValue;
            Color tokCol = so.FindProperty("tokenColor").colorValue;
            Color gBgCol = so.FindProperty("gaugeBgColor").colorValue;
            Color gLowCol = so.FindProperty("gaugeLowColor").colorValue;
            Color gHighCol = so.FindProperty("gaugeHighColor").colorValue;

            Vector3 barCenter = ownerPos + Vector3.up * offsetY;

            // ═══ 1. HP 바 미리보기 (배경 + 70% 채움) ═══
            Color hpBgPreview = hpBgCol; hpBgPreview.a = Mathf.Max(hpBgPreview.a, 0.85f);
            DrawFilledRect(barCenter, bW, bH, hpBgPreview);
            float previewRatio = 0.7f;
            Vector3 fillCenter = barCenter - Vector3.right * bW * (1f - previewRatio) * 0.5f;
            Color hpFillPreview = Color.Lerp(hpLowCol, hpFullCol, previewRatio);
            hpFillPreview.a = 0.7f;
            DrawFilledRect(fillCenter, bW * previewRatio, bH * 0.85f, hpFillPreview);
            Handles.color = Color.white;
            DrawWireRect(barCenter, bW, bH);

            // ═══ 2. HP 바 Y위치 핸들 (중앙) ═══
            Handles.color = Color.cyan;
            EditorGUI.BeginChangeCheck();
            Vector3 newBarCenter = Handles.FreeMoveHandle(
                barCenter,
                HandleUtility.GetHandleSize(barCenter) * 0.08f,
                Vector3.one * 0.05f, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                so.FindProperty("barOffsetY").floatValue = Mathf.Max(0.5f, newBarCenter.y - ownerPos.y);
                so.ApplyModifiedProperties();
            }

            // ═══ 3. HP 바 너비 핸들 (우측 끝) ═══
            Vector3 rightEdge = barCenter + Vector3.right * bW * 0.5f;
            Handles.color = Color.yellow;
            EditorGUI.BeginChangeCheck();
            Vector3 newRight = Handles.FreeMoveHandle(
                rightEdge,
                HandleUtility.GetHandleSize(rightEdge) * 0.06f,
                Vector3.one * 0.05f, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                float newW = Mathf.Clamp(Mathf.Abs(newRight.x - barCenter.x) * 2f, 0.3f, 5f);
                so.FindProperty("barWidth").floatValue = newW;
                so.ApplyModifiedProperties();
            }

            // ═══ 4. 토큰 마커 ◆ (인스펙터 컬러) ═══
            Vector3 tokenPos = barCenter + Vector3.up * tokOffY;
            Color tokPreview = tokCol; tokPreview.a = 0.85f;
            DrawDiamond(tokenPos, tokSz, tokPreview);

            Handles.color = new Color(tokCol.r, tokCol.g, tokCol.b, 0.9f);
            EditorGUI.BeginChangeCheck();
            Vector3 newTokPos = Handles.FreeMoveHandle(
                tokenPos,
                HandleUtility.GetHandleSize(tokenPos) * 0.1f,
                Vector3.one * 0.05f, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                so.FindProperty("tokenOffsetY").floatValue = Mathf.Clamp(newTokPos.y - barCenter.y, 0.1f, 2f);
                so.ApplyModifiedProperties();
            }

            // ═══ 5. 히트 게이지 바 (배경 + 50% 채움 미리보기) ═══
            Vector3 gaugeCenter = barCenter + Vector3.up * gOffY;
            Color gBgPreview = gBgCol; gBgPreview.a = Mathf.Max(gBgPreview.a, 0.8f);
            DrawFilledRect(gaugeCenter, gW, gH, gBgPreview);
            float gPreview = 0.5f;
            Vector3 gFillCenter = gaugeCenter - Vector3.right * gW * (1f - gPreview) * 0.5f;
            Color gFillPreview = Color.Lerp(gLowCol, gHighCol, gPreview);
            gFillPreview.a = 0.6f;
            DrawFilledRect(gFillCenter, gW * gPreview, gH * 0.85f, gFillPreview);
            DrawWireRect(gaugeCenter, gW, gH);

            // 게이지 너비 핸들 (우측 끝)
            Vector3 gRight = gaugeCenter + Vector3.right * gW * 0.5f;
            Handles.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            EditorGUI.BeginChangeCheck();
            Vector3 newGRight = Handles.FreeMoveHandle(
                gRight,
                HandleUtility.GetHandleSize(gRight) * 0.05f,
                Vector3.one * 0.05f, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                float newGW = Mathf.Clamp(Mathf.Abs(newGRight.x - gaugeCenter.x) * 2f, 0.2f, 4f);
                so.FindProperty("gaugeWidth").floatValue = newGW;
                so.ApplyModifiedProperties();
            }

            // ═══ 라벨 ═══
            Handles.color = Color.white;
            Handles.Label(barCenter + Vector3.left * (bW * 0.5f + 0.15f) + Vector3.up * 0.08f,
                $"HP ({bW:F1}×{bH:F2})", EditorStyles.boldLabel);
            Handles.Label(tokenPos + Vector3.right * (tokSz + 0.1f),
                $"◆ Token", EditorStyles.miniLabel);
            Handles.Label(gaugeCenter + Vector3.left * (gW * 0.5f + 0.15f),
                $"Gauge ({gW:F1}×{gH:F2})", EditorStyles.miniLabel);
        }

        // ============================================================
        //  도형 헬퍼 (CS0117 DrawSolidRectangleAndOutline 회피)
        // ============================================================

        /// <summary>솔리드 사각형 — DrawAAConvexPolygon 사용.</summary>
        private void DrawFilledRect(Vector3 center, float width, float height, Color color)
        {
            Handles.color = color;
            Vector3 hw = Vector3.right * width * 0.5f;
            Vector3 hh = Vector3.up * height * 0.5f;
            Handles.DrawAAConvexPolygon(
                center - hw - hh,
                center + hw - hh,
                center + hw + hh,
                center - hw + hh
            );
        }

        /// <summary>와이어 사각형 — DrawLine x4.</summary>
        private void DrawWireRect(Vector3 center, float width, float height)
        {
            Vector3 hw = Vector3.right * width * 0.5f;
            Vector3 hh = Vector3.up * height * 0.5f;
            Vector3 bl = center - hw - hh;
            Vector3 br = center + hw - hh;
            Vector3 tr = center + hw + hh;
            Vector3 tl = center - hw + hh;
            Handles.DrawLine(bl, br);
            Handles.DrawLine(br, tr);
            Handles.DrawLine(tr, tl);
            Handles.DrawLine(tl, bl);
        }

        /// <summary>솔리드 다이아몬드 ◆.</summary>
        private void DrawDiamond(Vector3 center, float size, Color color)
        {
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                center + Vector3.up * size,
                center + Vector3.right * size * 0.6f,
                center - Vector3.up * size,
                center - Vector3.right * size * 0.6f
            );
        }
    }
}
