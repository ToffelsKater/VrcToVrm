using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace VrcToVrm {
    // Compiled only while UniVRM is missing (defineConstraints in the asmdef).
    // A package.json can't depend on a git URL or another registry, so this adds UniVRM to the project manifest
    // from OpenUPM instead. That needs no git install.
    [InitializeOnLoad]
    public static class UniVrmInstaller {
        const string Registry = "https://package.openupm.com";
        const string Package = "com.vrmc.vrm", Version = "0.131.4";

        static UniVrmInstaller() => EditorApplication.delayCall += () => {
            if (Application.isBatchMode || SessionState.GetBool("VrcToVrm.UniVrmAsked", false)) return;
            SessionState.SetBool("VrcToVrm.UniVrmAsked", true);
            if (EditorUtility.DisplayDialog("VRChat to VRM 1.0",
                    $"This tool needs UniVRM {Version}. Add it to the project from OpenUPM ({Registry})?", "Add", "Later"))
                Install();
        };

        [MenuItem("Tools/Install UniVRM for VRChat to VRM")]
        public static void Install() {
            const string path = "Packages/manifest.json";
            var manifest = JObject.Parse(File.ReadAllText(path));
            if (!(manifest["scopedRegistries"] is JArray registries)) manifest["scopedRegistries"] = registries = new JArray();
            var openUpm = registries.OfType<JObject>().FirstOrDefault(r => ((string)r["url"])?.TrimEnd('/') == Registry);
            if (openUpm == null) registries.Add(openUpm = new JObject { ["name"] = "OpenUPM", ["url"] = Registry });
            if (!(openUpm["scopes"] is JArray scopes)) openUpm["scopes"] = scopes = new JArray();
            if (!scopes.Any(s => (string)s == "com.vrmc")) scopes.Add("com.vrmc");
            manifest["dependencies"][Package] = Version; // pulls com.vrmc.gltf with it
            File.WriteAllText(path, manifest.ToString());
            Client.Resolve();
        }
    }
}
