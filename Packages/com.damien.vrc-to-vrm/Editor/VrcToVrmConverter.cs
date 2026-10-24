using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRM10.MToon10;
using Object = UnityEngine.Object;

namespace VrcToVrm {
    [Serializable]
    public class ConvertOptions {
        public bool TPose = true;
        public bool ConvertMaterials = true;
        // PhysBone and VRM spring bones integrate differently, so the mapping is approximate. These scale the result.
        public float StiffnessScale = 1, DragScale = 1, GravityScale = 1;
    }

    public static class VrcToVrmConverter {
        // VRC_AvatarDescriptor.VisemeBlendShapes order: sil PP FF TH DD kk CH SS nn RR aa E ih oh ou
        const int Aa = 10, E = 11, Ih = 12, Oh = 13, Ou = 14;

        /// <summary>Converts a copy of <paramref name="avatar"/> and writes it to <paramref name="path"/> as VRM 1.0. The source is not touched.</summary>
        public static void Convert(VRCAvatarDescriptor avatar, VRM10ObjectMeta meta, ConvertOptions o, string path) {
            var root = Object.Instantiate(avatar.gameObject);
            var temp = new List<Object> { root };
            try {
                root.name = avatar.name;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t && t != root.transform && t.CompareTag("EditorOnly")) Object.DestroyImmediate(t.gameObject);

                var animator = root.GetComponent<Animator>();
                if (!animator || !animator.isHuman) throw new InvalidOperationException("The avatar needs an Animator with a humanoid Avatar.");
                if (o.TPose && !InternalTPose.TryMakePoseValid(root)) Debug.LogWarning("[VRC to VRM] Could not set the T-pose, exporting the current pose.");

                var vrm = ScriptableObject.CreateInstance<VRM10Object>();
                temp.Add(vrm);
                var instance = root.AddComponent<Vrm10Instance>(); // also adds UniHumanoid.Humanoid
                instance.Vrm = vrm;
                instance.UpdateType = Vrm10Instance.UpdateTypes.None;
                if (!root.GetComponent<UniHumanoid.Humanoid>().AssignBonesFromAnimator())
                    throw new InvalidOperationException("Could not read the humanoid bones from the Animator.");

                var descriptor = root.GetComponent<VRCAvatarDescriptor>();
                Expressions(root.transform, descriptor, vrm.Expression, temp);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                vrm.LookAt.OffsetFromHead = root.transform.TransformPoint(descriptor.ViewPosition) - head.position;
                Springs(root.transform, instance.SpringBone, o);
                if (o.ConvertMaterials) Materials(root, temp);

                Export(root, meta, path);
            } finally {
                foreach (var x in temp) if (x) Object.DestroyImmediate(x);
            }
        }

        // same steps as UniVRM's VRM10ExportDialog.ExportPath
        static void Export(GameObject root, VRM10ObjectMeta meta, string path) {
            var settings = new GltfExportSettings {
                UseSparseAccessorForMorphTarget = true,
                ExportOnlyBlendShapePosition = true,
                DivideVertexBuffer = true,
            };
            using (var arrayManager = new NativeArrayManager())
            using (var exporter = new Vrm10Exporter(settings, textureSerializer: new EditorTextureSerializer())) {
                var converter = new ModelExporter();
                var model = converter.Export(settings, arrayManager, root);
                model.ConvertCoordinate(VrmLib.Coordinates.Vrm1);
                exporter.Export(root, model, converter, new VrmLib.ExportArgs { sparse = true }, meta);
                File.WriteAllBytes(path, exporter.Storage.ToGlbBytes());
            }
        }

        static void Expressions(Transform root, VRCAvatarDescriptor d, VRM10ObjectExpression e, List<Object> temp) {
            VRM10Expression Clip(string name, SkinnedMeshRenderer smr, params int[] shapes) {
                if (!smr || !smr.sharedMesh) return null;
                var valid = shapes.Where(i => i >= 0 && i < smr.sharedMesh.blendShapeCount).ToArray();
                if (valid.Length == 0) return null;
                var clip = ScriptableObject.CreateInstance<VRM10Expression>();
                clip.name = name;
                var path = UniVRM10.UnityExtensions.RelativePathFrom(smr.transform, root);
                clip.MorphTargetBindings = valid.Select(i => new MorphTargetBinding(path, i, 1f)).ToArray();
                temp.Add(clip);
                return clip;
            }

            var mouth = d.VisemeSkinnedMesh;
            int Shape(string name) => mouth && mouth.sharedMesh && !string.IsNullOrEmpty(name) ? mouth.sharedMesh.GetBlendShapeIndex(name) : -1;
            string Viseme(int i) => d.VisemeBlendShapes != null && i < d.VisemeBlendShapes.Length ? d.VisemeBlendShapes[i] : null;

            if (d.lipSync == VRCAvatarDescriptor.LipSyncStyle.VisemeBlendShape) {
                e.Aa = Clip("aa", mouth, Shape(Viseme(Aa)));
                e.Ih = Clip("ih", mouth, Shape(Viseme(Ih)));
                e.Ou = Clip("ou", mouth, Shape(Viseme(Ou)));
                e.Ee = Clip("ee", mouth, Shape(Viseme(E)));
                e.Oh = Clip("oh", mouth, Shape(Viseme(Oh)));
            } else if (d.lipSync == VRCAvatarDescriptor.LipSyncStyle.JawFlapBlendShape) {
                e.Aa = Clip("aa", mouth, Shape(d.MouthOpenBlendShapeName));
            }

            var eyes = d.customEyeLookSettings;
            if (eyes.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes && eyes.eyelidsBlendshapes != null) {
                // eyelidsBlendshapes: [0] blink, [1] looking up, [2] looking down
                int Lid(int i) => i < eyes.eyelidsBlendshapes.Length ? eyes.eyelidsBlendshapes[i] : -1;
                e.Blink = Clip("blink", eyes.eyelidsSkinnedMesh, Lid(0));
                e.LookUp = Clip("lookUp", eyes.eyelidsSkinnedMesh, Lid(1));
                e.LookDown = Clip("lookDown", eyes.eyelidsSkinnedMesh, Lid(2));
            }
        }

        static void Springs(Transform root, Vrm10InstanceSpringBone springBone, ConvertOptions o) {
            var groups = new Dictionary<VRCPhysBoneColliderBase, VRM10SpringBoneColliderGroup>();
            VRM10SpringBoneColliderGroup Group(VRCPhysBoneColliderBase c) {
                if (groups.TryGetValue(c, out var g)) return g;
                var host = (c.rootTransform ? c.rootTransform : c.transform).gameObject;
                var col = host.AddComponent<VRM10SpringBoneCollider>();
                var axis = c.rotation * Vector3.up;
                col.Radius = c.radius;
                col.Offset = c.position;
                switch (c.shapeType) {
                    case VRCPhysBoneColliderBase.ShapeType.Capsule:
                        // PhysBone capsule height includes both caps, VRM wants the two cap centers
                        var half = axis * Mathf.Max(0, c.height * 0.5f - c.radius);
                        col.ColliderType = c.insideBounds ? VRM10SpringBoneColliderTypes.CapsuleInside : VRM10SpringBoneColliderTypes.Capsule;
                        col.Offset = c.position - half;
                        col.Tail = c.position + half;
                        break;
                    case VRCPhysBoneColliderBase.ShapeType.Plane:
                        col.ColliderType = VRM10SpringBoneColliderTypes.Plane;
                        col.Normal = axis;
                        break;
                    default:
                        col.ColliderType = c.insideBounds ? VRM10SpringBoneColliderTypes.SphereInside : VRM10SpringBoneColliderTypes.Sphere;
                        break;
                }
                g = host.AddComponent<VRM10SpringBoneColliderGroup>();
                g.Name = c.name;
                g.Colliders.Add(col);
                springBone.ColliderGroups.Add(g);
                return groups[c] = g;
            }

            var used = new HashSet<Transform>(); // a bone may only be simulated by one spring
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBoneBase>(true)) {
                if (!pb.isActiveAndEnabled) continue;
                var start = pb.rootTransform ? pb.rootTransform : pb.transform;
                if (used.Contains(start)) continue;
                var ignore = new HashSet<Transform>(pb.ignoreTransforms?.Where(t => t) ?? Enumerable.Empty<Transform>());

                // one VRM spring per unbranched run of bones. At a branch the run ends and every child starts a new one,
                // which matches PhysBone's default "Ignore" multi-child behaviour at the root.
                // ponytail: branches below the root lose the parent-to-branch segment's motion; split into separate PhysBones if that shows.
                var chains = new List<List<Transform>>();
                void Walk(Transform t, List<Transform> chain) {
                    chain.Add(t);
                    var kids = t.Cast<Transform>().Where(k => !ignore.Contains(k) && !used.Contains(k)).ToList();
                    if (kids.Count == 1) {
                        Walk(kids[0], chain);
                        return;
                    }
                    if (kids.Count == 0 && pb.endpointPosition != Vector3.zero) {
                        var end = new GameObject(t.name + "_end").transform;
                        end.SetParent(t, false);
                        end.localPosition = pb.endpointPosition;
                        chain.Add(end);
                    }
                    if (chain.Count > 1) chains.Add(chain);
                    foreach (var k in kids) Walk(k, new List<Transform>());
                }
                Walk(start, new List<Transform>());
                if (chains.Count == 0) continue; // a single bone without endpoint has nothing to swing

                // PhysBone curves run from 0 at the root to 1 at the deepest bone
                int Depth(Transform t) => t == start ? 0 : Depth(t.parent) + 1;
                var maxDepth = Mathf.Max(1, chains.SelectMany(c => c).Max(Depth));
                var radiusScale = Mathf.Max(Mathf.Abs(pb.transform.lossyScale.x), Mathf.Abs(pb.transform.lossyScale.y), Mathf.Abs(pb.transform.lossyScale.z));
                var colliderGroups = (pb.colliders ?? new List<VRCPhysBoneColliderBase>()).Where(c => c && c.transform.IsChildOf(root)).Select(Group).Distinct().ToList();

                foreach (var chain in chains) {
                    var joints = new List<VRM10SpringBoneJoint>();
                    foreach (var t in chain) {
                        used.Add(t);
                        var x = (float)Depth(t) / maxDepth;
                        var j = t.gameObject.AddComponent<VRM10SpringBoneJoint>();
                        j.m_stiffnessForce = (pb.pull * Eval(pb.pullCurve, x) + pb.stiffness * Eval(pb.stiffnessCurve, x)) * 4f * o.StiffnessScale;
                        j.m_dragForce = Mathf.Clamp01((1 - pb.spring * Eval(pb.springCurve, x)) * o.DragScale);
                        j.m_gravityPower = pb.gravity * Eval(pb.gravityCurve, x) * o.GravityScale;
                        j.m_gravityDir = Vector3.down;
                        j.m_jointRadius = pb.radius * Eval(pb.radiusCurve, x) * radiusScale;
                        joints.Add(j);
                    }
                    springBone.Springs.Add(new Vrm10InstanceSpringBone.Spring(pb.name) { Joints = joints, ColliderGroups = colliderGroups });
                }
            }
        }

        static float Eval(AnimationCurve c, float x) => c != null && c.length > 0 ? c.Evaluate(x) : 1f;

        static void Materials(GameObject root, List<Object> temp) {
            var shader = Shader.Find("VRM10/MToon10");
            if (!shader) throw new InvalidOperationException("Shader VRM10/MToon10 not found. Is UniVRM (com.vrmc.vrm) installed?");
            var map = new Dictionary<Material, Material>();
            Material Swap(Material m) {
                if (!m || m.shader == shader) return m;
                if (!map.TryGetValue(m, out var n)) temp.Add(map[m] = n = ToMToon(m, shader));
                return n;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(Swap).ToArray();
        }

        // Reads the common property names of lilToon, Poiyomi, Standard, UTS and MToon 0.x.
        static Material ToMToon(Material src, Shader shader) {
            var dst = new Material(shader) { name = src.name };
            var m = new MToon10Context(dst);

            var mainProp = new[] { "_MainTex", "_BaseMap", "_BaseColorMap" }.FirstOrDefault(src.HasProperty);
            var main = mainProp != null ? src.GetTexture(mainProp) : null;
            var color = Col(src, Color.white, "_Color", "_BaseColor");
            m.BaseColorTexture = main;
            m.BaseColorFactorSrgb = color;
            if (mainProp != null) {
                m.TextureScale = src.GetTextureScale(mainProp);
                m.TextureOffset = src.GetTextureOffset(mainProp);
            }

            // MToon shade = shade texture * shade color, so fall back to the main texture to keep a textured shadow
            var shade = Tex(src, "_ShadowColorTex", "_1st_ShadeMap", "_ShadeTexture");
            var tint = Flag(src, "_UseShadow") ? Col(src, new Color(0.75f, 0.75f, 0.8f), "_ShadowColor", "_1st_ShadeColor", "_ShadeColor") : Color.white;
            m.ShadeColorTexture = shade ? shade : main;
            m.ShadeColorFactorSrgb = shade ? tint : color * tint;

            var normal = Tex(src, "_BumpMap", "_NormalMap");
            if (normal && Flag(src, "_UseBumpMap")) {
                m.NormalTexture = normal;
                m.NormalTextureScale = Float(src, 1, "_BumpScale");
            }

            if (src.IsKeywordEnabled("_EMISSION") || Float(src, 0, "_UseEmission", "_EnableEmission") > 0.5f) {
                m.EmissiveTexture = Tex(src, "_EmissionMap");
                m.EmissiveFactorLinear = Col(src, Color.black, "_EmissionColor").linear;
            }

            var renderType = src.GetTag("RenderType", false, "");
            m.AlphaMode = renderType == "Transparent" || src.renderQueue >= 2900 ? MToon10AlphaMode.Transparent
                : renderType.Contains("Cutout") || src.renderQueue >= 2450 ? MToon10AlphaMode.Cutout
                : MToon10AlphaMode.Opaque;
            m.AlphaCutoff = Float(src, 0.5f, "_Cutoff");
            m.DoubleSidedMode = Float(src, 2, "_Cull", "_CullMode") == 0 ? MToon10DoubleSidedMode.On : MToon10DoubleSidedMode.Off;

            // ponytail: lilToon outline only (_OutlineWidth in cm). Poiyomi's _LineWidth is not read.
            if (src.shader.name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0 && src.HasProperty("_OutlineWidth")) {
                m.OutlineWidthMode = MToon10OutlineMode.World;
                m.OutlineWidthFactor = src.GetFloat("_OutlineWidth") * 0.01f;
                m.OutlineColorFactorSrgb = Col(src, Color.black, "_OutlineColor");
                m.OutlineWidthMultiplyTexture = Tex(src, "_OutlineWidthMask");
            }

            m.Validate();
            return dst;
        }

        static Texture Tex(Material m, params string[] names) {
            foreach (var n in names)
                if (m.HasProperty(n) && m.GetTexture(n))
                    return m.GetTexture(n);
            return null;
        }

        static Color Col(Material m, Color fallback, params string[] names) {
            var n = names.FirstOrDefault(m.HasProperty);
            return n != null ? m.GetColor(n) : fallback;
        }

        static float Float(Material m, float fallback, params string[] names) {
            var n = names.FirstOrDefault(m.HasProperty);
            return n != null ? m.GetFloat(n) : fallback;
        }

        // toggles like lilToon's _UseShadow; a shader without the toggle counts as on
        static bool Flag(Material m, string name) => !m.HasProperty(name) || m.GetFloat(name) > 0.5f;
    }
}
