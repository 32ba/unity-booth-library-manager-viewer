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
        private List<BoothItem> _allItems = new List<BoothItem>();
        private List<BoothItem> _filteredItems = new List<BoothItem>();
        private List<string> _categories = new List<string>();

        private string _searchText = "";
        private int _selectedCategoryIndex;
        private string[] _categoryOptions = { "All" };
        private Vector2 _scrollPosition;
        private bool _isLoaded;

        private const float ThumbnailSize = 64f;
        private const float RowPadding = 4f;

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
            if (!_isLoaded)
                Refresh();
        }

        private void OnDisable()
        {
            ThumbnailCache.Clear();
        }

        private void Refresh()
        {
            _isLoaded = true;

            if (!BoothDatabaseReader.DatabaseExists())
            {
                _allItems.Clear();
                _filteredItems.Clear();
                _categories.Clear();
                _categoryOptions = new[] { "All" };
                _selectedCategoryIndex = 0;
                return;
            }

            var (items, categories) = BoothDatabaseReader.LoadItems();
            _allItems = items;
            _categories = categories;

            var options = new List<string> { "All" };
            options.AddRange(categories);
            _categoryOptions = options.ToArray();
            _selectedCategoryIndex = 0;
            _searchText = "";

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            _filteredItems = _allItems;

            // Category filter
            if (_selectedCategoryIndex > 0 && _selectedCategoryIndex < _categoryOptions.Length)
            {
                var selectedCategory = _categoryOptions[_selectedCategoryIndex];
                _filteredItems = _filteredItems
                    .Where(item => BoothDatabaseReader.FormatCategory(item.ParentCategoryName, item.SubCategoryName) == selectedCategory)
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
        }

        private void OnGUI()
        {
            if (!BoothDatabaseReader.DatabaseExists())
            {
                DrawDatabaseNotFound();
                return;
            }

            DrawToolbar();
            DrawItemList();
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

            GUILayout.FlexibleSpace();

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
            {
                item.FolderTree = FolderTreeBuilder.Build(item.FolderPath);
                item.CurrentViewNode = item.FolderTree;
            }

            if (item.CurrentViewNode == null)
                item.CurrentViewNode = item.FolderTree;

            EditorGUILayout.Space(2);

            // Breadcrumb navigation
            DrawBreadcrumb(item);

            EditorGUILayout.Space(2);

            // Draw separator line
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.x += 20;
            rect.width -= 40;
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));

            EditorGUILayout.Space(2);

            // Current directory contents
            var currentNode = item.CurrentViewNode;

            if (currentNode.Children.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                EditorGUILayout.LabelField("\u30d5\u30a9\u30eb\u30c0\u306f\u7a7a\u3067\u3059", EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
                return;
            }

            foreach (var child in currentNode.Children)
            {
                DrawFileRow(child, item);
            }
        }

        private void DrawBreadcrumb(BoothItem item)
        {
            // Build path from root to current node
            var path = new List<FolderTreeNode>();
            var node = item.CurrentViewNode;
            while (node != null)
            {
                path.Add(node);
                node = node.Parent;
            }
            path.Reverse();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(20);

            var breadcrumbStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Normal,
                padding = new RectOffset(4, 4, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
            };

            var separatorStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            for (int i = 0; i < path.Count; i++)
            {
                var segment = path[i];
                var label = i == 0 ? "\u2302" : segment.Name;

                // Truncate long names in breadcrumb
                if (label.Length > 20)
                    label = label.Substring(0, 17) + "...";

                if (GUILayout.Button(label, breadcrumbStyle))
                {
                    item.CurrentViewNode = segment;
                }

                if (i < path.Count - 1)
                {
                    GUILayout.Label(">", separatorStyle, GUILayout.Width(12));
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFileRow(FolderTreeNode node, BoothItem item)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(24);

            if (node.IsDirectory)
            {
                var folderStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = false,
                    fontSize = 11,
                };

                if (GUILayout.Button("\ud83d\udcc1 " + node.Name, folderStyle))
                {
                    item.CurrentViewNode = node;
                }
            }
            else
            {
                var icon = node.IsUnityPackage ? "\ud83d\udce6" : "\ud83d\udcc4";
                var fileStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = false,
                    fontSize = 11,
                };

                if (GUILayout.Button(icon + " " + node.Name, fileStyle))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = node.FullPath,
                        UseShellExecute = true,
                    });
                }
            }

            GUILayout.FlexibleSpace();

            // File size
            if (!node.IsDirectory)
            {
                var sizeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                };
                EditorGUILayout.LabelField(FormatFileSize(node.FileSize), sizeStyle, GUILayout.Width(70));
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
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
