using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Lets real Luna builds reuse work the size dashboard already did.
    //  - Compiled scripts: shared automatically through the compile cache (msbuild-cache.sh), whose keys do not
    //    depend on where the compile runs.
    //  - Brotli: Luna's pipeline honours BROTLI_PATH_OVERRIDE; Luna's editor starts the pipeline with this editor's
    //    environment, so setting it here routes real builds through the same content-keyed cache as the dashboard.
    //    The wrapper still runs Luna's own brotli binary on a miss, so outputs are identical.
    [InitializeOnLoad]
    public static class LunaBuildAcceleration
    {
        public const string PREF = "LunaBuildAcceleration.Enabled";
        const string OVERRIDE = "BROTLI_PATH_OVERRIDE";
        const string REAL = "LUNA_REAL_BROTLI";
        const string CACHE = "LUNA_BROTLI_CACHE";
        const string WRAPPER = "Tools~/LunaSizeDashboard/brotli-cache.sh";
        const int MAX_ATTEMPTS = 30;
        static int attempts;

        // EditorApplication.update, not delayCall: delayCall does not run while Unity is in the background.
        static LunaBuildAcceleration()
        {
            EditorApplication.update += ApplyOnStartup;
        }

        static void ApplyOnStartup()
        {
            if (Apply() || ++attempts >= MAX_ATTEMPTS)
                EditorApplication.update -= ApplyOnStartup;
        }

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(PREF, true); }
            set
            {
                EditorPrefs.SetBool(PREF, value);
                Apply();
            }
        }

        public static bool Active
        {
            get { return Environment.GetEnvironmentVariable(OVERRIDE) == WrapperPath(); }
        }

        // Returns false when it should be tried again (package information can be unavailable right after a reload).
        static bool Apply()
        {
            var wrapper = WrapperPath();
            if (wrapper == null && Enabled && Application.platform == RuntimePlatform.OSXEditor)
                return false;
            if (!Enabled || Application.platform != RuntimePlatform.OSXEditor || wrapper == null)
            {
                // Only undo what this class set; never touch an override someone else configured.
                if (wrapper != null && Environment.GetEnvironmentVariable(OVERRIDE) == wrapper)
                {
                    Environment.SetEnvironmentVariable(OVERRIDE, null);
                    Environment.SetEnvironmentVariable(REAL, null);
                    Environment.SetEnvironmentVariable(CACHE, null);
                }
                return true;
            }
            var current = Environment.GetEnvironmentVariable(OVERRIDE);
            if (!string.IsNullOrEmpty(current) && current != wrapper)
                return true;

            var install = LunaInstall.Find();
            if (install == null)
                return false;
            var brotli = Path.Combine(install.RootPath, "tools", "brotli", "mac64", "brotli");
            if (!File.Exists(brotli))
                return true;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            Environment.SetEnvironmentVariable(REAL, brotli);
            Environment.SetEnvironmentVariable(CACHE, Path.Combine(projectRoot, LunaStatsRunner.WORK_FOLDER, "cache-" + install.Version, "brotli"));
            Environment.SetEnvironmentVariable(OVERRIDE, wrapper);
            return true;
        }

        static string WrapperPath()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LunaBuildAcceleration).Assembly);
            var path = package != null ? Path.Combine(package.resolvedPath, WRAPPER) : null;
            return path != null && File.Exists(path) ? path : null;
        }
    }
}
