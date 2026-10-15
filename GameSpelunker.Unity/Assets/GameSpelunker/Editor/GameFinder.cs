using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameSpelunker
{
    /// <summary>A Unity game found on this computer.</summary>
    public sealed class FoundGame
    {
        public string Name = "";
        public string Folder = "";
        public string Source = "";
    }

    /// <summary>Lists Unity games installed through Steam (every library folder) and other common launchers.</summary>
    public static class GameFinder
    {
        public static List<FoundGame> FindInstalledUnityGames()
        {
            var games = new List<FoundGame>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var common in SteamCommonFolders())
                AddUnityGames(games, seen, common, "Steam");

            foreach (var (folder, label) in OtherLauncherFolders())
                AddUnityGames(games, seen, folder, label);

            return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddUnityGames(List<FoundGame> games, HashSet<string> seen, string root, string source)
        {
            if (!Directory.Exists(root))
                return;
            foreach (var folder in SafeDirectories(root))
            {
                if (!seen.Add(folder) || BuildInspector.FindDataFolder(folder) == null)
                    continue;
                games.Add(new FoundGame { Name = Path.GetFileName(folder), Folder = folder, Source = source });
            }
        }

        private static IEnumerable<string> SafeDirectories(string root)
        {
            try { return Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        /// <summary>steamapps/common of every Steam library, read from libraryfolders.vdf.</summary>
        private static IEnumerable<string> SteamCommonFolders()
        {
            // Unity's .NET profile has no registry API: look where Steam usually lives on every fixed drive.
            var steamRoots = new List<string>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                string root = drive.RootDirectory.FullName;
                steamRoots.Add(Path.Combine(root, "Program Files (x86)", "Steam"));
                steamRoots.Add(Path.Combine(root, "Program Files", "Steam"));
                steamRoots.Add(Path.Combine(root, "Steam"));
                steamRoots.Add(Path.Combine(root, "SteamLibrary"));
            }

            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var steam in steamRoots.Where(Directory.Exists))
            {
                libraries.Add(steam);
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }

            return libraries.Select(l => Path.Combine(l, "steamapps", "common"));
        }

        private static IEnumerable<(string, string)> OtherLauncherFolders()
        {
            yield return (@"C:\Program Files\Epic Games", "Epic Games");
            yield return (@"C:\Program Files (x86)\GOG Galaxy\Games", "GOG");
            yield return (@"C:\GOG Games", "GOG");
            string itch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "apps");
            yield return (itch, "itch.io");
        }
    }
}
