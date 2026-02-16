using System.Collections.Generic;

namespace BoothLibraryViewer
{
    public class BoothItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ShopSubdomain { get; set; }
        public string ShopName { get; set; }
        public string ThumbnailUrl { get; set; }
        public string SubCategoryName { get; set; }
        public string ParentCategoryName { get; set; }
        public string RegisteredItemId { get; set; }
        public string FolderPath { get; set; }
        public bool FolderExists { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> UnityPackages { get; set; } = new List<string>();
        public bool IsExpanded { get; set; }
        public FolderTreeNode FolderTree { get; set; }
    }
}
