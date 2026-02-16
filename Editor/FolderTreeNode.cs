using System;
using System.Collections.Generic;

namespace BoothLibraryViewer
{
    public class FolderTreeNode
    {
        public string Name;
        public string FullPath;
        public bool IsDirectory;
        public long FileSize;
        public FolderTreeNode Parent;
        public List<FolderTreeNode> Children = new List<FolderTreeNode>();

        public bool IsUnityPackage =>
            !IsDirectory && Name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
    }
}
