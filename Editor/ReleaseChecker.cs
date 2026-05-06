using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BoothLibraryViewer
{
    [InitializeOnLoad]
    public static class ReleaseChecker
    {
        private const string PackageId = "net.32ba.booth-library-manager-viewer";
        private const string ReleasePageUrl = "https://github.com/32ba/unity-booth-library-manager-viewer/releases";
        private const string LastCheckKeyPrefix = "BoothLibraryViewer.LastVersionCheck";
        private const double CheckIntervalHours = 24.0;

        private static readonly VpmApiClient Api = new VpmApiClient(PackageId);

        public static string LatestVersion { get; private set; }
        public static bool HasNewVersion { get; private set; }
        public static bool IsChecking { get; private set; }
        public static string CheckError { get; private set; }

        public static event Action OnUpdateCheckCompleted;

        static ReleaseChecker()
        {
            EditorApplication.delayCall += () => CheckForUpdates();
        }

        public static void CheckForUpdates(bool forceCheck = false)
        {
            if (IsChecking)
                return;

            if (!forceCheck && !ShouldCheckForUpdates())
                return;

            IsChecking = true;
            HasNewVersion = false;
            CheckError = null;
            OnUpdateCheckCompleted?.Invoke();

            EditorCoroutine.Start(CheckRoutine());
        }

        public static void OpenReleasePage()
        {
            Application.OpenURL(ReleasePageUrl);
        }

        public static string GetCurrentVersion()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReleaseChecker).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.version))
                    return packageInfo.version;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BOOTH Library Viewer] Failed to read package version: {ex.Message}");
            }

            return "0.0.0";
        }

        private static IEnumerator CheckRoutine()
        {
            yield return Api.GetLatestVersionCoroutine(HandleSuccess, HandleError);
        }

        private static void HandleSuccess(string latest)
        {
            IsChecking = false;

            if (string.IsNullOrEmpty(latest))
            {
                CheckError = "Empty version response";
                OnUpdateCheckCompleted?.Invoke();
                return;
            }

            LatestVersion = latest;
            var current = GetCurrentVersion();
            EditorPrefs.SetString(GetLastCheckKey(), DateTime.Now.ToBinary().ToString());

            if (VersionUtility.IsNewerVersion(current, latest))
            {
                HasNewVersion = true;
                Debug.Log($"[BOOTH Library Viewer] New version available: {current} -> {latest}");
            }
            else
            {
                Debug.Log($"[BOOTH Library Viewer] Package is up to date: {current}");
            }

            OnUpdateCheckCompleted?.Invoke();
        }

        private static void HandleError(string error)
        {
            IsChecking = false;
            CheckError = error;
            Debug.LogWarning($"[BOOTH Library Viewer] Update check failed: {error}");
            OnUpdateCheckCompleted?.Invoke();
        }

        private static bool ShouldCheckForUpdates()
        {
            var stored = EditorPrefs.GetString(GetLastCheckKey(), "");
            if (string.IsNullOrEmpty(stored))
                return true;

            if (long.TryParse(stored, out var binary))
            {
                var last = DateTime.FromBinary(binary);
                return (DateTime.Now - last).TotalHours >= CheckIntervalHours;
            }

            return true;
        }

        private static string GetLastCheckKey()
        {
            return $"{LastCheckKeyPrefix}.{GetProjectScopeSuffix()}";
        }

        private static string GetProjectScopeSuffix()
        {
            var projectPath = Application.dataPath;
            if (string.IsNullOrEmpty(projectPath))
                return "unknown";

            using (var sha1 = SHA1.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(projectPath);
                var hash = sha1.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    internal class EditorCoroutine
    {
        private readonly IEnumerator _routine;
        private IEnumerator _nested;

        public static EditorCoroutine Start(IEnumerator routine)
        {
            var coroutine = new EditorCoroutine(routine);
            EditorApplication.update += coroutine.Update;
            return coroutine;
        }

        private EditorCoroutine(IEnumerator routine)
        {
            _routine = routine;
        }

        private void Update()
        {
            if (_nested != null)
            {
                if (_nested.MoveNext())
                    return;
                _nested = null;
            }

            if (!_routine.MoveNext())
            {
                EditorApplication.update -= Update;
                return;
            }

            if (_routine.Current is IEnumerator nestedRoutine)
                _nested = nestedRoutine;
        }
    }
}
