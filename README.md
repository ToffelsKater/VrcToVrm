# VRChat to VRM 1.0 Converter

Unity editor tool that turns a VRChat avatar (anything with a VRC Avatar Descriptor, prefab or scene object) into a VRM 1.0 file.
The source avatar is never changed: the tool works on a temporary copy and deletes it after export.

Website and VCC listing: https://toffelskater.github.io/VrcToVrm/

## Requirements

- Unity **2022.3.22f1** (the version VRChat currently supports)
- VRChat Avatars SDK 3.7 or newer
- UniVRM **0.128** or newer (`com.vrmc.vrm` and `com.vrmc.gltf`, the VRM 1.0 packages). The tool installs it for you. Tested with 0.128.1 and 0.131.4.

## Install

1. Add this package, in one of two ways:
   - VRChat Creator Companion: add the listing `https://toffelskater.github.io/VrcToVrm/index.json` (Settings > Packages > Add Repository), then add **VRChat to VRM 1.0 Converter** to your project.
   - From disk: Window > Package Manager > + > Add package from disk > `Packages/com.damien.vrc-to-vrm/package.json`.
2. If UniVRM is missing, a dialog asks to add it. Click **Add**. The tool adds the OpenUPM registry (scope `com.vrmc`) and `com.vrmc.vrm` to `Packages/manifest.json`, and Unity downloads UniVRM. No git install is needed.
   The version matches the UniGLTF (`com.vrmc.gltf`) already in the project, for example 0.128.1 from the VRM Converter for VRChat listing, because the two only compile together. Without one it installs 0.131.4.
   If you clicked **Later**, use **Tools > Install UniVRM for VRChat to VRM**. This menu item and the dialog go away once UniVRM is installed.

The converter itself only compiles when UniVRM 0.128 or newer is present, so the project has no compile errors before then.
A package.json cannot depend on a git URL or on a package from another registry, which is why UniVRM is not listed in its `dependencies`.

To install UniVRM by hand instead, add these lines to `dependencies` in `Packages/manifest.json` (this needs git):
```json
"com.vrmc.gltf": "https://github.com/vrm-c/UniVRM.git?path=/Packages/UniGLTF#v0.131.4",
"com.vrmc.vrm": "https://github.com/vrm-c/UniVRM.git?path=/Packages/VRM10#v0.131.4",
```

### Troubleshooting

**No VRChat to VRM menu, and compile errors in `com.vrmc.vrm`** (for example `'UniGLTFLogger' does not contain a definition for 'Warning'`): `com.vrmc.vrm` and `com.vrmc.gltf` have different versions. This happens when UniVRM is updated through a VCC listing later. In `Packages/manifest.json`, set `"com.vrmc.vrm"` to the version of `Packages/com.vrmc.gltf/package.json`.

**UniVRM older than 0.128** (from an old VCC listing): update or remove it in VCC, then use **Tools > Install UniVRM for VRChat to VRM**.

## Use

1. Open **Tools > VRChat to VRM 1.0**.
2. Drag the avatar into **Avatar**.
3. Fill in the meta: name, authors, version, thumbnail and the license fields (who may use the avatar, commercial use, redistribution, and so on). Name and at least one author are required.
4. Set the options and click **Export VRM 1.0**.

With **Bake first** on (the default), the copy is built the way an upload builds it before conversion: VRCFury, Modular Avatar and other NDMF tools apply their changes, the same as VRCFury's "Build an editor test copy". Afterwards the post-build cleanup runs, so lilToon restores its shader settings and NDMF removes its temporary files. Turn it off to convert the avatar exactly as it is in the scene.

## What gets converted

| VRChat | VRM 1.0 |
| --- | --- |
| Humanoid Animator | Humanoid bones (pose set to T-pose when **Force T-Pose** is on) |
| Avatar size, including a scaled avatar root | Scaled so the meshes are 1.6 m tall when **Scale to height** is on (change the height in the window, or turn it off to keep the original size). The window shows the avatar's current height and the factor. Height runs from the lowest to the highest vertex, so ears and hair count. |
| PhysBone | Spring per unbranched bone chain. Pull + stiffness map to stiffness, momentum to drag, gravity to gravity power, radius to joint radius. The PhysBone curves are applied per joint, and the endpoint position adds a tail bone. Ignore transforms are respected. |
| PhysBone Collider | Spring bone collider plus collider group: sphere, capsule, plane, and the inside sphere and capsule variants |
| Viseme blend shapes (aa, E, ih, oh, ou) | Expressions aa, ee, ih, oh, ou |
| Jaw flap blend shape | Expression aa |
| Eyelid blend shapes (blink, looking up, looking down) | Expressions blink, lookUp, lookDown |
| View position | LookAt offset from the head (first-person eye point) |
| Materials (lilToon, Poiyomi, Standard, UTS, MToon 0.x) | MToon 1.0: main texture and color, shade color and texture, normal map, emission, alpha mode (opaque, cutout, transparent), cutoff, culling, tiling, and lilToon outlines |
| VRCFury, Modular Avatar, other NDMF tools | Applied to the copy before conversion (**Bake first**) |
| Objects tagged EditorOnly | Removed |
| Disabled meshes | Not exported |

## Limitations

- PhysBone and VRM spring bones are simulated differently, so the values are mapped approximately. Use the Stiffness, Drag and Gravity sliders to tune the result.
- PhysBone angle limits, immobile, stretch and grab settings have no VRM 1.0 equivalent and are skipped.
- At a branch below the PhysBone root, the segment from the parent to each branch does not swing. Split such chains into separate PhysBones if this is visible.
- The emotion expressions (happy, angry, sad, relaxed, surprised) are not created: VRChat has no standard for them. Add them in UniVRM after importing the VRM.
- Material conversion reads common property names only. Matcap, rim light and Poiyomi outlines are not converted.

## Development

This repository is a Unity 2022.3.22f1 project built from VRChat's package template. The package lives in `Packages/com.damien.vrc-to-vrm`.

- **Build Release** (run it by hand in GitHub Actions) zips the package, makes a `.unitypackage`, and publishes a GitHub release tagged with the version from `package.json`.
- **Build Repo Listing** runs after each release. It builds the VCC listing (`index.json`) and the website from `Website/` and deploys both to GitHub Pages.
- `Website/index.html` and `Website/app.js` are [Scriban](https://github.com/scriban/scriban) templates. The listing build fills in `listingInfo` and `packages`, so `app.js` must never contain two opening braces in a row.
