using UnityEditor;
using UnityEngine;
using FreeFlowHero.Combat.Enemy;

namespace FreeFlowHero.Combat.Editor
{
    /// <summary>
    /// AttackCoordinator Scene 뷰 핸들.
    /// 슬롯 구체를 드래그하여 surroundRadius / minEnemySpacing 을 직접 조절할 수 있다.
    /// - 좌/우 첫 번째 슬롯(가장 가까운) 드래그 → surroundRadius 조절
    /// - 좌/우 두 번째 슬롯 드래그 → minEnemySpacing 조절
    /// </summary>
    [CustomEditor(typeof(AttackCoordinator))]
    public class AttackCoordinatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // ── Reset Default 버튼 ──
            GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
            if (GUILayout.Button("포메이션 기본값 초기화", GUILayout.Height(30)))
            {
                SerializedObject so = serializedObject;
                so.Update();
                so.FindProperty("surroundRadius").floatValue = 2.5f;
                so.FindProperty("minEnemySpacing").floatValue = 1.3f;
                so.FindProperty("standoffDistance").floatValue = 2.2f;
                so.FindProperty("standoffHysteresis").floatValue = 0.3f;
                so.FindProperty("retreatSpeedMultiplier").floatValue = 0.6f;
                so.FindProperty("surroundApproachSpeed").floatValue = 3.0f;
                so.FindProperty("formationSlotCount").intValue = 2;
                so.FindProperty("closeRangeThreshold").floatValue = 2.0f;
                so.FindProperty("holderEngageDistance").floatValue = 0.9f;
                so.FindProperty("maxSimultaneousAttackers").intValue = 1;
                so.FindProperty("breathingTime").floatValue = 0.5f;
                so.ApplyModifiedProperties();
                Debug.Log("[AttackCoordinator] 모든 값을 기본값으로 초기화했습니다.");
            }
            GUI.backgroundColor = Color.white;
        }

        private void OnSceneGUI()
        {
            var coord = (AttackCoordinator)target;
            if (coord == null) return;

            // 기준점: Player 태그 오브젝트 또는 AttackCoordinator 위치
            Vector3 pivotPos;
            if (Application.isPlaying)
            {
                // 런타임: 등록된 적에서 플레이어 참조 (public API 없으므로 Player 태그 폴백)
                var player = GameObject.FindGameObjectWithTag("Player");
                pivotPos = player != null ? player.transform.position : coord.transform.position;
            }
            else
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                pivotPos = player != null ? player.transform.position : coord.transform.position;
            }

            // 직렬화 프로퍼티로 값 접근
            SerializedObject so = serializedObject;
            so.Update();

            var propRadius = so.FindProperty("surroundRadius");
            var propSpacing = so.FindProperty("minEnemySpacing");

            float radius = propRadius.floatValue;
            float spacing = propSpacing.floatValue;

            // 슬롯 위치 계산 (좌 4 + 우 4)
            // 좌: -radius, -(radius+s), -(radius+2s), -(radius+3s)
            // 우:  radius,  radius+s,    radius+2s,    radius+3s
            float[] offsets = new float[8];
            offsets[0] = -radius;
            offsets[1] = -(radius + spacing);
            offsets[2] = -(radius + 2f * spacing);
            offsets[3] = -(radius + 3f * spacing);
            offsets[4] = radius;
            offsets[5] = radius + spacing;
            offsets[6] = radius + 2f * spacing;
            offsets[7] = radius + 3f * spacing;

            float handleSize = 0.3f;
            float y = pivotPos.y + 0.1f;

            // ── 비상호작용 슬롯 (3~4번째) — 표시만 ──
            for (int i = 0; i < 8; i++)
            {
                // 0,1,4,5는 핸들로 처리, 나머지는 표시만
                if (i == 0 || i == 1 || i == 4 || i == 5) continue;

                Vector3 pos = new Vector3(pivotPos.x + offsets[i], y, pivotPos.z);
                Color c = i < 4
                    ? new Color(0.3f, 0.5f, 1f, 0.3f)
                    : new Color(1f, 0.5f, 0.3f, 0.3f);
                Handles.color = c;
                Handles.SphereHandleCap(0, pos, Quaternion.identity, handleSize * 2f, EventType.Repaint);
            }

            // ── 좌측 첫 번째 슬롯 (index 0) → surroundRadius 조절 ──
            {
                Vector3 slotPos = new Vector3(pivotPos.x + offsets[0], y, pivotPos.z);
                Handles.color = new Color(0.3f, 0.5f, 1f, 0.9f);
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.FreeMoveHandle(slotPos, handleSize,
                    Vector3.one * 0.1f, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    float newRadius = Mathf.Abs(newPos.x - pivotPos.x);
                    newRadius = Mathf.Max(0.5f, newRadius);
                    propRadius.floatValue = newRadius;
                    so.ApplyModifiedProperties();
                }
                // 라벨
                Handles.Label(slotPos + Vector3.up * 0.5f, $"Radius: {radius:F1}",
                    EditorStyles.boldLabel);
            }

            // ── 우측 첫 번째 슬롯 (index 4) → surroundRadius 조절 ──
            {
                Vector3 slotPos = new Vector3(pivotPos.x + offsets[4], y, pivotPos.z);
                Handles.color = new Color(1f, 0.5f, 0.3f, 0.9f);
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.FreeMoveHandle(slotPos, handleSize,
                    Vector3.one * 0.1f, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    float newRadius = Mathf.Abs(newPos.x - pivotPos.x);
                    newRadius = Mathf.Max(0.5f, newRadius);
                    propRadius.floatValue = newRadius;
                    so.ApplyModifiedProperties();
                }
            }

            // ── 좌측 두 번째 슬롯 (index 1) → minEnemySpacing 조절 ──
            {
                Vector3 slotPos = new Vector3(pivotPos.x + offsets[1], y, pivotPos.z);
                Handles.color = new Color(0.2f, 0.4f, 0.9f, 0.7f);
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.FreeMoveHandle(slotPos, handleSize * 0.8f,
                    Vector3.one * 0.1f, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    // spacing = |슬롯1 - 슬롯0| = |(radius+spacing) - radius|
                    float newAbsX = Mathf.Abs(newPos.x - pivotPos.x);
                    float newSpacing = newAbsX - radius;
                    newSpacing = Mathf.Max(0.3f, newSpacing);
                    propSpacing.floatValue = newSpacing;
                    so.ApplyModifiedProperties();
                }
                // 라벨
                Handles.Label(slotPos + Vector3.up * 0.5f, $"Spacing: {spacing:F1}",
                    EditorStyles.boldLabel);
            }

            // ── 우측 두 번째 슬롯 (index 5) → minEnemySpacing 조절 ──
            {
                Vector3 slotPos = new Vector3(pivotPos.x + offsets[5], y, pivotPos.z);
                Handles.color = new Color(0.9f, 0.4f, 0.2f, 0.7f);
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.FreeMoveHandle(slotPos, handleSize * 0.8f,
                    Vector3.one * 0.1f, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    float newAbsX = Mathf.Abs(newPos.x - pivotPos.x);
                    float newSpacing = newAbsX - radius;
                    newSpacing = Mathf.Max(0.3f, newSpacing);
                    propSpacing.floatValue = newSpacing;
                    so.ApplyModifiedProperties();
                }
            }

            // ── 플레이어 기준 중심선 ──
            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            Handles.DrawDottedLine(
                pivotPos + Vector3.left * 15f,
                pivotPos + Vector3.right * 15f, 4f);
        }
    }
}
