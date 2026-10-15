using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GameSpelunker
{
    public static class SpawnScanner
    {
        public const string SpawnsFolder = "GameSpelunker/Editor/Spawns";

        private static readonly string[] HighFirst = { "high", "lod0", "mid", "medium", "lod1", "low", "lod2", "lod3" };
        private static readonly string[] MidFirst = { "mid", "medium", "lod1", "high", "lod0", "low", "lod2", "lod3" };
        private const int ManyInstances = 2000;

        private static readonly Regex Header = new Regex(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex Reference = new Regex(@"^  (\w+): \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}", RegexOptions.Compiled);
        private static readonly Regex Vector = new Regex(@"\{x: ([^,]+), y: ([^,]+), z: ([^,}]+)(?:, w: ([^}]+))?\}", RegexOptions.Compiled);

        private sealed class Node
        {
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public Vector3 Scale = Vector3.one;
            public string Father = "0";
            public bool HasWorld;
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;
            public Vector3 WorldScale;
        }

        private sealed class Spawner
        {
            public string GameObject = "";
            public readonly Dictionary<string, string> Prefabs = new Dictionary<string, string>();
        }

        public static int Scan(string assetsFolder)
        {
            var prefabs = new HashSet<string>(Directory.EnumerateFiles(assetsFolder, "*.prefab.meta", SearchOption.AllDirectories)
                .Select(ReadGuid).Where(g => g != null).Select(g => g!));
            string output = Path.Combine(assetsFolder, SpawnsFolder);
            if (Directory.Exists(output))
                Directory.Delete(output, true);

            int total = 0;
            foreach (var scene in Directory.EnumerateFiles(assetsFolder, "*.unity", SearchOption.AllDirectories))
            {
                string? sceneGuid = ReadGuid(scene + ".meta");
                if (sceneGuid == null)
                    continue;
                var lines = ScanScene(scene, prefabs);
                if (lines.Count == 0)
                    continue;
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, sceneGuid + ".txt"), string.Join("\n", lines) + "\n");
                total += lines.Count;
            }
            return total;
        }

        private static List<string> ScanScene(string scene, HashSet<string> prefabs)
        {
            var transforms = new Dictionary<string, Node>();
            var transformOfObject = new Dictionary<string, string>();
            var spawners = new List<Spawner>();

            string type = "", id = "";
            Node? node = null;
            Spawner? spawner = null;
            foreach (var line in File.ReadLines(scene))
            {
                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    if (spawner != null && spawner.Prefabs.Count > 0) spawners.Add(spawner);
                    node = null;
                    spawner = null;
                    var header = Header.Match(line);
                    type = header.Success ? header.Groups[1].Value : "";
                    id = header.Success ? header.Groups[2].Value : "";
                    if (line.EndsWith(" stripped", StringComparison.Ordinal))
                        type = "";
                    if (type == "4" || type == "224")
                        transforms[id] = node = new Node();
                    else if (type == "114")
                        spawner = new Spawner();
                    continue;
                }
                if (node != null)
                {
                    if (line.StartsWith("  m_LocalPosition: ", StringComparison.Ordinal)) node.Position = ReadVector(line);
                    else if (line.StartsWith("  m_LocalRotation: ", StringComparison.Ordinal)) node.Rotation = ReadQuaternion(line);
                    else if (line.StartsWith("  m_LocalScale: ", StringComparison.Ordinal)) node.Scale = ReadVector(line);
                    else if (line.StartsWith("  m_Father: ", StringComparison.Ordinal)) node.Father = ReadFileId(line);
                    else if (line.StartsWith("  m_GameObject: ", StringComparison.Ordinal)) transformOfObject[ReadFileId(line)] = id;
                }
                else if (spawner != null)
                {
                    if (line.StartsWith("  m_GameObject: ", StringComparison.Ordinal))
                    {
                        spawner.GameObject = ReadFileId(line);
                        continue;
                    }
                    var reference = Reference.Match(line);
                    if (!reference.Success || !reference.Groups[3].Success)
                        continue;
                    string field = reference.Groups[1].Value.ToLowerInvariant();
                    string guid = reference.Groups[3].Value;
                    if (HighFirst.Contains(field) && prefabs.Contains(guid))
                        spawner.Prefabs[field] = guid;
                }
            }
            if (spawner != null && spawner.Prefabs.Count > 0) spawners.Add(spawner);

            var order = spawners.Count > ManyInstances ? MidFirst : HighFirst;
            var result = new List<string>(spawners.Count);
            foreach (var s in spawners)
            {
                string transformId;
                if (!transformOfObject.TryGetValue(s.GameObject, out transformId) || !transforms.ContainsKey(transformId))
                    continue;
                string prefab = order.Where(s.Prefabs.ContainsKey).Select(f => s.Prefabs[f]).First();
                var world = World(transformId, transforms, 0);
                result.Add(string.Join(" ", new[]
                {
                    prefab,
                    F(world.WorldPosition.x), F(world.WorldPosition.y), F(world.WorldPosition.z),
                    F(world.WorldRotation.x), F(world.WorldRotation.y), F(world.WorldRotation.z), F(world.WorldRotation.w),
                    F(world.WorldScale.x), F(world.WorldScale.y), F(world.WorldScale.z),
                }));
            }
            return result;
        }

        private static Node World(string id, Dictionary<string, Node> transforms, int depth)
        {
            var node = transforms[id];
            if (node.HasWorld)
                return node;
            Node? parent;
            if (depth < 256 && node.Father != "0" && transforms.TryGetValue(node.Father, out parent))
            {
                var p = World(node.Father, transforms, depth + 1);
                node.WorldPosition = p.WorldPosition + p.WorldRotation * Vector3.Scale(p.WorldScale, node.Position);
                node.WorldRotation = p.WorldRotation * node.Rotation;
                node.WorldScale = Vector3.Scale(p.WorldScale, node.Scale);
            }
            else
            {
                node.WorldPosition = node.Position;
                node.WorldRotation = node.Rotation;
                node.WorldScale = node.Scale;
            }
            node.HasWorld = true;
            return node;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static float P(string value)
        {
            float result;
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0f;
        }

        private static Vector3 ReadVector(string line)
        {
            var m = Vector.Match(line);
            return m.Success ? new Vector3(P(m.Groups[1].Value), P(m.Groups[2].Value), P(m.Groups[3].Value)) : Vector3.zero;
        }

        private static Quaternion ReadQuaternion(string line)
        {
            var m = Vector.Match(line);
            if (!m.Success || !m.Groups[4].Success)
                return Quaternion.identity;
            var q = new Quaternion(P(m.Groups[1].Value), P(m.Groups[2].Value), P(m.Groups[3].Value), P(m.Groups[4].Value));
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 0.0001f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }

        private static string ReadFileId(string line)
        {
            var m = Regex.Match(line, @"fileID: (-?\d+)");
            return m.Success ? m.Groups[1].Value : "0";
        }

        private static string? ReadGuid(string meta)
        {
            if (!File.Exists(meta))
                return null;
            foreach (var line in File.ReadLines(meta).Take(4))
                if (line.StartsWith("guid: ", StringComparison.Ordinal))
                    return line.Substring(6).Trim();
            return null;
        }
    }
}
