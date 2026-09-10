<img src="https://img.shields.io/badge/VibeCoded-100%25-green" alt="AI Generated Content"/>

# Unity To Godot/Redot Exporter

This is ONLY meant for (ex) Unity Industry customers! *For non-industry users, Unity is still the best.<br>
Reasons: https://discussions.unity.com/t/questions-about-unity-industry-runtime-fee/1697714

### Pull Requests

Are welcome, i'm out of tokens!! : )
Goal is to make 1 click exporter for as much stuff that it can handle.
And later add Ollama/OpenAI support for converting scripts and other logic perhaps?

### Images

<img width="777" height="640" alt="image" src="https://github.com/user-attachments/assets/844d5e19-1875-44b5-a30c-715e0af2ea60" />

<img width="865" height="547" alt="image" src="https://github.com/user-attachments/assets/aa973a1d-80bb-480c-9505-7d061b79f9d3" />

### AI Generated Readme

# Godot Exporter

`com.unitycoder.godotexporter`

Unity-side half of a Unity → Godot/Redot converter. Exports scenes into an
engine-agnostic intermediate representation (JSON + copied assets). A separate
Godot editor plugin reads that IR and writes `.tscn` / `.tres`.

No script conversion. This package covers the mechanical tier only — the parts
that need no LLM.

## Install

**Package Manager → Add package from git URL**

```
https://github.com/unitycoder/UnityToGodotExporter.git
```

**Or from disk:** Package Manager → Add package from disk… → pick `package.json`.

**Or manually:** add to `Packages/manifest.json`

```json
"com.unitycoder.godotexporter": "https://github.com/unitycoder/UnityToGodotExporter.git"
```

Requires Unity 2021.3 or newer. Editor-only assembly, nothing ships in a build.

## Use

`Tools → Godot Exporter → Export Scenes`

Pick an output folder, tick scenes, press Export. Scenes are opened one at a
time in Single mode and the originally open scene is restored afterwards.

## Output layout

```
<output>/
  manifest.json     project name, Unity version, pipeline, color space, stats
  idmap.json        "guid:localFileId" -> "res://..."   (every cross-ref goes through this)
  materials.json    all referenced materials as StandardMaterial3D IR
  scenes/*.json     node trees
  assets/           copied source files, relative paths preserved
  report.html       what converted, what did not, and why
```

`assets/` mirrors the paths under `Assets/`, so `Assets/Models/Rock.fbx`
becomes `assets/Models/Rock.fbx` and is referenced as `res://Models/Rock.fbx`.
Godot's own importers handle the actual files.

## IR node shape

```json
{
  "name": "Crate",
  "type": "RigidBody3D",
  "visible": true,
  "unityPath": "Level/Props/Crate",
  "prefabSource": "Assets/Prefabs/Crate.prefab",
  "transform": { "position": [1,0,-3], "rotation": [0,0,0,1], "scale": [1,1,1] },
  "props": { "mass": 8.0, "gravity_scale": 1.0 },
  "children": [
    { "name": "Crate_Mesh", "type": "MeshInstance3D", "generated": true, "...": "..." },
    { "name": "CollisionShape3D", "type": "CollisionShape3D", "generated": true, "...": "..." }
  ],
  "unconverted": [
    { "unityType": "CrateHealth", "reason": "MonoBehaviour script — ..." }
  ]
}
```

Unity puts many components on one GameObject; Godot wants one job per node. So
each GameObject becomes one node whose type comes from the *primary* component
(Rigidbody > Collider > Camera > Light > MeshRenderer), and the rest become
generated child nodes.

## Coordinate conversion

All of it lives in `Conv.cs` and nowhere else.

Unity is left-handed with +Z forward, Godot right-handed with −Z forward. The
basis change is `M = diag(1, 1, −1)`, so `p' = M·p` and `R' = M·R·M`, which for a
quaternion is `(x, y, z, w) → (−x, −y, z, w)`.

All numbers are written with `InvariantCulture`. On a Finnish (or any
comma-decimal) system a stray `ToString()` would emit `1,5` and silently produce
invalid JSON.

## What converts

| Unity | Godot |
|---|---|
| GameObject + Transform | Node3D |
| MeshFilter + MeshRenderer | MeshInstance3D |
| Built-in Cube/Sphere/Capsule/Cylinder/Plane/Quad | BoxMesh / SphereMesh / … with correct dimensions |
| Rigidbody | RigidBody3D (AnimatableBody3D if kinematic) |
| Collider without Rigidbody | StaticBody3D + CollisionShape3D |
| Box/Sphere/Capsule/MeshCollider | BoxShape3D / SphereShape3D / CapsuleShape3D / Con(vex\|cave)PolygonShape3D |
| Camera | Camera3D (ortho size ×2, both FOVs vertical) |
| Light | DirectionalLight3D / OmniLight3D / SpotLight3D (spot angle halved) |
| AudioSource | AudioStreamPlayer3D / AudioStreamPlayer |
| Standard, URP/Lit, Unlit | StandardMaterial3D |

## What does not

Flagged in the report, never silently dropped: scripts, animation, skinned
meshes, particles, UI/Canvas, terrain, TextMeshPro, LODGroup, NavMesh,
reflection probes, custom shaders.

Known rough edges the report calls out by name:

- **Light intensity** units differ; energy is passed through raw and needs rebalancing.
- **Metallic/smoothness maps** pack smoothness in alpha where Godot wants roughness — the importer has to invert that channel.
- **isTrigger colliders** need an Area3D parent, not a body. Emitted as a plain shape.
- **Rigidbody drag** and Godot damp are not the same units.
- Unresolved materials keep a `rawProperties` dump so a later LLM stage has something to work from.

## Roadmap

1. Godot-side importer that walks this IR and writes `.tscn` / `.tres`.
2. Prefabs as separate `.tscn` files with scene instancing, instead of flattening.
3. Animation clips (curve data is easy; property paths need the idmap).
4. The script tier: Roslyn pre-pass → C# → GDScript with `--headless --check-only` validation.

## License

MIT. See [LICENSE.md](LICENSE.md).
