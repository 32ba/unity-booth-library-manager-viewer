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
        private string[] _categoryOptions = { "All" };
        private int _selectedListIndex;
        private string[] _listOptions = { "All" };
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
            "Name A-Z",
            "Name Z-A",
            "Registered Newest",
            "Registered Oldest",
            "Updated Newest",
            "Updated Oldest",
            "Published Newest",
            "Published Oldest",
            "Shop A-Z",
        };
        private const float ThumbnailSize = 64f;
        private const float TileWidth = 190f;
        private const float TileHeight = 235f;
        private const float TileThumbnailSize = 128f;
        private const float TileSpacing = 8f;
        private const float RowPadding = 4f;
        private const float DragStartThreshold = 6f;

        [MenuItem("Tools/BOOTH Library Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<BoothLibraryViewerWindow>();
            window.titleContent = new GUIContent("BOOTH Library Viewer");
            window.minSize = new Vector2(500, 300);
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
                _categoryOptions = new[] { "All" };
                _selectedCategoryIndex = 0;
                _listOptions = new[] { "All" };
                _selectedListIndex = 0;
                _selectedTags.Clear();
                return;
            }

            var (items, categories, tags, lists) = BoothDatabaseReader.LoadItems();
            _allItems = items;
            _categories = categories;
            _tags = tags;
            _lists = lists;

            var catOptions = new List<string> { "All" };
            catOptions.AddRange(categories);
            _categoryOptions = catOptions.ToArray();
            _selectedCategoryIndex = 0;

            var listOpts = new List<string> { "All" };
            foreach (var l in _lists)
                listOpts.Add(l.Title);
            _listOptions = listOpts.ToArray();
            _selectedListIndex = 0;

            _selectedTags.Clear();

            _searchText = "";

            ApplyFilters();
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
                        .ThenBy(item => item.Id)
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
                        .ThenBy(item => item.Id)
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
            EditorGUILayout.BeginVertical();

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
            if (GUILayout.Button("BOOTH Library Manager を開く", GUILayout.Width(250)))
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
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                ThumbnailCache.Clear();
                Refresh();
            }

            GUILayout.Space(8);

            // Search field
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.MinWidth(100));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();

            GUILayout.Space(8);

            // Category filter
            EditorGUILayout.LabelField("Category:", GUILayout.Width(60));
            EditorGUI.BeginChangeCheck();
            _selectedCategoryIndex = EditorGUILayout.Popup(_selectedCategoryIndex, _categoryOptions, EditorStyles.toolbarPopup, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();

            GUILayout.Space(8);

            // Tag filter
            var tagLabel = _selectedTags.Count > 0 ? $"Tags ({_selectedTags.Count})" : "Tags";
            if (GUILayout.Button(tagLabel, EditorStyles.toolbarDropDown, GUILayout.Width(100)))
            {
                var popup = new TagFilterPopup(_tags, _selectedTags, () =>
                {
                    ApplyFilters();
                    Repaint();
                });
                PopupWindow.Show(GUILayoutUtility.GetLastRect(), popup);
            }

            GUILayout.Space(8);

            // List filter
            EditorGUILayout.LabelField("List:", GUILayout.Width(30));
            EditorGUI.BeginChangeCheck();
            _selectedListIndex = EditorGUILayout.Popup(_selectedListIndex, _listOptions, EditorStyles.toolbarPopup, GUILayout.Width(160));
            if (EditorGUI.EndChangeCheck())
                ApplyFilters();

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("Sort:", GUILayout.Width(32));
            EditorGUI.BeginChangeCheck();
            _sortMode = (SortMode)EditorGUILayout.Popup((int)_sortMode, SortModeLabels, EditorStyles.toolbarPopup, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(SortModePreferenceKey, (int)_sortMode);
                ApplyFilters();
                _scrollPosition = Vector2.zero;
            }

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            _viewMode = (LibraryViewMode)GUILayout.Toolbar((int)_viewMode, new[] { "Tile", "List" }, EditorStyles.toolbarButton, GUILayout.Width(92));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(ViewModePreferenceKey, (int)_viewMode);
                _scrollPosition = Vector2.zero;
            }

            GUILayout.Space(8);

            EditorGUILayout.LabelField($"{_filteredItems.Count} items", GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawItemList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var item in _filteredItems)
            {
                DrawItemRow(item);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawItemTiles()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            var viewWidth = Mathf.Max(position.width - 20f, TileWidth);
            var columnCount = Mathf.Max(1, Mathf.FloorToInt((viewWidth + TileSpacing) / (TileWidth + TileSpacing)));

            for (var i = 0; i < _filteredItems.Count; i += columnCount)
            {
                var expandedItems = new List<BoothItem>();

                EditorGUILayout.BeginHorizontal();
                for (var column = 0; column < columnCount; column++)
                {
                    var itemIndex = i + column;
                    if (itemIndex >= _filteredItems.Count)
                    {
                        GUILayout.Space(TileWidth + TileSpacing);
                        continue;
                    }

                    var item = _filteredItems[itemIndex];
                    DrawItemTile(item);
                    if (item.IsExpanded)
                        expandedItems.Add(item);

                    if (column < columnCount - 1)
                        GUILayout.Space(TileSpacing);
                }
                EditorGUILayout.EndHorizontal();

                foreach (var expandedItem in expandedItems)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(expandedItem.Name, EditorStyles.boldLabel);
                    DrawFolderTree(expandedItem);
                    EditorGUILayout.EndVertical();
                }

                GUILayout.Space(TileSpacing);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawItemTile(BoothItem item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(TileWidth), GUILayout.Height(TileHeight));

            var thumbnail = ThumbnailCache.Get(item.ThumbnailUrl);
            var thumbnailRowRect = GUILayoutUtility.GetRect(TileWidth - RowPadding * 2f, TileThumbnailSize, GUILayout.Width(TileWidth - RowPadding * 2f), GUILayout.Height(TileThumbnailSize));
            var thumbnailRect = new Rect(
                thumbnailRowRect.x + (thumbnailRowRect.width - TileThumbnailSize) * 0.5f,
                thumbnailRowRect.y,
                TileThumbnailSize,
                TileThumbnailSize);

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
            if (GUILayout.Button(arrow + item.Name, nameStyle, GUILayout.Height(38)))
                item.IsExpanded = !item.IsExpanded;

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
            };
            EditorGUILayout.LabelField(item.ShopName, subStyle, GUILayout.Height(16));

            var category = BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName);
            EditorGUILayout.LabelField(category, subStyle, GUILayout.Height(16));

            if (!item.FolderExists && !string.IsNullOrEmpty(item.RegisteredItemId))
            {
                var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0.2f) },
                };
                EditorGUILayout.LabelField("(フォルダ未検出)", warnStyle, GUILayout.Height(16));
            }
            else
            {
                GUILayout.Space(16);
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("BOOTH", EditorStyles.miniButton))
                Application.OpenURL($"https://booth.pm/ja/items/{item.Id}");

            using (new EditorGUI.DisabledScope(!item.FolderExists))
            {
                if (GUILayout.Button("Folder", EditorStyles.miniButton))
                    Process.Start("explorer.exe", "\"" + item.FolderPath.Replace('/', '\\') + "\"");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawItemRow(BoothItem item)
        {
            // Main item row
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Thumbnail
            var thumbnail = ThumbnailCache.Get(item.ThumbnailUrl);
            var thumbRect = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize, GUILayout.Width(ThumbnailSize), GUILayout.Height(ThumbnailSize));
            if (thumbnail != null)
                GUI.DrawTexture(thumbRect, thumbnail, ScaleMode.ScaleToFit);

            GUILayout.Space(8);

            // Info column
            EditorGUILayout.BeginVertical();

            // Row 1: Item name + buttons
            EditorGUILayout.BeginHorizontal();

            // Expand/collapse toggle + item name
            var arrow = item.IsExpanded ? "\u25BC " : "\u25B6 ";
            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = false,
                richText = false,
            };
            if (GUILayout.Button(arrow + item.Name, nameStyle))
            {
                item.IsExpanded = !item.IsExpanded;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("BOOTH", EditorStyles.miniButton, GUILayout.Width(55)))
            {
                Application.OpenURL($"https://booth.pm/ja/items/{item.Id}");
            }

            if (item.FolderExists)
            {
                if (GUILayout.Button("Folder", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    Process.Start("explorer.exe", "\"" + item.FolderPath.Replace('/', '\\') + "\"");
                }
            }

            EditorGUILayout.EndHorizontal();

            // Row 2: Shop name + category
            EditorGUILayout.BeginHorizontal();
            var subStyle = new GUIStyle(EditorStyles.miniLabel) { richText = false };

            var shopAndCategory = item.ShopName;
            var category = BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName);
            if (!string.IsNullOrEmpty(category))
                shopAndCategory += "  |  " + category;

            EditorGUILayout.LabelField(shopAndCategory, subStyle);
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
                EditorGUILayout.LabelField(tagText, tagStyle);
            }

            // Row 4: Folder not found warning
            if (!item.FolderExists && !string.IsNullOrEmpty(item.RegisteredItemId))
            {
                var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.6f, 0.2f) },
                };
                EditorGUILayout.LabelField("(フォルダ未検出)", warnStyle);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // Expanded section: folder tree
            if (item.IsExpanded)
            {
                DrawFolderTree(item);
            }

            EditorGUILayout.EndVertical();
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
                node.IsExpanded = EditorGUILayout.Foldout(node.IsExpanded, node.Name, true);

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
            DragAndDrop.objectReferences = new Object[0];
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
