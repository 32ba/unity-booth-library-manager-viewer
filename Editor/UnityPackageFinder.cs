using System;
using System.Collections.Generic;
using System.IO;

namespace BoothLibraryViewer
{
    public static class UnityPackageFinder
    {
        public static List<string> Find(string folderPath)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return results;

            try
            {
                var files = Directory.GetFiles(folderPath, "*.unitypackage", SearchOption.AllDirectories);
                results.AddRange(files);
            }
            catch (Exception)
            {
                // Ignore permission errors or other IO exceptions
            }

            return results;
        }

        public static string GetDisplayName(string fullPath)
        {
            return Path.GetFileName(fullPath);
        }
    }
}
