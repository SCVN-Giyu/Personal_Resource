using System;
using System.IO;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // The Luna install the project uses (any version) and the image and sound tools it ships, read from Luna's config.json.
    public class LunaInstall
    {
        const string CONFIG_FILE = "config.json";
        const string PIPELINE_FOLDER = "pipeline";

        [Serializable]
        class Config
        {
            public string version;
            public TextureTools textures;
            public SoundTools sounds;
        }

        [Serializable]
        class SoundTools
        {
            public string ffmpegMac64;
            public string ffmpegWin64;
            public string ffmpegLinux64;
        }

        [Serializable]
        class TextureTools
        {
            public string convertMac64;
            public string convertWin64;
            public string convertLinux64;
            public string pngquantMac64;
            public string pngquantWin64;
            public string pngquantLinux64;
            public string pngcrushMac64;
            public string pngcrushWin64;
            public string pngcrushLinux64;
            public string cwebpMac64;
            public string cwebpWin64;
            public string cwebpLinux64;
        }

        public string RootPath { get; private set; }
        public string Version { get; private set; }
        public string MagickPath { get; private set; }
        public string PngquantPath { get; private set; }
        public string PngcrushPath { get; private set; }
        public string CwebpPath { get; private set; }
        public string FfmpegPath { get; private set; }

        // The Luna package's folder ("<Luna>/scripts") sits next to config.json and the pipeline folder.
        public static LunaInstall Find()
        {
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (!package.isDirectDependency || string.IsNullOrEmpty(package.resolvedPath))
                    continue;
                var root = Path.GetDirectoryName(package.resolvedPath);
                var configPath = root != null ? Path.Combine(root, CONFIG_FILE) : null;
                if (configPath == null || !File.Exists(configPath) || !Directory.Exists(Path.Combine(root, PIPELINE_FOLDER)))
                    continue;

                var config = JsonUtility.FromJson<Config>(File.ReadAllText(configPath));
                if (config == null || config.textures == null)
                    continue;
                return Create(root, config);
            }
            return null;
        }

        static LunaInstall Create(string root, Config config)
        {
            var tools = config.textures;
            var sounds = config.sounds ?? new SoundTools();
            var install = new LunaInstall { RootPath = root, Version = config.version };
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    install.MagickPath = Resolve(root, tools.convertMac64);
                    install.PngquantPath = Resolve(root, tools.pngquantMac64);
                    install.PngcrushPath = Resolve(root, tools.pngcrushMac64);
                    install.CwebpPath = Resolve(root, tools.cwebpMac64);
                    install.FfmpegPath = Resolve(root, sounds.ffmpegMac64);
                    break;
                case RuntimePlatform.WindowsEditor:
                    install.MagickPath = Resolve(root, tools.convertWin64);
                    install.PngquantPath = Resolve(root, tools.pngquantWin64);
                    install.PngcrushPath = Resolve(root, tools.pngcrushWin64);
                    install.CwebpPath = Resolve(root, tools.cwebpWin64);
                    install.FfmpegPath = Resolve(root, sounds.ffmpegWin64);
                    break;
                default:
                    // Luna expects system-installed tools on Linux and uses these paths as they are.
                    install.MagickPath = tools.convertLinux64;
                    install.PngquantPath = tools.pngquantLinux64;
                    install.PngcrushPath = tools.pngcrushLinux64;
                    install.CwebpPath = tools.cwebpLinux64;
                    install.FfmpegPath = sounds.ffmpegLinux64;
                    break;
            }
            return install;
        }

        static string Resolve(string root, string relativePath)
        {
            return string.IsNullOrEmpty(relativePath) ? null : Path.GetFullPath(Path.Combine(root, relativePath));
        }
    }
}
