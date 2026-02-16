using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SQLite;
using UnityEngine;

namespace BoothLibraryViewer
{
    public static class BoothDatabaseReader
    {
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pm.booth.library-manager",
            "data.db");

        public static bool DatabaseExists()
        {
            return File.Exists(DbPath);
        }

        public static string GetDatabasePath()
        {
            return DbPath;
        }

        public static (List<BoothItem> items, List<string> categories, List<string> tags, List<BoothList> lists) LoadItems()
        {
            var items = new List<BoothItem>();
            var categorySet = new HashSet<string>();
            var tagSet = new HashSet<string>();
            var lists = new List<BoothList>();

            if (!DatabaseExists())
                return (items, new List<string>(), new List<string>(), lists);

            try
            {
                var itemDirectoryPath = ReadItemDirectoryPath(DbPath);

                using (var db = new SQLiteConnection(DbPath, SQLiteOpenFlags.ReadOnly))
                {
                    // Query all registered items with booth_items, shops, and categories
                    var query = @"
                        SELECT
                            ri.id AS registered_item_id,
                            bi.id AS booth_item_id,
                            bi.name AS item_name,
                            bi.shop_subdomain,
                            s.name AS shop_name,
                            bi.thumbnail_url,
                            sc.id AS sub_category_id,
                            sc.name AS sub_category_name,
                            sc.parent_category_id AS parent_category_id,
                            pc.name AS parent_category_name
                        FROM registered_items ri
                        LEFT JOIN booth_items bi ON ri.booth_item_id = bi.id
                        LEFT JOIN shops s ON bi.shop_subdomain = s.subdomain
                        LEFT JOIN sub_categories sc ON bi.sub_category = sc.id
                        LEFT JOIN parent_categories pc ON sc.parent_category_id = pc.id
                        ORDER BY bi.name";

                    var rows = db.Query<ItemRow>(query);

                    // Query all tag relations
                    var tagQuery = @"
                        SELECT booth_item_id, tag
                        FROM booth_item_tag_relations
                        ORDER BY booth_item_id";
                    var tagRows = db.Query<TagRow>(tagQuery);

                    // Build tag lookup
                    var tagLookup = new Dictionary<int, List<string>>();
                    foreach (var tr in tagRows)
                    {
                        if (!tagLookup.ContainsKey(tr.booth_item_id))
                            tagLookup[tr.booth_item_id] = new List<string>();
                        tagLookup[tr.booth_item_id].Add(tr.tag);
                    }

                    foreach (var row in rows)
                    {
                        var item = new BoothItem
                        {
                            Id = row.booth_item_id,
                            Name = row.item_name ?? "(Unknown)",
                            ShopSubdomain = row.shop_subdomain ?? "",
                            ShopName = row.shop_name ?? "",
                            ThumbnailUrl = row.thumbnail_url,
                            SubCategoryId = row.sub_category_id,
                            SubCategoryName = row.sub_category_name ?? "",
                            ParentCategoryId = row.parent_category_id,
                            ParentCategoryName = row.parent_category_name ?? "",
                            RegisteredItemId = row.registered_item_id,
                        };

                        // Set folder path
                        if (!string.IsNullOrEmpty(itemDirectoryPath) && !string.IsNullOrEmpty(item.RegisteredItemId))
                        {
                            item.FolderPath = Path.Combine(itemDirectoryPath, item.RegisteredItemId);
                            item.FolderExists = Directory.Exists(item.FolderPath);
                        }

                        // Set tags
                        if (tagLookup.TryGetValue(item.Id, out var tags))
                        {
                            item.Tags = tags;
                            foreach (var tag in tags)
                                tagSet.Add(tag);
                        }

                        // Find .unitypackage files
                        if (item.FolderExists)
                            item.UnityPackages = UnityPackageFinder.Find(item.FolderPath);

                        // Collect category for filter
                        var categoryDisplay = FormatCategory(item.ParentCategoryName, item.SubCategoryName);
                        if (!string.IsNullOrEmpty(categoryDisplay))
                            categorySet.Add(categoryDisplay);

                        items.Add(item);
                    }

                    // Load manual lists
                    var listRows = db.Query<ListRow>("SELECT id, title FROM lists ORDER BY title");
                    var listItemRows = db.Query<ListItemRow>("SELECT list_id, item_id FROM list_items");
                    var listItemLookup = new Dictionary<int, HashSet<string>>();
                    foreach (var li in listItemRows)
                    {
                        if (!listItemLookup.ContainsKey(li.list_id))
                            listItemLookup[li.list_id] = new HashSet<string>();
                        listItemLookup[li.list_id].Add(li.item_id);
                    }
                    foreach (var lr in listRows)
                    {
                        var boothList = new BoothList
                        {
                            Id = lr.id,
                            Title = lr.title ?? "(Untitled)",
                            IsSmart = false,
                        };
                        if (listItemLookup.TryGetValue(lr.id, out var ids))
                            boothList.ItemIds = ids;
                        lists.Add(boothList);
                    }

                    // Load smart lists
                    var smartListRows = db.Query<SmartListRow>("SELECT id, title FROM smart_lists ORDER BY title");
                    var criteriaRows = db.Query<SmartListCriteriaRow>(
                        "SELECT id, smart_list_id, text, category_id, subcategory_id FROM smart_list_criteria");
                    var smartTagRows = db.Query<SmartListTagRow>(
                        "SELECT id, smart_list_id, tag FROM smart_list_tags");

                    var criteriaLookup = new Dictionary<int, SmartListCriteriaRow>();
                    foreach (var cr in criteriaRows)
                    {
                        // Use the first criteria row per smart list
                        if (!criteriaLookup.ContainsKey(cr.smart_list_id))
                            criteriaLookup[cr.smart_list_id] = cr;
                    }
                    var smartTagLookup = new Dictionary<int, List<string>>();
                    foreach (var st in smartTagRows)
                    {
                        if (!smartTagLookup.ContainsKey(st.smart_list_id))
                            smartTagLookup[st.smart_list_id] = new List<string>();
                        smartTagLookup[st.smart_list_id].Add(st.tag);
                    }

                    foreach (var sl in smartListRows)
                    {
                        var criteria = new SmartListCriteria();
                        if (criteriaLookup.TryGetValue(sl.id, out var cr))
                        {
                            criteria.Text = cr.text;
                            criteria.CategoryId = cr.category_id;
                            criteria.SubcategoryId = cr.subcategory_id;
                        }
                        if (smartTagLookup.TryGetValue(sl.id, out var stags))
                            criteria.Tags = stags;

                        lists.Add(new BoothList
                        {
                            Id = sl.id,
                            Title = sl.title ?? "(Untitled)",
                            IsSmart = true,
                            Criteria = criteria,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BOOTH Library Viewer] Failed to read database: {e.Message}\n{e.StackTrace}");
            }

            var categories = new List<string>(categorySet);
            categories.Sort();
            var tagList = new List<string>(tagSet);
            tagList.Sort();
            return (items, categories, tagList, lists);
        }

        public static string FormatCategory(string parentCategory, string subCategory)
        {
            if (string.IsNullOrEmpty(parentCategory) && string.IsNullOrEmpty(subCategory))
                return "";
            if (string.IsNullOrEmpty(parentCategory))
                return subCategory;
            if (string.IsNullOrEmpty(subCategory))
                return parentCategory;
            return $"{parentCategory} > {subCategory}";
        }

        private static string ReadItemDirectoryPath(string dbPath)
        {
            try
            {
                using (var db = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadOnly))
                {
                    var query = "SELECT item_directory_path FROM preferences LIMIT 1";
                    var stmt = db.CreateCommand(query);
                    var rows = stmt.ExecuteDeferredQuery<PreferencesRow>();
                    foreach (var row in rows)
                    {
                        if (row.item_directory_path != null)
                        {
                            return Encoding.Unicode.GetString(row.item_directory_path).TrimEnd('\0');
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BOOTH Library Viewer] Failed to read item_directory_path: {e.Message}");
            }

            return null;
        }

        // Row types for sqlite-net query mapping
        private class ItemRow
        {
            public string registered_item_id { get; set; }
            public int booth_item_id { get; set; }
            public string item_name { get; set; }
            public string shop_subdomain { get; set; }
            public string shop_name { get; set; }
            public string thumbnail_url { get; set; }
            public int? sub_category_id { get; set; }
            public string sub_category_name { get; set; }
            public int? parent_category_id { get; set; }
            public string parent_category_name { get; set; }
        }

        private class TagRow
        {
            public int booth_item_id { get; set; }
            public string tag { get; set; }
        }

        private class PreferencesRow
        {
            public byte[] item_directory_path { get; set; }
        }

        private class ListRow
        {
            public int id { get; set; }
            public string title { get; set; }
        }

        private class ListItemRow
        {
            public int list_id { get; set; }
            public string item_id { get; set; }
        }

        private class SmartListRow
        {
            public int id { get; set; }
            public string title { get; set; }
        }

        private class SmartListCriteriaRow
        {
            public int id { get; set; }
            public int smart_list_id { get; set; }
            public string text { get; set; }
            public int? category_id { get; set; }
            public int? subcategory_id { get; set; }
        }

        private class SmartListTagRow
        {
            public int id { get; set; }
            public int smart_list_id { get; set; }
            public string tag { get; set; }
        }

        public static bool MatchesSmartList(BoothItem item, SmartListCriteria criteria)
        {
            // Text: partial match on item name (case-insensitive)
            if (!string.IsNullOrEmpty(criteria.Text))
            {
                if (item.Name == null || !item.Name.ToLowerInvariant().Contains(criteria.Text.ToLowerInvariant()))
                    return false;
            }

            // CategoryId: match on parent category
            if (criteria.CategoryId.HasValue)
            {
                if (!item.ParentCategoryId.HasValue || item.ParentCategoryId.Value != criteria.CategoryId.Value)
                    return false;
            }

            // SubcategoryId: match on sub category
            if (criteria.SubcategoryId.HasValue)
            {
                if (!item.SubCategoryId.HasValue || item.SubCategoryId.Value != criteria.SubcategoryId.Value)
                    return false;
            }

            // Tags: item must have at least one of the criteria tags (OR)
            if (criteria.Tags != null && criteria.Tags.Count > 0)
            {
                var hasMatchingTag = false;
                foreach (var tag in criteria.Tags)
                {
                    if (item.Tags.Contains(tag))
                    {
                        hasMatchingTag = true;
                        break;
                    }
                }
                if (!hasMatchingTag)
                    return false;
            }

            return true;
        }
    }
}
