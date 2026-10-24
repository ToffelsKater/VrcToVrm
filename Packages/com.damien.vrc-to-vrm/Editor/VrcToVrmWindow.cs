using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using VRC.SDK3.Avatars.Components;

namespace VrcToVrm {
    public class VrcToVrmWindow : EditorWindow {
        [SerializeField] VRCAvatarDescriptor avatar;
        [SerializeField] VRM10Object vrm; // holds the meta the user types in, survives domain reloads
        [SerializeField] ConvertOptions options = new ConvertOptions();
        VRM10MetaEditor metaEditor;
        Vector2 scroll;
        float height; // of the picked avatar's meshes, 0 when unknown
        [SerializeField] bool showHeight;
        [SerializeField] bool showShapes;
        [SerializeField] string shapeSearch = "";
        readonly HashSet<string> openMeshes = new HashSet<string>();
        readonly List<(string path, SkinnedMeshRenderer smr)> shapeMeshes = new List<(string, SkinnedMeshRenderer)>();

        [MenuItem("Tools/VRChat to VRM 1.0")]
        static void Open() => GetWindow<VrcToVrmWindow>("VRC to VRM");

        void OnEnable() {
            if (!vrm) {
                vrm = CreateInstance<VRM10Object>();
                vrm.hideFlags = HideFlags.DontSave;
                vrm.Meta.Authors = new List<string> { "" };
            }
            metaEditor = VRM10MetaEditor.Create(new SerializedObject(vrm));
            Measure();
            ListShapes();
            SceneView.duringSceneGui += DrawHeight;
        }

        void OnDisable() => SceneView.duringSceneGui -= DrawHeight;

        // A stick figure of the target height beside the avatar, with a dotted line across the avatar at that height
        void DrawHeight(SceneView view) {
            if (!showHeight || !avatar || EditorUtility.IsPersistent(avatar)) return;
            var h = options.TargetHeight;
            var right = Vector3.ProjectOnPlane(view.camera.transform.right, Vector3.up).normalized;
            var off = Mathf.Max(height, h) * .6f;
            var foot = avatar.transform.position + right * off;
            Vector3 P(float x, float y) => foot + (right * x + Vector3.up * y) * h;
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(P(0, .935f), Vector3.Cross(right, Vector3.up), .065f * h);
            Handles.DrawLines(new[] {
                P(0, .87f), P(0, .52f), // spine
                P(-.13f, .82f), P(.13f, .82f), // shoulders
                P(-.13f, .82f), P(-.18f, .5f), P(.13f, .82f), P(.18f, .5f), // arms
                P(0, .52f), P(-.09f, 0), P(0, .52f), P(.09f, 0), // legs
            });
            var top = avatar.transform.position + Vector3.up * h;
            Handles.DrawDottedLine(top - right * off, top + right * off, 4);
            Handles.Label(P(0, 1.08f), $"{h:0.00} m");
        }

        void ListShapes() {
            shapeMeshes.Clear();
            if (!avatar) return;
            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh && smr.sharedMesh.blendShapeCount > 0)
                    shapeMeshes.Add((AnimationUtility.CalculateTransformPath(smr.transform, avatar.transform), smr));
        }

        void ShapesGUI() {
            showShapes = EditorGUILayout.Foldout(showShapes, $"Blend shapes ({options.BlendShapes.Count} changed)", true);
            if (!showShapes || !avatar) return;
            EditorGUILayout.LabelField("Every non-zero blend shape is baked into the mesh. Sliders start at the avatar's current weights.", EditorStyles.wordWrappedMiniLabel);
            shapeSearch = EditorGUILayout.TextField("Search", shapeSearch);
            var searching = !string.IsNullOrWhiteSpace(shapeSearch);
            foreach (var (path, smr) in shapeMeshes) {
                if (!smr || !smr.sharedMesh) continue;
                var mesh = smr.sharedMesh;
                var matches = Enumerable.Range(0, mesh.blendShapeCount)
                    .Where(i => !searching || mesh.GetBlendShapeName(i).IndexOf(shapeSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (matches.Count == 0) continue;
                var open = EditorGUILayout.Foldout(searching || openMeshes.Contains(path), $"{path} ({mesh.blendShapeCount})", true);
                if (!searching) {
                    if (open) openMeshes.Add(path);
                    else openMeshes.Remove(path);
                }
                if (!open) continue;
                EditorGUI.indentLevel++;
                foreach (var i in matches) {
                    var name = mesh.GetBlendShapeName(i);
                    var o = options.BlendShapes.Find(x => x.Path == path && x.Shape == name);
                    EditorGUI.BeginChangeCheck();
                    var w = EditorGUILayout.Slider(name, o?.Weight ?? smr.GetBlendShapeWeight(i), 0, 100);
                    if (!EditorGUI.EndChangeCheck()) continue;
                    if (o == null) options.BlendShapes.Add(o = new BlendShapeOverride { Path = path, Shape = name });
                    o.Weight = w;
                }
                EditorGUI.indentLevel--;
            }
            if (options.BlendShapes.Count > 0 && GUILayout.Button("Reset to the avatar's weights")) options.BlendShapes.Clear();
        }

        void OnDestroy() => DestroyImmediate(vrm);

        void Measure() {
            height = 0;
            if (!avatar) return;
            if (!EditorUtility.IsPersistent(avatar)) {
                height = VrcToVrmConverter.MeasureHeight(avatar.gameObject);
                return;
            }
            // a prefab asset: open it in an isolated scene so the open scene isn't touched
            var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(avatar));
            try {
                height = VrcToVrmConverter.MeasureHeight(contents);
            } finally {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        void OnGUI() {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            var picked = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", avatar, typeof(VRCAvatarDescriptor), true);
            if (picked != avatar) {
                avatar = picked;
                if (avatar && string.IsNullOrEmpty(vrm.Meta.Name)) vrm.Meta.Name = avatar.name;
                Measure();
                options.BlendShapes.Clear();
                ListShapes();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Meta", EditorStyles.boldLabel);
            metaEditor.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            options.Bake = EditorGUILayout.Toggle(new GUIContent("Bake first",
                "Runs the upload build steps (VRCFury, Modular Avatar, other NDMF tools) on a copy and converts that copy"), options.Bake);
            options.TPose = EditorGUILayout.Toggle(new GUIContent("Force T-Pose", "VRM 1.0 expects the model in T-pose"), options.TPose);
            options.ConvertMaterials = EditorGUILayout.Toggle(new GUIContent("Materials to MToon", "Off: materials are exported as plain glTF PBR"), options.ConvertMaterials);
            options.ScaleToHeight = EditorGUILayout.Toggle(new GUIContent("Scale to height", "Most VRM games expect a human-sized avatar"), options.ScaleToHeight);
            using (new EditorGUI.DisabledScope(!options.ScaleToHeight))
                options.TargetHeight = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Height (m)", "Lowest to highest vertex, ears and hair included"), options.TargetHeight), .3f, 3f);
            if (height > 0)
                EditorGUILayout.LabelField(" ", options.ScaleToHeight
                    ? $"Avatar is {height:0.00} m tall, exported at ×{options.TargetHeight / height:0.##}"
                    : $"Avatar is {height:0.00} m tall", EditorStyles.miniLabel);
            showHeight = EditorGUILayout.Toggle(new GUIContent("Show height in scene",
                "Draws a person of the height above next to the avatar in the Scene view (avatar must be in the open scene)"), showHeight);
            EditorGUILayout.LabelField("Spring bone tuning (PhysBone values are mapped approximately)", EditorStyles.miniLabel);
            options.StiffnessScale = EditorGUILayout.Slider("Stiffness ×", options.StiffnessScale, 0, 4);
            options.DragScale = EditorGUILayout.Slider("Drag ×", options.DragScale, 0, 2);
            options.GravityScale = EditorGUILayout.Slider("Gravity ×", options.GravityScale, 0, 4);

            EditorGUILayout.Space();
            ShapesGUI();

            EditorGUILayout.Space();
            var errors = vrm.Meta.Validate().Where(v => !v.CanExport).Select(v => v.Message).ToList();
            if (!avatar) errors.Insert(0, "Pick an avatar that has a VRC Avatar Descriptor.");
            foreach (var e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            using (new EditorGUI.DisabledScope(errors.Count > 0))
                if (GUILayout.Button("Export VRM 1.0", GUILayout.Height(30))) Export();

            EditorGUILayout.EndScrollView();
            if (GUI.changed) SceneView.RepaintAll();
        }

        void Export() {
            var path = EditorUtility.SaveFilePanel("Export VRM 1.0", "", vrm.Meta.Name + ".vrm", "vrm");
            if (string.IsNullOrEmpty(path)) return;
            try {
                VrcToVrmConverter.Convert(avatar, vrm.Meta, options, path);
                Debug.Log($"[VRC to VRM] Exported {path}");
                EditorUtility.RevealInFinder(path);
            } catch (Exception e) {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("VRC to VRM", e.Message, "OK");
            }
        }
    }
}
