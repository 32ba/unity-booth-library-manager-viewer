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
                IsExpanded = true,
            };

            if (!Directory.Exists(rootPath))
                return root;

            try
            {
                PopulateChildren(root);
            }
            catch (Exception)
            {
                // Ignore permission errors or other IO exceptions
            }

            return root;
        }

        private static void PopulateChildren(FolderTreeNode node)
        {
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
                        IsExpanded = false,
                    };

                    try
                    {
                        PopulateChildren(child);
                    }
                    catch (Exception)
                    {
                        // Skip inaccessible directories
                    }

                    children.Add(child);
                }

                // Then files
                var files = Directory.GetFiles(node.FullPath)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    children.Add(new FolderTreeNode
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false,
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
