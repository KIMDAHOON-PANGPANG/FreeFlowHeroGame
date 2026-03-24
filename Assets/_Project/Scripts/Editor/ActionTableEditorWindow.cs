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
        private int selectedActorIndex = -1;  // ★ -1로 초기화해야 디폴트 PC_Hero 선택 로직 작동
        private ActorActionTable currentTable;
        private int selectedActionIndex = -1;
        private int lastRenderedActionIndex = -1;  // 타임라인 갱신 감지용
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

        // ─── 히트박스 2D 시각화 ───
        private GameObject hitboxCubeObj;       // 프리뷰 씬 내 히트박스 Quad (2D 평면)
        private Material hitboxMaterial;         // 반투명 빨간 머티리얼
        private Material hitboxWireMaterial;     // 와이어프레임 머티리얼
        private bool isHitboxSelected;           // 히트박스가 클릭으로 선택되었는지
        private bool isHitboxDragging;           // 히트박스 드래그 중 여부
        private Vector2 hitboxDragStartMouse;    // 드래그 시작 마우스 위치
        private Vector3 hitboxDragStartOffset;   // 드래그 시작 시 오프셋 값

        // ─── 히트박스 트랜스폼 모드 (W/E/R 단축키) ───
        private enum HitboxGizmoMode { Move, Rotate, Scale }
        private HitboxGizmoMode hitboxGizmoMode = HitboxGizmoMode.Move;
        private float previewFrame = 0f;
        private bool isPreviewPlaying;
        private double lastPlayTime;

        private const float DefaultCamDistance = 3.5f;
        private const float DefaultPlaybackSpeed = 1.0f;
        private const int DefaultLoopMode = 0;

        // 2D 고정 뷰: 회전 없음, 줌과 패닝만 사용
        private float previewCamDistance = DefaultCamDistance;
        private Vector2 previewPanOffset = Vector2.zero; // 2D 뷰 패닝 오프셋 (Z, Y)
        private GUIStyle overlayWhiteStyle;
        private GUIStyle overlayShadowStyle;
        private Vector2 previewDragStart;
        private bool isDraggingPreview;
        private int loopMode = DefaultLoopMode;
        private static readonly string[] LoopModeLabels = { "전체", "Startup", "Active", "Recovery" };
        private float playbackSpeed = DefaultPlaybackSpeed;

        // (View 전환 UI 제거됨 — 2D 고정 뷰)
        private static readonly float[] SpeedPresets = { 0.5f, 1.0f, 2.0f };
        private static readonly string[] SpeedLabels = { "0.5x", "1.0x", "2.0x" };

        // ─── 타임라인 ───
        private const int MinTracks = 1;
        private const int MaxTracksLimit = 10;
        private int trackCount = 2;   // 동적 트랙 수 (1~10), 기본 2줄
        private const float TrackHeight = 26f;
        private const float TimelineHeaderWidth = 100f;
        private const float TimeRulerHeight = 28f;  // 프레임 번호 + 시간(초) 2줄 표시
        private const float TimelinePadding = 24f;  // 타임라인 좌우 여백 (눈금 라벨이 잘리지 않도록)
        private Vector2 timelineScroll;
        private float timelineZoom = 4.0f;  // Ctrl+휠 줌 배율 (0.5~4.0), 기본값=최대 확대
        private int selectedNotifyIndex = -1;  // 선택된 노티파이 인덱스 (-1=미선택)

        // ★ 고정 타임라인 길이: 노티파이 endFrame 변경 시에도 타임라인 스케일이 변하지 않도록 함
        //   클립 길이 또는 레거시 TotalFrames를 기준으로 설정, 노티파이가 넘어가면 자동 확장
        private int fixedTimelineFrames = 0;   // 0이면 아직 미설정 → 자동 계산

        // ★ 프레임 ↔ 초 표시 토글
        private bool showTimeAsSeconds = false;

        // 트랙 활성 상태 (에디터 전용, 비활성 트랙의 노티파이는 disabled 처리)
        private bool[] trackEnabled = { true, true, true, true, true, true, true, true, true, true };

        // 드래그 상태
        private enum DragMode { None, Move, ResizeLeft, ResizeRight, Scrub }
        private DragMode currentDragMode = DragMode.None;
        private int dragNotifyIndex = -1;
        private int dragStartFrame;
        private int dragEndFrame;
        private float dragMouseStartX;

        // ─── 리사이즈 가능 패널 크기 ───
        private float leftPanelWidth = 220f;       // 좌측 액션 목록
        private float rightPanelWidth = 280f;      // 우측 인스펙터
        private float previewHeight = 500f;         // 프리뷰 높이 (기본값=최대)

        private const float MinPanelWidth = 120f;
        private const float MaxPanelWidth = 500f;
        private const float MinPreviewHeight = 80f;
        private const float MaxPreviewHeight = 500f;
        private const float SplitterSize = 4f;

        private enum SplitterDrag { None, Left, Right, PreviewBottom }
        private SplitterDrag activeSplitter = SplitterDrag.None;

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
            // ★ 디폴트 액터 자동 로드 (PC_Hero 우선)
            if (currentTable == null && actorFiles != null && actorFiles.Length > 0)
            {
                LoadSelectedActor();
            }
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

        // ★ 클립 프레임 캐시: FindClipForAction은 AssetDatabase 검색이라 매우 무거움.
        //   액션 ID → 클립 프레임 수를 캐싱하여 프레임당 수십 회 호출되어도 성능 문제 없음.
        private readonly Dictionary<string, int> clipFramesCache = new Dictionary<string, int>();

        /// <summary>캐시 무효화 (액션 변경, 클립 변경 시 호출)</summary>
        private void InvalidateClipFramesCache()
        {
            clipFramesCache.Clear();
        }

        /// <summary>특정 액션의 캐시만 무효화</summary>
        private void InvalidateClipFramesCache(string actionId)
        {
            if (actionId != null) clipFramesCache.Remove(actionId);
        }

        /// <summary>
        /// 액션의 총 재생 프레임 수.
        /// ★ 클립 길이 우선: 노티파이 endFrame을 줄여도 액션 총 길이는 변하지 않음.
        ///   1순위: 애니메이션 클립 길이 (60fps 환산, 캐싱됨)
        ///   2순위: 레거시 TotalFrames (startup + active + recovery)
        ///   3순위: 노티파이 범위 (폴백)
        /// </summary>
        private int GetEffectiveTotalFrames(ActionEntry action)
        {
            // 1순위: 캐시된 클립 프레임 조회
            string key = action.id ?? "";
            if (clipFramesCache.TryGetValue(key, out int cached))
            {
                if (cached > 0) return cached;
            }
            else
            {
                // 캐시 미스: 클립 검색 (1회만 실행)
                AnimationClip clip = FindClipForAction(action);
                int clipFrames = 0;
                if (clip != null)
                    clipFrames = Mathf.CeilToInt(clip.length * 60f);
                clipFramesCache[key] = clipFrames;
                if (clipFrames > 0) return clipFrames;
            }

            // 2순위: 레거시 TotalFrames
            if (action.TotalFrames > 0)
                return action.TotalFrames;

            // 3순위: 노티파이 범위 (폴백)
            return action.HasNotifies ? action.NotifyTotalFrames : 1;
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

        // ★ 데이터 튜닝: 액션 길이가 타임라인에서 차지하는 기본 비율 (0.8 = 80%)
        private const float TimelineFillRatio = 0.8f;

        /// <summary>
        /// 액션이 바뀔 때 호출: 클립 길이 또는 레거시 프레임으로 고정 타임라인 길이 설정.
        /// 액션 길이가 타임라인의 약 80%를 차지하도록 자동 스케일링한다.
        /// </summary>
        private void UpdateFixedTimelineFrames(ActionEntry action)
        {
            int actionFrames = 0;

            // 1순위: 실제 애니메이션 클립 길이
            AnimationClip clip = FindClipForAction(action);
            if (clip != null)
            {
                float rate = action.playbackRate > 0f ? action.playbackRate : 1f;
                actionFrames = Mathf.CeilToInt(clip.length * 60f / rate);
            }

            // 2순위: 레거시 TotalFrames (startup + active + recovery)
            if (actionFrames <= 0 && action.TotalFrames > 0)
            {
                actionFrames = action.TotalFrames;
            }

            // 3순위: 현재 노티파이 범위
            if (actionFrames <= 0)
            {
                actionFrames = GetEffectiveTotalFrames(action);
            }

            actionFrames = Mathf.Max(actionFrames, 1);

            // 액션 길이가 타임라인의 80%를 차지하도록 총 길이 계산
            fixedTimelineFrames = Mathf.CeilToInt(actionFrames / TimelineFillRatio);
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

            // ═══ 스플리터 드래그 처리 ═══
            HandleSplitterDrag();

            // ═══ 메인 3컬럼 레이아웃 ═══
            EditorGUILayout.BeginHorizontal();

            // ── 좌측: 액션 목록 ──
            DrawActionList();

            // ── 좌측 스플리터 ──
            DrawVerticalSplitter(SplitterDrag.Left);

            // ── 중앙: 프리뷰(상단) + 타임라인(하단) ──
            DrawCenterPanel();

            // ── 우측 스플리터 ──
            DrawVerticalSplitter(SplitterDrag.Right);

            // ── 우측: 인스펙터 ──
            DrawInspectorPanel();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>세로 스플리터 바 렌더링 + 커서 설정</summary>
        private void DrawVerticalSplitter(SplitterDrag id)
        {
            Rect splitterRect = GUILayoutUtility.GetRect(SplitterSize, SplitterSize,
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(splitterRect, new Color(0.15f, 0.15f, 0.15f));
            // 중앙에 얇은 밝은 선
            EditorGUI.DrawRect(new Rect(splitterRect.x + SplitterSize * 0.5f - 0.5f,
                splitterRect.y, 1, splitterRect.height), new Color(0.35f, 0.35f, 0.35f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && splitterRect.Contains(e.mousePosition))
            {
                activeSplitter = id;
                e.Use();
            }
        }

        /// <summary>수평 스플리터 바 (프리뷰↔타임라인) — 드래그로 프리뷰 높이 조절</summary>
        private int horizontalSplitterControlId;
        private void DrawHorizontalSplitter()
        {
            // 히트 영역은 넓게 (10px), 시각은 중앙 선 (2px)
            const float hitHeight = 10f;
            Rect splitterRect = GUILayoutUtility.GetRect(0, hitHeight, GUILayout.ExpandWidth(true));
            horizontalSplitterControlId = GUIUtility.GetControlID(FocusType.Passive);

            // 배경
            EditorGUI.DrawRect(splitterRect, new Color(0.13f, 0.13f, 0.13f));

            // 호버/드래그 시 하이라이트
            Event e = Event.current;
            bool isActive = GUIUtility.hotControl == horizontalSplitterControlId;
            bool isHovered = splitterRect.Contains(e.mousePosition);
            Color lineColor = isActive ? new Color(0.4f, 0.6f, 1f, 0.9f)  // 드래그 중: 파랑
                : isHovered ? new Color(0.5f, 0.5f, 0.5f, 0.8f)            // 호버: 밝은 회색
                : new Color(0.3f, 0.3f, 0.3f, 0.6f);                       // 기본: 어두운 선

            // 중앙 가로선 (2px)
            float lineY = splitterRect.y + hitHeight * 0.5f - 1f;
            EditorGUI.DrawRect(new Rect(splitterRect.x, lineY, splitterRect.width, 2), lineColor);

            // 드래그 핸들 점 (중앙 5개)
            if (isHovered || isActive)
            {
                float cx = splitterRect.center.x;
                float dotY = splitterRect.y + hitHeight * 0.5f - 1f;
                Color dotColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
                for (int i = -2; i <= 2; i++)
                {
                    EditorGUI.DrawRect(new Rect(cx + i * 8 - 1, dotY, 3, 3), dotColor);
                }
            }

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            // ── hotControl 기반 드래그 (IMGUI 표준 패턴, 다른 컨트롤에 이벤트 빼앗기지 않음) ──
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && splitterRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = horizontalSplitterControlId;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == horizontalSplitterControlId)
                    {
                        previewHeight += e.delta.y;
                        previewHeight = Mathf.Clamp(previewHeight, MinPreviewHeight, MaxPreviewHeight);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == horizontalSplitterControlId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }

            // 호버 시 리페인트 (하이라이트 갱신)
            if (isHovered && e.type == EventType.Repaint)
                Repaint();
        }

        /// <summary>세로 스플리터(좌우 패널) 드래그 중 패널 크기 조절</summary>
        private void HandleSplitterDrag()
        {
            Event e = Event.current;

            if (activeSplitter == SplitterDrag.None) return;

            if (e.type == EventType.MouseDrag)
            {
                switch (activeSplitter)
                {
                    case SplitterDrag.Left:
                        leftPanelWidth += e.delta.x;
                        leftPanelWidth = Mathf.Clamp(leftPanelWidth, MinPanelWidth, MaxPanelWidth);
                        break;
                    case SplitterDrag.Right:
                        rightPanelWidth -= e.delta.x;
                        rightPanelWidth = Mathf.Clamp(rightPanelWidth, MinPanelWidth, MaxPanelWidth);
                        break;
                }
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                activeSplitter = SplitterDrag.None;
                e.Use();
            }
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
            EditorGUILayout.BeginVertical(GUILayout.Width(leftPanelWidth));

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

            // ── 프리뷰↔타임라인 수평 스플리터 ──
            DrawHorizontalSplitter();

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

            // ── 재생 상태 바 (액션 실제 범위 기준으로 표시) ──
            int curFrame = Mathf.RoundToInt(previewFrame);
            int actionTotalF = GetEffectiveTotalFrames(action);
            string playState = isPreviewPlaying ? "▶ Playing" : "⏸ Paused";
            string timeInfo = showTimeAsSeconds
                ? $"{curFrame * CombatConstants.FrameDuration:F2}s / {actionTotalF * CombatConstants.FrameDuration:F2}s"
                : $"{curFrame} / {actionTotalF}f   ({curFrame / 60f:F2}s / {actionTotalF / 60f:F2}s)";
            EditorGUILayout.LabelField($"{playState}   {timeInfo}", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
        }

        // ═══════════════════════════════════════════════════════
        //  우측 패널: 인스펙터
        // ═══════════════════════════════════════════════════════

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(rightPanelWidth));
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
            string prevClip = action.clip;
            action.clip = EditorGUILayout.TextField("Animation Clip", action.clip);
            if (prevClip != action.clip)
                InvalidateClipFramesCache(action.id); // ★ 클립 변경 시 해당 액션 캐시 무효화

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
            action.playbackRate = EditorGUILayout.Slider("재생배율", action.playbackRate, 0.1f, 3.0f);
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
                float notifyRate1 = (action.playbackRate > 0f) ? action.playbackRate : 1f;
                string rateInfo1 = (Mathf.Abs(notifyRate1 - 1f) > 0.01f)
                    ? $"  | 모션:{notify.DurationTime / notifyRate1:F3}s (x{notifyRate1:F1})"
                    : "";
                EditorGUILayout.LabelField(
                    $"구간: {notify.Duration}f ({notify.DurationTime:F3}s)  [frame {notify.startFrame}~{notify.endFrame}]{rateInfo1}",
                    EditorStyles.miniLabel);
            }
            else
            {
                // ── 프레임(frame) 모드 ──
                notify.startFrame = EditorGUILayout.IntField("Start Frame", notify.startFrame);
                notify.endFrame = EditorGUILayout.IntField("End Frame", notify.endFrame);
                float notifyRate2 = (action.playbackRate > 0f) ? action.playbackRate : 1f;
                string rateInfo2 = (Mathf.Abs(notifyRate2 - 1f) > 0.01f)
                    ? $"  | 모션:{notify.DurationTime / notifyRate2:F3}s (x{notifyRate2:F1})"
                    : "";
                EditorGUILayout.LabelField(
                    $"구간: {notify.Duration}f ({notify.DurationTime:F3}s)  [{notify.StartTime:F3}s ~ {notify.EndTime:F3}s]{rateInfo2}",
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

                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("히트박스 트랜스폼", EditorStyles.boldLabel);

                    // 오프셋 (2D: X, Y만 — Z는 2D 횡스크롤이므로 제외)
                    EditorGUILayout.LabelField("Offset (캐릭터 기준)", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("X", GUILayout.Width(14));
                    notify.hitboxOffsetX = EditorGUILayout.FloatField(notify.hitboxOffsetX);
                    EditorGUILayout.LabelField("Y", GUILayout.Width(14));
                    notify.hitboxOffsetY = EditorGUILayout.FloatField(
                        notify.hitboxOffsetY == 0f ? ActionNotify.DefaultHitboxOffsetY : notify.hitboxOffsetY);
                    EditorGUILayout.EndHorizontal();

                    // 크기 (2D: X, Y만)
                    EditorGUILayout.LabelField("Size (박스 크기)", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("X", GUILayout.Width(14));
                    notify.hitboxSizeX = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                        notify.hitboxSizeX == 0f ? ActionNotify.DefaultHitboxSizeX : notify.hitboxSizeX));
                    EditorGUILayout.LabelField("Y", GUILayout.Width(14));
                    notify.hitboxSizeY = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                        notify.hitboxSizeY == 0f ? ActionNotify.DefaultHitboxSizeY : notify.hitboxSizeY));
                    EditorGUILayout.EndHorizontal();

                    // 기본값 리셋 버튼
                    if (GUILayout.Button("히트박스 기본값 리셋", GUILayout.Height(18)))
                    {
                        PushUndoSnapshot();
                        notify.ResetHitboxToDefaults();
                        isDirty = true;
                    }
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
            // ★ 고정 타임라인 길이 사용 — 액션 변경 또는 미설정 시 자동 갱신
            if (fixedTimelineFrames <= 0 || lastRenderedActionIndex != selectedActionIndex)
            {
                // ★ 디폴트: 최대 확대 (액션 노티파이 범위가 타임라인을 꽉 채움)
                int baseFrames = GetEffectiveTotalFrames(action);
                fixedTimelineFrames = Mathf.Max(baseFrames, 1);
                timelineZoom = 4.0f;

                lastRenderedActionIndex = selectedActionIndex;
            }
            int totalFrames = Mathf.Max(GetTimelineTotalFrames(action), 1);

            // 타임라인 전체 영역
            float timelineHeight = trackCount * TrackHeight + TimeRulerHeight + 4;
            Rect outerRect = GUILayoutUtility.GetRect(0, timelineHeight, GUILayout.ExpandWidth(true));

            if (outerRect.width < 50) return;

            // 배경
            EditorGUI.DrawRect(outerRect, new Color(0.13f, 0.13f, 0.13f));

            // 트랙 헤더 영역
            Rect headerRect = new Rect(outerRect.x, outerRect.y, TimelineHeaderWidth, outerRect.height);
            // 트랙 컨텐츠 영역 (좌우 패딩 포함)
            Rect contentRect = new Rect(outerRect.x + TimelineHeaderWidth, outerRect.y,
                outerRect.width - TimelineHeaderWidth, outerRect.height - TimeRulerHeight);
            // 실제 프레임이 그려지는 영역 (패딩 적용)
            Rect paddedRect = new Rect(contentRect.x + TimelinePadding, contentRect.y,
                contentRect.width - TimelinePadding * 2f, contentRect.height);
            // 시간축 영역
            Rect rulerRect = new Rect(contentRect.x, contentRect.yMax, contentRect.width, TimeRulerHeight);

            // ── 액션 실제 범위 vs 여분 구간 색상 구분 ──
            int effectiveFrames = GetEffectiveTotalFrames(action);
            if (totalFrames > 0 && paddedRect.width > 0)
            {
                // 액션 실제 범위 (0 ~ effectiveFrames): 약간 밝은 색조
                float actionEndX = paddedRect.x + (paddedRect.width * effectiveFrames / totalFrames);
                actionEndX = Mathf.Min(actionEndX, paddedRect.xMax);
                Rect activeZone = new Rect(paddedRect.x, paddedRect.y,
                    actionEndX - paddedRect.x, paddedRect.height);
                EditorGUI.DrawRect(activeZone, new Color(0.18f, 0.20f, 0.25f)); // 파란 틴트 활성 구간

                // 여분 구간 (effectiveFrames ~ totalFrames): 어두운 회색 + 빗금 느낌
                if (effectiveFrames < totalFrames)
                {
                    Rect inactiveZone = new Rect(actionEndX, paddedRect.y,
                        paddedRect.xMax - actionEndX, paddedRect.height);
                    EditorGUI.DrawRect(inactiveZone, new Color(0.10f, 0.10f, 0.10f)); // 더 어두운 회색

                    // 경계선 (액션 범위 끝 표시)
                    EditorGUI.DrawRect(new Rect(actionEndX - 1, paddedRect.y, 2, paddedRect.height),
                        new Color(0.4f, 0.4f, 0.4f, 0.6f));
                }

                // ★ 액션 구간 좌우 아웃라인 (시작/종료 강조 세로선)
                Color outlineColor = new Color(0.7f, 0.8f, 1.0f, 0.8f);
                // 좌측 (0프레임)
                EditorGUI.DrawRect(new Rect(paddedRect.x, paddedRect.y, 2, paddedRect.height), outlineColor);
                // 우측 (액션 끝 프레임)
                EditorGUI.DrawRect(new Rect(actionEndX - 1, paddedRect.y, 2, paddedRect.height), outlineColor);
            }

            // ── 트랙 헤더 (체크박스 + 이름) ──
            EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f));
            var trackLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                fontSize = 10
            };

            for (int t = 0; t < trackCount; t++)
            {
                float rowY = headerRect.y + t * TrackHeight;

                // 트랙 이름 (1번부터 넘버링)
                string trackName = $"Track {t + 1}";
                Rect labelRect = new Rect(headerRect.x + 4, rowY, headerRect.width - 24, TrackHeight);

                // 비활성 트랙은 어둡게
                var labelStyle = new GUIStyle(trackLabelStyle);
                if (!trackEnabled[t])
                    labelStyle.normal.textColor = new Color(0.4f, 0.4f, 0.4f);
                GUI.Label(labelRect, trackName, labelStyle);

                // 체크박스 (ON/OFF) — 우측에 배치
                Rect toggleRect = new Rect(headerRect.xMax - 20, rowY + 4, 18, 18);
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

                // 트랙 구분선
                EditorGUI.DrawRect(new Rect(outerRect.x, rowY + TrackHeight,
                    outerRect.width, 1), new Color(0.25f, 0.25f, 0.25f));
            }

            // ── 트랙 헤더 우클릭: 트랙 추가/제거 메뉴 ──
            HandleTrackHeaderRightClick(headerRect, action);

            // ── 세로 그리드 라인 (프레임별, 패딩 영역 사용) ──
            DrawTimelineGridLines(paddedRect, totalFrames, effectiveFrames);

            // ── 노티파이 블록 렌더링 (paddedRect 기준) ──
            if (action.notifies != null)
            {
                for (int i = 0; i < action.notifies.Length; i++)
                {
                    var notify = action.notifies[i];
                    if (notify.track < 0 || notify.track >= trackCount) continue;

                    float startX = paddedRect.x + (paddedRect.width * notify.startFrame / totalFrames);
                    float endX = paddedRect.x + (paddedRect.width * notify.endFrame / totalFrames);
                    float blockW = Mathf.Max(endX - startX, 4);
                    float blockY = paddedRect.y + notify.track * TrackHeight + 2;
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

                    // ── 인스턴스 모드: 사각형 블록 + start 마름모 마커 ──
                    if (notify.isInstance)
                    {
                        float centerY = blockY + blockH * 0.5f;

                        // 사각형 블록 배경 (스테이트와 동일하게 불투명 유지)
                        EditorGUI.DrawRect(blockRect, blockColor);

                        // 선택 테두리
                        if (isSelected)
                        {
                            EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, blockRect.width, 2), Color.white);
                            EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.yMax - 2, blockRect.width, 2), Color.white);
                        }

                        // start 지점에만 마름모 마커
                        float diamondSize = Mathf.Min(blockH * 0.45f, 6f);
                        Color markerColor = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.8f);
                        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 2, blockRect.height), markerColor);
                        DrawDiamondMarker(blockRect.x, centerY, diamondSize, markerColor);

                        // 블록 라벨
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
                            string label = $"I {typeName} @{notify.startFrame}";
                            if (isDisabled) label = $"({label})";
                            GUI.Label(blockRect, label, blockLabel);
                        }
                    }
                    else
                    {
                        // ── 스테이트 모드: 구간 블록 ──
                        EditorGUI.DrawRect(blockRect, blockColor);

                        // ── 시작/끝 마커 (세로 바 + 마름모) ──
                        Color markerColor = isSelected
                            ? Color.white
                            : new Color(1f, 1f, 1f, 0.6f);
                        float centerY = blockY + blockH * 0.5f;
                        float diamondSize = Mathf.Min(blockH * 0.45f, 6f);

                        // 시작 마커 (세로선 + 마름모)
                        EditorGUI.DrawRect(new Rect(blockRect.x, blockRect.y, 2, blockRect.height), markerColor);
                        DrawDiamondMarker(blockRect.x, centerY, diamondSize, markerColor);

                        // 끝 마커 (세로선 + 마름모)
                        EditorGUI.DrawRect(new Rect(blockRect.xMax - 2, blockRect.y, 2, blockRect.height), markerColor);
                        DrawDiamondMarker(blockRect.xMax, centerY, diamondSize, markerColor);

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

            // ── 재생 헤드 (빨간 세로선, paddedRect 기준) ──
            float headX = paddedRect.x + (paddedRect.width * previewFrame / totalFrames);
            // 트랙 영역 + 눈금 영역까지만 (outerRect 내부로 제한)
            float headTop = contentRect.y;
            float headBottom = Mathf.Min(rulerRect.yMax, outerRect.yMax);
            EditorGUI.DrawRect(new Rect(headX - 1, headTop, 3, headBottom - headTop),
                new Color(1f, 0.2f, 0.2f, 0.9f));
            // 상단 삼각형 마커
            EditorGUI.DrawRect(new Rect(headX - 4, headTop - 3, 9, 3), Color.red);

            // ── 시간 눈금 (paddedRect 기준, effectiveFrames로 눈금 표시) ──
            Rect paddedRulerRect = new Rect(paddedRect.x, rulerRect.y, paddedRect.width, rulerRect.height);
            float rulerPlaybackRate = (action != null && action.playbackRate > 0f) ? action.playbackRate : 1f;
            DrawTimeRuler(paddedRulerRect, totalFrames, effectiveFrames, rulerPlaybackRate);
            // 눈금 좌우 빈 영역 배경
            EditorGUI.DrawRect(new Rect(rulerRect.x, rulerRect.y, TimelinePadding, rulerRect.height),
                new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(new Rect(paddedRect.xMax, rulerRect.y, TimelinePadding, rulerRect.height),
                new Color(0.16f, 0.16f, 0.16f));

            // 눈금 영역에도 액션 범위 끝 경계선 표시
            if (effectiveFrames < totalFrames && totalFrames > 0)
            {
                float rulerEndX = paddedRect.x + (paddedRect.width * effectiveFrames / totalFrames);
                EditorGUI.DrawRect(new Rect(rulerEndX - 1, rulerRect.y, 2, rulerRect.height),
                    new Color(0.4f, 0.4f, 0.4f, 0.6f));
            }

            // ── 마우스 입력 처리 (paddedRect 기준 + 전체 contentRect로 스크러빙) ──
            // 스크러빙은 액션 실제 범위(effectiveFrames) 내로 클램프
            HandleTimelineMouseInput(paddedRect, action, totalFrames, effectiveFrames);
            HandleRulerScrub(paddedRulerRect, totalFrames, effectiveFrames);

            // ── Ctrl+휠: 타임라인 줌 ──
            HandleTimelineZoom(outerRect, action);
        }

        /// <summary>Ctrl+마우스 휠로 타임라인 줌 인/아웃</summary>
        private void HandleTimelineZoom(Rect timelineArea, ActionEntry action)
        {
            Event e = Event.current;
            if (e.type != EventType.ScrollWheel) return;
            if (!timelineArea.Contains(e.mousePosition)) return;
            if (!e.control) return; // Ctrl 키 필수

            float prevZoom = timelineZoom;
            float zoomDelta = -e.delta.y * 0.05f; // 위로 스크롤 = 줌인
            timelineZoom = Mathf.Clamp(timelineZoom + zoomDelta, 0.5f, 4.0f);

            // ★ 줌에 따라 fixedTimelineFrames 재계산
            //   최대 줌(4.0x) = 액션 프레임이 타임라인 100% 차지
            //   최소 줌(0.5x) = 클립 전체 길이 기반으로 넓게 표시
            int baseFrames = GetEffectiveTotalFrames(action);

            // 줌 1.0x 기준 타임라인(클립 길이 기반)
            UpdateFixedTimelineFrames(action);
            int fullTimeline = fixedTimelineFrames;

            // 줌 레벨에 따라 보간: 4.0x→baseFrames, 0.5x→fullTimeline
            float t = Mathf.InverseLerp(4.0f, 0.5f, timelineZoom); // 4.0→0, 0.5→1
            int zoomedFrames = Mathf.RoundToInt(Mathf.Lerp(baseFrames, fullTimeline, t));
            fixedTimelineFrames = Mathf.Max(zoomedFrames, baseFrames);

            Debug.Log($"[Timeline Zoom] {prevZoom:F2}→{timelineZoom:F2} base:{baseFrames} full:{fullTimeline} result:{fixedTimelineFrames}");

            e.Use();
            Repaint();
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
                menu.AddItem(new GUIContent($"트랙 추가 (Track {clickedTrack + 1} 아래에)"), false, () =>
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
                    ? $"Track {clickedTrack + 1} 제거 (노티파이 {notifyCountOnTrack}개 포함 삭제)"
                    : $"Track {clickedTrack + 1} 제거";

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

        /// <summary>시작/끝 지점 마름모(다이아몬드) 마커 렌더링</summary>
        private void DrawDiamondMarker(float cx, float cy, float size, Color color)
        {
            Vector3[] diamond = {
                new Vector3(cx, cy - size, 0),
                new Vector3(cx + size, cy, 0),
                new Vector3(cx, cy + size, 0),
                new Vector3(cx - size, cy, 0),
            };
            Handles.color = color;
            Handles.DrawAAConvexPolygon(diamond);
        }

        private void DrawTimeRuler(Rect rulerRect, int totalFrames, int actionFrames, float playbackRate = 1f)
        {
            EditorGUI.DrawRect(rulerRect, new Color(0.16f, 0.16f, 0.16f));

            // ── 재생배율 != 1.0일 때 상단에 배율 표시 ──
            if (Mathf.Abs(playbackRate - 1.0f) > 0.01f)
            {
                var rateStyle = new GUIStyle(EditorStyles.miniLabel);
                rateStyle.normal.textColor = new Color(1f, 0.6f, 0.2f);
                rateStyle.alignment = TextAnchor.UpperRight;
                rateStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(rulerRect.xMax - 80, rulerRect.y, 78, 12),
                    $"재생배율: {playbackRate:F1}x", rateStyle);
            }

            // ── 줌 레벨에 따른 눈금 간격 결정 ──
            float pixelsPerFrame = rulerRect.width / Mathf.Max(totalFrames, 1);

            // 기본 5프레임 간격, 공간 부족하면 10, 20으로 확대
            int majorStep = 5;
            if (pixelsPerFrame * 5 < 30f) majorStep = 10;
            if (pixelsPerFrame * 10 < 30f) majorStep = 20;

            // 줌이 충분하면 1프레임 간격 마이너 틱 표시
            bool showMinorTicks = pixelsPerFrame >= 6f;

            var frameLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                alignment = TextAnchor.UpperCenter
            };
            var timeLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 7,
                normal = { textColor = new Color(0.5f, 0.6f, 0.7f) },
                alignment = TextAnchor.UpperCenter
            };

            // ── 마이너 틱 (1프레임 간격) ──
            if (showMinorTicks)
            {
                for (int f = 0; f <= actionFrames; f++)
                {
                    if (f % majorStep == 0) continue; // 메이저 틱은 별도 처리
                    float x = rulerRect.x + (rulerRect.width * f / totalFrames);
                    float tickH = 3f;
                    EditorGUI.DrawRect(new Rect(x, rulerRect.y, 1, tickH), new Color(0.35f, 0.35f, 0.35f, 0.5f));
                }
            }

            // ── 메이저 틱 (5프레임 간격) — 프레임 번호 + 시간 동시 표시 ──
            float labelW = 44f; // 라벨 폭
            // 라벨이 잘리지 않도록 rulerRect 바깥의 허용 범위 (좌우 패딩 영역까지 활용)
            float labelMinX = rulerRect.x - TimelinePadding;
            float labelMaxX = rulerRect.xMax + TimelinePadding;

            for (int f = 0; f <= actionFrames; f += majorStep)
            {
                float x = rulerRect.x + (rulerRect.width * f / totalFrames);
                bool isBig = (f % (majorStep * 2) == 0) || f == 0;
                float tickH = isBig ? rulerRect.height * 0.35f : rulerRect.height * 0.2f;
                EditorGUI.DrawRect(new Rect(x, rulerRect.y, 1, tickH), new Color(0.5f, 0.5f, 0.5f));

                // ★ 0프레임은 틱 오른쪽에 왼쪽 정렬, 나머지는 중앙 정렬
                float labelX;
                var usedFrameLabel = frameLabel;
                var usedTimeLabel = timeLabel;
                if (f == 0)
                {
                    // 0프레임: 틱 위치에서 약간 오른쪽으로 (잘림 방지)
                    labelX = x + 2f;
                    usedFrameLabel = new GUIStyle(frameLabel) { alignment = TextAnchor.UpperLeft };
                    usedTimeLabel = new GUIStyle(timeLabel) { alignment = TextAnchor.UpperLeft };
                }
                else
                {
                    labelX = ClampLabelX(x, labelW, labelMinX, labelMaxX);
                }

                // 프레임 번호 (상단)
                string frameTxt = $"{f}f";
                GUI.Label(new Rect(labelX, rulerRect.y + tickH - 1, labelW, 11), frameTxt, usedFrameLabel);

                // 시간 표시 (하단)
                float timeSec = f * CombatConstants.FrameDuration;
                string timeTxt = timeSec < 1f ? $"{timeSec:F3}s" : $"{timeSec:F2}s";
                GUI.Label(new Rect(labelX, rulerRect.y + tickH + 8, labelW, 10), timeTxt, usedTimeLabel);
            }

            // ── 액션 끝 프레임 (majorStep 배수가 아닌 경우 추가 표시) ──
            if (actionFrames % majorStep != 0)
            {
                float endX = rulerRect.x + (rulerRect.width * actionFrames / totalFrames);
                float tickH = rulerRect.height * 0.35f;
                EditorGUI.DrawRect(new Rect(endX, rulerRect.y, 1, tickH), new Color(0.6f, 0.5f, 0.3f));

                // ★ 끝 프레임은 틱 왼쪽에 오른쪽 정렬 (잘림 방지)
                float endLabelX = endX - labelW - 2f;
                endLabelX = Mathf.Max(endLabelX, labelMinX);

                var endFrameLabel = new GUIStyle(frameLabel)
                {
                    normal = { textColor = new Color(0.9f, 0.7f, 0.4f) },
                    alignment = TextAnchor.UpperRight
                };
                var endTimeLabel = new GUIStyle(timeLabel)
                {
                    normal = { textColor = new Color(0.7f, 0.6f, 0.3f) },
                    alignment = TextAnchor.UpperRight
                };
                GUI.Label(new Rect(endLabelX, rulerRect.y + tickH - 1, labelW, 11),
                    $"{actionFrames}f", endFrameLabel);
                float endTimeSec = actionFrames * CombatConstants.FrameDuration;
                string endTimeTxt = endTimeSec < 1f ? $"{endTimeSec:F3}s" : $"{endTimeSec:F2}s";
                GUI.Label(new Rect(endLabelX, rulerRect.y + tickH + 8, labelW, 10),
                    endTimeTxt, endTimeLabel);
            }
        }

        /// <summary>타임라인 컨텐츠 영역에 세로 그리드 라인 렌더링</summary>
        private void DrawTimelineGridLines(Rect contentRect, int totalFrames, int actionFrames)
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

            // 액션 범위 내만 그리드 라인 표시 (여분 구간은 비워둠)
            for (int f = frameStep; f <= actionFrames; f += frameStep)
            {
                float x = contentRect.x + (contentRect.width * f / totalFrames);
                Color c = (f % 10 == 0) ? lineColor10 : (f % 5 == 0) ? lineColor5 : lineColor;
                EditorGUI.DrawRect(new Rect(x, contentRect.y, 1, contentRect.height), c);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  타임라인 마우스 입력
        // ═══════════════════════════════════════════════════════

        private void HandleTimelineMouseInput(Rect contentRect, ActionEntry action, int totalFrames, int actionFrames)
        {
            Event e = Event.current;

            // 드래그 중이면 영역 밖에서도 계속 처리 (특히 Scrub)
            bool isDragging = currentDragMode != DragMode.None;
            if (!isDragging && !contentRect.Contains(e.mousePosition)) return;

            float framesPerPixel = (float)totalFrames / contentRect.width;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:  // 좌클릭
                    HandleTimelineLeftClick(contentRect, action, totalFrames, actionFrames, e);
                    break;

                case EventType.MouseDown when e.button == 1:  // 우클릭
                    HandleTimelineRightClick(contentRect, action, totalFrames, e);
                    break;

                case EventType.MouseDrag:
                    if (isDragging)
                        HandleTimelineDrag(contentRect, action, totalFrames, actionFrames, e, framesPerPixel);
                    break;

                case EventType.MouseUp when e.button == 0:
                    if (isDragging)
                    {
                        currentDragMode = DragMode.None;
                        dragNotifyIndex = -1;
                        e.Use();
                    }
                    break;
            }
        }

        private void HandleTimelineLeftClick(Rect contentRect, ActionEntry action, int totalFrames, int actionFrames, Event e)
        {
            float mouseFrame = ((e.mousePosition.x - contentRect.x) / contentRect.width) * totalFrames;
            int track = Mathf.FloorToInt((e.mousePosition.y - contentRect.y) / TrackHeight);

            // 블록 히트 테스트 (역순으로 = 위에 그려진 것 우선)
            if (action.notifies != null)
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

            // 빈 영역 클릭 = 노티파이 선택 해제 + 프레임 스크러빙 (액션 범위 내로 클램프)
            selectedNotifyIndex = -1;
            previewFrame = Mathf.Clamp(mouseFrame, 0, actionFrames);
            isPreviewPlaying = false;
            currentDragMode = DragMode.Scrub;
            e.Use();
            Repaint();
        }

        private void HandleTimelineDrag(Rect contentRect, ActionEntry action, int totalFrames, int actionFrames, Event e, float framesPerPixel)
        {
            // ── 스크러빙 모드: 재생 헤드 드래그 (액션 범위 내로 클램프) ──
            if (currentDragMode == DragMode.Scrub)
            {
                float mouseFrame = ((e.mousePosition.x - contentRect.x) / contentRect.width) * totalFrames;
                previewFrame = Mathf.Clamp(mouseFrame, 0, actionFrames);
                isPreviewPlaying = false;
                e.Use();
                Repaint();
                return;
            }

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

        /// <summary>눈금(룰러) 영역에서 클릭/드래그로 재생 헤드 스크러빙</summary>
        private void HandleRulerScrub(Rect rulerRect, int totalFrames, int actionFrames)
        {
            Event e = Event.current;

            // 룰러 영역 클릭 → 스크러빙 시작 (액션 범위 내로 클램프)
            if (e.type == EventType.MouseDown && e.button == 0 && rulerRect.Contains(e.mousePosition))
            {
                float mouseFrame = ((e.mousePosition.x - rulerRect.x) / rulerRect.width) * totalFrames;
                previewFrame = Mathf.Clamp(mouseFrame, 0, actionFrames);
                isPreviewPlaying = false;
                currentDragMode = DragMode.Scrub;
                selectedNotifyIndex = -1;
                e.Use();
                Repaint();
            }
        }

        private void HandleTimelineRightClick(Rect contentRect, ActionEntry action, int totalFrames, Event e)
        {
            float mouseFrame = ((e.mousePosition.x - contentRect.x) / contentRect.width) * totalFrames;
            int effectiveMax = GetEffectiveTotalFrames(action);
            int clickedFrame = Mathf.Clamp(Mathf.RoundToInt(mouseFrame), 0, Mathf.Max(0, effectiveMax - 1));
            int clickedTrack = Mathf.Clamp(
                Mathf.FloorToInt((e.mousePosition.y - contentRect.y) / TrackHeight), 0, trackCount - 1);

            // ── 검색 가능한 팝업 항목 구성 ──
            // clickedTrack을 캡처하여 노티파이 생성 시 해당 트랙에 배치
            int targetTrack = clickedTrack;
            var items = new List<NotifySearchPopup.PopupItem>();

            // ── STARTUP 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "STARTUP 추가",
                SearchTag = "startup 선딜 시작 전딜 windup",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateStartup(clickedFrame, clickedFrame + 5, action.moveSpeed), targetTrack)
            });

            // ── COLLISION 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "COLLISION 추가",
                SearchTag = "collision 히트박스 히트 판정 active 액티브 hitbox attack 공격",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCollision(clickedFrame, clickedFrame + 8), targetTrack)
            });

            items.Add(new NotifySearchPopup.PopupItem { IsSeparator = true });

            // ── CANCEL_WINDOW 계열 ──
            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 전체 캔슬",
                SearchTag = "cancel window 캔슬 윈도우 전체 all 공격 이동 회피 카운터",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10), targetTrack)
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 공격만",
                SearchTag = "cancel window 캔슬 윈도우 공격 attack skill 스킬 콤보",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: true, move: false, dodge: false, counter: false), targetTrack)
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 회피만",
                SearchTag = "cancel window 캔슬 윈도우 회피 dodge 구르기 대시",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: false, dodge: true, counter: false), targetTrack)
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 카운터만",
                SearchTag = "cancel window 캔슬 윈도우 카운터 counter 반격 패리 parry",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: false, dodge: false, counter: true), targetTrack)
            });

            items.Add(new NotifySearchPopup.PopupItem
            {
                Label = "CANCEL_WINDOW — 이동만",
                SearchTag = "cancel window 캔슬 윈도우 이동 move 무브 걷기 달리기",
                OnSelected = () => AddNotifyToTrack(action,
                    ActionNotify.CreateCancelWindow(clickedFrame, clickedFrame + 10,
                        skill: false, move: true, dodge: false, counter: false), targetTrack)
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

        /// <summary>노티파이를 생성하면서 클릭된 트랙 번호를 강제 지정</summary>
        private void AddNotifyToTrack(ActionEntry action, ActionNotify notify, int track)
        {
            notify.track = track;
            AddNotify(action, notify);
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

            // 프리뷰 렌더 영역 (리사이즈 가능)
            Rect previewRect = GUILayoutUtility.GetRect(0, previewHeight, GUILayout.ExpandWidth(true));
            if (previewRect.width < 10 || previewRect.height < 10) return;

            EnsurePreviewSetup();
            if (previewRender == null) return;

            SamplePreviewAnimation(clip, action);
            HandlePreviewMouseInput(previewRect);
            HandleHitboxDragInput(previewRect, action); // Step 4: 히트박스 드래그 조작

            previewRender.BeginPreview(previewRect, GUIStyle.none);

            // ── 2D 고정 뷰: 직교(Orthographic) 카메라, Left 뷰 고정 ──
            // 카메라: +X 방향에서 원점을 바라봄 → 화면 오른쪽 = +Z, 위 = +Y
            // 2D 횡스크롤 게임의 사이드 뷰와 일치
            float camHeight = 1.0f;
            Vector3 camTarget = new Vector3(0, camHeight + previewPanOffset.y, previewPanOffset.x);
            float camDist = 10f; // 직교 카메라이므로 거리는 클리핑만 영향
            Vector3 camPos = camTarget + new Vector3(camDist, 0, 0); // Left 뷰 고정

            previewRender.camera.transform.position = camPos;
            previewRender.camera.transform.LookAt(camTarget);
            previewRender.camera.orthographic = true;
            previewRender.camera.orthographicSize = previewCamDistance * 0.5f; // 줌 조절용
            previewRender.camera.nearClipPlane = 0.1f;
            previewRender.camera.farClipPlane = 50f;
            previewRender.lights[0].transform.rotation = Quaternion.Euler(50, 60, 0); // Left 뷰 고정 조명
            previewRender.lights[0].intensity = 1.2f;

            // ── 히트박스 3D 큐브 업데이트 (카메라 렌더 직전) ──
            int hitboxFrame = Mathf.RoundToInt(previewFrame);
            UpdateHitboxCube(action, hitboxFrame);

            previewRender.camera.Render();

            // ── 히트박스 와이어프레임 (GL 라인, 카메라 렌더 후) ──
            if (hitboxCubeObj != null && hitboxCubeObj.activeSelf)
            {
                DrawHitboxWireframe(previewRender.camera, hitboxCubeObj.transform.position, hitboxCubeObj.transform.localScale);
            }

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

            // ── COLLISION 히트박스 시각화 오버레이 ──
            DrawCollisionOverlay(previewRect, action, currentFrame);

            // ── 히트박스 트랜스폼 GUI 오버레이 (선택된 경우에만) ──
            if (isHitboxSelected && hitboxCubeObj != null && hitboxCubeObj.activeSelf)
            {
                DrawHitboxTransformOverlay(previewRect, action);
                DrawHitboxSelectionHighlight(previewRect);
            }
        }

        /// <summary>
        /// COLLISION 노티파이 구간에 진입하면 프리뷰에 빨간 히트박스 판정 오버레이를 표시.
        /// 구간을 벗어나면 사라진다.
        /// </summary>
        private void DrawCollisionOverlay(Rect previewRect, ActionEntry action, int currentFrame)
        {
            if (action.notifies == null) return;

            // 현재 프레임에서 활성인 COLLISION 노티파이를 찾는다
            bool isCollisionActive = false;
            float damageScale = 1f;
            string hitboxId = "";

            for (int i = 0; i < action.notifies.Length; i++)
            {
                var n = action.notifies[i];
                if (n.disabled) continue;
                if (n.TypeEnum != NotifyType.COLLISION) continue;

                if (n.isInstance)
                {
                    // 인스턴스 모드: 정확히 해당 프레임일 때
                    if (currentFrame == n.startFrame)
                    {
                        isCollisionActive = true;
                        damageScale = n.damageScale;
                        hitboxId = n.hitboxId ?? "";
                        break;
                    }
                }
                else
                {
                    // 스테이트 모드: 구간 내 포함
                    if (currentFrame >= n.startFrame && currentFrame < n.endFrame)
                    {
                        isCollisionActive = true;
                        damageScale = n.damageScale;
                        hitboxId = n.hitboxId ?? "";
                        break;
                    }
                }
            }

            if (!isCollisionActive) return;

            // ── 프리뷰 상단에 작은 "COLLISION ACTIVE" 뱃지만 표시 (전체 오버레이 제거) ──
            string hitLabel = "● COLLISION";
            if (!string.IsNullOrEmpty(hitboxId))
                hitLabel += $" [{hitboxId}]";
            if (Mathf.Abs(damageScale - 1f) > 0.01f)
                hitLabel += $" x{damageScale:F1}";

            // 뱃지 배경
            float badgeW = hitLabel.Length * 7f + 12f;
            Rect badgeRect = new Rect(previewRect.xMax - badgeW - 4, previewRect.y + 20, badgeW, 18);
            EditorGUI.DrawRect(badgeRect, new Color(1f, 0.15f, 0.1f, 0.6f));

            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(badgeRect, hitLabel, badgeStyle);
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

            // 2D 뷰 고정 라벨
            EditorGUILayout.LabelField("2D Side View", EditorStyles.miniLabel, GUILayout.Width(70));

            GUILayout.Space(4);

            if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(18)))
            {
                previewCamDistance = DefaultCamDistance;
                previewPanOffset = Vector2.zero;
                playbackSpeed = DefaultPlaybackSpeed;
                loopMode = DefaultLoopMode;
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
            previewRender.camera.orthographic = true;
            previewRender.camera.orthographicSize = previewCamDistance * 0.5f;
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

            // ── 히트박스 큐브 생성 ──
            EnsureHitboxCube();
        }

        /// <summary>
        /// 프리뷰 씬에 히트박스 시각화용 반투명 Quad(2D 평면)를 생성.
        /// 2D 횡스크롤이므로 깊이(Z축)가 없는 평면으로 표현한다.
        /// 빌보드 방식으로 항상 카메라를 바라보며, 캐릭터의 forward 방향에 배치된다.
        /// </summary>
        private void EnsureHitboxCube()
        {
            if (hitboxCubeObj != null) return;
            if (previewRender == null) return;

            hitboxCubeObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hitboxCubeObj.name = "_HitboxPreview";
            hitboxCubeObj.hideFlags = HideFlags.HideAndDontSave;

            // 콜라이더 불필요 (시각 전용)
            var col = hitboxCubeObj.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            // 반투명 빨간 양면 머티리얼 (PreviewRenderUtility에서 확실히 동작하는 셰이더 사용)
            if (hitboxMaterial == null)
            {
                hitboxMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                hitboxMaterial.hideFlags = HideFlags.HideAndDontSave;
                hitboxMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                hitboxMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                hitboxMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);   // 양면 렌더링
                hitboxMaterial.SetInt("_ZWrite", 0);                                         // 반투명이므로 ZWrite 끔
                hitboxMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                hitboxMaterial.color = new Color(1f, 0.2f, 0.1f, 0.25f);
            }
            hitboxCubeObj.GetComponent<MeshRenderer>().sharedMaterial = hitboxMaterial;

            // 프리뷰 레이어 설정
            hitboxCubeObj.layer = 1;
            hitboxCubeObj.SetActive(false); // 기본 비활성

            // PreviewRenderUtility 씬에 추가
            previewRender.AddSingleGO(hitboxCubeObj);
        }

        /// <summary>
        /// 현재 프레임의 활성 COLLISION 노티파이에 따라 히트박스 Quad를 업데이트.
        ///
        /// ★ 2D 횡스크롤 → 3D 프리뷰 좌표 매핑 (디스플레이 좌표):
        ///   hitboxOffsetX (전방 거리) → world +Z  (Left 뷰에서 화면 오른쪽)
        ///   hitboxOffsetY (높이)     → world +Y  (화면 위)
        ///   world X = 0 고정
        ///
        /// 이 매핑은 Left 뷰(기본 편집 뷰)에서 2D 게임 레이아웃과 일치:
        ///   Z+ = 캐릭터 전방 = 화면 오른쪽, Y+ = 위
        ///
        /// Quad 배치: Y축 90도 고정 회전 — Left 2D 뷰에서 정면으로 보임
        ///   → 직교 카메라 + 고정 뷰이므로 빌보드 불필요
        /// </summary>
        private void UpdateHitboxCube(ActionEntry action, int currentFrame)
        {
            if (hitboxCubeObj == null) return;

            ActionNotify activeCollision = null;

            // 1순위: 선택된 COLLISION 노티파이
            if (selectedNotifyIndex >= 0 && action.notifies != null &&
                selectedNotifyIndex < action.notifies.Length)
            {
                var sel = action.notifies[selectedNotifyIndex];
                if (sel.TypeEnum == NotifyType.COLLISION && !sel.disabled)
                    activeCollision = sel;
            }

            // 2순위: 현재 프레임에서 활성인 COLLISION 노티파이
            if (activeCollision == null && action.notifies != null)
            {
                for (int i = 0; i < action.notifies.Length; i++)
                {
                    var n = action.notifies[i];
                    if (n.disabled || n.TypeEnum != NotifyType.COLLISION) continue;
                    bool active = n.isInstance
                        ? currentFrame == n.startFrame
                        : currentFrame >= n.startFrame && currentFrame < n.endFrame;
                    if (active) { activeCollision = n; break; }
                }
            }

            if (activeCollision == null)
            {
                hitboxCubeObj.SetActive(false);
                return;
            }

            // ── 2D 데이터 읽기 ──
            float fwd = activeCollision.hitboxOffsetX;  // 전방 거리
            float up  = activeCollision.hitboxOffsetY == 0f
                ? ActionNotify.DefaultHitboxOffsetY
                : activeCollision.hitboxOffsetY;
            float sizeW = activeCollision.hitboxSizeX == 0f ? ActionNotify.DefaultHitboxSizeX : activeCollision.hitboxSizeX;
            float sizeH = activeCollision.hitboxSizeY == 0f ? ActionNotify.DefaultHitboxSizeY : activeCollision.hitboxSizeY;

            // ── 3D 프리뷰 좌표 변환 ──
            // 디스플레이 좌표: Left 뷰(기본 뷰) 기준 최적화
            //   hitboxOffsetX(전방) → world +Z  (Left 뷰 화면 오른쪽)
            //   hitboxOffsetY(높이) → world +Y  (화면 위)
            //   world X = 0 고정
            // 이 매핑은 Left 뷰에서 2D 게임과 동일한 시각적 레이아웃을 제공한다
            Vector3 hitboxPos = new Vector3(0f, up, fwd);
            hitboxCubeObj.SetActive(true);
            hitboxCubeObj.transform.position = hitboxPos;
            hitboxCubeObj.transform.localScale = new Vector3(sizeW, sizeH, 1f);

            // 2D 고정 뷰: Left 뷰(카메라 +X→-X)에서 Quad가 정면으로 보이도록 Y축 90도 회전
            // Quad 기본 XY 평면을 YZ 평면(화면 정면)으로 변환
            hitboxCubeObj.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // 구간 내/밖 투명도 조절
            bool inRange = activeCollision.isInstance
                ? currentFrame == activeCollision.startFrame
                : currentFrame >= activeCollision.startFrame && currentFrame < activeCollision.endFrame;

            if (hitboxMaterial != null)
            {
                if (isHitboxSelected)
                {
                    // 선택됨: 노란색 계열로 강조
                    hitboxMaterial.color = inRange
                        ? new Color(1f, 0.85f, 0.1f, 0.35f)
                        : new Color(1f, 0.85f, 0.1f, 0.15f);
                }
                else
                {
                    hitboxMaterial.color = inRange
                        ? new Color(1f, 0.2f, 0.1f, 0.3f)
                        : new Color(1f, 0.2f, 0.1f, 0.12f);
                }
            }
        }

        /// <summary>
        /// GL.Lines로 히트박스의 2D 사각형 와이어프레임(4개 엣지 + X 대각선)을 그린다.
        /// Quad는 Y축 90도 고정 회전 (Left 2D 뷰 전용).
        /// camera.Render() 이후, EndPreview() 이전에 호출해야 한다.
        /// </summary>
        private void DrawHitboxWireframe(Camera cam, Vector3 center, Vector3 scale)
        {
            if (hitboxWireMaterial == null)
            {
                hitboxWireMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                hitboxWireMaterial.hideFlags = HideFlags.HideAndDontSave;
                hitboxWireMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                hitboxWireMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                hitboxWireMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                hitboxWireMaterial.SetInt("_ZWrite", 0);
                hitboxWireMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            // Y축 90도 고정 회전 기준 정점 계산
            // Quad 로컬 (x,y,0) → Y90° 회전 후 (0, y, -x)
            float halfW = scale.x * 0.5f; // sizeX → Z축 방향 (화면 가로)
            float halfH = scale.y * 0.5f; // sizeY → Y축 방향 (화면 세로)

            Vector3[] v = new Vector3[4];
            v[0] = center + new Vector3(0f, -halfH,  halfW); // 좌하
            v[1] = center + new Vector3(0f, -halfH, -halfW); // 우하
            v[2] = center + new Vector3(0f,  halfH, -halfW); // 우상
            v[3] = center + new Vector3(0f,  halfH,  halfW); // 좌상

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            hitboxWireMaterial.SetPass(0);

            GL.Begin(GL.LINES);
            // 선택 시 노란색, 미선택 시 빨간색 와이어
            Color wireColor = isHitboxSelected
                ? new Color(1f, 0.9f, 0.2f, 1f)
                : new Color(1f, 0.3f, 0.15f, 0.9f);
            GL.Color(wireColor);

            // 사각형 4변
            GLLine(v[0], v[1]); GLLine(v[1], v[2]);
            GLLine(v[2], v[3]); GLLine(v[3], v[0]);
            // 대각선 X 표시 (판정 영역임을 강조)
            GLLine(v[0], v[2]); GLLine(v[1], v[3]);

            GL.End();
            GL.PopMatrix();
        }

        private static void GLLine(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        // ═══════════════════════════════════════════════════════
        //  히트박스 드래그 조작 + W/E/R 단축키
        // ═══════════════════════════════════════════════════════

        private enum HitboxDragType { None, Move, Scale }
        private HitboxDragType hitboxDragType = HitboxDragType.None;
        private Vector3 hitboxDragStartSize;

        /// <summary>
        /// 프리뷰 영역 내에서 히트박스 드래그 입력 처리 + W/E/R 단축키.
        /// W = Move(이동), E = Rotate(현재 2D라 미사용, 예약), R = Scale(크기).
        /// 좌클릭+드래그: 현재 gizmo 모드에 따라 이동 또는 크기 조절.
        /// </summary>
        /// <summary>마우스 위치(EditorWindow 좌표)를 프리뷰의 2D 직교 월드 좌표(Z, Y)로 변환</summary>
        private Vector2 PreviewMouseToWorld(Rect previewRect, Vector2 mousePos)
        {
            float orthoSize = previewCamDistance * 0.5f;
            float aspect = previewRect.width / Mathf.Max(previewRect.height, 1f);
            float camTargetZ = previewPanOffset.x;
            float camTargetY = 1.0f + previewPanOffset.y;

            float normX = (mousePos.x - previewRect.center.x) / previewRect.width;   // -0.5 ~ 0.5
            float normY = (mousePos.y - previewRect.center.y) / previewRect.height;   // -0.5 ~ 0.5

            float worldZ = camTargetZ + normX * orthoSize * 2f * aspect;
            float worldY = camTargetY - normY * orthoSize * 2f; // 화면 Y 반전
            return new Vector2(worldZ, worldY);
        }

        /// <summary>히트박스 영역 내에 월드 좌표가 있는지 히트테스트</summary>
        private bool HitTestHitbox(Vector2 worldZY, ActionNotify collision)
        {
            float fwd = collision.hitboxOffsetX;
            float up  = collision.hitboxOffsetY == 0f ? ActionNotify.DefaultHitboxOffsetY : collision.hitboxOffsetY;
            float halfW = (collision.hitboxSizeX == 0f ? ActionNotify.DefaultHitboxSizeX : collision.hitboxSizeX) * 0.5f;
            float halfH = (collision.hitboxSizeY == 0f ? ActionNotify.DefaultHitboxSizeY : collision.hitboxSizeY) * 0.5f;

            // 히트박스 중심 = (Z=fwd, Y=up), 약간의 여유분 추가(클릭 편의)
            float margin = 0.05f;
            return worldZY.x >= fwd - halfW - margin && worldZY.x <= fwd + halfW + margin &&
                   worldZY.y >= up  - halfH - margin && worldZY.y <= up  + halfH + margin;
        }

        private void HandleHitboxDragInput(Rect previewRect, ActionEntry action)
        {
            if (hitboxCubeObj == null || !hitboxCubeObj.activeSelf)
            {
                isHitboxSelected = false;
                return;
            }

            ActionNotify activeCollision = GetActiveCollisionNotify(action);
            if (activeCollision == null)
            {
                isHitboxSelected = false;
                return;
            }

            Event e = Event.current;

            // ── W/E/R 단축키 (선택된 상태에서만) ──
            if (isHitboxSelected && e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.W:
                        hitboxGizmoMode = HitboxGizmoMode.Move;
                        e.Use(); Repaint(); break;
                    case KeyCode.E:
                        hitboxGizmoMode = HitboxGizmoMode.Rotate;
                        e.Use(); Repaint(); break;
                    case KeyCode.R:
                        hitboxGizmoMode = HitboxGizmoMode.Scale;
                        e.Use(); Repaint(); break;
                    case KeyCode.Escape:
                        isHitboxSelected = false;
                        e.Use(); Repaint(); break;
                }
            }

            // 프리뷰 영역 내부가 아니면 드래그 처리 안 함
            if (!previewRect.Contains(e.mousePosition) && !isHitboxDragging)
                return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && !isDraggingPreview)
                    {
                        // 히트테스트: 클릭한 곳이 히트박스 위인지 확인
                        Vector2 worldZY = PreviewMouseToWorld(previewRect, e.mousePosition);
                        bool hitOnBox = HitTestHitbox(worldZY, activeCollision);

                        if (hitOnBox)
                        {
                            // ── 히트박스 선택 + 드래그 시작 ──
                            isHitboxSelected = true;

                            if (hitboxGizmoMode == HitboxGizmoMode.Scale)
                            {
                                hitboxDragType = HitboxDragType.Scale;
                                hitboxDragStartSize = new Vector3(
                                    activeCollision.hitboxSizeX == 0f ? ActionNotify.DefaultHitboxSizeX : activeCollision.hitboxSizeX,
                                    activeCollision.hitboxSizeY == 0f ? ActionNotify.DefaultHitboxSizeY : activeCollision.hitboxSizeY,
                                    0f);
                            }
                            else
                            {
                                hitboxDragType = HitboxDragType.Move;
                                hitboxDragStartOffset = new Vector3(
                                    activeCollision.hitboxOffsetX,
                                    activeCollision.hitboxOffsetY == 0f ? ActionNotify.DefaultHitboxOffsetY : activeCollision.hitboxOffsetY,
                                    0f);
                            }
                            hitboxDragStartMouse = e.mousePosition;
                            isHitboxDragging = true;
                            PushUndoSnapshot();
                            e.Use();
                        }
                        else
                        {
                            // ── 빈 곳 클릭 → 선택 해제 ──
                            isHitboxSelected = false;
                            Repaint();
                            // e.Use() 안 함 → 다른 입력 핸들러가 처리할 수 있도록
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (isHitboxDragging && e.button == 0)
                    {
                        Vector2 delta = e.mousePosition - hitboxDragStartMouse;
                        float sensitivity = previewCamDistance / Mathf.Max(previewRect.width, 200f) * 2f;

                        if (hitboxDragType == HitboxDragType.Move)
                        {
                            // 2D 고정 뷰 (Left): 마우스 가로 → hitboxOffsetX(전방, +Z)
                            //                     마우스 세로 → hitboxOffsetY(높이, +Y)
                            activeCollision.hitboxOffsetX = hitboxDragStartOffset.x + delta.x * sensitivity;
                            activeCollision.hitboxOffsetY = hitboxDragStartOffset.y - delta.y * sensitivity;
                        }
                        else if (hitboxDragType == HitboxDragType.Scale)
                        {
                            // 스케일: 2D Left 뷰에서 드래그 방향과 일치하도록 부호 보정
                            // 마우스 오른쪽(+delta.x) → 폭 증가, 마우스 위(-delta.y) → 높이 증가
                            activeCollision.hitboxSizeX = Mathf.Max(0.05f, hitboxDragStartSize.x - delta.x * sensitivity);
                            activeCollision.hitboxSizeY = Mathf.Max(0.05f, hitboxDragStartSize.y + delta.y * sensitivity);
                        }

                        isDirty = true;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (isHitboxDragging && e.button == 0)
                    {
                        isHitboxDragging = false;
                        hitboxDragType = HitboxDragType.None;
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>현재 선택/활성 중인 COLLISION 노티파이를 반환</summary>
        private ActionNotify GetActiveCollisionNotify(ActionEntry action)
        {
            if (action.notifies == null) return null;

            // 1순위: 선택된 COLLISION 노티파이
            if (selectedNotifyIndex >= 0 && selectedNotifyIndex < action.notifies.Length)
            {
                var sel = action.notifies[selectedNotifyIndex];
                if (sel.TypeEnum == NotifyType.COLLISION && !sel.disabled)
                    return sel;
            }

            // 2순위: 현재 프레임에서 활성인 COLLISION 노티파이
            int frame = Mathf.RoundToInt(previewFrame);
            for (int i = 0; i < action.notifies.Length; i++)
            {
                var n = action.notifies[i];
                if (n.disabled || n.TypeEnum != NotifyType.COLLISION) continue;
                bool active = n.isInstance
                    ? frame == n.startFrame
                    : frame >= n.startFrame && frame < n.endFrame;
                if (active) return n;
            }

            return null;
        }

        /// <summary>
        /// 프리뷰 영역에 히트박스 트랜스폼 정보 오버레이를 그린다.
        /// W/E/R 모드 표시, 좌표/크기 수치, 드래그 힌트를 포함.
        /// </summary>
        /// <summary>선택된 히트박스에 노란 선택 테두리를 프리뷰 위에 그린다.</summary>
        private void DrawHitboxSelectionHighlight(Rect previewRect)
        {
            if (hitboxCubeObj == null || !hitboxCubeObj.activeSelf) return;

            // 히트박스 월드 좌표를 프리뷰 스크린 좌표로 변환
            Vector3 pos = hitboxCubeObj.transform.position;
            Vector3 scale = hitboxCubeObj.transform.localScale;
            float halfW = scale.x * 0.5f; // sizeX → Z 방향
            float halfH = scale.y * 0.5f; // sizeY → Y 방향

            float orthoSize = previewCamDistance * 0.5f;
            float aspect = previewRect.width / Mathf.Max(previewRect.height, 1f);
            float camTargetZ = previewPanOffset.x;
            float camTargetY = 1.0f + previewPanOffset.y;

            // 월드 Z/Y → 스크린 X/Y
            float screenCenterX = previewRect.center.x + (pos.z - camTargetZ) / (orthoSize * 2f * aspect) * previewRect.width;
            float screenCenterY = previewRect.center.y - (pos.y - camTargetY) / (orthoSize * 2f) * previewRect.height;
            float screenHalfW = halfW / (orthoSize * 2f * aspect) * previewRect.width;
            float screenHalfH = halfH / (orthoSize * 2f) * previewRect.height;

            // 선택 하이라이트 테두리 (노란색 점선 스타일)
            Rect highlightRect = new Rect(
                screenCenterX - screenHalfW,
                screenCenterY - screenHalfH,
                screenHalfW * 2f,
                screenHalfH * 2f);

            Color selectColor = new Color(1f, 0.9f, 0.2f, 0.8f);
            float bw = 2f;
            EditorGUI.DrawRect(new Rect(highlightRect.x - bw, highlightRect.y - bw, highlightRect.width + bw * 2, bw), selectColor);
            EditorGUI.DrawRect(new Rect(highlightRect.x - bw, highlightRect.yMax, highlightRect.width + bw * 2, bw), selectColor);
            EditorGUI.DrawRect(new Rect(highlightRect.x - bw, highlightRect.y, bw, highlightRect.height), selectColor);
            EditorGUI.DrawRect(new Rect(highlightRect.xMax, highlightRect.y, bw, highlightRect.height), selectColor);

            // 네 꼭짓점에 작은 핸들 사각형
            float handleSize = 5f;
            Color handleColor = new Color(1f, 0.95f, 0.3f, 1f);
            Rect[] corners = {
                new Rect(highlightRect.x - handleSize, highlightRect.y - handleSize, handleSize * 2, handleSize * 2),
                new Rect(highlightRect.xMax - handleSize, highlightRect.y - handleSize, handleSize * 2, handleSize * 2),
                new Rect(highlightRect.x - handleSize, highlightRect.yMax - handleSize, handleSize * 2, handleSize * 2),
                new Rect(highlightRect.xMax - handleSize, highlightRect.yMax - handleSize, handleSize * 2, handleSize * 2),
            };
            foreach (var cr in corners)
                EditorGUI.DrawRect(cr, handleColor);
        }

        private void DrawHitboxTransformOverlay(Rect previewRect, ActionEntry action)
        {
            ActionNotify ac = GetActiveCollisionNotify(action);
            if (ac == null) return;

            float offX = ac.hitboxOffsetX;
            float offY = ac.hitboxOffsetY == 0f ? ActionNotify.DefaultHitboxOffsetY : ac.hitboxOffsetY;
            float szX = ac.hitboxSizeX == 0f ? ActionNotify.DefaultHitboxSizeX : ac.hitboxSizeX;
            float szY = ac.hitboxSizeY == 0f ? ActionNotify.DefaultHitboxSizeY : ac.hitboxSizeY;

            // ── 좌측 하단: 좌표/크기 수치 ──
            var coordStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.LowerLeft
            };
            string coordText = $"Offset({offX:F2}, {offY:F2})  Size({szX:F2}, {szY:F2})";
            GUI.Label(new Rect(previewRect.x + 4, previewRect.yMax - 32, previewRect.width - 8, 14), coordText, coordStyle);

            // ── 좌측 하단: 드래그 모드 힌트 ──
            string modeLabel;
            Color modeColor;
            switch (hitboxGizmoMode)
            {
                case HitboxGizmoMode.Move:
                    modeLabel = isHitboxDragging ? "드래그: 위치 이동 중" : "[W] Move  |  E Rotate  |  R Scale";
                    modeColor = new Color(0.3f, 1f, 0.3f, 0.9f); // 초록
                    break;
                case HitboxGizmoMode.Rotate:
                    modeLabel = "[E] Rotate (2D 예약)  |  W Move  |  R Scale";
                    modeColor = new Color(0.3f, 0.6f, 1f, 0.9f); // 파랑
                    break;
                case HitboxGizmoMode.Scale:
                    modeLabel = isHitboxDragging ? "드래그: 크기 조절 중" : "W Move  |  E Rotate  |  [R] Scale";
                    modeColor = new Color(1f, 0.6f, 0.2f, 0.9f); // 주황
                    break;
                default:
                    modeLabel = ""; modeColor = Color.white; break;
            }

            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = modeColor },
                alignment = TextAnchor.LowerLeft
            };
            GUI.Label(new Rect(previewRect.x + 4, previewRect.yMax - 18, previewRect.width - 8, 16), modeLabel, hintStyle);

            // ── 좌측 상단: "HITBOX" 표시 + 모드 아이콘 ──
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = modeColor },
                alignment = TextAnchor.UpperLeft
            };
            string modeIcon = hitboxGizmoMode == HitboxGizmoMode.Move ? "✥"
                : hitboxGizmoMode == HitboxGizmoMode.Scale ? "⬚" : "↻";
            GUI.Label(new Rect(previewRect.x + 4, previewRect.y + 2, 200, 16),
                $"{modeIcon} HITBOX ({ac.hitboxId ?? "default"})", titleStyle);
        }

        private void CleanupPreview()
        {
            isPreviewPlaying = false;
            if (AnimationMode.InAnimationMode())
            {
                try { AnimationMode.StopAnimationMode(); } catch { }
            }
            // 히트박스 큐브 정리
            if (hitboxCubeObj != null) { DestroyImmediate(hitboxCubeObj); hitboxCubeObj = null; }
            if (hitboxMaterial != null) { DestroyImmediate(hitboxMaterial); hitboxMaterial = null; }
            if (hitboxWireMaterial != null) { DestroyImmediate(hitboxWireMaterial); hitboxWireMaterial = null; }

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
                // 2D 고정 뷰: 좌클릭 드래그는 히트박스 전용 (카메라 회전 없음)
                // 중클릭(휠 클릭) 드래그로 카메라 패닝만 허용
                case EventType.MouseDown when e.button == 2: // 중클릭 = 패닝
                    isDraggingPreview = true;
                    previewDragStart = e.mousePosition;
                    e.Use();
                    break;
                case EventType.MouseDrag when isDraggingPreview:
                    // 2D 패닝: 마우스 이동 → 카메라 타겟 이동 (직교 뷰에서 상하좌우)
                    float panSensitivity = previewCamDistance * 0.5f / Mathf.Max(previewRect.height, 100f);
                    float deltaZ = -(e.mousePosition.x - previewDragStart.x) * panSensitivity;
                    float deltaY = (e.mousePosition.y - previewDragStart.y) * panSensitivity;
                    previewPanOffset += new Vector2(deltaZ, deltaY);
                    previewDragStart = e.mousePosition;
                    Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 2:
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

            // ★ 디폴트 액터: PC_Hero 우선 선택
            if (actorFiles.Length > 0 && selectedActorIndex < 0)
            {
                selectedActorIndex = 0; // 기본값
                for (int i = 0; i < actorFileNames.Length; i++)
                {
                    if (actorFileNames[i] == "PC_Hero")
                    {
                        selectedActorIndex = i;
                        break;
                    }
                }
            }
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
            InvalidateClipFramesCache(); // ★ 액터 변경 시 클립 캐시 초기화
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

        /// <summary>
        /// 라벨 X 좌표를 클램프하여 좌우 잘림을 방지.
        /// 기본은 중앙 정렬(x - labelW/2)이지만 경계에서는 밀어냄.
        /// </summary>
        private static float ClampLabelX(float tickX, float labelW, float minX, float maxX)
        {
            float centered = tickX - labelW * 0.5f;
            // 왼쪽 잘림 방지
            if (centered < minX) centered = minX;
            // 오른쪽 잘림 방지
            if (centered + labelW > maxX) centered = maxX - labelW;
            return centered;
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
