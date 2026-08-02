using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BoothLibraryViewer
{
    public static class FolderTreeBuilder
    {
        public static FolderTreeNode Build(string rootPath)
        {
            var root = new FolderTreeNode
            {
                Name = Path.GetFileName(rootPath),
                FullPath = rootPath,
                IsDirectory = true,
                Parent = null,
            };

            if (!Directory.Exists(rootPath))
                return root;

            EnsureChildren(root);

            return root;
        }

        public static void EnsureChildren(FolderTreeNode node)
        {
            if (node == null || !node.IsDirectory || node.ChildrenLoaded)
                return;

            node.ChildrenLoaded = true;
            var children = new List<FolderTreeNode>();

            try
            {
                // Directories first
                var dirs = Directory.GetDirectories(node.FullPath)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var dir in dirs)
                {
                    var child = new FolderTreeNode
                    {
                        Name = Path.GetFileName(dir),
                        FullPath = dir,
                        IsDirectory = true,
                        Parent = node,
                    };

                    children.Add(child);
                }

                // Then files
                var files = Directory.GetFiles(node.FullPath)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    long size = 0;
                    try
                    {
                        size = new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                        // Ignore
                    }

                    children.Add(new FolderTreeNode
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false,
                        FileSize = size,
                        Parent = node,
                    });
                }
            }
            catch (Exception)
            {
                // Ignore permission errors
            }

            node.Children = children;
        }
    }
}
