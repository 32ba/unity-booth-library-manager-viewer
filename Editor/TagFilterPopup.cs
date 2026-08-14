using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BoothLibraryViewer
{
    public class TagFilterPopup : PopupWindowContent
    {
        private readonly List<string> _allTags;
        private readonly HashSet<string> _selectedTags;
        private readonly Action _onChanged;
        private string _searchText = "";
        private Vector2 _scrollPosition;

        public TagFilterPopup(List<string> allTags, HashSet<string> selectedTags, Action onChanged)
        {
            _allTags = allTags;
            _selectedTags = selectedTags;
            _onChanged = onChanged;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(250, 300);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.Space(4);

            // Search field
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
                _scrollPosition = Vector2.zero;

            EditorGUILayout.Space(2);

            // Filtered tag list
            var filtered = string.IsNullOrEmpty(_searchText)
                ? _allTags
                : _allTags.Where(t => t.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                false,
                true,
                GUILayout.MinWidth(0),
                GUILayout.ExpandWidth(true));

            foreach (var tag in filtered)
            {
                var wasSelected = _selectedTags.Contains(tag);
                var isSelected = EditorGUILayout.ToggleLeft(
                    tag,
                    wasSelected,
                    GUILayout.MinWidth(0),
                    GUILayout.ExpandWidth(true));
                if (isSelected != wasSelected)
                {
                    if (isSelected)
                        _selectedTags.Add(tag);
                    else
                        _selectedTags.Remove(tag);
                    _onChanged();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2);
            if (GUILayout.Button("選択をすべて解除"))
            {
                if (_selectedTags.Count > 0)
                {
                    _selectedTags.Clear();
                    _onChanged();
                }
            }
            EditorGUILayout.Space(4);
        }
    }
}
