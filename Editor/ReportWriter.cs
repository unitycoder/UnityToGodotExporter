using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    /// <summary>
    /// The report is the headline feature of the whole converter: a run that
    /// honestly says what it could not do beats one that silently emits a
    /// broken project.
    /// </summary>
    public static class ReportWriter
    {
        public static void Write(ExportContext ctx, string path)
        {
            int manual = 0, warning = 0, info = 0;
            foreach (var i in ctx.Issues)
            {
                if (i.Severity == Severity.Manual) manual++;
                else if (i.Severity == Severity.Warning) warning++;
                else info++;
            }

            var sb = new StringBuilder();
            sb.Append(@"<!doctype html><html><head><meta charset=""utf-8"">
<title>Unity to Godot export report</title><style>
body{background:#15171a;color:#d6d9dd;font:14px/1.5 ui-monospace,Menlo,Consolas,monospace;margin:0;padding:32px}
h1{font-size:20px;margin:0 0 4px}h2{font-size:15px;margin:28px 0 8px;color:#8fb8ff}
.sub{color:#7a828c;margin-bottom:24px}
.cards{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:8px}
.card{background:#1d2126;border:1px solid #2b3138;border-radius:6px;padding:12px 18px;min-width:110px}
.card b{display:block;font-size:22px;font-weight:600}
table{border-collapse:collapse;width:100%;margin-top:8px}
th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #262b31;vertical-align:top}
th{color:#7a828c;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.04em}
tr:hover td{background:#1b1f24}
.sev{white-space:nowrap;font-weight:600}
.manual{color:#ff7b72}.warning{color:#e3b341}.info{color:#6aa9ff}
td.t{color:#9aa3ad;word-break:break-all}
.ok{color:#5ec27a}
</style></head><body>");

            sb.Append("<h1>Unity &rarr; Godot export report</h1>");
            sb.Append("<div class=\"sub\">").Append(Esc(PlayerSettings.productName))
              .Append(" &middot; Unity ").Append(Esc(Application.unityVersion))
              .Append(" &middot; ").Append(Esc(ProjectExporter.DetectPipeline()))
              .Append("</div>");

            sb.Append("<div class=\"cards\">");
            Card(sb, "Nodes", ctx.NodeCount.ToString(), "ok");
            Card(sb, "Mesh refs", ctx.MeshRefCount.ToString(), "ok");
            Card(sb, "Materials", ctx.PendingMaterials.Count.ToString(), "ok");
            Card(sb, "Textures", ctx.TextureCount.ToString(), "ok");
            Card(sb, "Manual", manual.ToString(), "manual");
            Card(sb, "Warnings", warning.ToString(), "warning");
            Card(sb, "Notes", info.ToString(), "info");
            sb.Append("</div>");

            Section(sb, ctx, Severity.Manual, "Cannot be converted &mdash; needs manual work");
            Section(sb, ctx, Severity.Warning, "Converted, but verify");
            Section(sb, ctx, Severity.Info, "Notes");

            sb.Append("</body></html>");
            File.WriteAllText(path, sb.ToString());
        }

        static void Card(StringBuilder sb, string label, string value, string cls)
        {
            sb.Append("<div class=\"card\"><b class=\"").Append(cls).Append("\">")
              .Append(value).Append("</b>").Append(label).Append("</div>");
        }

        static void Section(StringBuilder sb, ExportContext ctx, Severity sev, string title)
        {
            var rows = new List<Issue>();
            foreach (var i in ctx.Issues) if (i.Severity == sev) rows.Add(i);
            if (rows.Count == 0) return;

            // Collapse identical (type, message) pairs so one bad prefab used 200
            // times does not produce 200 rows.
            var grouped = new Dictionary<string, List<Issue>>();
            var order = new List<string>();
            foreach (var i in rows)
            {
                string key = i.UnityType + "|" + i.Message;
                if (!grouped.ContainsKey(key)) { grouped[key] = new List<Issue>(); order.Add(key); }
                grouped[key].Add(i);
            }

            sb.Append("<h2>").Append(title).Append(" (").Append(rows.Count).Append(")</h2>");
            sb.Append("<table><tr><th>Type</th><th>Count</th><th>Detail</th><th>First occurrence</th></tr>");
            foreach (var key in order)
            {
                var g = grouped[key];
                var first = g[0];
                sb.Append("<tr><td class=\"sev ").Append(sev.ToString().ToLower()).Append("\">")
                  .Append(Esc(first.UnityType)).Append("</td><td>").Append(g.Count).Append("</td><td>")
                  .Append(Esc(first.Message)).Append("</td><td class=\"t\">")
                  .Append(Esc(string.IsNullOrEmpty(first.Scene) ? first.Target : first.Scene + " : " + first.Target))
                  .Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
