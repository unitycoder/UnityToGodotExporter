using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    public sealed class ExporterWindow : EditorWindow
    {
        const string OutputKey = "UnityCoder.GodotExporter.Output";

        string _output;
        List<string> _scenes = new List<string>();
        List<bool> _selected = new List<bool>();
        Vector2 _scroll;
        string _lastResult;
        string _lastReport;

        [MenuItem("Tools/Godot Exporter/Export Scenes")]
        static void Open()
        {
            var w = GetWindow<ExporterWindow>("U\u2192G Export");
            w.minSize = new Vector2(620, 460);
        }

        void OnEnable()
        {
            _output = EditorPrefs.GetString(OutputKey, DefaultOutput());
            RefreshScenes();
        }

        static string DefaultOutput()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GodotExport");
        }

        void RefreshScenes()
        {
            _scenes.Clear();
            _selected.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.StartsWith("Assets/")) continue;
                _scenes.Add(p);
                _selected.Add(false);
            }
            // Pre-select whatever is currently open.
            string active = EditorSceneManager.GetActiveScene().path;
            int idx = _scenes.IndexOf(active);
            if (idx >= 0) _selected[idx] = true;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Intermediate representation export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Writes a Godot-agnostic IR (JSON + copied assets). Scenes, transforms, meshes, " +
                "materials, colliders, cameras, lights and audio only. Scripts are not converted.",
                MessageType.None);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Output", GUILayout.Width(50));
                _output = EditorGUILayout.TextField(_output);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string chosen = EditorUtility.SaveFolderPanel("Export folder", _output, "GodotExport");
                    if (!string.IsNullOrEmpty(chosen)) _output = chosen;
                }
            }

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Scenes (" + _scenes.Count + ")", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("All", GUILayout.Width(40))) SetAll(true);
                if (GUILayout.Button("None", GUILayout.Width(50))) SetAll(false);
                if (GUILayout.Button("Refresh", GUILayout.Width(60))) RefreshScenes();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUI.skin.box);
            for (int i = 0; i < _scenes.Count; i++)
                _selected[i] = EditorGUILayout.ToggleLeft(_scenes[i], _selected[i]);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Pipeline detected: " + ProjectExporter.DetectPipeline());

            GUI.enabled = AnySelected() && !string.IsNullOrEmpty(_output);
            if (GUILayout.Button("Export", GUILayout.Height(30))) DoExport();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open folder")) EditorUtility.RevealInFinder(_output);
                    if (GUILayout.Button("Open report")) Application.OpenURL("file://" + _lastReport);
                }
            }
        }

        void SetAll(bool v) { for (int i = 0; i < _selected.Count; i++) _selected[i] = v; }

        bool AnySelected()
        {
            foreach (var s in _selected) if (s) return true;
            return false;
        }

        void DoExport()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var paths = new List<string>();
            for (int i = 0; i < _scenes.Count; i++) if (_selected[i]) paths.Add(_scenes[i]);

            EditorPrefs.SetString(OutputKey, _output);
            Directory.CreateDirectory(_output);

            var ctx = ProjectExporter.Run(paths, _output);

            int manual = 0, warning = 0;
            foreach (var i in ctx.Issues)
            {
                if (i.Severity == Severity.Manual) manual++;
                else if (i.Severity == Severity.Warning) warning++;
            }

            _lastReport = Path.Combine(_output, "report.html");
            _lastResult = string.Format(
                "{0} scene(s), {1} nodes, {2} materials, {3} textures.\n{4} items need manual work, {5} warnings.",
                paths.Count, ctx.NodeCount, ctx.PendingMaterials.Count, ctx.TextureCount, manual, warning);

            Debug.Log("[GodotExporter] " + _lastResult.Replace("\n", " "));
        }
    }
}
