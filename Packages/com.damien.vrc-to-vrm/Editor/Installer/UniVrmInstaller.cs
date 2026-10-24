using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace VrcToVrm {
    // Compiled only while UniVRM's VRM 1.0 package is missing (defineConstraints in the asmdef).
    // A package.json can't depend on a git URL or another registry, so this adds UniVRM to the project manifest
    // from OpenUPM instead. That needs no git install.
    [InitializeOnLoad]
    public static class UniVrmInstaller {
        const string Registry = "https://package.openupm.com";
        const string Package = "com.vrmc.vrm", Latest = "0.131.4";

        static UniVrmInstaller() => EditorApplication.delayCall += () => {
            if (Application.isBatchMode || SessionState.GetBool("VrcToVrm.UniVrmAsked", false)) return;
            SessionState.SetBool("VrcToVrm.UniVrmAsked", true);
            if (!Supported(TargetVersion()) || EditorUtility.DisplayDialog("VRChat to VRM 1.0",
                    $"This tool needs UniVRM's VRM 1.0 package. Add {Package} {TargetVersion()} to the project from OpenUPM ({Registry})?", "Add", "Later"))
                Install();
        };

        // com.vrmc.vrm only compiles against the same UniGLTF version. VCC listings (VRM Converter for VRChat, for one)
        // put their own com.vrmc.gltf into Packages/, so follow that one when it is there.
        static string TargetVersion() => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.vrmc.gltf")?.version ?? Latest;

        static bool Supported(string version) => Version.TryParse(version.Split('-')[0], out var v) && v >= new Version(0, 128, 0);

        [MenuItem("Tools/Install UniVRM for VRChat to VRM")]
        public static void Install() {
            var version = TargetVersion();
            if (!Supported(version)) {
                EditorUtility.DisplayDialog("VRChat to VRM 1.0",
                    $"This project has UniVRM {version}. VRChat to VRM needs 0.128 or newer: update or remove it in VCC, then use Tools > Install UniVRM for VRChat to VRM.", "OK");
                return;
            }
            const string path = "Packages/manifest.json";
            var manifest = JObject.Parse(File.ReadAllText(path));
            if (!(manifest["scopedRegistries"] is JArray registries)) manifest["scopedRegistries"] = registries = new JArray();
            var openUpm = registries.OfType<JObject>().FirstOrDefault(r => ((string)r["url"])?.TrimEnd('/') == Registry);
            if (openUpm == null) registries.Add(openUpm = new JObject { ["name"] = "OpenUPM", ["url"] = Registry });
            if (!(openUpm["scopes"] is JArray scopes)) openUpm["scopes"] = scopes = new JArray();
            if (!scopes.Any(s => (string)s == "com.vrmc")) scopes.Add("com.vrmc");
            manifest["dependencies"][Package] = version; // pulls com.vrmc.gltf with it when the project has none
            File.WriteAllText(path, manifest.ToString());
            Client.Resolve();
        }
    }
}
