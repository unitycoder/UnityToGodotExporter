using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    /// <summary>
    /// Walks a GameObject hierarchy and emits Godot node IR.
    ///
    /// Key structural difference: Unity puts many components on one GameObject,
    /// Godot wants one job per node. So each GameObject becomes one node whose
    /// type comes from the "primary" component, and the remaining components
    /// become generated child nodes.
    /// </summary>
    public static class NodeExporter
    {
        public static void WriteNode(GameObject go, JsonWriter w, ExportContext ctx, string path)
        {
            path = string.IsNullOrEmpty(path) ? go.name : path + "/" + go.name;
            ctx.NodeCount++;

            Rigidbody rb = null;
            Camera cam = null;
            Light light = null;
            MeshFilter mf = null;
            MeshRenderer mr = null;
            var colliders = new List<Collider>();
            var audio = new List<AudioSource>();
            var unconverted = new List<Component>();

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    ctx.Report(Severity.Warning, path, "(missing)", "GameObject has a missing script reference.");
                    continue;
                }
                if (c is Transform) continue;
                if (c is Rigidbody) { rb = (Rigidbody)c; continue; }
                if (c is Camera) { cam = (Camera)c; continue; }
                if (c is Light) { light = (Light)c; continue; }
                if (c is MeshFilter) { mf = (MeshFilter)c; continue; }
                if (c is MeshRenderer) { mr = (MeshRenderer)c; continue; }
                if (c is Collider) { colliders.Add((Collider)c); continue; }
                if (c is AudioSource) { audio.Add((AudioSource)c); continue; }
                unconverted.Add(c);
            }

            bool hasMesh = mf != null && mr != null;
            string nodeType = "Node3D";
            if (rb != null) nodeType = rb.isKinematic ? "AnimatableBody3D" : "RigidBody3D";
            else if (colliders.Count > 0) nodeType = "StaticBody3D";
            else if (cam != null) nodeType = "Camera3D";
            else if (light != null) nodeType = LightNodeType(light, ctx, path);
            else if (hasMesh) nodeType = "MeshInstance3D";

            bool meshIsPrimary = nodeType == "MeshInstance3D";
            bool camIsPrimary = nodeType == "Camera3D";
            bool lightIsPrimary = nodeType.EndsWith("Light3D");

            w.BeginObject();
            w.Str("name", Conv.NodeName(go.name));
            w.Str("type", nodeType);
            w.Bool("visible", go.activeSelf);
            w.Str("unityPath", path);

            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(prefab)) w.Str("prefabSource", prefab);
            }

            var t = go.transform;
            w.BeginObject("transform");
            w.Vec3("position", Conv.Pos(t.localPosition));
            w.Quat("rotation", Conv.Rot(t.localRotation));
            w.Vec3("scale", Conv.Scale(t.localScale));
            w.EndObject();

            w.BeginObject("props");
            if (meshIsPrimary) WriteMeshProps(mf, mr, w, ctx, path);
            else if (camIsPrimary) WriteCameraProps(cam, w, ctx, path);
            else if (lightIsPrimary) WriteLightProps(light, w, ctx, path);
            else if (rb != null) WriteBodyProps(rb, w, ctx, path);
            w.EndObject();

            w.BeginArray("children");

            // Components that lost the fight for the primary slot become children.
            if (hasMesh && !meshIsPrimary)
            {
                w.BeginObject();
                w.Str("name", Conv.NodeName(go.name) + "_Mesh");
                w.Str("type", "MeshInstance3D");
                w.Bool("generated", true);
                Identity(w);
                w.BeginObject("props");
                WriteMeshProps(mf, mr, w, ctx, path);
                w.EndObject();
                w.BeginArray("children");
                w.EndArray();
                w.EndObject();
            }

            for (int i = 0; i < colliders.Count; i++)
                WriteColliderNode(colliders[i], i, w, ctx, path);

            for (int i = 0; i < audio.Count; i++)
                WriteAudioNode(audio[i], i, w, ctx, path);

            if (cam != null && !camIsPrimary) ctx.Report(Severity.Warning, path, "Camera", "Camera shares a GameObject with a physics body; emitted only as a note.");
            if (light != null && !lightIsPrimary) ctx.Report(Severity.Warning, path, "Light", "Light shares a GameObject with a physics body; emitted only as a note.");

            foreach (Transform child in t)
                WriteNode(child.gameObject, w, ctx, path);

            w.EndArray(); // children

            w.BeginArray("unconverted");
            foreach (var c in unconverted)
            {
                string reason = ReasonFor(c);
                w.BeginObject();
                w.Str("unityType", c.GetType().Name);
                w.Str("reason", reason);
                w.EndObject();
                ctx.Report(Severity.Manual, path, c.GetType().Name, reason);
            }
            w.EndArray();

            w.EndObject();
        }

        static void Identity(JsonWriter w)
        {
            w.BeginObject("transform");
            w.Vec3("position", Vector3.zero);
            w.Quat("rotation", Quaternion.identity);
            w.Vec3("scale", Vector3.one);
            w.EndObject();
        }

        // ------------------------------------------------------------ components

        static void WriteMeshProps(MeshFilter mf, MeshRenderer mr, JsonWriter w, ExportContext ctx, string path)
        {
            ctx.WriteMeshRef(w, "mesh", mf.sharedMesh, path);
            w.BeginArray("surfaceMaterials");
            var mats = mr.sharedMaterials;
            foreach (var m in mats) ctx.WriteMaterialRef(w, null, m, path);
            w.EndArray();
            w.Bool("visible", mr.enabled);
            // Godot cast_shadow: 0 Off, 1 On, 2 DoubleSided, 3 ShadowsOnly
            switch (mr.shadowCastingMode)
            {
                case UnityEngine.Rendering.ShadowCastingMode.Off: w.Int("cast_shadow", 0); break;
                case UnityEngine.Rendering.ShadowCastingMode.TwoSided: w.Int("cast_shadow", 2); break;
                case UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly: w.Int("cast_shadow", 3); break;
                default: w.Int("cast_shadow", 1); break;
            }
        }

        static void WriteBodyProps(Rigidbody rb, JsonWriter w, ExportContext ctx, string path)
        {
            w.Num("mass", rb.mass);
            w.Num("gravity_scale", rb.useGravity ? 1f : 0f);
            w.Num("linear_damp", rb.linearDamping);
            w.Num("angular_damp", rb.angularDamping);
            w.Bool("freeze", rb.isKinematic);
            ctx.ReportOnce("rb-damp", Severity.Info, path, "Rigidbody",
                "Unity drag and Godot damp are not the same units. Expect to retune damping values.");
        }

        static void WriteCameraProps(Camera cam, JsonWriter w, ExportContext ctx, string path)
        {
            w.Str("projection", cam.orthographic ? "orthogonal" : "perspective");
            w.Num("fov", cam.fieldOfView);                 // both are vertical FOV by default
            w.Num("size", cam.orthographicSize * 2f);      // Unity stores half-height, Godot full
            w.Num("near", cam.nearClipPlane);
            w.Num("far", cam.farClipPlane);
            w.Bool("current", cam.CompareTag("MainCamera"));
            w.Int("cull_mask", cam.cullingMask);
            if (cam.clearFlags == CameraClearFlags.SolidColor) w.Col("clearColor", cam.backgroundColor);
        }

        static string LightNodeType(Light l, ExportContext ctx, string path)
        {
            switch (l.type)
            {
                case LightType.Directional: return "DirectionalLight3D";
                case LightType.Point: return "OmniLight3D";
                case LightType.Spot: return "SpotLight3D";
                default:
                    ctx.Report(Severity.Manual, path, "Light", "Light type '" + l.type + "' has no direct Godot equivalent.");
                    return "OmniLight3D";
            }
        }

        static void WriteLightProps(Light l, JsonWriter w, ExportContext ctx, string path)
        {
            w.Col("light_color", l.color);
            w.Num("light_energy", l.intensity);
            w.Num("light_indirect_energy", l.bounceIntensity);
            w.Bool("shadow_enabled", l.shadows != LightShadows.None);
            if (l.type == LightType.Point || l.type == LightType.Spot) w.Num("range", l.range);
            if (l.type == LightType.Spot)
            {
                w.Num("spot_angle", l.spotAngle * 0.5f); // Unity full cone, Godot half angle
                w.Num("spot_angle_attenuation", 1f);
            }
            ctx.ReportOnce("light-units", Severity.Warning, path, "Light",
                "Light intensity units differ between Unity (esp. URP physical lights) and Godot. " +
                "Energy values are passed through raw and will need rebalancing.");
        }

        static void WriteColliderNode(Collider col, int index, JsonWriter w, ExportContext ctx, string path)
        {
            w.BeginObject();
            w.Str("name", "CollisionShape3D" + (index > 0 ? index.ToString() : string.Empty));
            w.Str("type", "CollisionShape3D");
            w.Bool("generated", true);

            Vector3 center = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            w.BeginObject("props");
            w.BeginObject("shape");

            if (col is BoxCollider)
            {
                var b = (BoxCollider)col;
                center = b.center;
                w.Str("type", "BoxShape3D");
                w.Vec3("size", b.size);
            }
            else if (col is SphereCollider)
            {
                var s = (SphereCollider)col;
                center = s.center;
                w.Str("type", "SphereShape3D");
                w.Num("radius", s.radius);
            }
            else if (col is CapsuleCollider)
            {
                var c = (CapsuleCollider)col;
                center = c.center;
                w.Str("type", "CapsuleShape3D");
                w.Num("radius", c.radius);
                w.Num("height", Mathf.Max(c.height, c.radius * 2f));
                // Godot capsules are always Y-aligned.
                if (c.direction == 0) rot = Quaternion.Euler(0, 0, 90);
                else if (c.direction == 2) rot = Quaternion.Euler(90, 0, 0);
            }
            else if (col is MeshCollider)
            {
                var mc = (MeshCollider)col;
                w.Str("type", mc.convex ? "ConvexPolygonShape3D" : "ConcavePolygonShape3D");
                ctx.WriteMeshRef(w, "sourceMesh", mc.sharedMesh, path);
            }
            else
            {
                w.Str("type", "unresolved");
                w.Str("reason", col.GetType().Name + " has no mapping");
                ctx.Report(Severity.Manual, path, col.GetType().Name, "Collider type not supported by the prototype.");
            }

            w.EndObject(); // shape

            if (col.isTrigger)
            {
                w.Bool("isTrigger", true);
                ctx.ReportOnce("trigger", Severity.Warning, path, "Collider",
                    "isTrigger colliders need an Area3D parent in Godot, not a body. Emitted as CollisionShape3D — restructure manually.");
            }
            w.EndObject(); // props

            w.BeginObject("transform");
            w.Vec3("position", Conv.Pos(center));
            w.Quat("rotation", Conv.Rot(rot));
            w.Vec3("scale", Vector3.one);
            w.EndObject();

            w.BeginArray("children");
            w.EndArray();
            w.EndObject();
        }

        static void WriteAudioNode(AudioSource a, int index, JsonWriter w, ExportContext ctx, string path)
        {
            bool spatial = a.spatialBlend > 0.01f;
            w.BeginObject();
            w.Str("name", "AudioPlayer" + (index > 0 ? index.ToString() : string.Empty));
            w.Str("type", spatial ? "AudioStreamPlayer3D" : "AudioStreamPlayer");
            w.Bool("generated", true);
            Identity(w);

            w.BeginObject("props");
            string clipRes = a.clip != null ? ctx.CopyAsset(a.clip) : null;
            w.Str("streamPath", clipRes);
            w.Num("volume_db", Conv.LinearToDb(a.volume));
            w.Num("pitch_scale", a.pitch);
            w.Bool("autoplay", a.playOnAwake);
            if (spatial) w.Num("max_distance", a.maxDistance);
            if (a.loop)
            {
                w.Bool("loopRequested", true);
                ctx.ReportOnce("audio-loop", Severity.Info, path, "AudioSource",
                    "Looping is a property of the stream resource in Godot, not the player. The importer must set loop on the imported audio.");
            }
            w.EndObject();

            w.BeginArray("children");
            w.EndArray();
            w.EndObject();
        }

        // ---------------------------------------------------------------- reasons

        static string ReasonFor(Component c)
        {
            switch (c.GetType().Name)
            {
                case "SkinnedMeshRenderer": return "Skinned meshes and skeletons are not implemented yet.";
                case "Animator":
                case "Animation": return "Animation is not implemented yet (AnimationPlayer / AnimationTree).";
                case "ParticleSystem":
                case "ParticleSystemRenderer": return "Particles are not implemented yet (GPUParticles3D).";
                case "Canvas":
                case "CanvasRenderer":
                case "CanvasScaler":
                case "RectTransform":
                case "GraphicRaycaster": return "UI (Canvas) is not implemented yet (Control tree).";
                case "Terrain":
                case "TerrainCollider": return "Unity Terrain has no Godot equivalent; needs a mesh/heightmap export.";
                case "CharacterController": return "Map manually to CharacterBody3D + a CollisionShape3D.";
                case "NavMeshAgent": return "Map manually to NavigationAgent3D.";
                case "ReflectionProbe": return "Map manually to ReflectionProbe (settings differ).";
                case "LODGroup": return "Map manually; Godot uses per-mesh visibility ranges.";
                case "TextMeshPro":
                case "TextMeshProUGUI": return "TextMeshPro is not implemented yet (Label3D / RichTextLabel).";
            }
            if (c is MonoBehaviour)
                return "MonoBehaviour script — script conversion is not part of this prototype.";
            return "No mapping implemented for " + c.GetType().Name + ".";
        }
    }
}
