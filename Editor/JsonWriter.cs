using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UnityCoder.GodotExporter
{
    /// <summary>
    /// Minimal dependency-free JSON writer.
    /// JsonUtility can't do dictionaries or polymorphic trees, and Newtonsoft
    /// isn't guaranteed to be in the project. Everything is written with
    /// InvariantCulture so a Finnish (or any comma-decimal) locale can't
    /// corrupt the output.
    /// </summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder _sb = new StringBuilder(1 << 16);
        int _indent;
        bool _needsComma;

        public JsonWriter BeginObject(string name = null) { Sep(name); _sb.Append('{'); _indent++; _needsComma = false; return this; }
        public JsonWriter EndObject() { Close('}'); return this; }
        public JsonWriter BeginArray(string name = null) { Sep(name); _sb.Append('['); _indent++; _needsComma = false; return this; }
        public JsonWriter EndArray() { Close(']'); return this; }

        public JsonWriter Str(string name, string v) { Sep(name); _sb.Append(v == null ? "null" : Quote(v)); return this; }
        public JsonWriter Num(string name, float v) { Sep(name); _sb.Append(F(v)); return this; }
        public JsonWriter Int(string name, int v) { Sep(name); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Bool(string name, bool v) { Sep(name); _sb.Append(v ? "true" : "false"); return this; }
        public JsonWriter Raw(string name, string raw) { Sep(name); _sb.Append(raw); return this; }
        public JsonWriter Null(string name) { Sep(name); _sb.Append("null"); return this; }

        public JsonWriter Vec2(string name, Vector2 v) { return Raw(name, "[" + F(v.x) + ", " + F(v.y) + "]"); }
        public JsonWriter Vec3(string name, Vector3 v) { return Raw(name, "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]"); }
        public JsonWriter Quat(string name, Quaternion q) { return Raw(name, "[" + F(q.x) + ", " + F(q.y) + ", " + F(q.z) + ", " + F(q.w) + "]"); }
        public JsonWriter Col(string name, Color c) { return Raw(name, "[" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ", " + F(c.a) + "]"); }

        public JsonWriter StrArray(string name, IEnumerable<string> items)
        {
            BeginArray(name);
            foreach (var s in items) Str(null, s);
            return EndArray();
        }

        void Sep(string name)
        {
            if (_needsComma) _sb.Append(',');
            if (_sb.Length > 0) _sb.Append('\n').Append(' ', _indent * 2);
            if (name != null) _sb.Append(Quote(name)).Append(": ");
            _needsComma = true;
        }

        void Close(char c)
        {
            _indent--;
            _sb.Append('\n').Append(' ', _indent * 2).Append(c);
            _needsComma = true;
        }

        public static string F(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
            if (Mathf.Abs(v) < 1e-6f) return "0";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public override string ToString() { return _sb.ToString(); }
    }
}
