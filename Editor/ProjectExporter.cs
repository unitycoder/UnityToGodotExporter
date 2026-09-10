using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityCoder.GodotExporter
{
    public static class ProjectExporter
    {
        public const int FormatVersion = 1;

        public static ExportContext Run(IList<string> scenePaths, string outputRoot)
        {
            var ctx = new ExportContext(outputRoot);
            Directory.CreateDirectory(Path.Combine(outputRoot, "scenes"));
            Directory.CreateDirectory(Path.Combine(outputRoot, "assets"));

            string originalScene = SceneManager.GetActiveScene().path;
            var exportedScenes = new List<string>();

            try
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    string p = scenePaths[i];
                    EditorUtility.DisplayProgressBar("Exporting to Godot IR", p, (float)i / Mathf.Max(1, scenePaths.Count));

                    Scene scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                    ctx.CurrentScene = scene.name;

                    var w = new JsonWriter();
                    w.BeginObject();
                    w.Int("formatVersion", FormatVersion);
                    w.Str("name", scene.name);
                    w.Str("source", scene.path);
                    w.Str("target", Conv.ResPath(scene.path, ".tscn"));
                    w.BeginObject("root");
                    w.Str("name", Conv.NodeName(scene.name));
                    w.Str("type", "Node3D");
                    w.Bool("visible", true);
                    w.BeginObject("transform");
                    w.Vec3("position", Vector3.zero);
                    w.Quat("rotation", Quaternion.identity);
                    w.Vec3("scale", Vector3.one);
                    w.EndObject();
                    w.BeginObject("props");
                    w.EndObject();
                    w.BeginArray("children");
                    foreach (var go in scene.GetRootGameObjects())
                        NodeExporter.WriteNode(go, w, ctx, string.Empty);
                    w.EndArray();
                    w.BeginArray("unconverted");
                    w.EndArray();
                    w.EndObject(); // root
                    w.EndObject();

                    string file = "scenes/" + Sanitize(scene.name) + ".json";
                    File.WriteAllText(Path.Combine(outputRoot, file), w.ToString());
                    exportedScenes.Add(file);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(originalScene))
                    EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }

            ctx.CurrentScene = string.Empty;
            WriteMaterials(ctx, outputRoot);
            WriteIdMap(ctx, outputRoot);
            WriteManifest(ctx, outputRoot, exportedScenes);
            ReportWriter.Write(ctx, Path.Combine(outputRoot, "report.html"));

            return ctx;
        }

        static void WriteMaterials(ExportContext ctx, string outputRoot)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Int("formatVersion", FormatVersion);
            w.Str("colorSpace", PlayerSettings.colorSpace == ColorSpace.Linear ? "linear" : "gamma");
            w.BeginArray("materials");
            // The list can grow while we iterate, so index instead of foreach.
            for (int i = 0; i < ctx.PendingMaterials.Count; i++)
                MaterialExporter.Write(ctx.PendingMaterials[i], w, ctx);
            w.EndArray();
            w.EndObject();
            File.WriteAllText(Path.Combine(outputRoot, "materials.json"), w.ToString());
        }

        static void WriteIdMap(ExportContext ctx, string outputRoot)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Int("formatVersion", FormatVersion);
            w.BeginObject("entries");
            foreach (var kv in ctx.IdMap) w.Str(kv.Key, kv.Value);
            w.EndObject();
            w.EndObject();
            File.WriteAllText(Path.Combine(outputRoot, "idmap.json"), w.ToString());
        }

        static void WriteManifest(ExportContext ctx, string outputRoot, List<string> scenes)
        {
            int manual = 0, warning = 0;
            foreach (var i in ctx.Issues)
            {
                if (i.Severity == Severity.Manual) manual++;
                else if (i.Severity == Severity.Warning) warning++;
            }

            var w = new JsonWriter();
            w.BeginObject();
            w.Int("formatVersion", FormatVersion);
            w.Str("exportedAt", DateTime.UtcNow.ToString("o"));
            w.Str("unityVersion", Application.unityVersion);
            w.Str("projectName", PlayerSettings.productName);
            w.Str("renderPipeline", DetectPipeline());
            w.Str("colorSpace", PlayerSettings.colorSpace.ToString());
            w.StrArray("scenes", scenes);
            w.Str("materials", "materials.json");
            w.Str("idmap", "idmap.json");
            w.BeginObject("stats");
            w.Int("nodes", ctx.NodeCount);
            w.Int("meshRefs", ctx.MeshRefCount);
            w.Int("materials", ctx.PendingMaterials.Count);
            w.Int("textures", ctx.TextureCount);
            w.Int("needsManualWork", manual);
            w.Int("warnings", warning);
            w.EndObject();
            w.EndObject();
            File.WriteAllText(Path.Combine(outputRoot, "manifest.json"), w.ToString());
        }

        public static string DetectPipeline()
        {
            var rp = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            if (rp == null) return "BuiltIn";
            string n = rp.GetType().Name;
            if (n.Contains("Universal")) return "URP";
            if (n.Contains("HD")) return "HDRP";
            return n;
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
