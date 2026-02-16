using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BoothLibraryViewer
{
    public static class ThumbnailCache
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "32ba", "UnityBoothLibraryManagerViewer", "ThumbnailCache");

        private static readonly Dictionary<string, Texture2D> MemoryCache = new Dictionary<string, Texture2D>();
        private static readonly Queue<string> PendingUrls = new Queue<string>();
        private static readonly HashSet<string> QueuedUrls = new HashSet<string>();
        private static readonly List<DownloadRequest> ActiveDownloads = new List<DownloadRequest>();
        private static readonly HashSet<string> FailedUrls = new HashSet<string>();
        private const int MaxConcurrentDownloads = 4;
        private static Texture2D _placeholder;
        private static bool _isPolling;

        private class DownloadRequest
        {
            public string Url;
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
        }

        public static Texture2D GetPlaceholder()
        {
            if (_placeholder == null)
            {
                _placeholder = new Texture2D(64, 64);
                var pixels = new Color[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(0.2f, 0.2f, 0.2f, 1f);
                _placeholder.SetPixels(pixels);
                _placeholder.Apply();
            }
            return _placeholder;
        }

        public static Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url))
                return GetPlaceholder();

            // Check memory cache
            if (MemoryCache.TryGetValue(url, out var tex) && tex != null)
                return tex;

            // Check disk cache
            var diskPath = GetDiskCachePath(url);
            if (File.Exists(diskPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(diskPath);
                    var texture = new Texture2D(2, 2);
                    if (texture.LoadImage(bytes))
                    {
                        MemoryCache[url] = texture;
                        return texture;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BOOTH Library Viewer] Failed to load cached thumbnail: {e.Message}");
                }
            }

            // Enqueue download if not already queued/failed
            if (!QueuedUrls.Contains(url) && !FailedUrls.Contains(url))
            {
                PendingUrls.Enqueue(url);
                QueuedUrls.Add(url);
                StartPolling();
            }

            return GetPlaceholder();
        }

        public static void Clear()
        {
            foreach (var kvp in MemoryCache)
            {
                if (kvp.Value != null && kvp.Value != _placeholder)
                    UnityEngine.Object.DestroyImmediate(kvp.Value);
            }
            MemoryCache.Clear();
            PendingUrls.Clear();
            QueuedUrls.Clear();
            FailedUrls.Clear();

            foreach (var dl in ActiveDownloads)
            {
                dl.Request?.Dispose();
            }
            ActiveDownloads.Clear();
        }

        private static void StartPolling()
        {
            if (_isPolling) return;
            _isPolling = true;
            EditorApplication.update += PollDownloads;
        }

        private static void StopPolling()
        {
            if (!_isPolling) return;
            _isPolling = false;
            EditorApplication.update -= PollDownloads;
        }

        private static void PollDownloads()
        {
            // Start new downloads if slots available
            while (ActiveDownloads.Count < MaxConcurrentDownloads && PendingUrls.Count > 0)
            {
                var url = PendingUrls.Dequeue();
                if (MemoryCache.ContainsKey(url))
                    continue;

                try
                {
                    var request = UnityWebRequestTexture.GetTexture(url);
                    var op = request.SendWebRequest();
                    ActiveDownloads.Add(new DownloadRequest
                    {
                        Url = url,
                        Request = request,
                        Operation = op,
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BOOTH Library Viewer] Failed to start thumbnail download for {url}: {e.Message}");
                    FailedUrls.Add(url);
                }
            }

            // Check completed downloads
            bool anyCompleted = false;
            for (int i = ActiveDownloads.Count - 1; i >= 0; i--)
            {
                var dl = ActiveDownloads[i];
                if (!dl.Operation.isDone)
                    continue;

                ActiveDownloads.RemoveAt(i);
                anyCompleted = true;

                if (dl.Request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var texture = DownloadHandlerTexture.GetContent(dl.Request);
                        if (texture != null)
                        {
                            MemoryCache[dl.Url] = texture;
                            SaveToDiskCache(dl.Url, texture);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[BOOTH Library Viewer] Failed to process thumbnail {dl.Url}: {e.Message}");
                        FailedUrls.Add(dl.Url);
                    }
                }
                else
                {
                    Debug.LogWarning($"[BOOTH Library Viewer] Thumbnail download failed for {dl.Url}: {dl.Request.error}");
                    FailedUrls.Add(dl.Url);
                }

                dl.Request.Dispose();
            }

            if (anyCompleted)
            {
                // Repaint any open BoothLibraryViewerWindow
                var windows = Resources.FindObjectsOfTypeAll<BoothLibraryViewerWindow>();
                foreach (var w in windows)
                    w.Repaint();
            }

            // Stop polling when nothing left to do
            if (ActiveDownloads.Count == 0 && PendingUrls.Count == 0)
                StopPolling();
        }

        private static void SaveToDiskCache(string url, Texture2D texture)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                    Directory.CreateDirectory(CacheDir);

                var path = GetDiskCachePath(url);
                var bytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BOOTH Library Viewer] Failed to save thumbnail to disk cache: {e.Message}");
            }
        }

        private static string GetDiskCachePath(string url)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
                var sb = new StringBuilder();
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return Path.Combine(CacheDir, sb.ToString() + ".png");
            }
        }
    }
}
