using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using FreeFlowHero.Combat.Core;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 액션 테이블 에디터 윈도우 v2 — 노티파이 타임라인 시스템.
    ///
    /// 레이아웃:
    ///   상단 바    — 액터 선택, 리로드, 저장, Undo/Redo
    ///   좌측 패널  — 액션 목록 (검색, 추가/삭제)
    ///   중앙 상단  — 애니메이션 프리뷰 (3D 모델)
    ///   중앙 하단  — 타임라인 트랙 (노티파이 블록 배치)
    ///   우측 패널  — 인스펙터 (기본정보, 프레임데이터, 캔슬경로, 태그, 노티파이 상세)
    /// </summary>
    public class ActionTableEditorWindow : EditorWindow
    {
        [MenuItem("REPLACED/Action Table Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<ActionTableEditorWindow>("Action Table Editor");
            window.minSize = new Vector2(1100, 600);
        }

        // ═══════════════════════════════════════════════════════
        //  상태
        // ═══════════════════════════════════════════════════════

        private string[] actorFiles;
        private string[] actorFileNames;
        private int selectedActorIndex;
        private ActorActionTable currentTable;
        private int selectedActionIndex = -1;
        private string searchFilter = "";
        private Vector2 leftScroll;
        private Vector2 inspectorScroll;
        private bool isDirty;

        // ─── Undo/Redo ───
        private const int MaxUndoSteps = 50;
        private List<string> undoStack = new List<string>();
        private List<string> redoStack = new List<string>();
        private string lastSnapshotJson = "";

        // ─── 캔슬 경로 편집용 ───
        private static readonly string[] InputTypeOptions = {
            "Attack", "Heavy", "Dodge", "Counter", "Huxley", "Move", "Skill"
        };

        // ─── 애니메이션 프리뷰 ───
        private PreviewRenderUtility previewRender;
        private GameObject previewInstance;
        private Animator previewAnimator;
        private AnimationClip currentPreviewClip;
        private string cachedClipName = "";
        private float previewFrame = 0f;
        private bool isPreviewPlaying;
        private double lastPlayTime;

        private const float DefaultRotationY = 90f;
        private const float DefaultPitchX = 0f;
        private const float DefaultCamDistance = 3.5f;
        private const float DefaultPlaybackSpeed = 1.0f;
        private const int DefaultLoopMode = 0;

        private float previewRotationY = DefaultRotationY;
        private float previewPitchX = DefaultPitchX;
        private float previewCamDistance = DefaultCamDistance;
        private GUIStyle overlayWhiteStyle;
        private GUIStyle overlayShadowStyle;
        private Vector2 previewDragStart;
        private bool isDraggingPreview;
        private int loopMode = DefaultLoopMode;
        private static readonly string[] LoopModeLabels = { "전체", "Startup", "Active", "Recovery" };
        private float playbackSpeed = DefaultPlaybackSpeed;

        private int selectedViewIndex = 1;
        private static readonly string[] ViewLabels = { "Front", "Left", "Right", "Top", "Down" };
        private static readonly float[] SpeedPresets = { 0.5f, 1.0f, 2.0f };
        private static readonly string[] SpeedLabels = { "0.5x", "1.0x", "2.0x" };

        // ─── 타임라인 ───
        private const int MinTracks = 1;
        private const int MaxTracksLimit = 10;
        private int trackCount = 5;   // 동적 트랙 수 (1~10)
        private const float TrackHeight = 26f;
        private const float TimelineHeaderWidth = 100f;
        private const float TimeRulerHeight = 20f;
        private Vector2 timelineScroll;
        private int selectedNotifyIndex = -1;  // 선택된 노티파이 인덱스 (-1=미선택)

        // ★ 고정 타임라인 길이: 노티파이 endFrame 변경 시에도 타임라인 스케일이 변하지 않도록 함
        //   클립 길이 또는 레거시 TotalFrames를 기준으로 설정, 노티파이가 넘어가면 자동 확장
        private int fixedTimelineFrames = 0;   // 0이면 아직 미설정 → 자동 계산

        // ★ 프레임 ↔ 초 표시 토글
        private bool showTimeAsSeconds = false;

        // 트랙 활성 상태 (에디터 전용, 비활성 트랙의 노티파이는 disabled 처리)
        private bool[] trackEnabled = { true, true, true, true, true, true, true, true, true, true };

        // 드래그 상태
        private enum DragMode { None, Move, ResizeLeft, ResizeRight }
        private DragMode currentDragMode = DragMode.None;
        private int dragNotifyIndex = -1;
        private int dragStartFrame;
        private int dragEndFrame;
        private float dragMouseStartX;

        // ─── 스타일 ───
        private GUIStyle headerStyle;
        private GUIStyle selectedStyle;
        private GUIStyle tagStyle;
        private GUIStyle frameBarBg;
        private bool stylesInit;

        // ─── 경로 ───
        private static string TableFolderPath =>
            Path.Combine(Application.dataPath, "_Project", "Resources", "ActionTables");

        // ═══════════════════════════════════════════════════════
        //  라이프사이클
        // ═══════════════════════════════════════════════════════

        private void OnEnable()
        {
            RefreshFileList();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanupPreview();
        }

        private double lastRepaintTime;
        private const double RepaintInterval = 1.0 / 60.0;

        private void OnEditorUpdate()
        {
            if (!isPreviewPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            double elapsed = now - lastPlayTime;
            lastPlayTime = now;

            if (elapsed > 0.1) elapsed = RepaintInterval;

            float actionRate = 1.0f;
            if (currentTable?.actions != null && selectedActionIndex >= 0 &&
                selectedActionIndex < currentTable.actions.Length)
            {
                actionRate = currentTable.actions[selectedActionIndex].playbackRate;
                if (actionRate <= 0f) actionRate = 1.0f;
            }

            previewFrame += (float)(elapsed * 60.0 * playbackSpeed * actionRate);

            // 루프 범위
            int loopStart = 0;
            int loopEnd = 1;
            if (currentTable?.actions != null && selectedActionIndex >= 0 &&
                selectedActionIndex < currentTable.actions.Length)
            {
                var action = currentTable.actions[selectedActionIndex];
                int totalF = GetEffectiveTotalFrames(action);
                switch (loopMode)
                {
                    case 1: loopStart = 0; loopEnd = action.startup; break;
                    case 2: loopStart = action.startup; loopEnd = action.startup + action.active; break;
                    case 3: loopStart = action.startup + action.active; loopEnd = totalF; break;
                    default: loopStart = 0; loopEnd = totalF; break;
                }
            }
            if (loopEnd <= loopStart) { loopStart = 0; loopEnd = Mathf.Max(loopEnd, 1); }

            if (previewFrame >= loopEnd)
                previewFrame = loopStart;

            if (now - lastRepaintTime >= RepaintInterval)
            {
                lastRepaintTime = now;
                Repaint();
            }
        }

        /// <summary>노티파이 있으면 노티파이 기반, 없으면 레거시 TotalFrames</summary>
        private int GetEffectiveTotalFrames(ActionEntry action)
        {
            return action.HasNotifies ? action.NotifyTotalFrames : action.TotalFrames;
        }

        /// <summary>
        /// 타임라인 렌더링에 사용할 고정 총 프레임 수.
        /// fixedTimelineFrames가 설정되어 있으면 그 값과 실제 노티파이 범위 중 큰 값을 반환.
        /// 이렇게 하면 endFrame을 줄여도 타임라인 스케일이 갑자기 변하지 않는다.
        /// </summary>
        private int GetTimelineTotalFrames(ActionEntry action)
        {
            int actual = GetEffectiveTotalFrames(action);
            if (fixedTimelineFrames > 0)
                return Mathf.Max(fixedTimelineFrames, actual);
            return actual;
        }

        /// <summary>
        /// 액션이 바뀔 때 호출: 클립 길이 또는 레거시 프레임으로 고정 타임라인 길이 설정.
        /// </summary>
        private void UpdateFixedTimelineFrames(ActionEntry action)
        {
            // 1순위: 실제 애니메이션 클립 길이
            AnimationClip clip = FindClipForAction(action);
            if (clip != null)
            {
                // 클립 프레임 수 = 클립 길이(초) × 60fps, playbackRate 적용
                float rate = action.playbackRate > 0f ? action.playbackRate : 1f;
                int clipFrames = Mathf.CeilToInt(clip.length * 60f / rate);
                fixedTimelineFrames = Mathf.Max(clipFrames, 1);
                return;
            }

            // 2순위: 레거시 TotalFrames (startup + active + recovery)
            if (action.TotalFrames > 0)
            {
                fixedTimelineFrames = action.TotalFrames;
                return;
            }

            // 3순위: 현재 노티파이 범위
            fixedTimelineFrames = GetEffectiveTotalFrames(action);
        }

        private void InitStyles()
        {
            if (stylesInit) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.3f, 0.8f, 1f) }
            };

            selectedStyle = new GUIStyle(EditorStyles.label);
            var selTex = MakeTex(1, 1, new Color(0.2f, 0.4f, 0.6f, 0.5f));
            selectedStyle.normal.background = selTex;
            selectedStyle.normal.textColor = Color.white;
            selectedStyle.fontStyle = FontStyle.Bold;
            selectedStyle.padding = new RectOffset(6, 6, 3, 3);

            tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.7f, 0.3f) },
                fontStyle = FontStyle.Italic
            };

            frameBarBg = new GUIStyle();
            frameBarBg.normal.background = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.15f));

            stylesInit = true;
        }

        // ═══════════════════════════════════════════════════════
        //  Undo/Redo (기존 유지)
        // ═══════════════════════════════════════════════════════

        private void PushUndoSnapshot()
        {
            if (currentTable == null) return;
            string json = JsonUtility.ToJson(currentTable, false);
            if (json == lastSnapshotJson) return;
            undoStack.Add(lastSnapshotJson);
            if (undoStack.Count > MaxUndoSteps) undoStack.RemoveAt(0);
            redoStack.Clear();
            lastSnapshotJson = json;
        }

        private void PerformUndo()
        {
            if (undoStack.Count == 0) return;
            if (currentTable != null)
                redoStack.Add(JsonUtility.ToJson(currentTable, false));
            string prev = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            RestoreFromJson(prev);
            lastSnapshotJson = prev;
            isDirty = true;
            Repaint();
        }

        private void PerformRedo()
        {
            if (redoStack.Count == 0) return;
            if (currentTable != null)
                undoStack.Add(JsonUtility.ToJson(currentTable, false));
            string next = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            RestoreFromJson(next);
            lastSnapshotJson = next;
            isDirty = true;
            Repaint();
        }

        private void RestoreFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var restored = JsonUtility.FromJson<ActorActionTable>(json);
            if (restored != null)
            {
                currentTable = restored;
                currentTable.BuildMap();
                if (currentTable.actions == null || currentTable.actions.Length == 0)
                    selectedActionIndex = -1;
                else if (selectedActionIndex >= currentTable.actions.Length)
                    selectedActionIndex = currentTable.actions.Length - 1;
            }
        }

        private void ResetUndoHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
            lastSnapshotJson = currentTable != null ? JsonUtility.ToJson(currentTable, false) : "";
        }

        // ═══════════════════════════════════════════════════════
        //  메인 GUI — 새 레이아웃
        // ═══════════════════════════════════════════════════════

        private void OnGUI()
        {
            InitStyles();
            HandleKeyboardShortcuts();
            DrawToolbar();

            if (currentTable == null)
            {
                EditorGUILayout.HelpBox("액터 JSON 파일을 선택하세요.", MessageType.Info);
                return;
            }

            // ═══ 메인 3컬럼 레이아웃 ═══
            EditorGUILayout.BeginHorizontal();

            // ── 좌측: 액션 목록 ──
            DrawActionList();

            // 구분선
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

            // ── 중앙: 프리뷰(상단) + 타임라인(하단) ──
            DrawCenterPanel();

            // 구분선
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

            // ── 우측: 인스펙터 ──
            DrawInspectorPanel();

            EditorGUILayout.EndHorizontal();
        }

        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;

            if (ctrl && e.keyCode == KeyCode.Z)
            {
                if (e.shift) PerformRedo(); else PerformUndo();
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.Y) { PerformRedo(); e.Use(); }
            else if (e.keyCode == KeyCode.Delete && selectedNotifyIndex >= 0)
            {
                DeleteSelectedNotify();
                e.Use();
            }
        }

        // ═══════════════════════════════════════════════════════
        //  상단 툴바 (기존 유지)
        // ═══════════════════════════════════════════════════════

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Actor:", GUILayout.Width(40));
            int newIndex = EditorGUILayout.Popup(selectedActorIndex, actorFileNames ?? new string[0],
                EditorStyles.toolbarPopup, GUILayout.Width(160));
            if (newIndex != selectedActorIndex)
            {
                if (isDirty && !ConfirmDiscardOrSave("다른 액터로 전환하려 합니다."))
                { /* 취소 */ }
                else { selectedActorIndex = newIndex; LoadSelectedActor(); }
            }

            GUILayout.Space(10);

            if (GUILayout.Button("↻ Reload", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                if (isDirty)
                {
                    if (!ConfirmDiscardOrSave("리로드하면 에디터의 내용이 디스크 JSON으로 대체됩니다."))
                    { EditorGUILayout.EndHorizontal(); return; }
                }
                RefreshFileList();
                LoadSelectedActor();
                Repaint();
            }

            GUI.backgroundColor = isDirty ? Color.yellow : Color.white;
            if (GUILayout.Button(isDirty ? "★ Save" : "Save", EditorStyles.toolbarButton, GUILayout.Width(70)))
                SaveCurrentTable();
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);

            GUI.enabled = undoStack.Count > 0;
            if (GUILayout.Button("↩ Undo", EditorStyles.toolbarButton, GUILayout.Width(60)))
                PerformUndo();
            GUI.enabled = redoStack.Count > 0;
            if (GUILayout.Button("↪ Redo", EditorStyles.toolbarButton, GUILayout.Width(60)))
                PerformRedo();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (currentTable != null)
            {
                string undoInfo = undoStack.Count > 0 ? $"  |  Undo: {undoStack.Count}" : "";
                EditorGUILayout.LabelField(
                    $"{currentTable.actorId}  |  {currentTable.actions?.Length ?? 0} actions{undoInfo}",
                    EditorStyles.miniLabel, GUILayout.Width(260));
            }

            if (GUILayout.Button("+ New Actor", EditorStyles.toolbarButton, GUILayout.Width(80)))
                CreateNewActorFile();

            EditorGUILayout.EndHorizontal();
        }

        private bool ConfirmDiscardOrSave(string context)
        {
            int result = ConfirmDialog.Show(
                "변경사항 있음",
                $"저장하지 않은 변경사항이 있습니다.\n{context}",
                this.position
            );
            switch (result)
            {
                case 0: SaveCurrentTable(); return true;
                case 2: return true;
                default: return false;
            }
        }

        private class ConfirmDialog : EditorWindow
        {
            private string message;
            private int result = 1;
            private bool decided;

            public static int Show(string title, string message, Rect parentRect)
            {
                var dialog = CreateInstance<ConfirmDialog>();
                dialog.titleContent = new GUIContent(title);
                dialog.message = message;
                float w = 360, h = 130;
                float x = parentRect.x + (parentRect.width - w) * 0.5f;
                float y = parentRect.y + (parentRect.height - h) * 0.5f;
                dialog.position = new Rect(x, y, w, h);
                dialog.minSize = new Vector2(w, h);
                dialog.maxSize = new Vector2(w, h);
                dialog.ShowModalUtility();
                return dialog.result;
            }

            private void OnGUI()
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("저장 후 계속", GUILayout.Width(100), GUILayout.Height(26)))
                { result = 0; decided = true; Close(); }
                if (GUILayout.Button("변경사항 버리기", GUILayout.Width(110), GUILayout.Height(26)))
                { result = 2; decided = true; Close(); }
                if (GUILayout.Button("취소", GUILayout.Width(60), GUILayout.Height(26)))
                { result = 1; decided = true; Close(); }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(8);
            }

            private void OnDestroy() { if (!decided) result = 1; }
        }

        // ═══════════════════════════════════════════════════════
        //  좌측 패널: 액션 목록 (기존 유지)
        // ═══════════════════════════════════════════════════════

        private void DrawActionList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
                searchFilter = "";
            EditorGUILayout.EndHorizontal();

            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            if (currentTable?.actions != null)
            {
                for (int i = 0; i < currentTable.actions.Length; i++)
                {
                    var action = currentTable.actions[i];
                    if (action == null) continue;

                    if (!string.IsNullOrEmpty(searchFilter))
                    {
                        bool match = (action.id ?? "").ToLower().Contains(searchFilter.ToLower()) ||
                                     (action.name ?? "").ToLower().Contains(searchFilter.ToLower());
                        if (!match) continue;
                    }

                    Color itemColor = GetActionColor(action);
                    GUI.backgroundColor = (i == selectedActionIndex)
                        ? new Color(0.3f, 0.5f, 0.8f, 0.6f)
                        : new Color(itemColor.r, itemColor.g, itemColor.b, 0.15f);

                    EditorGUILayout.BeginHorizontal("box");
                    GUI.backgroundColor = Color.white;

                    var colorRect = GUILayoutUtility.GetRect(4, 20, GUILayout.Width(4));
                    EditorGUI.DrawRect(colorRect, itemColor);

                    var style = (i == selectedActionIndex) ? selectedStyle : EditorStyles.label;
                    if (GUILayout.Button($"  {action.id}\n  {action.name}", style, GUILayout.Height(32)))
                    {
                        selectedActionIndex = i;
                        selectedNotifyIndex = -1;
                        UpdateFixedTimelineFrames(action);
                    }

                    int totalF = GetEffectiveTotalFrames(action);
                    EditorGUILayout.LabelField($"{totalF}f", EditorStyles.miniLabel, GUILayout.Width(30));

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add")) AddNewAction();
            GUI.enabled = selectedActionIndex >= 0;
            if (GUILayout.Button("- Remove")) RemoveSelectedAction();
            if (GUILayout.Button("↑") && selectedActionIndex > 0)
            { PushUndoSnapshot(); SwapActions(selectedActionIndex, selectedActionIndex - 1); selectedActionIndex--; }
            if (GUILayout.Button("↓") && selectedActionIndex < (currentTable?.actions?.Length ?? 0) - 1)
            { PushUndoSnapshot(); SwapActions(selectedActionIndex, selectedActionIndex + 1); selectedActionIndex++; }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════
        //  중앙 패널: 프리뷰(상단) + 타임라인(하단)
        // ═══════════════════════════════════════════════════════

        private void DrawCenterPanel()
        {
            EditorGUILayout.BeginVertical();

            if (selectedActionIndex < 0 || currentTable?.actions == null ||
                selectedActionIndex >= currentTable.actions.Length)
            {
                EditorGUILayout.HelpBox("좌측에서 액션을 선택하세요.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var action = currentTable.actions[selectedActionIndex];

            // ── 프리뷰 (상단) ──
            DrawAnimationPreview(action);

            // ── 재생 컨트롤 ──
            AnimationClip clip = FindClipForAction(action);
            if (clip != null)
            {
                DrawPreviewControls(clip, GetEffectiveTotalFrames(action));
            }

            GUILayout.Space(4);

            // ── 타임라인 옵션 바 (프레임↔초 토글 + 고정 타임라인 길이) ──
            EditorGUILayout.BeginHorizontal();
            {
                // 프레임↔초 토글
                showTimeAsSeconds = GUILayout.Toggle(showTimeAsSeconds,
                    showTimeAsSeconds ? "  초(s) 표시  " : "  프레임(f) 표시  ",
                    EditorStyles.miniButton, GUILayout.Width(100));

                GUILayout.FlexibleSpace();

                // 고정 타임라인 길이 표시/편집
                EditorGUILayout.LabelField("타임라인 길이:", GUILayout.Width(80));
                int newFixed = EditorGUILayout.IntField(fixedTimelineFrames, GUILayout.Width(50));
                if (newFixed != fixedTimelineFrames && newFixed > 0)
                    fixedTimelineFrames = newFixed;
                if (showTimeAsSeconds)
                    EditorGUILayout.LabelField($"({fixedTimelineFrames * CombatConstants.FrameDuration:F2}s)", EditorStyles.miniLabel, GUILayout.Width(60));
                else
                    EditorGUILayout.LabelField("f", EditorStyles.miniLabel, GUILayout.Width(12));

                if (GUILayout.Button("Auto", EditorStyles.miniButton, GUILayout.Width(40)))
                    UpdateFixedTimelineFrames(action);
            }
            EditorGUILayout.EndHorizontal();

            // ── 타임라인 트랙 (하단) ──
            DrawNotifyTimeline(action);

            // ── 재생 상태 바 ──
            int curFrame = Mathf.RoundToInt(previewFrame);
            int totalF = GetTimelineTotalFrames(action);
            string playState = isPreviewPlaying ? "▶ Playing" : "⏸ Paused";
            string timeInfo = showTimeAsSeconds
                ? $"{curFrame * CombatConstants.FrameDuration:F2}s / {totalF * CombatConstants.FrameDuration:F2}s"
                : $"{curFrame} / {totalF}f   ({curFrame / 60f:F2}s / {totalF / 60f:F2}s)";
            EditorGUILayout.LabelField($"{playState}   {timeInfo}", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════
        //  우측 패널: 인스펙터
        // ═══════════════════════════════════════════════════════

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);

            if (selectedActionIndex < 0 || currentTable?.actions == null ||
                selectedActionIndex >= currentTable.actions.Length)
            {
                EditorGUILayout.HelpBox("액션을 선택하세요.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var action = currentTable.actions[selectedActionIndex];
            EditorGUI.BeginChangeCheck();

            // ── 기본 정보 ──
            EditorGUILayout.LabelField("기본 정보", headerStyle);
            action.id = EditorGUILayout.TextField("Action ID", action.id);
            action.name = EditorGUILayout.TextField("표시 이름", action.name);
            action.clip = EditorGUILayout.TextField("Animation Clip", action.clip);

            EditorGUILayout.Space(6);

            // ── 레거시 프레임 데이터 (하위호환) ──
            EditorGUILayout.LabelField("프레임 데이터 (레거시)", headerStyle);
            action.startup = EditorGUILayout.IntSlider("Startup", action.startup, 0, 30);
            action.active = EditorGUILayout.IntSlider("Active", action.active, 0, 30);
            action.recovery = EditorGUILayout.IntSlider("Recovery", action.recovery, 0, 40);
            EditorGUILayout.LabelField($"총 {action.TotalFrames}f = {action.TotalDuration:F3}s",
                EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(4);

            // ── 재생 배율 ──
            EditorGUILayout.BeginHorizontal();
            action.playbackRate = EditorGUILayout.Slider("Playback Rate", action.playbackRate, 0.1f, 3.0f);
            if (GUILayout.Button("1x", EditorStyles.miniButton, GUILayout.Width(28)))
                action.playbackRate = 1.0f;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ── 캔슬 설정 ──
            EditorGUILayout.LabelField("캔슬 설정", headerStyle);
            action.cancelRatio = EditorGUILayout.Slider("Cancel Ratio", action.cancelRatio, 0f, 1f);
            action.moveSpeed = EditorGUILayout.FloatField("Move Speed", action.moveSpeed);
            action.defaultNext = EditorGUILayout.TextField("Default Next", action.defaultNext);

            EditorGUILayout.Space(6);

            // ── 캔슬 경로 ──
            EditorGUILayout.LabelField("캔슬 경로", headerStyle);
            DrawCancelRoutes(action);

            EditorGUILayout.Space(6);

            // ── 태그 ──
            EditorGUILayout.LabelField("태그", headerStyle);
            DrawTags(action);

            EditorGUILayout.Space(8);

            // ── 선택된 노티파이 상세 편집 ──
            DrawNotifyInspector(action);

            if (EditorGUI.EndChangeCheck())
            {
                PushUndoSnapshot();
                isDirty = true;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════
        //  노티파이 인스펙터 (우측 패널 하단)
        // ═══════════════════════════════════════════════════════

        private void DrawNotifyInspector(ActionEntry action)
        {
            EditorGUILayout.LabelField("노티파이 상세", headerStyle);

            if (action.notifies == null || action.notifies.Length == 0)
            {
                EditorGUILayout.HelpBox("타임라인에서 우클릭으로 노티파이를 추가하세요.", MessageType.Info);
                return;
            }

            if (selectedNotifyIndex < 0 || selectedNotifyIndex >= action.notifies.Length)
            {
                EditorGUILayout.HelpBox("타임라인에서 노티파이 블록을 클릭하여 선택하세요.", MessageType.Info);
                return;
            }

            var notify = action.notifies[selectedNotifyIndex];
            var notifyType = notify.TypeEnum;

            // 공통 정보
            EditorGUILayout.LabelField($"타입: {NotifyTypeInfo.GetDisplayName(notifyType)}",
                EditorStyles.boldLabel);

            Color typeColor = NotifyTypeInfo.GetColor(notifyType);
            var colorRect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(colorRect, typeColor);

            EditorGUILayout.Space(4);

            // ── 실행 모드 (Instance vs State) ──
            EditorGUILayout.BeginHorizontal();
            bool prevInstance = notify.isInstance;
            notify.isInstance = EditorGUILayout.Toggle("Instance (1회 실행)", notify.isInstance);
            EditorGUILayout.EndHorizontal();
            if (notify.isInstance)
            {
                EditorGUILayout.HelpBox(
                    "인스턴스 모드: startFrame에서 단 1회만 발화됩니다.\nendFrame은 타임라인 시각 표시용입니다.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "스테이트 모드: startFrame~endFrame 구간 동안 매 프레임 활성.\n구간 양끝을 드래그하여 길이를 조절할 수 있습니다.",
                    MessageType.None);
            }

            EditorGUILayout.Space(2);

            // ── Disabled 토글 ──
            bool prevDisabled = notify.disabled;
            notify.disabled = EditorGUILayout.Toggle("Disabled (비활성)", notify.disabled);
            if (prevDisabled != notify.disabled)
                isDirty = true;

            EditorGUILayout.Space(4);

            if (showTimeAsSeconds)
            {
                // ── 초(seconds) 모드 ──
                float startSec = EditorGUILayout.FloatField("Start (초)", notify.StartTime);
                float endSec = EditorGUILayout.FloatField("End (초)", notify.EndTime);
                // 초→프레임 역변환
                int newStart = Mathf.RoundToInt(startSec / CombatConstants.FrameDuration);
                int newEnd = Mathf.RoundToInt(endSec / CombatConstants.FrameDuration);
                if (newStart != notify.startFrame || newEnd != notify.endFrame)
                {
                    notify.startFrame = Mathf.Max(0, newStart);
                    notify.endFrame = Mathf.Max(notify.startFrame + 1, newEnd);
                    isDirty = true;
                }
                EditorGUILayout.LabelField(
                    $"구간: {notify.Duration}f ({notify.DurationTime:F3}s)  [frame {notify.startFrame}~{notify.endFrame}]",
                    EditorStyles.miniLabel);
            }
            else
            {
                // ── 프레임(frame) 모드 ──
                notify.startFrame = EditorGUILayout.IntField("Start Frame", notify.startFrame);
                notify.endFrame = EditorGUILayout.IntField("End Frame", notify.endFrame);
                EditorGUILayout.LabelField(
                    $"구간: {notify.Duration}f ({notify.DurationTime:F3}s)  [{notify.StartTime:F3}s ~ {notify.EndTime:F3}s]",
                    EditorStyles.miniLabel);
            }
            notify.track = EditorGUILayout.IntSlider("Track", notify.track, 0, trackCount - 1);

            EditorGUILayout.Space(4);

            // 타입별 파라미터
            switch (notifyType)
            {
                case NotifyType.STARTUP:
                    EditorGUILayout.LabelField("STARTUP 파라미터", EditorStyles.boldLabel);
                    notify.moveSpeed = EditorGUILayout.FloatField("Move Speed", notify.moveSpeed);
                    break;

                case NotifyType.COLLISION:
                    EditorGUILayout.LabelField("COLLISION 파라미터", EditorStyles.boldLabel);
                    notify.hitboxId = EditorGUILayout.TextField("Hitbox ID", notify.hitboxId);
                    notify.damageScale = EditorGUILayout.Slider("Damage Scale", notify.damageScale, 0f, 5f);
                    break;

                case NotifyType.CANCEL_WINDOW:
                    EditorGUILayout.LabelField("CANCEL_WINDOW 파라미터", EditorStyles.boldLabel);
                    notify.skillCancel = EditorGUILayout.Toggle("Skill Cancel (콤보)", notify.skillCancel);
                    notify.moveCancel = EditorGUILayout.Toggle("Move Cancel (이동)", notify.moveCancel);
                    notify.dodgeCancel = EditorGUILayout.Toggle("Dodge Cancel (회피)", notify.dodgeCancel);
                    notify.counterCancel = EditorGUILayout.Toggle("Counter Cancel", notify.counterCancel);
                    notify.nextAction = EditorGUILayout.TextField("Next Action", notify.nextAction);
                    break;
            }

            EditorGUILayout.Space(4);

            // 삭제 버튼
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("노티파이 삭제", GUILayout.Height(22)))
            {
                DeleteSelectedNotify();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DeleteSelectedNotify()
        {
            if (selectedActionIndex < 0 || currentTable?.actions == null) return;
            var action = currentTable.actions[selectedActionIndex];
            if (action.notifies == null || selectedNotifyIndex < 0 ||
                selectedNotifyIndex >= action.notifies.Length) return;

            PushUndoSnapshot();
            var list = new List<ActionNotify>(action.notifies);
            list.RemoveAt(selectedNotifyIndex);
            action.notifies = list.ToArray();
            selectedNotifyIndex = -1;
            isDirty = true;
        }

        // ═══════════════════════════════════════════════════════
        //  타임라인 트랙 시스템
        // ═══════════════════════════════════════════════════════

        private void DrawNotifyTimeline(ActionEntry action)
        {
            // ★ 고정 타임라인 길이 사용 — endFrame 줄여도 스케일 불변
            if (fixedTimelineFrames <= 0) UpdateFixedTimelineFrames(action);
            int totalFrames = Mathf.Max(GetTimelineTotalFrames(action), 1);

            // 타임라인 전체 영역
            float timelineHeight = trackCount * TrackHeight + TimeRulerHeight + 4;
            Rect outerRect = GUILayoutUtility.GetRect(0, timelineHeight, GUILayout.ExpandWidth(true));

            if (outerRect.width < 50) return;

            // 배경
            EditorGUI.DrawRect(outerRect, new Color(0.13f, 0.13f, 0.13f));

            // 트랙 헤더 영역
            Rect headerRect = new Rect(outerRect.x, outerRect.y, TimelineHeaderWidth, outerRect.height);
            // 트랙 컨텐츠 영역
            Rect contentRect = new Rect(outerRect.x + TimelineHeaderWidth, outerRect.y,
                outerRect.width - TimelineHeaderWidth, outerRect.height - TimeRulerHeight);
            // 시간축 영역
            Rect rulerRect = new Rect(contentRect.x, contentRect.yMax, contentRect.width, TimeRulerHeight);

            // ── 트랙 헤더 (체크박스 + 이름) ──
            EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f));
            var trackLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                fontSize = 10
            };

            string[] defaultTrackNames = { "STARTUP", "COLLISION", "CANCEL", "Track 3", "Track 4" };
            for (int t = 0; t < trackCount; t++)
            {
                float rowY = headerRect.y + t * TrackHeight;

                // 체크박스 (ON/OFF)
                Rect toggleRect = new Rect(headerRect.x + 2, rowY + 4, 18, 18);
                bool wasEnabled = trackEnabled[t];
                trackEnabled[t] = GUI.Toggle(toggleRect, trackEnabled[t], GUIContent.none);

                // 트랙 활성 상태 변경 시: 해당 트랙의 모든 노티파이 disabled 동기화
                if (wasEnabled != trackEnabled[t] && action.notifies != null)
                {
                    PushUndoSnapshot();
                    for (int ni = 0; ni < action.notifies.Length; ni++)
                    {
                        if (action.notifies[ni].track == t)
                            action.notifies[ni].disabled = !trackEnabled[t];
                    }
                    isDirty = true;
                }

                // 트랙 이름
                string trackName = t < defaultTrackNames.Length ? defaultTrackNames[t] : $"Track {t}";
                Rect labelRect = new Rect(headerRect.x + 20, rowY, headerRect.width - 24, TrackHeight);

                // 비활성 트랙은 어둡게
                var labelStyle = new GUIStyle(trackLabelStyle);
                if (!trackEnabled[t])
                    labelStyle.normal.textColor = new Color(0.4f, 0.4f, 0.4f);
                GUI.Label(labelRect, trackName, labelStyle);

                // 트랙 구분선
                EditorGUI.DrawRect(new Rect(outerRect.x, rowY + TrackHeight,
                    outerRect.width, 1), new Color(0.25f, 0.25f, 0.25f));
            }

            // ── 트랙 헤더 우클릭: 트랙 추가/제거 메뉴 ──
            HandleTrackHeaderRightClick(headerRect, action);

            // ── 세로 그리드 라인 (프레임별) ──
            DrawTimelineGridLines(contentRect, totalFrames);

            // ── 노티파이 블록 렌더링 ──
            if (action.notifies != null)
            {
                for (int i = 0; i < action.notifies.Length; i++)
                {
                    var notify = action.notifies[i];
                    if (notify.track < 0 || notify.track >= trackCount) continue;

                    float startX = contentRect.x + (contentRect.width * notify.startFrame / totalFrames);
                    float endX = contentRect.x + (contentRect.width * notify.endFrame / totalFrames);
                    float blockW = Mathf.Max(endX - startX, 4);
                    float blockY = contentRect.y + notify.track * TrackHeight + 2;
                    float blockH = TrackHeight - 4;

                    Rect blockRect = new Rect(startX, blockY, blockW, blockH);
                    bool isDisabled = notify.disabled;
                    bool isSelected = (i == selectedNotifyIndex);

                    // ── 색상 ──
                    Color blockColor = NotifyTypeInfo.GetColor(notify.TypeEnum);
                    if (isSelected)
                        blockColor = Color.Lerp(blockColor, Color.white, 0.3f);
                    if (isDisabled)
                        blockColor = new Color(blockColor.r, blockColor.g, blockColor.b, 0.25f); // 투명하게

                    // ── 인스턴스 모드: 마름모(다이아몬드) 형태 ──
                    if (notify.isInstance)
                    {
                        // 인스턴스 = 포인트 마커 (startFrame 위치에 다이아몬드)
                        float markerX = startX;
                        float markerSize = blockH * 0.7f;
                        float centerY = blockY + blockH * 0.5f;

                        // 다이아몬드 배경
                        Vector3[] diamond = {
                            new Vector3(markerX, centerY - markerSize * 0.5f, 0),
                            new Vector3(markerX + markerSize * 0.5f, centerY, 0),
                            new Vector3(markerX, centerY + markerSize * 0.5f, 0),
                            new Vector3(markerX - markerSize * 0.5f, centerY, 0),
                        };
                        Handles.color = blockColor;
                        Handles.DrawSolidRectangleWithOutline(diamond, blockColor,
                            isSelected ? Color.white : new Color(0, 0, 0, 0.5f));

                        // "I" 라벨 (인스턴스 표시)
                        if (markerSize > 10)
                        {
                            var instanceLabel = new GUIStyle(EditorStyles.miniLabel)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = Color.white },
                                fontSize = 8,
                                fontStyle = FontStyle.Bold
                            };
                            Rect labelR = new Rect(markerX - 6, centerY - 6, 12, 12);
                            GUI.Label(labelR, "I", instanceLabel);
                        }

                        // 인스턴스에도 endFrame까지의 가이드 라인 (옅은 선)
                        if (blockW > 6)
                        {
                            EditorGUI.DrawRect(new Rect(markerX, centerY - 0.5f, blockW, 1),
                                new Color(blockColor.r, blockColor.g, blockColor.b, 0.3f));
                        }
                    }
                    else
                    {
                        // ── 스테이트 모드: 구간 블록 ──
                        EditorGUI.DrawRect(blockRect, blockColor);

                        // ── 시작/끝 마커 (세로 바) ──
                        Color markerColor = isSelected
                            ? Color.white
                            : new Color(1f, 1f, 1f, 0.6f);

                        // 시작 마커 (왼쪽 세로선 + 삼각형)
                        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 3, blockRect.height), markerColor);
                        // 시작 삼각형 (▶)
                        DrawTriangleMarker(blockRect.x + 1, blockY, blockH, true, markerColor);

                        // 끝 마커 (오른쪽 세로선 + 삼각형)
                        EditorGUI.DrawRect(new Rect(blockRect.xMax - 3, blockRect.y, 3, blockRect.height), markerColor);
                        // 끝 삼각형 (◀)
                        DrawTriangleMarker(blockRect.xMax - 1, blockY, blockH, false, markerColor);

                        // ── 선택 테두리 ──
                        if (isSelected)
                        {
                            EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, blockRect.width, 2), Color.white);
                            EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.yMax - 2, blockRect.width, 2), Color.white);
                        }

                        // ── 블록 라벨 ──
                        if (blockW > 30)
                        {
                            var blockLabel = new GUIStyle(EditorStyles.miniLabel)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = isDisabled ? new Color(1, 1, 1, 0.4f) : Color.white },
                                fontSize = 9,
                                fontStyle = FontStyle.Bold,
                                clipping = TextClipping.Clip
                            };
                            string typeName = NotifyTypeInfo.GetDisplayName(notify.TypeEnum);
                            string rangeStr = showTimeAsSeconds
                                ? $"{notify.StartTime:F2}s-{notify.EndTime:F2}s"
                                : $"{notify.startFrame}-{notify.endFrame}";
                            string label = $"{typeName} [{rangeStr}]";
                            if (isDisabled) label = $"({label})";
                            GUI.Label(blockRect, label, blockLabel);
                        }

                        // ── 리사이즈 핸들 커서 (좌우 6px) ──
                        if (!isDisabled)
                        {
                            EditorGUIUtility.AddCursorRect(
                                new Rect(blockRect.x, blockRect.y, 6, blockRect.height), MouseCursor.ResizeHorizontal);
                            EditorGUIUtility.AddCursorRect(
                                new Rect(blockRect.xMax - 6, blockRect.y, 6, blockRect.height), MouseCursor.ResizeHorizontal);
                        }
                    }
                }
            }

            // ── 재생 헤드 (빨간 세로선) ──
            float headX = contentRect.x + (contentRect.width * previewFrame / totalFrames);
            EditorGUI.DrawRect(new Rect(headX - 1, contentRect.y - 2, 3, contentRect.height + TimeRulerHeight + 4),
                new Color(1f, 0.2f, 0.2f, 0.9f));
            EditorGUI.DrawRect(new Rect(headX - 4, contentRect.y - 5, 9, 4), Color.red);

            // ── 시간 눈금 ──
            DrawTimeRuler(rulerRect, totalFrames);

            // ── 마우스 입력 처리 ──
            HandleTimelineMouseInput(contentRect, action, totalFrames);
        }

        // ═══════════════════════════════════════════════════════
        //  트랙 헤더 우클릭 — 트랙 추가/제거
        // ═══════════════════════════════════════════════════════

        private void HandleTrackHeaderRightClick(Rect headerRect, ActionEntry action)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 1) return;
            if (!headerRect.Contains(e.mousePosition)) return;

            int clickedTrack = Mathf.FloorToInt((e.mousePosition.y - headerRect.y) / TrackHeight);
            clickedTrack = Mathf.Clamp(clickedTrack, 0, trackCount - 1);

            var menu = new GenericMenu();

            // ── 트랙 추가 (현재 트랙 아래에) ──
            if (trackCount < MaxTracksLimit)
            {
                menu.AddItem(new GUIContent($"트랙 추가 (Track {clickedTrack} 아래에)"), false, () =>
                {
                    PushUndoSnapshot();
                    int insertAt = clickedTrack + 1;
                    trackCount++;

                    // 기존 노티파이의 트랙 번호를 밀어줌
                    if (action.notifies != null)
                    {
                        for (int i = 0; i < action.notifies.Length; i++)
                        {
                            if (action.notifies[i].track >= insertAt)
                                action.notifies[i].track++;
                        }
                    }

                    // trackEnabled 배열 시프트
                    for (int t = trackCount - 1; t > insertAt; t--)
                        trackEnabled[t] = trackEnabled[t - 1];
                    trackEnabled[insertAt] = true;

                    isDirty = true;
                    Repaint();
                });

                menu.AddItem(new GUIContent("맨 아래에 트랙 추가"), false, () =>
                {
                    PushUndoSnapshot();
                    trackCount++;
                    trackEnabled[trackCount - 1] = true;
                    isDirty = true;
                    Repaint();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"트랙 추가 (최대 {MaxTracksLimit}개)"));
            }

            menu.AddSeparator("");

            // ── 트랙 제거 ──
            if (trackCount > MinTracks)
            {
                // 해당 트랙에 노티파이가 있는지 확인
                int notifyCountOnTrack = 0;
                if (action.notifies != null)
                {
                    for (int i = 0; i < action.notifies.Length; i++)
                        if (action.notifies[i].track == clickedTrack) notifyCountOnTrack++;
                }

                string removeLabel = notifyCountOnTrack > 0
                    ? $"Track {clickedTrack} 제거 (노티파이 {notifyCountOnTrack}개 포함 삭제)"
                    : $"Track {clickedTrack} 제거";

                menu.AddItem(new GUIContent(removeLabel), false, () =>
                {
                    PushUndoSnapshot();

                    // 해당 트랙의 노티파이 삭제
                    if (action.notifies != null)
                    {
                        var remaining = new List<ActionNotify>();
                        for (int i = 0; i < action.notifies.Length; i++)
                        {
                            if (action.notifies[i].track == clickedTrack) continue;
                            var n = action.notifies[i];
                            // 삭제된 트랙보다 위 번호는 1 감소
                            if (n.track > clickedTrack) n.track--;
                            remaining.Add(n);
                        }
                        action.notifies = remaining.ToArray();
                    }

                    // trackEnabled 배열 시프트
                    for (int t = clickedTrack; t < trackCount - 1; t++)
                        trackEnabled[t] = trackEnabled[t + 1];
                    trackEnabled[trackCount - 1] = true;

                    trackCount--;
                    selectedNotifyIndex = -1;
                    isDirty = true;
                    Repaint();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("트랙 제거 (최소 1개 필요)"));
            }

            menu.ShowAsContext();
            e.Use();
        }

        /// <summary>시작/끝 마커용 삼각형 렌더링</summary>
        private void DrawTriangleMarker(float x, float y, float height, bool pointRight, Color color)
        {
            float triH = Mathf.Min(height * 0.3f, 6f);
            float triW = triH * 0.6f;
            float cy = y + height * 0.5f;

            Vector3[] verts;
            if (pointRight)
            {
                verts = new[] {
                    new Vector3(x, cy - triH, 0),
                    new Vector3(x + triW, cy, 0),
                    new Vector3(x, cy + triH, 0),
                };
            }
            else
            {
                verts = new[] {
                    new Vector3(x, cy - triH, 0),
                    new Vector3(x - triW, cy, 0),
                    new Vector3(x, cy + triH, 0),
                };
            }

            Handles.color = color;
            Handles.DrawAAConvexPolygon(verts);
        }

        private void DrawTimeRuler(Rect rulerRect, int totalFrames)
        {
            EditorGUI.DrawRect(rulerRect, new Color(0.16f, 0.16f, 0.16f));

            var tickLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                alignment = TextAnchor.UpperCenter
            };

            int step = totalFrames > 60 ? 10 : 5;
            for (int f = 0; f <= totalFrames; f += step)
            {
                float x = rulerRect.x + (rulerRect.width * f / totalFrames);
                float tickH = (f % 10 == 0) ? rulerRect.height * 0.5f : rulerRect.height * 0.3f;
                EditorGUI.DrawRect(new Rect(x, rulerRect.y, 1, tickH), new Color(0.4f, 0.4f, 0.4f));

                if (f % 10 == 0)
                {
                    string tickText = showTimeAsSeconds
                        ? $"{f * CombatConstants.FrameDuration:F2}s"
                        : $"{f}";
                    float labelW = showTimeAsSeconds ? 36f : 28f;
                    GUI.Label(new Rect(x - labelW * 0.5f, rulerRect.y + tickH, labelW, 12), tickText, tickLabel);
                }
            }
        }

        /// <summary>타임라인 컨텐츠 영역에 세로 그리드 라인 렌더링</summary>
        private void DrawTimelineGridLines(Rect contentRect, int totalFrames)
        {
            if (totalFrames <= 0) return;

            // 프레임 간 픽셀 간격 계산
            float pixelsPerFrame = contentRect.width / totalFrames;

            // 간격이 너무 좁으면 스킵 수 조절 (최소 4px 간격 보장)
            int frameStep = 1;
            if (pixelsPerFrame < 4f) frameStep = 5;
            if (pixelsPerFrame < 2f) frameStep = 10;
            if (pixelsPerFrame < 1f) frameStep = 20;

            // 일반 프레임 라인 (옅은 색)
            Color lineColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);
            // 5프레임 간격 강조
            Color lineColor5 = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            // 10프레임 간격 강조
            Color lineColor10 = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            for (int f = frameStep; f <= totalFrames; f += frameStep)
            {
                float x = contentRect.x + (contentRect.width * f / totalFrames);
                Color c = (f % 10 == 0) ? lineColor10 : (f % 5 == 0) ? lineColor5 : lineColor;
                EditorGUI.DrawRect(new Rect(x, contentRect.y, 1, contentRect.height), c);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  타임라인 마우스 입력
        // ═══════════════════════════════════════════════════════

        private void HandleTimelineMouseInput(Rect contentRect, ActionEntry action, int totalFrames)
        {
            Event e = Event.current;
            if (!contentRect.Contains(e.mousePosition) && currentDragMode == DragMode.None) return;

            float framesPerPixel = (float)totalFrames / contentRect.width;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:  // 좌클릭
                    HandleTimelineLeftClick(contentRect, action, totalFrames, e);
                    break;

                case EventType.MouseDown when e.button == 1:  // 우클릭
                    HandleTimelineRightClick(contentRect, action, totalFrames, e);
                    break;

                case EventType.MouseDrag when currentDragMode != DragMode.None:
                    HandleTimelineDrag(contentRect, action, totalFrames, e, framesPerPixel);
                    break;

                case EventType.MouseUp when e.button == 0:
                    if (currentDragMode != DragMode.None)
                    {
                        currentDragMode = DragMode.None;
                        dragNotifyIndex = -1;
                        e.Use();
                    }
                    break;
            }
        }

        private void HandleTimelineLeftClick(Rect contentRect, ActionEntry action, int totalFrames, Event e)
        {
            if (action.notifies == null) return;

            float mouseFrame = ((e.mousePosition.x - contentRect.x) / contentRect.width) * totalFrames;
            int track = Mathf.FloorToInt((e.mousePosition.y - contentRect.y) / TrackHeight);

            // 블록 히트 테스트 (역순으로 = 위에 그려진 것 우선)
            for (int i = action.notifies.Length - 1; i >= 0; i--)
            {
                var notify = action.notifies[i];
                if (notify.track != track) continue;
                if (mouseFrame < notify.startFrame || mouseFrame >= notify.endFrame) continue;

                // 히트!
                selectedNotifyIndex = i;

                float startX = contentRect.x + (contentRect.width * notify.startFrame / totalFrames);
                float endX = contentRect.x + (contentRect.width * notify.endFrame / totalFrames);

                // 인스턴스 모드: 항상 Move만 (리사이즈 불가)
                // 스테이트 모드: 가장자리 6px → 리사이즈, 그 외 → 이동
                if (notify.isInstance)
                {
                    currentDragMode = DragMode.Move;
                }
                else
                {
                    float distToLeft = e.mousePosition.x - startX;
                    float distToRight = endX - e.mousePosition.x;
                    float handleZone = 6f;

                    // 블록이 좁을 때: 왼쪽/오른쪽 중 더 가까운 쪽 우선
                    bool nearLeft = distToLeft < handleZone;
                    bool nearRight = distToRight < handleZone;

                    if (nearLeft && nearRight)
                    {
                        // 양쪽 핸들 영역이 겹침 → 더 가까운 쪽 선택
                        currentDragMode = (distToLeft <= distToRight)
                            ? DragMode.ResizeLeft
                            : DragMode.ResizeRight;
                    }
                    else if (nearLeft)
                        currentDragMode = DragMode.ResizeLeft;
                    else if (nearRight)
                        currentDragMode = DragMode.ResizeRight;
                    else
                        currentDragMode = DragMode.Move;
                }

                dragNotifyIndex = i;
                dragStartFrame = notify.startFrame;
                dragEndFrame = notify.endFrame;
                dragMouseStartX = e.mousePosition.x;

                PushUndoSnapshot();
                e.Use();
                Repaint();
                return;
            }

            // 빈 영역 클릭 = 노티파이 선택 해제 + 프레임 스크러빙
            selectedNotifyIndex = -1;
            previewFrame = Mathf.Clamp(mouseFrame, 0, totalFrames);
            isPreviewPlaying = false;
            e.Use();
            Repaint();
        }

        private void HandleTimelineDrag(Rect contentRect, ActionEntry action, int totalFrames, Event e, float framesPerPixel)
        {
            if (dragNotifyIndex < 0 || dragNotifyIndex >= (action.notifies?.Length ?? 0)) return;

            var notify = action.notifies[dragNotifyIndex];
            float deltaX = e.mousePosition.x - dragMouseStartX;
            int deltaFrames = Mathf.RoundToInt(deltaX * framesPerPixel);

            switch (currentDragMode)
            {
                case DragMode.Move:
                    int duration = dragEndFrame - dragStartFrame;
                    int newStart = Mathf.Max(0, dragStartFrame + deltaFrames);
                    notify.startFrame = newStart;
                    notify.endFrame = newStart + duration;
                    break;

                case DragMode.ResizeLeft:
                    notify.startFrame = Mathf.Clamp(dragStartFrame + deltaFrames, 0, notify.endFrame - 1);
                    break;

                case DragMode.ResizeRight:
                    notify.endFrame = Mathf.Max(notify.startFrame + 1, dragEndFrame + deltaFrames);
                    break;
            }

            isDirty = true;
            e.Use();
            Repaint();
        }

        private void HandleTimelineRightClick(Rect contentRect, ActionEntry action, int totalFrames, Event e)
        {
            float mouseFrame = ((e.mousePosition.x - contentRect.x) / contentRect.width) * totalFrames;
            int clickedFrame = Mathf.RoundToInt(mouseFrame);
            int clickedTrack = Mathf.Clamp(
                Mathf.FloorToInt((e.mousePosition.y - contentRect.y) / TrackHeight), 0, trackCount - 1);

            // ── 검색 가능한 팝업 항목 구성 ──
            var items = new List<NotifySearchPopup.PopupItem>();

            // ── STARTUP 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "STARTUP 추가",
                SearchTag = "startup 선딜 시작 전딜 windup",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateStartup(clickedFrame, clickedFrame + 5, action.moveSpeed))
            });

            // ── COLLISION 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "COLLISION 추가",
                SearchTag = "collision 히트박스 히트 판정 active 액티브 hitbox attack 공격",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCollision(clickedFrame, clickedFrame + 8))
            });

            items.Add(new NotifySearchPopup.PopupItem { IsSeparator = true });

            // ── CANCEL_WINDOW 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 전체 캔슬",
                SearchTag = "cancel window 캔슬 윈도우 전체 all 공격 이동 회피 카운터",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10))
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 공격만",
                SearchTag = "cancel window 캔슬 윈도우 공격 attack skill 스킬 콤보",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: true, move: false, dodge: false, counter: false))
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 회피만",
                SearchTag = "cancel window 캔슬 윈도우 회피 dodge 구르기 대시",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: false, dodge: true, counter: false))
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 카운터만",
                SearchTag = "cancel window 캔슬 윈도우 카운터 counter 반격 패리 parry",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: false, dodge: false, counter: true))
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 이동만",
                SearchTag = "cancel window 캔슬 윈도우 이동 move 무브 걷기 달리기",
                OnSelected = () => AddNotify(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: true, dodge: false, counter: false))
            });

            items.Add(new NotifySearchPopup.PopupItem { IsSeparator = true });

            // ── 유틸리티 ──
            if (selectedNotifyIndex >= 0 && selectedNotifyIndex < (action.notifies?.Length ?? 0))
            {
                var selNotify = action.notifies[selectedNotifyIndex];
                items.Add(new NotifySearchPopup.PopupItem
                {
                    Label = $"선택된 노티파이 삭제 ({selNotify.type})",
                    SearchTag = "delete 삭제 제거 remove",
                    OnSelected = DeleteSelectedNotify
                });
            }

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "레거시 → 노티파이 변환",
                SearchTag = "legacy 레거시 변환 마이그레이션 migrate convert startup active recovery",
                OnSelected = () => MigrateToNotifies(action)
            });

            NotifySearchPopup.Show(items);
            e.Use();
        }

        private void AddNotify(ActionEntry action, ActionNotify notify)
        {
            PushUndoSnapshot();
            if (action.notifies == null)
                action.notifies = new ActionNotify[0];
            var list = new List<ActionNotify>(action.notifies);
            list.Add(notify);
            action.notifies = list.ToArray();
            selectedNotifyIndex = action.notifies.Length - 1;
            isDirty = true;
            Repaint();
        }

        /// <summary>레거시 startup/active/recovery → 노티파이 3개로 자동 변환</summary>
        private void MigrateToNotifies(ActionEntry action)
        {
            PushUndoSnapshot();

            var list = new List<ActionNotify>();

            if (action.startup > 0)
                list.Add(ActionNotify.CreateStartup(0, action.startup, action.moveSpeed));

            if (action.active > 0)
                list.Add(ActionNotify.CreateCollision(action.startup, action.startup + action.active));

            if (action.recovery > 0)
            {
                int cancelStart = action.startup + action.active +
                    Mathf.RoundToInt(action.recovery * action.cancelRatio);
                int end = action.TotalFrames;
                if (cancelStart < end)
                {
                    list.Add(ActionNotify.CreateCancelWindow(cancelStart, end,
                        skill: true, move: true, dodge: true, counter: false));
                }
            }

            action.notifies = list.ToArray();
            isDirty = true;

            Debug.Log($"[ActionTableEditor] '{action.id}' 레거시→노티파이 변환 완료: {list.Count}개 노티파이 생성");
            Repaint();
        }

        // ═══════════════════════════════════════════════════════
        //  애니메이션 프리뷰 (프리뷰 렌더 부분만, 기존 유지)
        // ═══════════════════════════════════════════════════════

        private void DrawAnimationPreview(ActionEntry action)
        {
            AnimationClip clip = FindClipForAction(action);
            if (clip == null)
            {
                EditorGUILayout.HelpBox(
                    $"클립 '{action.clip}'을 프로젝트에서 찾을 수 없습니다.",
                    MessageType.Warning);
                return;
            }

            // 프리뷰 렌더 영역
            float previewHeight = 200f;
            Rect previewRect = GUILayoutUtility.GetRect(0, previewHeight, GUILayout.ExpandWidth(true));
            if (previewRect.width < 10 || previewRect.height < 10) return;

            EnsurePreviewSetup();
            if (previewRender == null) return;

            SamplePreviewAnimation(clip, action);
            HandlePreviewMouseInput(previewRect);

            previewRender.BeginPreview(previewRect, GUIStyle.none);

            float camHeight = 1.0f;
            Vector3 camTarget = new Vector3(0, camHeight, 0);
            float yRad = previewRotationY * Mathf.Deg2Rad;
            float xRad = previewPitchX * Mathf.Deg2Rad;
            Vector3 camPos = camTarget + new Vector3(
                Mathf.Sin(yRad) * Mathf.Cos(xRad) * previewCamDistance,
                Mathf.Sin(xRad) * previewCamDistance,
                Mathf.Cos(yRad) * Mathf.Cos(xRad) * previewCamDistance);

            previewRender.camera.transform.position = camPos;
            previewRender.camera.transform.LookAt(camTarget);
            previewRender.camera.nearClipPlane = 0.1f;
            previewRender.camera.farClipPlane = 50f;
            previewRender.lights[0].transform.rotation = Quaternion.Euler(50, -30 + previewRotationY, 0);
            previewRender.lights[0].intensity = 1.2f;
            previewRender.camera.Render();

            Texture resultTex = previewRender.EndPreview();
            GUI.DrawTexture(previewRect, resultTex, ScaleMode.StretchToFill, false);

            // 테두리
            Color borderColor = new Color(0.3f, 0.3f, 0.3f);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, previewRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.yMax - 1, previewRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(previewRect.x, previewRect.y, 1, previewRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(previewRect.xMax - 1, previewRect.y, 1, previewRect.height), borderColor);

            // 오버레이 정보
            int currentFrame = Mathf.RoundToInt(previewFrame);
            int totalF = GetEffectiveTotalFrames(action);
            string phaseLabel = GetPhaseLabel(currentFrame, action);
            string overlayText = $"F:{currentFrame}/{totalF}  {phaseLabel}";

            if (overlayWhiteStyle == null)
            {
                overlayWhiteStyle = new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = Color.white }, fontSize = 12, alignment = TextAnchor.UpperRight };
                overlayShadowStyle = new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = new Color(0, 0, 0, 0.7f) }, fontSize = 12, alignment = TextAnchor.UpperRight };
            }
            GUI.Label(new Rect(previewRect.x + 1, previewRect.y + 1, previewRect.width - 6, 20),
                overlayText, overlayShadowStyle);
            GUI.Label(new Rect(previewRect.x, previewRect.y, previewRect.width - 5, 20),
                overlayText, overlayWhiteStyle);
        }

        private void DrawPreviewControls(AnimationClip clip, int totalFrames)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("⏮", GUILayout.Width(28), GUILayout.Height(20)))
            { previewFrame = 0; isPreviewPlaying = false; }

            if (GUILayout.Button("◀", GUILayout.Width(28), GUILayout.Height(20)))
            { previewFrame = Mathf.Max(0, previewFrame - 1); isPreviewPlaying = false; }

            string playLabel = isPreviewPlaying ? "⏸" : "▶";
            if (GUILayout.Button(playLabel, GUILayout.Width(36), GUILayout.Height(20)))
            {
                isPreviewPlaying = !isPreviewPlaying;
                if (isPreviewPlaying)
                {
                    lastPlayTime = EditorApplication.timeSinceStartup;
                    if (previewFrame >= totalFrames) previewFrame = 0;
                }
            }

            if (GUILayout.Button("▶", GUILayout.Width(28), GUILayout.Height(20)))
            { previewFrame = Mathf.Min(totalFrames, previewFrame + 1); isPreviewPlaying = false; }

            if (GUILayout.Button("⏭", GUILayout.Width(28), GUILayout.Height(20)))
            { previewFrame = totalFrames; isPreviewPlaying = false; }

            GUILayout.Space(8);

            EditorGUILayout.LabelField("Frame:", GUILayout.Width(38));
            int inputFrame = EditorGUILayout.IntField(Mathf.RoundToInt(previewFrame), GUILayout.Width(36));
            previewFrame = Mathf.Clamp(inputFrame, 0, totalFrames);

            GUILayout.Space(6);

            // Speed
            EditorGUILayout.LabelField("Speed:", GUILayout.Width(38));
            int speedIdx = -1;
            for (int i = 0; i < SpeedPresets.Length; i++)
            {
                if (Mathf.Approximately(playbackSpeed, SpeedPresets[i]))
                { speedIdx = i; break; }
            }
            int newSpeedIdx = GUILayout.Toolbar(speedIdx, SpeedLabels, GUILayout.Width(120), GUILayout.Height(18));
            if (newSpeedIdx != speedIdx && newSpeedIdx >= 0)
                playbackSpeed = SpeedPresets[newSpeedIdx];

            GUILayout.Space(6);

            // View
            EditorGUILayout.LabelField("View:", GUILayout.Width(32));
            int newView = GUILayout.Toolbar(selectedViewIndex, ViewLabels, GUILayout.Width(200), GUILayout.Height(18));
            if (newView != selectedViewIndex)
            {
                selectedViewIndex = newView;
                switch (selectedViewIndex)
                {
                    case 0: previewRotationY = 180f; previewPitchX = 0f; break;
                    case 1: previewRotationY = 90f;  previewPitchX = 0f; break;
                    case 2: previewRotationY = 270f; previewPitchX = 0f; break;
                    case 3: previewPitchX = 75f; break;
                    case 4: previewPitchX = -45f; break;
                }
            }

            GUILayout.Space(4);

            if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(18)))
            {
                previewRotationY = DefaultRotationY;
                previewPitchX = DefaultPitchX;
                previewCamDistance = DefaultCamDistance;
                playbackSpeed = DefaultPlaybackSpeed;
                loopMode = DefaultLoopMode;
                selectedViewIndex = 1;
                previewFrame = 0;
                isPreviewPlaying = false;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ─── 프리뷰 내부 헬퍼 (기존 유지) ───

        private void EnsurePreviewSetup()
        {
            if (previewRender != null && previewInstance != null) return;
            CleanupPreview();

            previewRender = new PreviewRenderUtility();
            previewRender.camera.fieldOfView = 30f;
            previewRender.camera.clearFlags = CameraClearFlags.SolidColor;
            previewRender.camera.backgroundColor = new Color(0.15f, 0.15f, 0.2f);

            GameObject modelPrefab = FindPreviewModel();
            if (modelPrefab != null)
            {
                previewInstance = previewRender.InstantiatePrefabInScene(modelPrefab);
                previewInstance.transform.position = Vector3.zero;
                previewInstance.transform.rotation = Quaternion.Euler(0, 90, 0);
                previewInstance.transform.localScale = Vector3.one;

                previewAnimator = previewInstance.GetComponentInChildren<Animator>();
                if (previewAnimator != null)
                {
                    previewAnimator.enabled = false;
                    previewAnimator.runtimeAnimatorController = null;
                }

                int previewLayer = 1;
                SetLayerRecursiveForPreview(previewInstance, previewLayer);
                previewRender.camera.cullingMask = 1 << previewLayer;
            }
        }

        private void CleanupPreview()
        {
            isPreviewPlaying = false;
            if (AnimationMode.InAnimationMode())
            {
                try { AnimationMode.StopAnimationMode(); } catch { }
            }
            if (previewInstance != null) { DestroyImmediate(previewInstance); previewInstance = null; }
            previewAnimator = null;
            if (previewRender != null) { previewRender.Cleanup(); previewRender = null; }
            currentPreviewClip = null;
            cachedClipName = "";
        }

        private GameObject FindPreviewModel()
        {
            string[] guids = AssetDatabase.FindAssets("EEJANAIbot t:Model");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));

            guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && importer.animationType == ModelImporterAnimationType.Human)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        private AnimationClip FindClipForAction(ActionEntry action)
        {
            if (string.IsNullOrEmpty(action.clip)) return null;
            if (action.clip == cachedClipName && currentPreviewClip != null)
                return currentPreviewClip;

            cachedClipName = action.clip;

            string[] guids = AssetDatabase.FindAssets($"{action.clip} t:AnimationClip");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var sub in subAssets)
                {
                    if (sub is AnimationClip aClip && !aClip.name.StartsWith("__preview__"))
                    {
                        if (aClip.name == action.clip || aClip.name.Contains(action.clip))
                        { currentPreviewClip = aClip; return aClip; }
                    }
                }
            }

            string[] allFbxGuids = AssetDatabase.FindAssets("t:Model",
                new[] { "Assets/EEJANAI_Team", "Assets/_Project", "Assets/Martial Art Animations Sample" });
            foreach (var guid in allFbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var sub in subAssets)
                {
                    if (sub is AnimationClip aClip && !aClip.name.StartsWith("__preview__") && aClip.name == action.clip)
                    { currentPreviewClip = aClip; return aClip; }
                }
            }

            currentPreviewClip = null;
            return null;
        }

        private void SamplePreviewAnimation(AnimationClip clip, ActionEntry action)
        {
            if (previewInstance == null || clip == null) return;
            try
            {
                int totalFrames = Mathf.Max(GetEffectiveTotalFrames(action), 1);
                float normalizedTime = Mathf.Clamp01(previewFrame / totalFrames);
                float sampleTime = normalizedTime * clip.length;

                if (!AnimationMode.InAnimationMode())
                    AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(previewInstance, clip, sampleTime);
                AnimationMode.EndSampling();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ActionTableEditor] 샘플링 오류: {e.Message}");
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            }
        }

        private void HandlePreviewMouseInput(Rect previewRect)
        {
            Event e = Event.current;
            if (!previewRect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    isDraggingPreview = true;
                    previewDragStart = e.mousePosition;
                    e.Use();
                    break;
                case EventType.MouseDrag when isDraggingPreview:
                    previewRotationY += (e.mousePosition.x - previewDragStart.x) * 0.5f;
                    previewPitchX = Mathf.Clamp(previewPitchX - (e.mousePosition.y - previewDragStart.y) * 0.3f, -80f, 80f);
                    previewDragStart = e.mousePosition;
                    selectedViewIndex = -1;
                    Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    isDraggingPreview = false;
                    e.Use();
                    break;
                case EventType.ScrollWheel:
                    previewCamDistance = Mathf.Clamp(previewCamDistance + e.delta.y * 0.1f, 1.5f, 8f);
                    Repaint();
                    e.Use();
                    break;
            }
        }

        private static void SetLayerRecursiveForPreview(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        private string GetPhaseLabel(int frame, ActionEntry action)
        {
            // 노티파이 기반이면 활성 노티파이 표시
            if (action.HasNotifies)
            {
                var startup = action.GetActiveNotify(frame, NotifyType.STARTUP);
                if (startup != null) return "[Startup]";
                var collision = action.GetActiveNotify(frame, NotifyType.COLLISION);
                if (collision != null) return "[Active]";
                var cancel = action.GetActiveNotify(frame, NotifyType.CANCEL_WINDOW);
                if (cancel != null) return "[Cancel]";
                return "[—]";
            }

            // 레거시
            if (frame < action.startup) return "[Startup]";
            if (frame < action.startup + action.active) return "[Active]";
            if (frame < action.TotalFrames) return "[Recovery]";
            return "[End]";
        }

        // ═══════════════════════════════════════════════════════
        //  캔슬 경로 + 태그 편집 (기존 유지)
        // ═══════════════════════════════════════════════════════

        private void DrawCancelRoutes(ActionEntry action)
        {
            if (action.cancels == null) action.cancels = new CancelRoute[0];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("→ Next", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("", GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            int removeIndex = -1;
            for (int i = 0; i < action.cancels.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                int currentIdx = System.Array.IndexOf(InputTypeOptions, action.cancels[i].input);
                if (currentIdx < 0) currentIdx = 0;
                int newIdx = EditorGUILayout.Popup(currentIdx, InputTypeOptions, GUILayout.Width(80));
                action.cancels[i].input = InputTypeOptions[newIdx];
                action.cancels[i].next = EditorGUILayout.TextField(action.cancels[i].next);
                if (GUILayout.Button("✕", GUILayout.Width(20))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                PushUndoSnapshot();
                var list = new List<CancelRoute>(action.cancels);
                list.RemoveAt(removeIndex);
                action.cancels = list.ToArray();
                isDirty = true;
            }

            if (GUILayout.Button("+ Add Cancel", GUILayout.Width(120)))
            {
                PushUndoSnapshot();
                var list = new List<CancelRoute>(action.cancels);
                list.Add(new CancelRoute { input = "Attack", next = "" });
                action.cancels = list.ToArray();
                isDirty = true;
            }
        }

        private void DrawTags(ActionEntry action)
        {
            if (action.tags == null) action.tags = new string[0];

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < action.tags.Length; i++)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.3f, 0.3f);
                EditorGUILayout.BeginHorizontal("box", GUILayout.Width(0));
                GUI.backgroundColor = Color.white;

                action.tags[i] = EditorGUILayout.TextField(action.tags[i], GUILayout.Width(60));
                if (GUILayout.Button("✕", GUILayout.Width(16), GUILayout.Height(16)))
                {
                    PushUndoSnapshot();
                    var list = new List<string>(action.tags);
                    list.RemoveAt(i);
                    action.tags = list.ToArray();
                    isDirty = true;
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+", GUILayout.Width(20), GUILayout.Height(18)))
            {
                PushUndoSnapshot();
                var list = new List<string>(action.tags);
                list.Add("new");
                action.tags = list.ToArray();
                isDirty = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════
        //  파일 관리 (기존 유지)
        // ═══════════════════════════════════════════════════════

        private void RefreshFileList()
        {
            if (!Directory.Exists(TableFolderPath)) Directory.CreateDirectory(TableFolderPath);
            var files = Directory.GetFiles(TableFolderPath, "*.json");
            actorFiles = files;
            actorFileNames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
            if (actorFiles.Length > 0 && selectedActorIndex < 0) selectedActorIndex = 0;
            if (selectedActorIndex >= actorFiles.Length) selectedActorIndex = actorFiles.Length - 1;
        }

        private void LoadSelectedActor()
        {
            if (actorFiles == null || selectedActorIndex < 0 || selectedActorIndex >= actorFiles.Length)
            { currentTable = null; return; }
            currentTable = ActionTableManager.LoadFromFile(actorFiles[selectedActorIndex]);
            selectedActionIndex = currentTable?.actions?.Length > 0 ? 0 : -1;
            selectedNotifyIndex = -1;
            isDirty = false;
            ResetUndoHistory();
            Repaint();
        }

        private void SaveCurrentTable()
        {
            if (currentTable == null || actorFiles == null || selectedActorIndex < 0) return;
            ActionTableManager.SaveToFile(currentTable, actorFiles[selectedActorIndex]);
            currentTable.InvalidateCache();
            currentTable.BuildMap();
            isDirty = false;
            lastSnapshotJson = JsonUtility.ToJson(currentTable, false);
            AssetDatabase.Refresh();
            Debug.Log($"[ActionTableEditor] Saved: {actorFileNames[selectedActorIndex]}");
        }

        private void CreateNewActorFile()
        {
            string actorId = "NewActor";
            string path = Path.Combine(TableFolderPath, $"{actorId}.json");
            int counter = 1;
            while (File.Exists(path))
            {
                actorId = $"NewActor_{counter++}";
                path = Path.Combine(TableFolderPath, $"{actorId}.json");
            }

            var newTable = new ActorActionTable
            {
                actorId = actorId,
                actorName = "새 액터",
                actions = new ActionEntry[]
                {
                    new ActionEntry
                    {
                        id = "Action1", name = "액션 1", clip = "",
                        startup = 5, active = 8, recovery = 12,
                        playbackRate = 1f, cancelRatio = 0.5f, moveSpeed = 2f,
                        cancels = new CancelRoute[0], tags = new string[0],
                        notifies = new ActionNotify[0]
                    }
                }
            };

            string json = JsonUtility.ToJson(newTable, true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();

            RefreshFileList();
            for (int i = 0; i < actorFileNames.Length; i++)
            {
                if (actorFileNames[i] == actorId)
                { selectedActorIndex = i; break; }
            }
            LoadSelectedActor();
        }

        private void AddNewAction()
        {
            if (currentTable == null) return;
            PushUndoSnapshot();

            var list = currentTable.actions != null
                ? new List<ActionEntry>(currentTable.actions)
                : new List<ActionEntry>();

            int num = list.Count + 1;
            list.Add(new ActionEntry
            {
                id = $"Action{num}", name = $"새 액션 {num}", clip = "",
                startup = 5, active = 8, recovery = 12,
                playbackRate = 1f, cancelRatio = 0.5f, moveSpeed = 2f,
                cancels = new CancelRoute[0], tags = new string[0],
                notifies = new ActionNotify[0]
            });

            currentTable.actions = list.ToArray();
            currentTable.InvalidateCache();
            currentTable.BuildMap();
            selectedActionIndex = list.Count - 1;
            selectedNotifyIndex = -1;
            isDirty = true;
        }

        private void RemoveSelectedAction()
        {
            if (currentTable?.actions == null || selectedActionIndex < 0) return;
            PushUndoSnapshot();

            var list = new List<ActionEntry>(currentTable.actions);
            list.RemoveAt(selectedActionIndex);
            currentTable.actions = list.ToArray();
            currentTable.InvalidateCache();
            currentTable.BuildMap();

            if (selectedActionIndex >= list.Count) selectedActionIndex = list.Count - 1;
            selectedNotifyIndex = -1;
            isDirty = true;
        }

        private void SwapActions(int a, int b)
        {
            var temp = currentTable.actions[a];
            currentTable.actions[a] = currentTable.actions[b];
            currentTable.actions[b] = temp;
            isDirty = true;
        }

        // ═══════════════════════════════════════════════════════
        //  유틸
        // ═══════════════════════════════════════════════════════

        private Color GetActionColor(ActionEntry action)
        {
            if (action.HasTag("light")) return new Color(0.3f, 0.9f, 0.3f);
            if (action.HasTag("heavy")) return new Color(0.9f, 0.6f, 0.2f);
            if (action.HasTag("dodge")) return new Color(0.3f, 0.6f, 0.9f);
            if (action.HasTag("counter")) return new Color(0.9f, 0.9f, 0.3f);
            if (action.HasTag("execution")) return new Color(0.9f, 0.3f, 0.9f);
            if (action.HasTag("huxley")) return new Color(0.3f, 0.9f, 0.9f);
            return new Color(0.6f, 0.6f, 0.6f);
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
