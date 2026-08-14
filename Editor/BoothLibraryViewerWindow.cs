using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BoothLibraryViewer
{
    public class BoothLibraryViewerWindow : EditorWindow
    {
        private enum LibraryViewMode
        {
            Tile,
            List,
        }

        private enum SortMode
        {
            NameAscending,
            NameDescending,
            RegisteredNewest,
            RegisteredOldest,
            UpdatedNewest,
            UpdatedOldest,
            PublishedNewest,
            PublishedOldest,
            ShopAscending,
        }

        private List<BoothItem> _allItems = new List<BoothItem>();
        private List<BoothItem> _filteredItems = new List<BoothItem>();
        private List<string> _categories = new List<string>();
        private List<string> _tags = new List<string>();
        private List<BoothList> _lists = new List<BoothList>();

        private string _searchText = "";
        private int _selectedCategoryIndex;
        private string[] _categoryOptions = { "すべて" };
        private int _selectedListIndex;
        private string[] _listOptions = { "すべて" };
        private HashSet<string> _selectedTags = new HashSet<string>();
        private Vector2 _scrollPosition;
        private string _pressedFilePath;
        private Vector2 _pressedFileMousePosition;
        private LibraryViewMode _viewMode = LibraryViewMode.Tile;
        private SortMode _sortMode = SortMode.NameAscending;

        private const string ViewModePreferenceKey = "BoothLibraryViewer.ViewMode";
        private const string SortModePreferenceKey = "BoothLibraryViewer.SortMode";
        private static readonly string[] SortModeLabels =
        {
            "名前 A-Z",
            "名前 Z-A",
            "登録が新しい順",
            "登録が古い順",
            "更新が新しい順",
            "更新が古い順",
            "公開が新しい順",
            "公開が古い順",
            "ショップ A-Z",
        };
        private const float ThumbnailSize = 64f;
        private const float TileWidth = 190f;
        private const float TileHeight = 235f;
        private const float TileThumbnailSize = 128f;
        private const float TileSpacing = 8f;
        private const float RowPadding = 4f;
        private const float DragStartThreshold = 6f;
        private const float CompactToolbarWidth = 440f;
        private const float CompactActiveFilterWidth = 860f;
        private const float CompactListWidth = 360f;

        [MenuItem("Tools/BOOTH Library Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<BoothLibraryViewerWindow>();
            window.titleContent = new GUIContent("BOOTH Library Viewer");
            window.minSize = new Vector2(280, 300);
            window.Show();
        }

        private void OnEnable()
        {
            ReleaseChecker.OnUpdateCheckCompleted += Repaint;
            _viewMode = ReadEnumPreference(ViewModePreferenceKey, LibraryViewMode.Tile);
            _sortMode = ReadEnumPreference(SortModePreferenceKey, SortMode.NameAscending);
            Refresh();
        }

        private void OnDisable()
        {
            ReleaseChecker.OnUpdateCheckCompleted -= Repaint;
            ThumbnailCache.Clear();
        }

        private void Refresh()
        {
            if (!BoothDatabaseReader.DatabaseExists())
            {
                _allItems.Clear();
                _filteredItems.Clear();
                _categories.Clear();
                _tags.Clear();
                _lists.Clear();
                _categoryOptions = new[] { "すべて" };
                _selectedCategoryIndex = 0;
                _listOptions = new[] { "すべて" };
                _selectedListIndex = 0;
                _selectedTags.Clear();
                return;
            }

            var selectedCategory = GetSelectedCategory();
            var selectedList = GetSelectedList();
            var selectedTags = new HashSet<string>(_selectedTags);
            var expandedItemKey = _allItems.FirstOrDefault(item => item.IsExpanded)?.RegisteredItemId;

            var (items, categories, tags, lists) = BoothDatabaseReader.LoadItems();
            _allItems = items;
            _categories = categories;
            _tags = tags;
            _lists = lists;

            var catOptions = new List<string> { "すべて" };
            catOptions.AddRange(categories);
            _categoryOptions = catOptions.ToArray();
            _selectedCategoryIndex = string.IsNullOrEmpty(selectedCategory)
                ? 0
                : Mathf.Max(0, Array.IndexOf(_categoryOptions, selectedCategory));

            var listOpts = new List<string> { "すべて" };
            foreach (var l in _lists)
                listOpts.Add(l.Title);
            _listOptions = listOpts.ToArray();
            _selectedListIndex = 0;
            if (selectedList != null)
            {
                var listIndex = _lists.FindIndex(list =>
                    list.Id == selectedList.Id && list.IsSmart == selectedList.IsSmart);
                if (listIndex >= 0)
                    _selectedListIndex = listIndex + 1;
            }

            _selectedTags = new HashSet<string>(selectedTags.Where(tag => _tags.Contains(tag)));

            if (!string.IsNullOrEmpty(expandedItemKey))
            {
                var expandedItem = _allItems.FirstOrDefault(item => item.RegisteredItemId == expandedItemKey);
                if (expandedItem != null)
                    expandedItem.IsExpanded = true;
            }

            ApplyFilters();
        }

        private string GetSelectedCategory()
        {
            return _selectedCategoryIndex > 0 && _selectedCategoryIndex < _categoryOptions.Length
                ? _categoryOptions[_selectedCategoryIndex]
                : null;
        }

        private BoothList GetSelectedList()
        {
            return _selectedListIndex > 0 && _selectedListIndex <= _lists.Count
                ? _lists[_selectedListIndex - 1]
                : null;
        }

        private void ApplyFilters()
        {
            _filteredItems = _allItems;

            // List filter
            if (_selectedListIndex > 0 && _selectedListIndex <= _lists.Count)
            {
                var selectedList = _lists[_selectedListIndex - 1];
                if (selectedList.IsSmart)
                {
                    _filteredItems = _filteredItems
                        .Where(item => BoothDatabaseReader.MatchesSmartList(item, selectedList.Criteria))
                        .ToList();
                }
                else
                {
                    _filteredItems = _filteredItems
                        .Where(item => selectedList.ItemIds.Contains(item.RegisteredItemId))
                        .ToList();
                }
            }

            // Category filter
            if (_selectedCategoryIndex > 0 && _selectedCategoryIndex < _categoryOptions.Length)
            {
                var selectedCategory = _categoryOptions[_selectedCategoryIndex];
                _filteredItems = _filteredItems
                    .Where(item => BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName) == selectedCategory)
                    .ToList();
            }

            // Tag filter (OR: item must have at least one selected tag)
            if (_selectedTags.Count > 0)
            {
                _filteredItems = _filteredItems
                    .Where(item => item.Tags.Any(tag => _selectedTags.Contains(tag)))
                    .ToList();
            }

            // Search filter
            if (!string.IsNullOrEmpty(_searchText))
            {
                var search = _searchText.ToLowerInvariant();
                _filteredItems = _filteredItems
                    .Where(item =>
                        (item.Name != null && item.Name.ToLowerInvariant().Contains(search)) ||
                        (item.ShopName != null && item.ShopName.ToLowerInvariant().Contains(search)))
                    .ToList();
            }

            ApplySort();
        }

        private void ApplySort()
        {
            switch (_sortMode)
            {
                case SortMode.NameDescending:
                    _filteredItems = _filteredItems
                        .OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(item => item.RegisteredItemId, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                case SortMode.RegisteredNewest:
                    _filteredItems = _filteredItems
                        .OrderByDescending(item => DateOrMin(ParseDate(item.RegisteredCreatedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.RegisteredOldest:
                    _filteredItems = _filteredItems
                        .OrderBy(item => DateOrMax(ParseDate(item.RegisteredCreatedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.UpdatedNewest:
                    _filteredItems = _filteredItems
                        .OrderByDescending(item => DateOrMin(ParseDate(item.RegisteredUpdatedAt) ?? ParseDate(item.UpdatedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.UpdatedOldest:
                    _filteredItems = _filteredItems
                        .OrderBy(item => DateOrMax(ParseDate(item.RegisteredUpdatedAt) ?? ParseDate(item.UpdatedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.PublishedNewest:
                    _filteredItems = _filteredItems
                        .OrderByDescending(item => DateOrMin(ParseDate(item.PublishedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.PublishedOldest:
                    _filteredItems = _filteredItems
                        .OrderBy(item => DateOrMax(ParseDate(item.PublishedAt)))
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.ShopAscending:
                    _filteredItems = _filteredItems
                        .OrderBy(item => item.ShopName, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    break;

                case SortMode.NameAscending:
                default:
                    _filteredItems = _filteredItems
                        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(item => item.RegisteredItemId, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
            }
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (DateTimeOffset.TryParse(value, out var date))
                return date;

            return null;
        }

        private static DateTimeOffset DateOrMin(DateTimeOffset? value)
        {
            return value ?? DateTimeOffset.MinValue;
        }

        private static DateTimeOffset DateOrMax(DateTimeOffset? value)
        {
            return value ?? DateTimeOffset.MaxValue;
        }

        private static T ReadEnumPreference<T>(string key, T defaultValue) where T : struct
        {
            var value = EditorPrefs.GetInt(key, Convert.ToInt32(defaultValue));
            return System.Enum.IsDefined(typeof(T), value) ? (T)(object)value : defaultValue;
        }

        private void OnGUI()
        {
            if (!BoothDatabaseReader.DatabaseExists())
            {
                DrawDatabaseNotFound();
                return;
            }

            DrawToolbar();
            DrawUpdateNotification();
            if (_viewMode == LibraryViewMode.Tile)
                DrawItemTiles();
            else
                DrawItemList();
        }

        private static void DrawUpdateNotification()
        {
            if (!ReleaseChecker.HasNewVersion || string.IsNullOrEmpty(ReleaseChecker.LatestVersion))
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("新しいバージョンがあります", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"{VersionUtility.FormatVersion(ReleaseChecker.GetCurrentVersion())} -> {VersionUtility.FormatVersion(ReleaseChecker.LatestVersion)}");

                if (GUILayout.Button("リリースページを開く"))
                    ReleaseChecker.OpenReleasePage();
            }
        }

        private void DrawDatabaseNotFound()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(320),
                GUILayout.ExpandWidth(true));

            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                wordWrap = true,
            };

            EditorGUILayout.LabelField("BOOTH Library Manager のデータベースが見つかりません。", style);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("BOOTH Library Manager をインストールし、アセットを登録してください。", style);
            EditorGUILayout.Space(16);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                "BOOTH Library Manager を開く",
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(250),
                GUILayout.ExpandWidth(true)))
            {
                Application.OpenURL("https://booth.pm/ja/items/4905899");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("再読み込み", GUILayout.Width(120)))
            {
                Refresh();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void DrawToolbar()
        {
            DrawSearchToolbar();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawCategoryFilter();

            if (position.width >= CompactToolbarWidth)
            {
                GUILayout.Space(8);
                DrawTagFilterButton();
                GUILayout.Space(8);
                DrawListFilter();
            }

            EditorGUILayout.EndHorizontal();

            if (position.width < CompactToolbarWidth)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                DrawTagFilterButton();
                GUILayout.Space(8);
                DrawListFilter();
                EditorGUILayout.EndHorizontal();
            }

            DrawSortToolbar();
            DrawActiveFilters();
        }

        private void DrawSearchToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("更新", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                ThumbnailCache.Clear();
                Refresh();
            }

            GUILayout.Space(8);

            EditorGUILayout.LabelField("検索", GUILayout.Width(30));
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField(
                _searchText,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();

            EditorGUILayout.LabelField($"{_filteredItems.Count}件", GUILayout.Width(52));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryFilter()
        {
            EditorGUILayout.LabelField("カテゴリ", GUILayout.Width(48));
            EditorGUI.BeginChangeCheck();
            _selectedCategoryIndex = EditorGUILayout.Popup(
                _selectedCategoryIndex,
                _categoryOptions,
                EditorStyles.toolbarPopup,
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(220),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();
        }

        private void DrawTagFilterButton()
        {
            var tagLabel = _selectedTags.Count > 0 ? $"タグ ({_selectedTags.Count})" : "タグ";
            if (GUILayout.Button(tagLabel, EditorStyles.toolbarDropDown, GUILayout.Width(100)))
            {
                var popup = new TagFilterPopup(_tags, _selectedTags, () =>
                {
                    ApplyFilters();
                    Repaint();
                });
                PopupWindow.Show(GUILayoutUtility.GetLastRect(), popup);
            }
        }

        private void DrawListFilter()
        {
            EditorGUILayout.LabelField("リスト", GUILayout.Width(34));
            EditorGUI.BeginChangeCheck();
            _selectedListIndex = EditorGUILayout.Popup(
                _selectedListIndex,
                _listOptions,
                EditorStyles.toolbarPopup,
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(180),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();
        }

        private void DrawSortToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("並び順", GUILayout.Width(42));
            EditorGUI.BeginChangeCheck();
            _sortMode = (SortMode)EditorGUILayout.Popup(
                (int)_sortMode,
                SortModeLabels,
                EditorStyles.toolbarPopup,
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(150),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(SortModePreferenceKey, (int)_sortMode);
                ApplyFilters();
            }

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            _viewMode = (LibraryViewMode)GUILayout.Toolbar(
                (int)_viewMode,
                new[] { "タイル", "リスト" },
                EditorStyles.toolbarButton,
                GUILayout.Width(104));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(ViewModePreferenceKey, (int)_viewMode);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawActiveFilters()
        {
            if (!HasActiveFilters())
                return;

            if (position.width < CompactActiveFilterWidth)
            {
                var summary = new List<string>();
                if (_selectedCategoryIndex > 0)
                    summary.Add($"カテゴリ: {GetSelectedCategory()}");
                if (_selectedListIndex > 0)
                    summary.Add($"リスト: {GetSelectedList()?.Title}");
                if (_selectedTags.Count > 0)
                    summary.Add($"タグ: {_selectedTags.Count}件");
                if (!string.IsNullOrEmpty(_searchText))
                    summary.Add("検索語あり");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("絞り込み中", EditorStyles.miniBoldLabel, GUILayout.Width(64));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("すべて解除", EditorStyles.miniButton, GUILayout.Width(72)))
                    ClearFilters();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    string.Join(" / ", summary),
                    EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("絞り込み中", EditorStyles.miniBoldLabel, GUILayout.Width(64));

            if (_selectedCategoryIndex > 0 &&
                GUILayout.Button(
                    $"カテゴリ: {GetSelectedCategory()}  ×",
                    EditorStyles.miniButton,
                    GUILayout.MinWidth(0),
                    GUILayout.MaxWidth(180)))
            {
                _selectedCategoryIndex = 0;
                ApplyFilters();
            }

            var selectedList = GetSelectedList();
            if (selectedList != null &&
                GUILayout.Button(
                    $"リスト: {selectedList.Title}  ×",
                    EditorStyles.miniButton,
                    GUILayout.MinWidth(0),
                    GUILayout.MaxWidth(160)))
            {
                _selectedListIndex = 0;
                ApplyFilters();
            }

            foreach (var tag in _selectedTags.Take(3).ToList())
            {
                if (GUILayout.Button(
                    $"{tag}  ×",
                    EditorStyles.miniButton,
                    GUILayout.MinWidth(0),
                    GUILayout.MaxWidth(110)))
                {
                    _selectedTags.Remove(tag);
                    ApplyFilters();
                }
            }

            if (_selectedTags.Count > 3)
                EditorGUILayout.LabelField($"+{_selectedTags.Count - 3}", EditorStyles.miniLabel, GUILayout.Width(28));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("すべて解除", EditorStyles.miniButton, GUILayout.Width(72)))
                ClearFilters();

            EditorGUILayout.EndHorizontal();
        }

        private bool HasActiveFilters()
        {
            return !string.IsNullOrEmpty(_searchText) ||
                   _selectedCategoryIndex > 0 ||
                   _selectedListIndex > 0 ||
                   _selectedTags.Count > 0;
        }

        private void ClearFilters()
        {
            _searchText = "";
            _selectedCategoryIndex = 0;
            _selectedListIndex = 0;
            _selectedTags.Clear();
            ApplyFilters();
        }

        private void DrawItemList()
        {
            if (_filteredItems.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                false,
                true,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            foreach (var item in _filteredItems)
            {
                DrawItemRow(item);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawItemTiles()
        {
            if (_filteredItems.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                false,
                true,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            var viewWidth = Mathf.Max(position.width - 20f, 1f);
            var columnCount = Mathf.Max(1, Mathf.FloorToInt((viewWidth + TileSpacing) / (TileWidth + TileSpacing)));
            var tileWidth = columnCount == 1 ? viewWidth : TileWidth;
            var thumbnailSize = Mathf.Max(1f, Mathf.Min(TileThumbnailSize, tileWidth - RowPadding * 2f));

            for (var i = 0; i < _filteredItems.Count; i += columnCount)
            {
                var expandedItems = new List<BoothItem>();

                EditorGUILayout.BeginHorizontal();
                for (var column = 0; column < columnCount; column++)
                {
                    var itemIndex = i + column;
                    if (itemIndex >= _filteredItems.Count)
                    {
                        GUILayout.Space(tileWidth + TileSpacing);
                        continue;
                    }

                    var item = _filteredItems[itemIndex];
                    DrawItemTile(item, tileWidth, thumbnailSize);
                    if (item.IsExpanded)
                        expandedItems.Add(item);

                    if (column < columnCount - 1)
                        GUILayout.Space(TileSpacing);
                }
                EditorGUILayout.EndHorizontal();

                foreach (var expandedItem in expandedItems)
                {
                    EditorGUILayout.BeginVertical(
                        EditorStyles.helpBox,
                        GUILayout.MinWidth(0),
                        GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(
                        expandedItem.Name,
                        EditorStyles.wordWrappedLabel,
                        GUILayout.MinWidth(0),
                        GUILayout.ExpandWidth(true));
                    DrawItemDetails(expandedItem);
                    EditorGUILayout.EndVertical();
                }

                GUILayout.Space(TileSpacing);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(
                GUILayout.MinWidth(0),
                GUILayout.MaxWidth(320),
                GUILayout.ExpandWidth(true));

            var title = _allItems.Count == 0
                ? "登録されているアイテムがありません"
                : "条件に一致するアイテムがありません";
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            EditorGUILayout.LabelField(title, style);

            if (HasActiveFilters())
            {
                EditorGUILayout.Space(8);
                if (GUILayout.Button("絞り込みをすべて解除"))
                    ClearFilters();
            }

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void DrawItemTile(BoothItem item, float tileWidth, float thumbnailSize)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.MinWidth(0),
                GUILayout.Width(tileWidth),
                GUILayout.Height(TileHeight));

            var thumbnail = GetThumbnail(item);
            var thumbnailRowWidth = Mathf.Max(1f, tileWidth - RowPadding * 2f);
            var thumbnailRowRect = GUILayoutUtility.GetRect(
                thumbnailRowWidth,
                thumbnailSize,
                GUILayout.Width(thumbnailRowWidth),
                GUILayout.Height(thumbnailSize));
            var thumbnailRect = new Rect(
                thumbnailRowRect.x + (thumbnailRowRect.width - thumbnailSize) * 0.5f,
                thumbnailRowRect.y,
                thumbnailSize,
                thumbnailSize);

            if (thumbnail != null)
                GUI.DrawTexture(thumbnailRect, thumbnail, ScaleMode.ScaleToFit);

            GUILayout.Space(4);

            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = false,
            };
            var arrow = item.IsExpanded ? "\u25BC " : "\u25B6 ";
            if (GUILayout.Button(
                arrow + item.Name,
                nameStyle,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(38)))
                ToggleItemExpansion(item);

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
            };
            EditorGUILayout.LabelField(
                item.ShopName,
                subStyle,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(16));

            var category = BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName);
            EditorGUILayout.LabelField(
                category,
                subStyle,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(16));

            if (!item.FolderExists && !string.IsNullOrEmpty(item.RegisteredItemId))
            {
                var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0.2f) },
                };
                EditorGUILayout.LabelField(
                    "(フォルダ未検出)",
                    warnStyle,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(16));
            }
            else
            {
                GUILayout.Space(16);
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
            if (item.HasBoothPage && GUILayout.Button("BOOTH", EditorStyles.miniButton, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
                Application.OpenURL($"https://booth.pm/ja/items/{item.Id}");

            using (new EditorGUI.DisabledScope(!item.FolderExists))
            {
                if (GUILayout.Button("フォルダ", EditorStyles.miniButton, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
                    Process.Start("explorer.exe", "\"" + item.FolderPath.Replace('/', '\\') + "\"");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawItemRow(BoothItem item)
        {
            // Main item row
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));

            // Thumbnail
            var thumbnail = GetThumbnail(item);
            var thumbRect = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize));
            if (thumbnail != null)
                GUI.DrawTexture(thumbRect, thumbnail, ScaleMode.ScaleToFit);

            GUILayout.Space(8);

            // Info column
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));

            // Row 1: Item name + buttons
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));

            // Expand/collapse toggle + item name
            var arrow = item.IsExpanded ? "\u25BC " : "\u25B6 ";
            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
            };
            if (GUILayout.Button(
                arrow + item.Name,
                nameStyle,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true)))
                ToggleItemExpansion(item);

            if (position.width >= CompactListWidth)
            {
                GUILayout.FlexibleSpace();
                DrawItemActionButtons(item);
            }

            EditorGUILayout.EndHorizontal();

            if (position.width < CompactListWidth)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                DrawItemActionButtons(item);
                EditorGUILayout.EndHorizontal();
            }

            // Row 2: Shop name + category
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = false,
            };

            var shopAndCategory = item.ShopName;
            var category = BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName);
            if (!string.IsNullOrEmpty(category))
                shopAndCategory += "  |  " + category;

            EditorGUILayout.LabelField(
                shopAndCategory,
                subStyle,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            // Row 3: Tags
            if (item.Tags.Count > 0)
            {
                var tagText = string.Join(", ", item.Tags);
                var tagStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    normal = { textColor = new Color(0.5f, 0.7f, 1f) },
                };
                EditorGUILayout.LabelField(
                    tagText,
                    tagStyle,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
            }

            // Row 4: Folder not found warning
            if (!item.FolderExists && !string.IsNullOrEmpty(item.RegisteredItemId))
            {
                var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0.2f) },
                };
                EditorGUILayout.LabelField(
                    "(フォルダ未検出)",
                    warnStyle,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // Expanded section: folder tree
            if (item.IsExpanded)
            {
                DrawItemDetails(item);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawItemActionButtons(BoothItem item)
        {
            if (item.HasBoothPage && GUILayout.Button("BOOTH", EditorStyles.miniButton, GUILayout.Width(55)))
            {
                Application.OpenURL($"https://booth.pm/ja/items/{item.Id}");
            }

            if (item.FolderExists &&
                GUILayout.Button("フォルダ", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                Process.Start("explorer.exe", "\"" + item.FolderPath.Replace('/', '\\') + "\"");
            }
        }

        private static Texture2D GetThumbnail(BoothItem item)
        {
            return item.IsUserItem
                ? ThumbnailCache.GetLocal(item.ThumbnailPath)
                : ThumbnailCache.Get(item.ThumbnailUrl);
        }

        private void ToggleItemExpansion(BoothItem item)
        {
            var willExpand = !item.IsExpanded;
            foreach (var otherItem in _allItems)
                otherItem.IsExpanded = false;

            item.IsExpanded = willExpand;
            if (willExpand)
                EnsureUnityPackages(item);
        }

        private static void EnsureUnityPackages(BoothItem item)
        {
            if (item.UnityPackagesLoaded)
                return;

            item.UnityPackagesLoaded = true;
            item.UnityPackages = item.FolderExists
                ? UnityPackageFinder.Find(item.FolderPath)
                : new List<string>();
        }

        private void DrawItemDetails(BoothItem item)
        {
            EnsureUnityPackages(item);
            DrawUnityPackages(item);
            DrawFolderTree(item);
        }

        private static void DrawUnityPackages(BoothItem item)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Unityパッケージ", EditorStyles.boldLabel);

            if (!item.FolderExists)
            {
                EditorGUILayout.LabelField("商品フォルダが見つからないため、インポートできません。", EditorStyles.miniLabel);
                return;
            }

            if (item.UnityPackages.Count == 0)
            {
                EditorGUILayout.LabelField(
                    ".unitypackage は見つかりませんでした。下のファイル一覧から個別に開くかドラッグできます。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (var packagePath in item.UnityPackages)
            {
                EditorGUILayout.BeginHorizontal(
                    EditorStyles.helpBox,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(
                    UnityPackageFinder.GetDisplayName(packagePath),
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("インポート…", GUILayout.Width(88)))
                    AssetDatabase.ImportPackage(packagePath, true);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFolderTree(BoothItem item)
        {
            if (!item.FolderExists)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("\u30d5\u30a9\u30eb\u30c0\u304c\u898b\u3064\u304b\u308a\u307e\u305b\u3093", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            // Build tree lazily on first expand
            if (item.FolderTree == null)
                item.FolderTree = FolderTreeBuilder.Build(item.FolderPath);

            if (item.FolderTree.Children.Count == 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("\u30d5\u30a9\u30eb\u30c0\u306f\u7a7a\u3067\u3059", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ファイル", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "クリックで開く・Projectウィンドウへドラッグして取り込む",
                EditorStyles.wordWrappedMiniLabel);

            var savedIndent = EditorGUI.indentLevel;

            foreach (var child in item.FolderTree.Children)
            {
                DrawTreeNode(child, 1);
            }

            EditorGUI.indentLevel = savedIndent;
        }

        private void DrawTreeNode(FolderTreeNode node, int depth)
        {
            EditorGUI.indentLevel = depth;

            if (node.IsDirectory)
            {
                EditorGUI.BeginChangeCheck();
                node.IsExpanded = EditorGUILayout.Foldout(
                    node.IsExpanded,
                    node.Name,
                    true);
                if (EditorGUI.EndChangeCheck() && node.IsExpanded)
                    FolderTreeBuilder.EnsureChildren(node);

                if (node.IsExpanded)
                {
                    foreach (var child in node.Children)
                    {
                        DrawTreeNode(child, depth + 1);
                    }
                }
            }
            else
            {
                var rect = EditorGUILayout.GetControlRect();
                rect = EditorGUI.IndentedRect(rect);

                // Hover highlight
                var isHovered = rect.Contains(Event.current.mousePosition);
                if (isHovered)
                {
                    EditorGUI.DrawRect(rect, new Color(0.172f, 0.365f, 0.529f, 0.5f));
                    Repaint();
                }

                // Name area (left) and size area (right)
                var sizeWidth = 70f;
                var nameRect = new Rect(rect.x, rect.y, rect.width - sizeWidth - 8, rect.height);
                var sizeRect = new Rect(rect.xMax - sizeWidth, rect.y, sizeWidth, rect.height);

                HandleFileInteraction(nameRect, node);
                GUI.Label(nameRect, node.Name, EditorStyles.label);

                // File size (right-aligned, dimmed)
                var sizeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                };
                GUI.Label(sizeRect, FormatFileSize(node.FileSize), sizeStyle);
            }
        }

        private void HandleFileInteraction(Rect rect, FolderTreeNode node)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            var currentEvent = Event.current;
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (currentEvent.button != 0 || !rect.Contains(currentEvent.mousePosition))
                        return;

                    _pressedFilePath = node.FullPath;
                    _pressedFileMousePosition = currentEvent.mousePosition;
                    currentEvent.Use();
                    return;

                case EventType.MouseDrag:
                    if (currentEvent.button != 0 || _pressedFilePath != node.FullPath)
                        return;

                    if ((currentEvent.mousePosition - _pressedFileMousePosition).sqrMagnitude < DragStartThreshold * DragStartThreshold)
                        return;

                    StartProjectDrag(node);
                    ResetFileInteraction();
                    currentEvent.Use();
                    return;

                case EventType.MouseUp:
                    if (currentEvent.button != 0 || _pressedFilePath != node.FullPath)
                        return;

                    if (rect.Contains(currentEvent.mousePosition))
                        OpenPath(node.FullPath);

                    ResetFileInteraction();
                    currentEvent.Use();
                    return;

                case EventType.MouseLeaveWindow:
                    if (_pressedFilePath == node.FullPath)
                        ResetFileInteraction();
                    return;
            }
        }

        private static void StartProjectDrag(FolderTreeNode node)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[0];
            DragAndDrop.paths = new[] { node.FullPath };
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            DragAndDrop.StartDrag(node.Name);
        }

        private static void OpenPath(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }

        private void ResetFileInteraction()
        {
            _pressedFilePath = null;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
