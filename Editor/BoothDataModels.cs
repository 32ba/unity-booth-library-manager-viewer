using System.Collections.Generic;

namespace BoothLibraryViewer
{
    public class BoothList
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public bool IsSmart { get; set; }
        public HashSet<string> ItemIds { get; set; } = new HashSet<string>();
        public SmartListCriteria Criteria { get; set; }
    }

    public class SmartListCriteria
    {
        public string Text { get; set; }
        public int? CategoryId { get; set; }
        public int? SubcategoryId { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class BoothItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ShopSubdomain { get; set; }
        public string ShopName { get; set; }
        public string ThumbnailUrl { get; set; }
        public string SubCategoryName { get; set; }
        public string ParentCategoryName { get; set; }
        public string RegisteredCreatedAt { get; set; }
        public string RegisteredUpdatedAt { get; set; }
        public string PublishedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string RegisteredItemId { get; set; }
        public string FolderPath { get; set; }
        public bool FolderExists { get; set; }
        public int? SubCategoryId { get; set; }
        public int? ParentCategoryId { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> UnityPackages { get; set; } = new List<string>();
        public bool UnityPackagesLoaded { get; set; }
        public bool IsExpanded { get; set; }
        public FolderTreeNode FolderTree { get; set; }
    }
}
