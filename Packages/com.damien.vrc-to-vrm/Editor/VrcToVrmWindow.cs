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
            }
            EditorGUILayout.HelpBox("Using VRCFury or Modular Avatar? Bake the avatar first (e.g. MA's Manual Bake), this tool reads the plain components.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Meta", EditorStyles.boldLabel);
            metaEditor.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            options.TPose = EditorGUILayout.Toggle(new GUIContent("Force T-Pose", "VRM 1.0 expects the model in T-pose"), options.TPose);
            options.ConvertMaterials = EditorGUILayout.Toggle(new GUIContent("Materials to MToon", "Off: materials are exported as plain glTF PBR"), options.ConvertMaterials);
            options.ScaleToHeight = EditorGUILayout.Toggle(new GUIContent("Scale to height", "Most VRM games expect a human-sized avatar"), options.ScaleToHeight);
            using (new EditorGUI.DisabledScope(!options.ScaleToHeight))
                options.TargetHeight = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Height (m)", "Lowest to highest vertex, ears and hair included"), options.TargetHeight), .3f, 3f);
            if (height > 0)
                EditorGUILayout.LabelField(" ", options.ScaleToHeight
                    ? $"Avatar is {height:0.00} m tall, exported at ×{options.TargetHeight / height:0.##}"
                    : $"Avatar is {height:0.00} m tall", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Spring bone tuning (PhysBone values are mapped approximately)", EditorStyles.miniLabel);
            options.StiffnessScale = EditorGUILayout.Slider("Stiffness ×", options.StiffnessScale, 0, 4);
            options.DragScale = EditorGUILayout.Slider("Drag ×", options.DragScale, 0, 2);
            options.GravityScale = EditorGUILayout.Slider("Gravity ×", options.GravityScale, 0, 4);

            EditorGUILayout.Space();
            var errors = vrm.Meta.Validate().Where(v => !v.CanExport).Select(v => v.Message).ToList();
            if (!avatar) errors.Insert(0, "Pick an avatar that has a VRC Avatar Descriptor.");
            foreach (var e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            using (new EditorGUI.DisabledScope(errors.Count > 0))
                if (GUILayout.Button("Export VRM 1.0", GUILayout.Height(30))) Export();

            EditorGUILayout.EndScrollView();
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
