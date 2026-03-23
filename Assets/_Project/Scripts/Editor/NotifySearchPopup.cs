#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FreeFlowHero.Editor
{
    /// <summary>
    /// 노티파이 추가용 검색 팝업 윈도우.
    /// 타임라인 우클릭 시 마우스 위치에 열리며,
    /// 텍스트 검색으로 노티파이 리스트를 필터링하여 선택할 수 있다.
    /// </summary>
    public class NotifySearchPopup : EditorWindow
    {
        // ─── 항목 정의 ───

        /// <summary>팝업에 표시할 항목 1개</summary>
        public struct PopupItem
        {
            public string Label;        // 표시 이름 (예: "STARTUP 추가")
            public string SearchTag;    // 검색용 태그 (예: "startup 선딜 시작")
            public Action OnSelected;   // 선택 시 콜백
            public bool IsSeparator;    // 구분선 여부
        }

        private List<PopupItem> allItems = new();
        private List<PopupItem> filteredItems = new();
        private string searchText = "";
        private int hoveredIndex = -1;
        private int keyboardIndex = -1;
        private Vector2 scrollPos;
        private bool focusSearchField = true;

        // 외관
        private const float ItemHeight = 24f;
        private const float SeparatorHeight = 8f;
        private const float SearchFieldHeight = 24f;
        private const float PopupWidth = 280f;
        private const float MaxPopupHeight = 400f;

        private GUIStyle itemStyle;
        private GUIStyle itemHoverStyle;
        private GUIStyle separatorStyle;
        private GUIStyle searchFieldStyle;
        private bool stylesInit;

        /// <summary>
        /// 팝업을 마우스 위치에 열기.
        /// </summary>
        /// <param name="items">팝업에 표시할 항목 목록</param>
        public static NotifySearchPopup Show(List<PopupItem> items)
        {
            var popup = CreateInstance<NotifySearchPopup>();
            popup.allItems = items;
            popup.filteredItems = new List<PopupItem>(items);

            // 팝업 크기 계산
            float contentHeight = SearchFieldHeight + 8f; // 검색창 + 여백
            foreach (var item in items)
                contentHeight += item.IsSeparator ? SeparatorHeight : ItemHeight;
            float height = Mathf.Min(contentHeight + 16f, MaxPopupHeight);

            // 마우스 위치에 표시
            Vector2 mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            popup.ShowAsDropDown(new Rect(mousePos.x, mousePos.y, 0, 0),
                new Vector2(PopupWidth, height));

            return popup;
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
        }

        private void InitStyles()
        {
            if (stylesInit) return;
            stylesInit = true;

            itemStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(12, 8, 4, 4),
                fixedHeight = ItemHeight,
                richText = true
            };

            itemHoverStyle = new GUIStyle(itemStyle);
            var hoverTex = new Texture2D(1, 1);
            hoverTex.SetPixel(0, 0, new Color(0.24f, 0.48f, 0.9f, 0.6f));
            hoverTex.Apply();
            itemHoverStyle.normal.background = hoverTex;
            itemHoverStyle.normal.textColor = Color.white;

            separatorStyle = new GUIStyle
            {
                fixedHeight = SeparatorHeight
            };

            searchFieldStyle = new GUIStyle(EditorStyles.toolbarSearchField)
            {
                fixedHeight = SearchFieldHeight,
                margin = new RectOffset(4, 4, 4, 4)
            };
        }

        private void OnGUI()
        {
            InitStyles();

            // 배경
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height),
                EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.22f, 0.22f, 1f)
                    : new Color(0.85f, 0.85f, 0.85f, 1f));

            // 테두리
            var borderRect = new Rect(0, 0, position.width, position.height);
            Handles.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            Handles.DrawSolidRectangleWithOutline(borderRect, Color.clear,
                new Color(0.1f, 0.1f, 0.1f, 0.5f));

            var e = Event.current;

            // ── 검색창 ──
            GUI.SetNextControlName("NotifySearchField");
            string prevSearch = searchText;
            searchText = EditorGUILayout.TextField(searchText, searchFieldStyle);

            if (focusSearchField)
            {
                EditorGUI.FocusTextInControl("NotifySearchField");
                focusSearchField = false;
            }

            // 검색어 변경 시 필터링
            if (searchText != prevSearch)
            {
                FilterItems();
                keyboardIndex = -1;
            }

            // ── 키보드 네비게이션 ──
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.DownArrow:
                        keyboardIndex = NextNonSeparator(keyboardIndex, +1);
                        hoveredIndex = keyboardIndex;
                        e.Use();
                        break;

                    case KeyCode.UpArrow:
                        keyboardIndex = NextNonSeparator(keyboardIndex, -1);
                        hoveredIndex = keyboardIndex;
                        e.Use();
                        break;

                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (keyboardIndex >= 0 && keyboardIndex < filteredItems.Count)
                        {
                            var item = filteredItems[keyboardIndex];
                            if (!item.IsSeparator)
                            {
                                item.OnSelected?.Invoke();
                                Close();
                            }
                        }
                        e.Use();
                        break;

                    case KeyCode.Escape:
                        Close();
                        e.Use();
                        break;
                }
            }

            // ── 항목 리스트 ──
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            for (int i = 0; i < filteredItems.Count; i++)
            {
                var item = filteredItems[i];

                if (item.IsSeparator)
                {
                    GUILayout.Space(2);
                    var sepRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(sepRect,
                        EditorGUIUtility.isProSkin
                            ? new Color(0.4f, 0.4f, 0.4f, 0.5f)
                            : new Color(0.6f, 0.6f, 0.6f, 0.5f));
                    GUILayout.Space(2);
                    continue;
                }

                bool isHovered = (i == hoveredIndex);
                var style = isHovered ? itemHoverStyle : itemStyle;

                var itemRect = GUILayoutUtility.GetRect(
                    new GUIContent(item.Label), style, GUILayout.ExpandWidth(true));

                // 마우스 호버 감지
                if (e.type == EventType.MouseMove && itemRect.Contains(e.mousePosition))
                {
                    hoveredIndex = i;
                    keyboardIndex = i;
                    Repaint();
                }

                // 클릭
                if (e.type == EventType.MouseDown && e.button == 0 && itemRect.Contains(e.mousePosition))
                {
                    item.OnSelected?.Invoke();
                    Close();
                    e.Use();
                    return;
                }

                GUI.Label(itemRect, item.Label, style);
            }

            if (filteredItems.Count == 0 ||
                filteredItems.TrueForAll(x => x.IsSeparator))
            {
                GUILayout.Space(8);
                EditorGUILayout.LabelField("  검색 결과 없음", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();

            // 팝업 밖 클릭 시 닫기
            if (e.type == EventType.MouseDown && !new Rect(0, 0, position.width, position.height).Contains(e.mousePosition))
            {
                Close();
            }

            // 마우스 이동 시 리페인트
            if (e.type == EventType.MouseMove)
                Repaint();
        }

        private void FilterItems()
        {
            filteredItems.Clear();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                filteredItems.AddRange(allItems);
                return;
            }

            string lower = searchText.ToLowerInvariant();

            foreach (var item in allItems)
            {
                if (item.IsSeparator) continue; // 필터링 시 구분선 숨김

                bool match = item.Label.ToLowerInvariant().Contains(lower)
                    || (!string.IsNullOrEmpty(item.SearchTag) && item.SearchTag.ToLowerInvariant().Contains(lower));

                if (match)
                    filteredItems.Add(item);
            }
        }

        /// <summary>다음 비-구분선 인덱스 찾기 (키보드 네비게이션)</summary>
        private int NextNonSeparator(int current, int direction)
        {
            if (filteredItems.Count == 0) return -1;

            int next = current + direction;

            // 범위 체크
            for (int attempts = 0; attempts < filteredItems.Count; attempts++)
            {
                if (next < 0) next = filteredItems.Count - 1;
                if (next >= filteredItems.Count) next = 0;

                if (!filteredItems[next].IsSeparator)
                    return next;

                next += direction;
            }

            return current; // 모두 구분선이면 현재 유지
        }
    }
}
#endif
