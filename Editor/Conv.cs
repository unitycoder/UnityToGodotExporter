using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    /// <summary>
    /// Single place where Unity -> Godot space conversion happens.
    /// Keep it that way. If this logic gets scattered you will never untangle it.
    ///
    /// Unity: left-handed, Y up, +Z forward.
    /// Godot: right-handed, Y up, -Z forward.
    /// Basis change is M = diag(1, 1, -1), so  p' = M*p  and  R' = M*R*M.
    /// For a quaternion that works out to (x, y, z, w) -> (-x, -y, z, w).
    /// </summary>
    public static class Conv
    {
        public static Vector3 Pos(Vector3 p) { return new Vector3(p.x, p.y, -p.z); }
        public static Vector3 Dir(Vector3 d) { return new Vector3(d.x, d.y, -d.z); }
        public static Quaternion Rot(Quaternion q) { return new Quaternion(-q.x, -q.y, q.z, q.w); }

        /// Scale is unaffected by the basis change.
        public static Vector3 Scale(Vector3 s) { return s; }

        static readonly Regex InvalidNodeChars = new Regex("[.:@/\"%]");

        /// Godot node names may not contain . : @ / " %
        public static string NodeName(string n)
        {
            n = InvalidNodeChars.Replace(n ?? string.Empty, "_").Trim();
            return string.IsNullOrEmpty(n) ? "Node" : n;
        }

        public static float LinearToDb(float linear)
        {
            return linear <= 0.0001f ? -80f : 20f * Mathf.Log10(linear);
        }

        /// "Assets/Scenes/Main.unity" -> "res://Scenes/Main.tscn"
        public static string ResPath(string assetPath, string newExtension = null)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string rel = assetPath.StartsWith("Assets/") ? assetPath.Substring(7) : assetPath;
            rel = rel.Replace('\\', '/');
            if (newExtension != null)
            {
                int dot = rel.LastIndexOf('.');
                if (dot >= 0) rel = rel.Substring(0, dot);
                rel += newExtension;
            }
            return "res://" + rel;
        }
    }
}