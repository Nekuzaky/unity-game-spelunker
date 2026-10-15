using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameSpelunker
{
    public static class ShaderRestorer
    {
        public const string PlaceholderMark = "DummyShaderTextExporter";

        private static readonly Regex NamePattern = new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Compiled);

        public static int Restore(string assetsFolder, Dictionary<string, string>? builtins = null)
        {
            if (!Directory.Exists(assetsFolder))
                return 0;
            int restored = 0;
            foreach (var file in Directory.EnumerateFiles(assetsFolder, "*.shader", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/" + ExtractedProject.ToolsFolder + "/"))
                    continue;
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }
                if (!text.Contains(PlaceholderMark))
                    continue;
                var name = NamePattern.Match(text);
                if (!name.Success)
                    continue;
                string? replacement = builtins != null && builtins.TryGetValue(name.Groups[1].Value, out var original) ? original : Generate(name.Groups[1].Value);
                if (replacement == null)
                    continue;
                File.WriteAllText(file, replacement);
                restored++;
            }
            foreach (var file in Directory.EnumerateFiles(assetsFolder, "*.asset", SearchOption.AllDirectories).ToList())
            {
                string? name = CompiledShaderName(file);
                if (name == null)
                    continue;
                string? replacement = builtins != null && builtins.TryGetValue(name, out var original) ? original : Generate(name);
                if (replacement == null)
                    continue;
                string meta = file + ".meta";
                var guid = File.Exists(meta) ? Regex.Match(File.ReadAllText(meta), @"^guid: (\w+)", RegexOptions.Multiline) : Match.Empty;
                if (!guid.Success)
                    continue;
                string source = Path.ChangeExtension(file, ".shader");
                if (File.Exists(source))
                    continue;
                File.WriteAllText(source, replacement);
                File.WriteAllText(source + ".meta", "fileFormatVersion: 2\nguid: " + guid.Groups[1].Value + "\nShaderImporter:\n  defaultTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n");
                File.Delete(file);
                File.Delete(meta);
                restored++;
            }
            return restored;
        }

        private static string? CompiledShaderName(string asset)
        {
            try
            {
                using var reader = new StreamReader(asset);
                string? third = null;
                for (int i = 0; i < 3; i++) third = reader.ReadLine();
                if (third == null || !third.StartsWith("--- !u!48 ", StringComparison.Ordinal))
                    return null;
                for (int i = 0; i < 8; i++)
                {
                    string? line = reader.ReadLine();
                    if (line == null) return null;
                    var name = Regex.Match(line, @"^  m_Name: (.+)$");
                    if (name.Success) return name.Groups[1].Value.Trim();
                }
            }
            catch (IOException) { }
            return null;
        }

        public static string? Generate(string name)
        {
            bool ngui = name.StartsWith("Unlit/", StringComparison.Ordinal) || name.StartsWith("Hidden/Unlit/", StringComparison.Ordinal);
            bool text = name.Contains("Text");
            if (name.StartsWith("GUI/", StringComparison.Ordinal) && text)
                return Ui(name, "fixed4(i.color.rgb, tex2D(_MainTex, i.uv).a * i.color.a)", "SrcAlpha OneMinusSrcAlpha", "white");
            if (!ngui)
                return null;
            if (text)
                return Ui(name, "fixed4(i.color.rgb, tex2D(_MainTex, i.uv).a * i.color.a)", "SrcAlpha OneMinusSrcAlpha", "white");
            if (name.Contains("Premultiplied Colored"))
                return Ui(name, "tex2D(_MainTex, i.uv) * i.color", "One OneMinusSrcAlpha", "black");
            if (new[] { "Transparent Colored", "Transparent Masked", "Transparent Packed", "Depth Colored" }.Any(name.Contains))
                return Ui(name, "tex2D(_MainTex, i.uv) * i.color", "SrcAlpha OneMinusSrcAlpha", "black");
            return null;
        }

        private static string Ui(string name, string color, string blend, string defaultTexture) =>
            "Shader \"" + name + "\"\n" +
            "{\n" +
            "\tProperties\n" +
            "\t{\n" +
            "\t\t_MainTex (\"Base (RGB), Alpha (A)\", 2D) = \"" + defaultTexture + "\" {}\n" +
            "\t}\n" +
            "\tSubShader\n" +
            "\t{\n" +
            "\t\tLOD 100\n" +
            "\t\tTags { \"Queue\" = \"Transparent\" \"IgnoreProjector\" = \"True\" \"RenderType\" = \"Transparent\" }\n" +
            "\t\tCull Off\n" +
            "\t\tLighting Off\n" +
            "\t\tZWrite Off\n" +
            "\t\tOffset -1, -1\n" +
            "\t\tBlend " + blend + "\n" +
            "\t\tPass\n" +
            "\t\t{\n" +
            "\t\t\tCGPROGRAM\n" +
            "\t\t\t#pragma vertex vert\n" +
            "\t\t\t#pragma fragment frag\n" +
            "\t\t\t#include \"UnityCG.cginc\"\n" +
            "\t\t\tsampler2D _MainTex;\n" +
            "\t\t\tfloat4 _MainTex_ST;\n" +
            "\t\t\tstruct appdata\n" +
            "\t\t\t{\n" +
            "\t\t\t\tfloat4 vertex : POSITION;\n" +
            "\t\t\t\tfloat2 uv : TEXCOORD0;\n" +
            "\t\t\t\tfixed4 color : COLOR;\n" +
            "\t\t\t};\n" +
            "\t\t\tstruct v2f\n" +
            "\t\t\t{\n" +
            "\t\t\t\tfloat4 vertex : SV_POSITION;\n" +
            "\t\t\t\tfloat2 uv : TEXCOORD0;\n" +
            "\t\t\t\tfixed4 color : COLOR;\n" +
            "\t\t\t};\n" +
            "\t\t\tv2f vert(appdata v)\n" +
            "\t\t\t{\n" +
            "\t\t\t\tv2f o;\n" +
            "\t\t\t\to.vertex = UnityObjectToClipPos(v.vertex);\n" +
            "\t\t\t\to.uv = TRANSFORM_TEX(v.uv, _MainTex);\n" +
            "\t\t\t\to.color = v.color;\n" +
            "\t\t\t\treturn o;\n" +
            "\t\t\t}\n" +
            "\t\t\tfixed4 frag(v2f i) : SV_Target\n" +
            "\t\t\t{\n" +
            "\t\t\t\treturn " + color + ";\n" +
            "\t\t\t}\n" +
            "\t\t\tENDCG\n" +
            "\t\t}\n" +
            "\t}\n" +
            "}\n";
    }
}
