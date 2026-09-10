# Changelog

All notable changes to this package are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-10

Initial prototype. Mechanical export tier only.

### Added
- `Tools → Godot Exporter → Export Scenes` editor window with scene selection and output folder.
- Scene graph export to JSON IR with Unity → Godot coordinate conversion.
- Node type resolution: Rigidbody → Collider → Camera → Light → MeshRenderer → Node3D, remaining components emitted as generated child nodes.
- Mesh references, including built-in Cube/Sphere/Capsule/Cylinder/Plane/Quad mapped to Godot primitive meshes with correct dimensions.
- Material export for Standard, URP/Lit, URP/Simple Lit and Unlit shaders to StandardMaterial3D IR; unknown shaders keep a raw property dump.
- Collider export: Box, Sphere, Capsule (with axis correction) and Mesh colliders.
- Camera, Light and AudioSource export.
- Asset copying into `assets/` with relative paths preserved, plus a `guid:localFileId → res://` id map.
- HTML conversion report grouping unconverted and needs-verification items.

### Known limitations
- Scripts, animation, skinned meshes, particles, UI/Canvas, terrain, TextMeshPro, LODGroup, NavMesh and reflection probes are reported, not converted.
- Prefab instances are flattened rather than emitted as instanced scenes.
- Light intensity and Rigidbody drag values are passed through raw and need rebalancing.
