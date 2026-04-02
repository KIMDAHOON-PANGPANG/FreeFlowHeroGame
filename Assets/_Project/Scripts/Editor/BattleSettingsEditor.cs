using UnityEngine;
using UnityEditor;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// BattleSettings 에셋 자동 생성 + 커스텀 인스펙터.
    /// 메뉴: REPLACED > Setup > 4. Generate BattleSettings Asset
    /// 메뉴: REPLACED > Battle Settings (에셋 열기)
    /// </summary>
    public static class BattleSettingsGenerator
    {
        private const string AssetPath = "Assets/_Project/Data/CombatConfig/BattleSettings.asset";

        [MenuItem("REPLACED/Advanced/4b. Generate BattleSettings Asset", priority = 40)]
        public static void GenerateAsset()
        {
            // 폴더 보장
            EnsureFolder("Assets/_Project/Data");
            EnsureFolder("Assets/_Project/Data/CombatConfig");

            // 기존 에셋 확인
            var existing = AssetDatabase.LoadAssetAtPath<BattleSettings>(AssetPath);
            if (existing != null)
            {
                Debug.Log($"[BattleSettings] 기존 에셋 발견: {AssetPath} — 덮어쓰지 않음");
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            // 새 에셋 생성
            var asset = ScriptableObject.CreateInstance<BattleSettings>();
            asset.ResetToDefaults();

            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            Debug.Log($"[BattleSettings] 전투 설정 에셋 생성 완료: {AssetPath}" +
                $"\n  CombatConstants 기본값으로 초기화됨" +
                $"\n  Inspector에서 수치를 조절하세요.");
        }

        [MenuItem("REPLACED/Battle Settings", priority = 20)]
        public static void OpenBattleSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<BattleSettings>(AssetPath);
            if (asset == null)
            {
                // 에셋이 없으면 자동 생성
                GenerateAsset();
                asset = AssetDatabase.LoadAssetAtPath<BattleSettings>(AssetPath);
            }

            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }

    /// <summary>
    /// BattleSettings 커스텀 인스펙터.
    /// 카테고리별 폴드아웃 + 툴팁 + 기본값 리셋 버튼 + 런타임 미리보기.
    /// </summary>
    [CustomEditor(typeof(BattleSettings))]
    public class BattleSettingsInspector : UnityEditor.Editor
    {
        // ─── 폴드아웃 상태 ───
        private bool foldFrame = true;
        private bool foldCombo = true;
        private bool foldInput = true;
        private bool foldDodge = true;
        private bool foldWarp = true;
        private bool foldTelegraph = true;
        private bool foldAttackTurn = true;
        private bool foldExecution = true;
        private bool foldHuxley = true;
        private bool foldComboBonus = true;
        private bool foldGroggy = true;
        private bool foldGuardCounterMotions = true;
        private bool foldExecutionMotions = true;

        // ─── 커스텀 툴팁 (자체 호버 감지) ───
        private string pendingTooltip;
        private GUIStyle tooltipStyle;
        private static readonly Vector2 TooltipOffset = new Vector2(18, -10);
        private const float TooltipMaxWidth = 280f;

        // ─── 스타일 캐시 ───
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;

        public override void OnInspectorGUI()
        {
            var bs = (BattleSettings)target;

            // 스타일 초기화
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.foldoutHeader)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
            }
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 6, 6)
                };
            }

            pendingTooltip = null; // ★ 매 프레임 리셋
            serializedObject.Update();

            // ── 타이틀 ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("REPLACED Battle Settings", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            });
            EditorGUILayout.LabelField("전투 시스템 공용 규칙 데이터", new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 11
            });
            EditorGUILayout.Space(8);

            EditorGUI.BeginChangeCheck();

            // ════════════ 프레임 기본 ════════════
            foldFrame = EditorGUILayout.Foldout(foldFrame, "프레임 기본", true, headerStyle);
            if (foldFrame)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.targetFPS = EditorGUILayout.IntField("Target FPS", bs.targetFPS);
                Tip("목표 프레임 레이트. 전투 판정의 기준 FPS (기본: 60)");
                EditorGUILayout.LabelField($"1 프레임 = {bs.FrameDuration * 1000f:F2}ms", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 콤보 시스템 ════════════
            foldCombo = EditorGUILayout.Foldout(foldCombo, "콤보 시스템", true, headerStyle);
            if (foldCombo)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.comboWindowDuration = EditorGUILayout.Slider("Combo Window (초)", bs.comboWindowDuration, 0.1f, 3.0f);
                Tip("마지막 히트 이후 다음 입력까지 콤보가 유지되는 시간.\n짧으면 콤보 끊기기 쉽고, 길면 여유롭다. (기본: 0.8초)");
                bs.maxComboCount = EditorGUILayout.IntField("Max Combo Count", bs.maxComboCount);
                Tip("콤보 카운트 최대치. 999 = 사실상 무제한");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 콤보 보너스 임계치 ════════════
            foldComboBonus = EditorGUILayout.Foldout(foldComboBonus, "콤보 보너스 임계치", true, headerStyle);
            if (foldComboBonus)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.comboThresholdGood = EditorGUILayout.IntField("Good (히트)", bs.comboThresholdGood);
                Tip("콤보 'Good' 등급 시작 히트 수 (기본: 5)");
                bs.comboThresholdGreat = EditorGUILayout.IntField("Great (히트)", bs.comboThresholdGreat);
                Tip("콤보 'Great' 등급 시작 히트 수 (기본: 10)");
                bs.comboThresholdAwesome = EditorGUILayout.IntField("Awesome (히트)", bs.comboThresholdAwesome);
                Tip("콤보 'Awesome' 등급 시작 히트 수 (기본: 20)");
                bs.comboThresholdUnstoppable = EditorGUILayout.IntField("Unstoppable (히트)", bs.comboThresholdUnstoppable);
                Tip("콤보 'Unstoppable' 등급 시작 히트 수 (기본: 50)");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 인풋 버퍼 ════════════
            foldInput = EditorGUILayout.Foldout(foldInput, "인풋 버퍼", true, headerStyle);
            if (foldInput)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.inputBufferDuration = EditorGUILayout.Slider("Input Buffer (초)", bs.inputBufferDuration, 0.05f, 0.5f);
                Tip("선입력이 유효한 시간.\n짧으면 정밀한 입력 요구, 길면 관대한 입력. (기본: 0.15초)");
                int bufferFrames = Mathf.RoundToInt(bs.inputBufferDuration / bs.FrameDuration);
                EditorGUILayout.LabelField($"= {bufferFrames}f @ {bs.targetFPS}fps", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 회피 ════════════
            foldDodge = EditorGUILayout.Foldout(foldDodge, "회피 (Dodge)", true, headerStyle);
            if (foldDodge)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.dodgeIFrames = EditorGUILayout.IntSlider("I-Frames (프레임)", bs.dodgeIFrames, 1, 30);
                Tip("회피 시 무적 프레임 수 (60fps 기준). (기본: 12f = 0.2초)");
                EditorGUILayout.LabelField($"= {bs.DodgeIFrameDuration:F3}초 무적", EditorStyles.miniLabel);
                bs.dodgeSpeed = EditorGUILayout.FloatField("Dodge Speed (유닛/초)", bs.dodgeSpeed);
                Tip("회피 이동 속도. (기본: 15)");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 워핑 ════════════
            foldWarp = EditorGUILayout.Foldout(foldWarp, "워핑 (Warp)", true, headerStyle);
            if (foldWarp)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.maxWarpDistance = EditorGUILayout.FloatField("Max Warp Distance (유닛)", bs.maxWarpDistance);
                Tip("워핑 시간 계산의 기준 최대 거리.\n이 거리 이상이면 최대 워프 시간 적용. (기본: 20)");

                EditorGUILayout.Space(4);
                bs.warpMinContactDistance = EditorGUILayout.Slider(
                    "Min Contact Distance (m)", bs.warpMinContactDistance, 0f, 2f);
                Tip("워핑 후 ROOT_MOTION으로 적에게 접근 가능한 최소 거리.\n이 거리 이내로는 전진이 차단됨.\n0.3 = 30cm, 0 = 제한 없음 (기본: 0.3)");
                float cmVal = bs.warpMinContactDistance * 100f;
                EditorGUILayout.LabelField($"= {cmVal:F0}cm", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 텔레그래프 ════════════
            foldTelegraph = EditorGUILayout.Foldout(foldTelegraph, "텔레그래프", true, headerStyle);
            if (foldTelegraph)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.telegraphMinDuration = EditorGUILayout.Slider("Min Duration (초)", bs.telegraphMinDuration, 0.1f, 1.0f);
                Tip("적 공격 예고 신호의 최소 표시 시간. (기본: 0.3초)");
                bs.telegraphMaxDuration = EditorGUILayout.Slider("Max Duration (초)", bs.telegraphMaxDuration, 0.2f, 2.0f);
                Tip("적 공격 예고 신호의 최대 표시 시간.\n난이도별로 조절 가능. (기본: 0.5초)");
                if (bs.telegraphMinDuration > bs.telegraphMaxDuration)
                    bs.telegraphMaxDuration = bs.telegraphMinDuration;
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 공격 턴 관리 ════════════
            foldAttackTurn = EditorGUILayout.Foldout(foldAttackTurn, "공격 턴 관리", true, headerStyle);
            if (foldAttackTurn)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.maxSimultaneousAttackers = EditorGUILayout.IntSlider("Max Simultaneous Attackers", bs.maxSimultaneousAttackers, 1, 5);
                Tip("플레이어를 동시에 공격할 수 있는 최대 적 수.\n높을수록 난이도 상승. (기본: 2)");
                bs.breathingTime = EditorGUILayout.Slider("Breathing Time (초)", bs.breathingTime, 0.1f, 2.0f);
                Tip("연속 공격 사이 최소 간격.\n플레이어에게 대응할 '숨 쉴 틈'을 준다. (기본: 0.5초)");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 처형 ════════════
            foldExecution = EditorGUILayout.Foldout(foldExecution, "처형 (Execution)", true, headerStyle);
            if (foldExecution)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.executionHPThreshold = EditorGUILayout.Slider("HP Threshold (%)", bs.executionHPThreshold, 0.05f, 0.5f);
                Tip("처형 가능 HP 비율. 0.2 = HP 20% 이하에서 처형 가능. (기본: 0.2)");
                EditorGUILayout.LabelField($"= HP {bs.executionHPThreshold * 100f:F0}% 이하", EditorStyles.miniLabel);
                bs.executionHPThresholdHighCombo = EditorGUILayout.Slider("High Combo Threshold (%)", bs.executionHPThresholdHighCombo, 0.05f, 0.5f);
                Tip("콤보 x50 이상일 때 상향된 처형 HP 임계치. (기본: 0.3)");
                EditorGUILayout.LabelField($"= HP {bs.executionHPThresholdHighCombo * 100f:F0}% 이하 (콤보 x50+)", EditorStyles.miniLabel);
                bs.executionRange = EditorGUILayout.FloatField("Execution Range (유닛)", bs.executionRange);
                Tip("처형 가능 거리. (기본: 2.0)");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // ════════════ 헉슬리 건 ════════════
            foldHuxley = EditorGUILayout.Foldout(foldHuxley, "헉슬리 건 (Huxley)", true, headerStyle);
            if (foldHuxley)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.huxleyBaseChargePerHit = EditorGUILayout.FloatField("Charge Per Hit (%)", bs.huxleyBaseChargePerHit);
                Tip("히트 1회당 헉슬리 게이지 충전량. (기본: 5%)");
                bs.huxleyMaxCharge = EditorGUILayout.FloatField("Max Charge (%)", bs.huxleyMaxCharge);
                Tip("헉슬리 게이지 최대치. (기본: 100%)");
                int hitsToFull = (bs.huxleyBaseChargePerHit > 0f)
                    ? Mathf.CeilToInt(bs.huxleyMaxCharge / bs.huxleyBaseChargePerHit)
                    : 999;
                EditorGUILayout.LabelField($"풀 충전까지 {hitsToFull}히트 필요", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            // ═══ 그로기 ═══
            EditorGUILayout.Space(4);
            foldGroggy = EditorGUILayout.Foldout(foldGroggy, "★ 그로기 (Groggy)", true, headerStyle);
            if (foldGroggy)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                bs.groggySoftDuration = EditorGUILayout.Slider("Soft 지속 시간 (초)", bs.groggySoftDuration, 0.3f, 3f);
                Tip("약한 그로기: 짧은 고개 흔들기 후 공격 복귀. (기본: 1.0초)");
                bs.groggyHardDuration = EditorGUILayout.Slider("Hard 지속 시간 (초)", bs.groggyHardDuration, 1f, 8f);
                Tip("강한 그로기: 별 이펙트 + 긴 경직. (기본: 3.0초)");
                EditorGUILayout.EndVertical();
            }

            // ═══ 가드 카운터 모션 ═══
            EditorGUILayout.Space(4);
            foldGuardCounterMotions = EditorGUILayout.Foldout(foldGuardCounterMotions, "★ 가드 카운터 모션", true, headerStyle);
            if (foldGuardCounterMotions)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                var gcProp = serializedObject.FindProperty("guardCounterMotions");
                if (gcProp != null)
                    EditorGUILayout.PropertyField(gcProp, true);
                Tip("퍼펙트 가드 시 발동할 카운터 모션 목록. weight로 확률 조절.");
                EditorGUILayout.EndVertical();
            }

            // ═══ 처형 모션 ═══
            EditorGUILayout.Space(4);
            foldExecutionMotions = EditorGUILayout.Foldout(foldExecutionMotions, "★ 처형 모션", true, headerStyle);
            if (foldExecutionMotions)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                var exProp = serializedObject.FindProperty("executionMotions");
                if (exProp != null)
                    EditorGUILayout.PropertyField(exProp, true);
                Tip("처형 시 발동할 모션 목록. weight로 확률 조절.");
                EditorGUILayout.EndVertical();
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(bs);
            }

            EditorGUILayout.Space(12);

            // ── 하단 버튼 ──
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("기본값으로 리셋", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("BattleSettings 리셋",
                    "모든 값을 CombatConstants 기본값으로 되돌리시겠습니까?", "리셋", "취소"))
                {
                    Undo.RecordObject(bs, "Reset BattleSettings");
                    bs.ResetToDefaults();
                    EditorUtility.SetDirty(bs);
                }
            }
            if (GUILayout.Button("CombatConstants 비교", GUILayout.Height(28)))
            {
                ShowDiffWithConstants(bs);
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();

            // ── 커스텀 툴팁 ──
            DrawCustomTooltip();
        }

        /// <summary>
        /// 직전 EditorGUILayout 필드의 Rect에 마우스가 올라가 있으면 pendingTooltip에 등록.
        /// </summary>
        private void Tip(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                if (r.Contains(Event.current.mousePosition))
                    pendingTooltip = text;
            }
        }

        /// <summary>
        /// pendingTooltip이 있으면 마우스 커서 근처에 커스텀 팝업을 그린다.
        /// Inspector 컨텍스트에서도 동작하도록 EditorWindow 없이 처리.
        /// </summary>
        private void DrawCustomTooltip()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (string.IsNullOrEmpty(pendingTooltip)) return;

            if (tooltipStyle == null)
            {
                tooltipStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    wordWrap = true,
                    richText = false,
                    padding = new RectOffset(8, 8, 6, 6),
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                    alignment = TextAnchor.UpperLeft
                };
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(0.18f, 0.18f, 0.18f, 0.96f));
                tex.Apply();
                tooltipStyle.normal.background = tex;
            }

            var content = new GUIContent(pendingTooltip);
            Vector2 size = tooltipStyle.CalcSize(content);
            if (size.x > TooltipMaxWidth)
            {
                size.x = TooltipMaxWidth;
                size.y = tooltipStyle.CalcHeight(content, TooltipMaxWidth);
            }

            Vector2 mouse = Event.current.mousePosition;
            float x = mouse.x + TooltipOffset.x;
            float y = mouse.y + TooltipOffset.y - size.y;

            // 위쪽 넘침 → 마우스 아래로
            if (y < 4f) y = mouse.y + 24f;
            // 왼쪽 넘침
            if (x < 4f) x = 4f;

            Rect tooltipRect = new Rect(x, y, size.x, size.y);

            // 배경 + 테두리
            EditorGUI.DrawRect(tooltipRect, new Color(0.15f, 0.15f, 0.15f, 0.96f));
            EditorGUI.DrawRect(new Rect(tooltipRect.x, tooltipRect.y, tooltipRect.width, 1), new Color(0.45f, 0.45f, 0.45f, 0.8f));
            EditorGUI.DrawRect(new Rect(tooltipRect.x, tooltipRect.yMax - 1, tooltipRect.width, 1), new Color(0.45f, 0.45f, 0.45f, 0.8f));
            EditorGUI.DrawRect(new Rect(tooltipRect.x, tooltipRect.y, 1, tooltipRect.height), new Color(0.45f, 0.45f, 0.45f, 0.8f));
            EditorGUI.DrawRect(new Rect(tooltipRect.xMax - 1, tooltipRect.y, 1, tooltipRect.height), new Color(0.45f, 0.45f, 0.45f, 0.8f));

            GUI.Label(tooltipRect, pendingTooltip, tooltipStyle);
        }

        /// <summary>현재 BattleSettings와 CombatConstants 기본값 차이를 콘솔에 출력</summary>
        private void ShowDiffWithConstants(BattleSettings bs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[BattleSettings] CombatConstants 기본값과의 차이:");
            int diffCount = 0;

            void Check(string name, float current, float def)
            {
                if (!Mathf.Approximately(current, def))
                {
                    sb.AppendLine($"  {name}: {def} → {current}");
                    diffCount++;
                }
            }
            void CheckInt(string name, int current, int def)
            {
                if (current != def)
                {
                    sb.AppendLine($"  {name}: {def} → {current}");
                    diffCount++;
                }
            }

            CheckInt("targetFPS", bs.targetFPS, CombatConstants.TargetFPS);
            Check("comboWindowDuration", bs.comboWindowDuration, CombatConstants.ComboWindowDuration);
            CheckInt("maxComboCount", bs.maxComboCount, CombatConstants.MaxComboCount);
            Check("inputBufferDuration", bs.inputBufferDuration, CombatConstants.InputBufferDuration);
            CheckInt("dodgeIFrames", bs.dodgeIFrames, CombatConstants.DodgeIFrames);
            Check("dodgeSpeed", bs.dodgeSpeed, CombatConstants.DodgeSpeed);
            Check("maxWarpDistance", bs.maxWarpDistance, CombatConstants.MaxWarpDistance);
            Check("telegraphMinDuration", bs.telegraphMinDuration, CombatConstants.TelegraphMinDuration);
            Check("telegraphMaxDuration", bs.telegraphMaxDuration, CombatConstants.TelegraphMaxDuration);
            CheckInt("maxSimultaneousAttackers", bs.maxSimultaneousAttackers, CombatConstants.MaxSimultaneousAttackers);
            Check("breathingTime", bs.breathingTime, CombatConstants.BreathingTime);
            Check("executionHPThreshold", bs.executionHPThreshold, CombatConstants.ExecutionHPThreshold);
            Check("executionHPThresholdHighCombo", bs.executionHPThresholdHighCombo, CombatConstants.ExecutionHPThresholdHighCombo);
            Check("executionRange", bs.executionRange, CombatConstants.ExecutionRange);
            Check("huxleyBaseChargePerHit", bs.huxleyBaseChargePerHit, CombatConstants.HuxleyBaseChargePerHit);
            Check("huxleyMaxCharge", bs.huxleyMaxCharge, CombatConstants.HuxleyMaxCharge);
            CheckInt("comboThresholdGood", bs.comboThresholdGood, CombatConstants.ComboThresholdGood);
            CheckInt("comboThresholdGreat", bs.comboThresholdGreat, CombatConstants.ComboThresholdGreat);
            CheckInt("comboThresholdAwesome", bs.comboThresholdAwesome, CombatConstants.ComboThresholdAwesome);
            CheckInt("comboThresholdUnstoppable", bs.comboThresholdUnstoppable, CombatConstants.ComboThresholdUnstoppable);

            if (diffCount == 0)
                sb.AppendLine("  (차이 없음 — 모든 값이 기본값과 동일)");
            else
                sb.AppendLine($"  총 {diffCount}개 항목 변경됨");

            Debug.Log(sb.ToString());
        }
    }
}
