using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityCoder.GodotExporter
{
    /// <summary>
    /// Maps Built-in Standard and URP/Lit onto Godot's StandardMaterial3D.
    /// Anything else is passed through with its properties dumped and flagged
    /// for manual work — a custom shader cannot be converted mechanically.
    /// </summary>
    public static class MaterialExporter
    {
        public static void Write(Material m, JsonWriter w, ExportContext ctx)
        {
            string target = ctx.MaterialTarget(m);
            string shaderName = m.shader != null ? m.shader.name : "(none)";

            w.BeginObject();
            w.Str("target", target);
            w.Str("name", m.name);
            w.Str("source", AssetDatabase.GetAssetPath(m));
            w.Str("sourceShader", shaderName);
            w.Str("godotType", "StandardMaterial3D");

            bool known = IsKnownShader(shaderName);
            if (!known)
            {
                ctx.Report(Severity.Manual, target, "Shader",
                    "Material '" + m.name + "' uses shader '" + shaderName +
                    "'. Only Standard / URP-Lit / Unlit are mapped; port this shader by hand.");
            }

            w.BeginObject("props");

            // --- albedo
            Color albedo = GetColor(m, "_BaseColor", "_Color");
            w.Col("albedo_color", albedo);
            ctx.WriteTextureRef(w, "albedo_texture", GetTex(m, "_BaseMap", "_MainTex"));

            // --- metallic / roughness
            w.Num("metallic", GetFloat(m, 0f, "_Metallic"));
            float smoothness = GetFloat(m, 0.5f, "_Smoothness", "_Glossiness");
            w.Num("roughness", Mathf.Clamp01(1f - smoothness));
            Texture metalMap = GetTex(m, "_MetallicGlossMap", "_MetallicSpecGlossMap");
            ctx.WriteTextureRef(w, "metallic_texture", metalMap);
            if (metalMap != null)
            {
                w.Int("metallic_texture_channel", 0);   // R
                w.Int("roughness_texture_channel", 3);  // A (Unity packs smoothness in alpha)
                w.Bool("roughness_invert_hint", true);
                ctx.ReportOnce("metal-gloss", Severity.Warning, target, "Material",
                    "Unity packs smoothness into the metallic map's alpha and Godot expects roughness. " +
                    "The importer must invert that channel or you get shiny/rough swapped.");
            }

            // --- normal
            Texture normal = GetTex(m, "_BumpMap", "_NormalMap");
            if (normal != null)
            {
                w.Bool("normal_enabled", true);
                ctx.WriteTextureRef(w, "normal_texture", normal);
                w.Num("normal_scale", GetFloat(m, 1f, "_BumpScale"));
            }

            // --- occlusion
            Texture ao = GetTex(m, "_OcclusionMap");
            if (ao != null)
            {
                w.Bool("ao_enabled", true);
                ctx.WriteTextureRef(w, "ao_texture", ao);
                w.Num("ao_light_affect", GetFloat(m, 1f, "_OcclusionStrength"));
            }

            // --- emission
            Color emission = GetColor(m, "_EmissionColor");
            bool emissive = m.IsKeywordEnabled("_EMISSION") && emission.maxColorComponent > 0.001f;
            w.Bool("emission_enabled", emissive);
            if (emissive)
            {
                float energy = Mathf.Max(1f, emission.maxColorComponent);
                w.Col("emission", emission / energy);
                w.Num("emission_energy_multiplier", energy);
                ctx.WriteTextureRef(w, "emission_texture", GetTex(m, "_EmissionMap"));
            }

            // --- transparency
            string renderType = m.GetTag("RenderType", false, "Opaque");
            if (renderType == "TransparentCutout")
            {
                w.Str("transparency", "ALPHA_SCISSOR");
                w.Num("alpha_scissor_threshold", GetFloat(m, 0.5f, "_Cutoff"));
            }
            else if (renderType == "Transparent")
            {
                w.Str("transparency", "ALPHA");
            }
            else
            {
                w.Str("transparency", "DISABLED");
            }

            // --- culling: Unity _Cull 0=Off 1=Front 2=Back / Godot 0=Back 1=Front 2=Disabled
            int unityCull = Mathf.RoundToInt(GetFloat(m, 2f, "_Cull"));
            w.Int("cull_mode", unityCull == 0 ? 2 : unityCull == 1 ? 1 : 0);

            // --- uv transform
            Vector2 tiling = GetTexScale(m, Vector2.one);
            Vector2 offset = GetTexOffset(m, Vector2.zero);
            w.Vec3("uv1_scale", new Vector3(tiling.x, tiling.y, 1f));
            w.Vec3("uv1_offset", new Vector3(offset.x, offset.y, 0f));

            w.Int("unityRenderQueue", m.renderQueue);
            w.EndObject(); // props

            // Keep the raw values around so the wizard / LLM stage has something
            // to work with for shaders we did not understand.
            if (!known) WriteRawProperties(m, w);

            w.EndObject();
        }

        static void WriteRawProperties(Material m, JsonWriter w)
        {
            w.BeginArray("rawProperties");
            var shader = m.shader;
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string pname = shader.GetPropertyName(i);
                    ShaderPropertyType ptype = shader.GetPropertyType(i);
                    w.BeginObject();
                    w.Str("name", pname);
                    w.Str("type", ptype.ToString());
                    switch (ptype)
                    {
                        case ShaderPropertyType.Color:
                            w.Col("value", m.GetColor(pname));
                            break;
                        case ShaderPropertyType.Vector:
                            Vector4 v = m.GetVector(pname);
                            w.Raw("value", "[" + JsonWriter.F(v.x) + ", " + JsonWriter.F(v.y) + ", " + JsonWriter.F(v.z) + ", " + JsonWriter.F(v.w) + "]");
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            w.Num("value", m.GetFloat(pname));
                            break;
                        case ShaderPropertyType.Texture:
                            var t = m.GetTexture(pname);
                            w.Str("value", t != null ? AssetDatabase.GetAssetPath(t) : null);
                            break;
                        default:
                            w.Null("value");
                            break;
                    }
                    w.EndObject();
                }
            }
            w.EndArray();
        }

        static bool IsKnownShader(string n)
        {
            return n == "Standard" || n == "Standard (Specular setup)"
                || n.StartsWith("Universal Render Pipeline/Lit")
                || n.StartsWith("Universal Render Pipeline/Simple Lit")
                || n.StartsWith("Universal Render Pipeline/Unlit")
                || n.StartsWith("Unlit/");
        }

        static Color GetColor(Material m, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) return m.GetColor(n);
            return Color.white;
        }

        static float GetFloat(Material m, float def, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) return m.GetFloat(n);
            return def;
        }

        static Texture GetTex(Material m, params string[] names)
        {
            foreach (var n in names) if (m.HasProperty(n)) { var t = m.GetTexture(n); if (t != null) return t; }
            return null;
        }

        static Vector2 GetTexScale(Material m, Vector2 def)
        {
            if (m.HasProperty("_BaseMap")) return m.GetTextureScale("_BaseMap");
            if (m.HasProperty("_MainTex")) return m.GetTextureScale("_MainTex");
            return def;
        }

        static Vector2 GetTexOffset(Material m, Vector2 def)
        {
            if (m.HasProperty("_BaseMap")) return m.GetTextureOffset("_BaseMap");
            if (m.HasProperty("_MainTex")) return m.GetTextureOffset("_MainTex");
            return def;
        }
    }
}
