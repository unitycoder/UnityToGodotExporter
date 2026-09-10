using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    public enum Severity { Info, Warning, Manual }

    public sealed class Issue
    {
        public Severity Severity;
        public string Scene;
        public string Target;
        public string UnityType;
        public string Message;
    }

    /// <summary>
    /// Shared state for one export run: where files go, what has been copied,
    /// the GUID -> res:// table, and everything that could not be converted.
    /// </summary>
    public sealed class ExportContext
    {
        public readonly string OutputRoot;
        public readonly List<Issue> Issues = new List<Issue>();

        /// "guid:localFileId" -> "res://..."  Every cross-reference flows through this.
        public readonly Dictionary<string, string> IdMap = new Dictionary<string, string>();

        public readonly List<Material> PendingMaterials = new List<Material>();
        readonly Dictionary<Material, string> _materialTargets = new Dictionary<Material, string>();
        readonly HashSet<string> _copiedAssets = new HashSet<string>();
        readonly HashSet<string> _onceKeys = new HashSet<string>();

        public string CurrentScene = string.Empty;
        public int NodeCount, MeshRefCount, TextureCount;

        public ExportContext(string outputRoot) { OutputRoot = outputRoot; }

        // ---------------------------------------------------------------- issues

        public void Report(Severity sev, string target, string unityType, string message)
        {
            Issues.Add(new Issue
            {
                Severity = sev,
                Scene = CurrentScene,
                Target = target,
                UnityType = unityType,
                Message = message
            });
        }

        /// For systemic notes that would otherwise repeat hundreds of times.
        public void ReportOnce(string key, Severity sev, string target, string unityType, string message)
        {
            if (_onceKeys.Add(key)) Report(sev, target, unityType, message);
        }

        // ---------------------------------------------------------------- assets

        /// Copies a source asset file into &lt;output&gt;/assets/ preserving its
        /// relative path, and returns the res:// path it will have in Godot.
        public string CopyAsset(Object obj)
        {
            if (obj == null) return null;
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/")) return null;

            string rel = assetPath.Substring(7).Replace('\\', '/');
            if (_copiedAssets.Add(assetPath))
            {
                string dest = Path.Combine(OutputRoot, "assets", rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(assetPath, dest, true);
            }
            string res = "res://" + rel;
            MapId(obj, res);
            return res;
        }

        public void MapId(Object obj, string resPath)
        {
            string guid; long fileId;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out fileId))
                IdMap[guid + ":" + fileId] = resPath;
        }

        // ------------------------------------------------------------- mesh refs

        sealed class Primitive { public string Type; public string Props; }

        static Primitive BuiltinMesh(string name)
        {
            switch (name)
            {
                case "Cube":     return new Primitive { Type = "BoxMesh",      Props = "{\"size\": [1, 1, 1]}" };
                case "Sphere":   return new Primitive { Type = "SphereMesh",   Props = "{\"radius\": 0.5, \"height\": 1}" };
                case "Capsule":  return new Primitive { Type = "CapsuleMesh",  Props = "{\"radius\": 0.5, \"height\": 2}" };
                case "Cylinder": return new Primitive { Type = "CylinderMesh", Props = "{\"top_radius\": 0.5, \"bottom_radius\": 0.5, \"height\": 2}" };
                case "Plane":    return new Primitive { Type = "PlaneMesh",    Props = "{\"size\": [10, 10]}" };
                case "Quad":     return new Primitive { Type = "QuadMesh",     Props = "{\"size\": [1, 1]}" };
                default:         return null;
            }
        }

        public void WriteMeshRef(JsonWriter w, string name, Mesh mesh, string target)
        {
            w.BeginObject(name);
            if (mesh == null)
            {
                w.Str("kind", "null");
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(mesh);
                bool isBuiltin = string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/");

                if (isBuiltin)
                {
                    var prim = BuiltinMesh(mesh.name);
                    if (prim != null)
                    {
                        w.Str("kind", "primitive");
                        w.Str("godotType", prim.Type);
                        w.Raw("props", prim.Props);
                    }
                    else
                    {
                        w.Str("kind", "unresolved");
                        w.Str("reason", "runtime-generated or built-in mesh '" + mesh.name + "'");
                        Report(Severity.Manual, target, "Mesh", "Mesh '" + mesh.name + "' has no source asset (generated at runtime or an unmapped built-in).");
                    }
                }
                else
                {
                    w.Str("kind", "file");
                    w.Str("path", CopyAsset(mesh));
                    w.Str("sub", mesh.name);
                    MeshRefCount++;
                }
            }
            w.EndObject();
        }

        // --------------------------------------------------------- material refs

        public void WriteMaterialRef(JsonWriter w, string name, Material m, string target)
        {
            w.BeginObject(name);
            if (m == null)
            {
                w.Str("kind", "null");
                w.EndObject();
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/"))
            {
                // Default-Material and friends.
                w.Str("kind", "default");
                w.Str("sourceName", m.name);
            }
            else
            {
                string res;
                if (!_materialTargets.TryGetValue(m, out res))
                {
                    res = assetPath.EndsWith(".mat")
                        ? Conv.ResPath(assetPath, ".tres")
                        : "res://materials/" + Conv.NodeName(Path.GetFileNameWithoutExtension(assetPath) + "_" + m.name) + ".tres";
                    _materialTargets[m] = res;
                    PendingMaterials.Add(m);
                    MapId(m, res);
                }
                w.Str("kind", "material");
                w.Str("path", res);
            }
            w.EndObject();
        }

        public string MaterialTarget(Material m)
        {
            string res;
            return _materialTargets.TryGetValue(m, out res) ? res : null;
        }

        // ---------------------------------------------------------- texture refs

        public void WriteTextureRef(JsonWriter w, string name, Texture t)
        {
            if (t == null) { w.Null(name); return; }
            string res = CopyAsset(t);
            if (res == null) { w.Null(name); return; }
            TextureCount++;
            w.Str(name, res);
        }
    }
}
