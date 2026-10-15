#if UNITY_2020_2_OR_NEWER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameSpelunker
{
    public sealed class MaterialRepairReport
    {
        public int Restored;
        public int Rebuilt;
        public int Terrain;
        public int Particles;
        public int Skies;
        public int Skipped;
        public int ShadersRenamed;
        public int GraphicsShadersReset;
        public string LitShader = "";

        public string Summary
        {
            get
            {
                int total = Restored + Rebuilt + Terrain + Particles + Skies;
                if (total == 0 && Skipped == 0)
                    return "No broken material found.";
                return string.Format("{0} material(s) repaired: {1} back on their original Unity shader, {2} rebuilt on {3}, " +
                                     "{4} terrain, {5} particle, {6} sky. {7} left as they are (effects driven by code, UI, text).",
                    total, Restored, Rebuilt, LitShader, Terrain, Particles, Skies, Skipped) +
                       (GraphicsShadersReset > 0 ? string.Format(" {0} rendering shader(s) of the graphics settings set back to Unity's own.", GraphicsShadersReset) : "");
            }
        }
    }

    public static class MaterialRepair
    {
        private const string DummyMarker = "DummyShaderTextExporter";
        private const string RenamedPrefix = "Hidden/Game Spelunker/Replaced/";

        private static readonly Regex ShaderNamePattern = new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Multiline);
        private static readonly Regex ShaderRefPattern = new Regex("m_Shader:\\s*\\{fileID:\\s*-?\\d+,\\s*guid:\\s*([0-9a-f]{32})");

        private const string NotColor = "normal|bump|nrm|spec|gloss|metal|rough|detail|mask|noise|flow|foam|emiss|illum|occlu|height|parallax|splat|control|cube|refl|lightmap|ramp|lut|wind|dissolve|trans";
        private const string SolidPattern = "bark|trunk";

        private static readonly string[] TextureSlots =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_BumpMap", "_NormalMap", "_OcclusionMap", "_EmissionMap", "_EmissiveColorMap",
            "_SpecGlossMap", "_MetallicGlossMap", "_MaskMap", "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_DetailMap",
            "_ParallaxMap", "_HeightMap",
        };

        private static readonly Dictionary<string, string> declarations = new Dictionary<string, string>();

        private static TexEnv Role(string shader, TexEnv env, string role)
        {
            string text;
            if (env == null || !declarations.TryGetValue(shader, out text))
                return env;
            var declared = Regex.Match(text, Regex.Escape(env.Name) + "\\s*\\(\\s*\"([^\"]*)\"");
            if (!declared.Success)
                return env;
            string description = declared.Groups[1].Value;
            if (!Regex.IsMatch(description, role, RegexOptions.IgnoreCase))
                return null;
            return Regex.IsMatch(description, "mask|noise", RegexOptions.IgnoreCase) && !Regex.IsMatch(role, "mask") ? null : env;
        }

        private static bool TintIsNotAColor(string shader)
        {
            string text;
            return declarations.TryGetValue(shader, out text)
                   && Regex.IsMatch(text, "_Color\\s*\\(\\s*\"[^\"]*(variation|variance|random)", RegexOptions.IgnoreCase);
        }

        private const string FoliagePattern ="cutout|alphatest|alpha test|foliage|leaf|leaves|grass|fern|bush|plant|billboard|branch|ivy|vine|hair|fence|stipple|tree";
        private const string TransparentPattern = "transparent|glass|alpha blended|fade|window";
        private const string WaterPattern = "(^|/)\\s*(water|ocean|lake|river)";

        private enum Pipeline { BuiltIn, Universal, HighDefinition }

        private sealed class TexEnv
        {
            public string Name = "";
            public Texture Texture;
            public Vector2 Scale = Vector2.one;
            public Vector2 Offset = Vector2.zero;
        }

        private sealed class Memo
        {
            private const string Prefix = "GameSpelunker|";
            private static readonly string[] Overwritten = { "_Glossiness", "_Smoothness" };

            public string Shader = "";
            public string[] Keywords = new string[0];
            public int Queue = -1;
            public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();

            public static Memo From(string shader, Saved saved)
            {
                var memo = new Memo { Shader = shader, Keywords = saved.Keywords.ToArray(), Queue = saved.CustomQueue };
                foreach (string name in Overwritten)
                    if (saved.Floats.ContainsKey(name))
                        memo.Floats[name] = saved.Floats[name];
                return memo;
            }

            public static Memo Parse(string text)
            {
                if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.Ordinal))
                    return null;
                var memo = new Memo();
                foreach (string part in text.Substring(Prefix.Length).Split('|'))
                {
                    int eq = part.IndexOf('=');
                    if (eq < 0) continue;
                    string key = part.Substring(0, eq), value = Uri.UnescapeDataString(part.Substring(eq + 1));
                    if (key == "shader") memo.Shader = value;
                    else if (key == "keywords") memo.Keywords = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    else if (key == "queue") int.TryParse(value, out memo.Queue);
                    else if (key.StartsWith("f:", StringComparison.Ordinal))
                    {
                        float f;
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f))
                            memo.Floats[key.Substring(2)] = f;
                    }
                }
                return memo;
            }

            public void ApplyTo(Saved saved)
            {
                saved.Keywords.Clear();
                foreach (string keyword in Keywords) saved.Keywords.Add(keyword);
                saved.CustomQueue = Queue;
                foreach (string name in Overwritten)
                {
                    saved.Floats.Remove(name);
                    if (Floats.ContainsKey(name)) saved.Floats[name] = Floats[name];
                }
            }

            public override string ToString()
            {
                var parts = new List<string>
                {
                    "shader=" + Uri.EscapeDataString(Shader),
                    "keywords=" + Uri.EscapeDataString(string.Join(" ", Keywords)),
                    "queue=" + Queue,
                };
                foreach (var pair in Floats)
                    parts.Add("f:" + pair.Key + "=" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Prefix + string.Join("|", parts.ToArray());
            }
        }

        private sealed class Saved
        {
            public readonly Dictionary<string, TexEnv> Textures = new Dictionary<string, TexEnv>();
            public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
            public readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
            public readonly HashSet<string> Keywords = new HashSet<string>();
            public int CustomQueue = -1;
        }

        [MenuItem("Assets/Game Spelunker/Repair Materials", false, 2000)]
        private static void RepairSelectedFolder()
        {
            string folder = SelectedFolder() ?? "Assets";
            var report = Run(folder, null);
            Debug.Log("[Game Spelunker] " + folder + ": " + report.Summary);
            EditorUtility.DisplayDialog("Game Spelunker", report.Summary, "OK");
        }

        public static void RunBatch()
        {
            var report = Run("Assets", null);
            Debug.Log("[Game Spelunker] " + report.Summary + " Placeholders renamed: " + report.ShadersRenamed +
                      ". Graphics settings shaders reset to built-in: " + report.GraphicsShadersReset + ".");
            EditorApplication.Exit(0);
        }

        private static string SelectedFolder()
        {
            foreach (var o in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (AssetDatabase.IsValidFolder(path))
                    return path;
            }
            return null;
        }

        public static MaterialRepairReport Run(string folder, IDictionary<string, string> shaderNamesByGuid)
        {
            var report = new MaterialRepairReport();
            var pipeline = DetectPipeline();
            var real = RealShaders();

            var placeholders = new Dictionary<string, string>();
            declarations.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string text = ReadText(path);
                if (text == null || text.IndexOf(DummyMarker, StringComparison.Ordinal) < 0)
                    continue;
                var name = ShaderNamePattern.Match(text);
                if (!name.Success)
                    continue;
                placeholders[guid] = name.Groups[1].Value;
                declarations[name.Groups[1].Value] = text;
                if (name.Groups[1].Value.StartsWith(RenamedPrefix, StringComparison.Ordinal))
                    declarations[name.Groups[1].Value.Substring(RenamedPrefix.Length)] = text;
            }

            Shader lit = LitShader(pipeline, false, real);
            report.LitShader = lit != null ? lit.name : "?";

            string[] materials = AssetDatabase.FindAssets("t:Material", new[] { folder });
            try
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(materials[i]);
                    if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (i % 20 == 0 && EditorUtility.DisplayCancelableProgressBar("Game Spelunker", "Repairing materials… " + Path.GetFileName(path), i / (float)materials.Length))
                        break;

                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                        continue;

                    var importer = AssetImporter.GetAtPath(path);
                    var memo = Memo.Parse(importer != null ? importer.userData : null);
                    string original;
                    string shaderGuid = ShaderGuid(path);
                    if (memo != null)
                    {
                        original = memo.Shader;
                    }
                    else if (shaderGuid != null && placeholders.TryGetValue(shaderGuid, out original))
                    {
                    }
                    else if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                    {
                        if (shaderGuid == null || shaderNamesByGuid == null || !shaderNamesByGuid.TryGetValue(shaderGuid, out original))
                            original = "";
                    }
                    else
                    {
                        int queue = material.renderQueue;
                        MatchQueueToBlendMode(material);
                        if (material.renderQueue != queue)
                            EditorUtility.SetDirty(material);
                        continue;
                    }

                    var saved = Read(material);
                    if (memo != null)
                    {
                        memo.ApplyTo(saved);
                    }
                    else if (importer != null)
                    {
                        importer.userData = Memo.From(original, saved).ToString();
                        AssetDatabase.WriteImportSettingsIfDirty(path);
                    }

                    Repair(material, original, saved, pipeline, real, report);
                    EditorUtility.SetDirty(material);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (var pair in placeholders)
            {
                if (!real.ContainsKey(pair.Value))
                    continue;
                string path = AssetDatabase.GUIDToAssetPath(pair.Key);
                string text = ReadText(path);
                if (text == null)
                    continue;
                File.WriteAllText(path, ShaderNamePattern.Replace(text, "Shader \"" + RenamedPrefix + pair.Value + "\"", 1));
                AssetDatabase.ImportAsset(path);
                report.ShadersRenamed++;
            }

            report.GraphicsShadersReset = ResetGraphicsShaders(placeholders);
            AssetDatabase.SaveAssets();
            return report;
        }

        private static int ResetGraphicsShaders(Dictionary<string, string> placeholders)
        {
            int reset = 0;
            var types = new[]
            {
                BuiltinShaderType.DeferredShading, BuiltinShaderType.DeferredReflections, BuiltinShaderType.ScreenSpaceShadows,
                BuiltinShaderType.DepthNormals, BuiltinShaderType.MotionVectors, BuiltinShaderType.LightHalo, BuiltinShaderType.LensFlare,
            };
            foreach (var type in types)
            {
                if (GraphicsSettings.GetShaderMode(type) != BuiltinShaderMode.UseCustom)
                    continue;
                var custom = GraphicsSettings.GetCustomShader(type);
                string guid = custom != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(custom)) : "";
                if (custom != null && !placeholders.ContainsKey(guid))
                    continue;
                GraphicsSettings.SetShaderMode(type, BuiltinShaderMode.UseBuiltin);
                reset++;
            }
            return reset;
        }

        private static void Repair(Material material, string original, Saved saved, Pipeline pipeline, Dictionary<string, Shader> real, MaterialRepairReport report)
        {
            Shader same;
            if (original.Length > 0 && real.TryGetValue(original, out same))
            {
                material.shader = same;
                MatchQueueToBlendMode(material);
                report.Restored++;
                return;
            }

            string name = (original + " " + material.name).ToLowerInvariant();
            string shader = original.ToLowerInvariant();

            if (shader.Contains("skybox") && pipeline != Pipeline.HighDefinition)
            {
                var sky = Find(real, new[] { "Skybox/Procedural" });
                if (sky == null) { report.Skipped++; return; }
                material.shader = sky;
                report.Skies++;
                return;
            }

            if (IsCodeDriven(shader))
            {
                report.Skipped++;
                return;
            }

            if (shader.Contains("terrain") && !Regex.IsMatch(shader, "detail|grass|tree|billboard|waving"))
            {
                var terrain = Find(real, pipeline == Pipeline.Universal ? new[] { "Universal Render Pipeline/Terrain/Lit" }
                    : pipeline == Pipeline.HighDefinition ? new[] { "HDRP/TerrainLit" }
                    : new[] { "Nature/Terrain/Standard", "Nature/Terrain/Diffuse" });
                if (terrain == null) { report.Skipped++; return; }
                material.shader = terrain;
                report.Terrain++;
                return;
            }

            if (shader.Contains("particle") || (original.Length == 0 && name.Contains("particle")))
            {
                if (RepairParticle(material, name.Contains("add"), pipeline, saved, real))
                    report.Particles++;
                else
                    report.Skipped++;
                return;
            }

            var specGloss = Role(original, Pick(saved, new[] { "_SpecGlossMap" }, "", ""), "spec");
            bool specular = specGloss != null || saved.Colors.ContainsKey("_SpecColor")
                            || (saved.Keywords.Contains("_SPECGLOSSMAP") && !HasTexture(saved, "_SpecGlossMap"));
            var target = LitShader(pipeline, specular, real);
            if (target == null) { report.Skipped++; return; }

            bool water = Regex.IsMatch(shader, WaterPattern);
            var albedo = water ? null : PickAlbedo(saved);
            bool alpha = albedo != null && HasAlpha(albedo.Texture);
            bool cutout = !water && (saved.Keywords.Contains("_ALPHATEST_ON") || (saved.CustomQueue >= 2450 && saved.CustomQueue < 3000)
                                     || (alpha && Regex.IsMatch(name, FoliagePattern) && !Regex.IsMatch(name, SolidPattern)));
            bool transparent = water || (!cutout && (saved.Keywords.Contains("_ALPHABLEND_ON") || saved.Keywords.Contains("_ALPHAPREMULTIPLY_ON")
                                                     || saved.CustomQueue >= 3000 || (alpha && Regex.IsMatch(name, TransparentPattern))));
            float cutoff = saved.Floats.ContainsKey("_Cutoff") ? saved.Floats["_Cutoff"] : 0.5f;

            var normal = Role(original, Pick(saved, new[] { "_BumpMap", "_NormalMap", "_Normal", "_NormalTex" }, "bump|normal|nrm", "detail|2"), "normal|bump");
            if (normal != null && !IsNormalMap(normal.Texture)) normal = null;
            var occlusion = Role(original, Pick(saved, new[] { "_OcclusionMap" }, "occlusion|_ao$", "detail"), "occlusion|\\bao\\b");
            var emission = Role(original, Pick(saved, new[] { "_EmissionMap" }, "emiss|illum|glow", ""), "emiss|illum|glow");
            var metallic = Role(original, Pick(saved, new[] { "_MetallicGlossMap" }, "", ""), "metal");
            Color color = TintIsNotAColor(original) ? Color.white
                : saved.Colors.ContainsKey("_Color") ? saved.Colors["_Color"] : saved.Colors.ContainsKey("_BaseColor") ? saved.Colors["_BaseColor"] : Color.white;
            if (!transparent)
                color.a = 1f;
            Color emissionColor = saved.Colors.ContainsKey("_EmissionColor") ? saved.Colors["_EmissionColor"] : Color.white;
            if (water)
                color = new Color(0.16f, 0.27f, 0.30f, 0.75f);

            material.shader = target;
            material.shaderKeywords = new string[0];
            foreach (string slot in TextureSlots)
                if (material.HasProperty(slot))
                    material.SetTexture(slot, null);

            if (pipeline == Pipeline.Universal)
            {
                SetTexture(material, "_BaseMap", albedo);
                SetTexture(material, "_MainTex", albedo);
                material.SetColor("_BaseColor", color);
                if (normal != null) { SetTexture(material, "_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
                if (occlusion != null) { SetTexture(material, "_OcclusionMap", occlusion); material.EnableKeyword("_OCCLUSIONMAP"); }
                if (metallic != null) { SetTexture(material, "_MetallicGlossMap", metallic); material.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (emission != null) { SetTexture(material, "_EmissionMap", emission); material.SetColor("_EmissionColor", emissionColor); material.EnableKeyword("_EMISSION"); }
                if (water) material.SetFloat("_Smoothness", 0.95f);
                else if (!saved.Floats.ContainsKey("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
                if (cutout)
                {
                    material.SetFloat("_AlphaClip", 1f);
                    material.SetFloat("_Cutoff", cutoff);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.SetFloat("_Cull", 0f);
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else if (transparent)
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    material.SetOverrideTag("RenderType", "");
                    material.renderQueue = -1;
                }
            }
            else if (pipeline == Pipeline.HighDefinition)
            {
                SetTexture(material, "_BaseColorMap", albedo);
                material.SetColor("_BaseColor", color);
                if (normal != null) { SetTexture(material, "_NormalMap", normal); material.EnableKeyword("_NORMALMAP"); }
                if (emission != null) { SetTexture(material, "_EmissiveColorMap", emission); material.SetColor("_EmissiveColor", emissionColor); material.EnableKeyword("_EMISSIVE_COLOR_MAP"); }
                if (cutout)
                {
                    material.SetFloat("_AlphaCutoffEnable", 1f);
                    material.SetFloat("_AlphaCutoff", cutoff);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else
                {
                    material.renderQueue = -1;
                }
            }
            else
            {
                SetTexture(material, "_MainTex", albedo);
                material.SetColor("_Color", color);
                if (normal != null) { SetTexture(material, "_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
                if (occlusion != null) SetTexture(material, "_OcclusionMap", occlusion);
                if (specular && specGloss != null) { SetTexture(material, "_SpecGlossMap", specGloss); material.EnableKeyword("_SPECGLOSSMAP"); }
                if (!specular && metallic != null) { SetTexture(material, "_MetallicGlossMap", metallic); material.EnableKeyword("_METALLICGLOSSMAP"); }
                if (emission != null) { SetTexture(material, "_EmissionMap", emission); material.SetColor("_EmissionColor", emissionColor); material.EnableKeyword("_EMISSION"); }
                if (water) material.SetFloat("_Glossiness", 0.95f);
                else if (!saved.Floats.ContainsKey("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);
                if (cutout)
                {
                    material.SetFloat("_Mode", 1f);
                    material.SetFloat("_Cutoff", cutoff);
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                else if (transparent)
                {
                    material.SetFloat("_Mode", 2f);
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    material.SetFloat("_Mode", 0f);
                    material.SetOverrideTag("RenderType", "");
                    material.renderQueue = -1;
                }
            }
            MatchQueueToBlendMode(material);
            report.Rebuilt++;
        }

        private static void MatchQueueToBlendMode(Material material)
        {
            if (!material.shader.name.StartsWith("Standard", StringComparison.Ordinal) || !material.HasProperty("_Mode"))
                return;
            int mode = Mathf.RoundToInt(material.GetFloat("_Mode"));
            int queue = material.renderQueue;
            if (mode == 1 && (queue < 2450 || queue > 2500))
                material.renderQueue = (int)RenderQueue.AlphaTest;
            else if (mode >= 2 && (queue < 2501 || queue > 3999))
                material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static bool RepairParticle(Material material, bool additive, Pipeline pipeline, Saved saved, Dictionary<string, Shader> real)
        {
            var texture = PickAlbedo(saved);
            Color tint = saved.Colors.ContainsKey("_TintColor") ? saved.Colors["_TintColor"] : saved.Colors.ContainsKey("_Color") ? saved.Colors["_Color"] : Color.white;
            if (pipeline == Pipeline.Universal)
            {
                var shader = Find(real, new[] { "Universal Render Pipeline/Particles/Unlit" });
                if (shader == null) return false;
                material.shader = shader;
                material.shaderKeywords = new string[0];
                SetTexture(material, "_BaseMap", texture);
                material.SetColor("_BaseColor", tint);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", additive ? 2f : 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                return true;
            }
            if (pipeline == Pipeline.BuiltIn)
            {
                var shader = additive ? Find(real, new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive" })
                                      : Find(real, new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended" });
                if (shader == null) return false;
                material.shader = shader;
                SetTexture(material, "_MainTex", texture);
                material.SetColor("_TintColor", tint);
                return true;
            }
            return false;
        }

        private static bool IsCodeDriven(string shader) =>
            Regex.IsMatch(shader, "^hidden/|postfx|post effect|posteffect|imageeffect|image effect|^ui/|^gui/|sprite|font|textmeshpro|blit|underwater|mask|depth");

        private static Shader LitShader(Pipeline pipeline, bool specular, Dictionary<string, Shader> real)
        {
            if (pipeline == Pipeline.Universal) return Find(real, new[] { "Universal Render Pipeline/Lit", "Lightweight Render Pipeline/Lit" });
            if (pipeline == Pipeline.HighDefinition) return Find(real, new[] { "HDRP/Lit" });
            return specular ? Find(real, new[] { "Standard (Specular setup)", "Standard" }) : Find(real, new[] { "Standard" });
        }

        private static Pipeline DetectPipeline()
        {
#if UNITY_2019_3_OR_NEWER
            var asset = GraphicsSettings.currentRenderPipeline;
#elif UNITY_2018_1_OR_NEWER
            var asset = GraphicsSettings.renderPipelineAsset;
#else
            UnityEngine.Object asset = null;
#endif
            if (asset == null) return Pipeline.BuiltIn;
            string type = asset.GetType().FullName ?? "";
            if (type.Contains("HighDefinition")) return Pipeline.HighDefinition;
            if (type.Contains("Universal") || type.Contains("Lightweight")) return Pipeline.Universal;
            return Pipeline.BuiltIn;
        }

        private static Dictionary<string, Shader> RealShaders()
        {
            var result = new Dictionary<string, Shader>();
            foreach (string builtIn in new[] { "Resources/unity_builtin_extra", "Library/unity default resources" })
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(builtIn))
                {
                    var shader = asset as Shader;
                    if (shader != null && !result.ContainsKey(shader.name))
                        result[shader.name] = shader;
                }
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Packages/", StringComparison.Ordinal))
                    continue;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && !result.ContainsKey(shader.name))
                    result[shader.name] = shader;
            }
            return result;
        }

        private static Shader Find(Dictionary<string, Shader> real, string[] names)
        {
            foreach (string name in names)
            {
                Shader shader;
                if (real.TryGetValue(name, out shader))
                    return shader;
                shader = Shader.Find(name);
                if (shader != null && !AssetDatabase.GetAssetPath(shader).StartsWith("Assets/", StringComparison.Ordinal))
                    return shader;
            }
            return null;
        }

        private static Saved Read(Material material)
        {
            var saved = new Saved();
            var so = new SerializedObject(material);

            var textures = so.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; textures != null && i < textures.arraySize; i++)
            {
                var entry = textures.GetArrayElementAtIndex(i);
                string name = EntryName(entry);
                var value = entry.FindPropertyRelative("second");
                if (name == null || value == null) continue;
                var texture = value.FindPropertyRelative("m_Texture");
                var scale = value.FindPropertyRelative("m_Scale");
                var offset = value.FindPropertyRelative("m_Offset");
                saved.Textures[name] = new TexEnv
                {
                    Name = name,
                    Texture = texture != null ? texture.objectReferenceValue as Texture : null,
                    Scale = scale != null ? scale.vector2Value : Vector2.one,
                    Offset = offset != null ? offset.vector2Value : Vector2.zero,
                };
            }

            var floats = so.FindProperty("m_SavedProperties.m_Floats");
            for (int i = 0; floats != null && i < floats.arraySize; i++)
            {
                var entry = floats.GetArrayElementAtIndex(i);
                string name = EntryName(entry);
                var value = entry.FindPropertyRelative("second");
                if (name != null && value != null) saved.Floats[name] = value.floatValue;
            }

            var colors = so.FindProperty("m_SavedProperties.m_Colors");
            for (int i = 0; colors != null && i < colors.arraySize; i++)
            {
                var entry = colors.GetArrayElementAtIndex(i);
                string name = EntryName(entry);
                var value = entry.FindPropertyRelative("second");
                if (name != null && value != null) saved.Colors[name] = value.colorValue;
            }

            foreach (string keyword in material.shaderKeywords)
                saved.Keywords.Add(keyword);
            foreach (string field in new[] { "m_InvalidKeywords", "m_ValidKeywords" })
            {
                var list = so.FindProperty(field);
                for (int i = 0; list != null && list.isArray && i < list.arraySize; i++)
                    saved.Keywords.Add(list.GetArrayElementAtIndex(i).stringValue);
            }
            var legacy = so.FindProperty("m_ShaderKeywords");
            if (legacy != null && legacy.propertyType == SerializedPropertyType.String)
                foreach (string keyword in legacy.stringValue.Split(' '))
                    if (keyword.Length > 0) saved.Keywords.Add(keyword);

            var queue = so.FindProperty("m_CustomRenderQueue");
            if (queue != null) saved.CustomQueue = queue.intValue;
            return saved;
        }

        private static string EntryName(SerializedProperty entry)
        {
            var first = entry.FindPropertyRelative("first");
            if (first == null) return null;
            if (first.propertyType == SerializedPropertyType.String) return first.stringValue;
            var name = first.FindPropertyRelative("name");
            return name != null ? name.stringValue : null;
        }

        private static TexEnv PickAlbedo(Saved saved)
        {
            var exact = Pick(saved, new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_AlbedoMap", "_Diffuse", "_DiffuseMap", "_MainTexture", "_Tex" },
                "albedo|diffuse|main|base|color|^_tex", NotColor);
            if (exact != null) return exact;
            return saved.Textures.Where(t => t.Value.Texture != null && !Regex.IsMatch(t.Key.ToLowerInvariant(), NotColor))
                .Select(t => t.Value).FirstOrDefault();
        }

        private static TexEnv Pick(Saved saved, string[] exact, string pattern, string exclude)
        {
            foreach (string name in exact)
            {
                TexEnv env;
                if (saved.Textures.TryGetValue(name, out env) && env.Texture != null)
                    return env;
            }
            if (pattern.Length == 0)
                return null;
            foreach (var pair in saved.Textures)
            {
                string name = pair.Key.ToLowerInvariant();
                if (pair.Value.Texture != null && Regex.IsMatch(name, pattern) && (exclude.Length == 0 || !Regex.IsMatch(name, exclude)))
                    return pair.Value;
            }
            return null;
        }

        private static bool HasTexture(Saved saved, string name)
        {
            TexEnv env;
            return saved.Textures.TryGetValue(name, out env) && env.Texture != null;
        }

        private static bool HasAlpha(Texture texture)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            return importer != null && importer.DoesSourceTextureHaveAlpha();
        }

        private static bool IsNormalMap(Texture texture)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            return importer != null && importer.textureType == TextureImporterType.NormalMap;
        }

        private static void SetTexture(Material material, string property, TexEnv env)
        {
            if (env == null || !material.HasProperty(property))
                return;
            material.SetTexture(property, env.Texture);
            material.SetTextureScale(property, env.Scale);
            material.SetTextureOffset(property, env.Offset);
        }

        private static string ShaderGuid(string materialPath)
        {
            string text = ReadText(materialPath);
            if (text == null) return null;
            var match = ShaderRefPattern.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ReadText(string path)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
