using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace CollabCharting
{
    internal static class ResourceSync
    {
        public static string CacheRoot =>
            Path.Combine(Main.Settings.GetWorkspacePath(), "Cache");

        public static ResourceManifest BuildManifest(string levelPath)
        {
            if (string.IsNullOrWhiteSpace(levelPath) || !File.Exists(levelPath))
            {
                throw new FileNotFoundException("当前编辑器没有打开可同步的 .adofai 关卡。", levelPath);
            }

            string root = Path.GetDirectoryName(Path.GetFullPath(levelPath)) ?? string.Empty;
            var manifest = new ResourceManifest
            {
                RootName = Path.GetFileName(root),
                LevelRelativePath = NormalizeRelativePath(GetRelativePath(root, levelPath))
            };

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = NormalizeRelativePath(GetRelativePath(root, file));
                if (!IsSafeRelativePath(relative))
                {
                    continue;
                }

                FileInfo info = new FileInfo(file);
                manifest.Files.Add(new ResourceManifestEntry
                {
                    RelativePath = relative,
                    Size = info.Length,
                    Sha256 = ComputeSha256(file)
                });
            }

            manifest.Files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
            return manifest;
        }

        public static string GetSessionCacheRoot(string lobbyId)
        {
            string id = string.IsNullOrWhiteSpace(lobbyId) ? "offline" : lobbyId;
            string root = Path.Combine(CacheRoot, SanitizePathPart(id));
            Directory.CreateDirectory(root);
            return root;
        }

        public static string ResolveCachePath(string lobbyId, string relativePath)
        {
            if (!IsSafeRelativePath(relativePath))
            {
                throw new InvalidDataException($"Unsafe relative path: {relativePath}");
            }

            string root = Path.GetFullPath(GetSessionCacheRoot(lobbyId));
            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Path escapes cache root: {relativePath}");
            }

            return full;
        }

        public static string ReadFileBase64(string rootLevelPath, string relativePath)
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(rootLevelPath)) ?? string.Empty;
            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Path escapes level root: {relativePath}");
            }

            return Convert.ToBase64String(File.ReadAllBytes(full));
        }

        public static void WriteFileBase64(string lobbyId, string relativePath, string base64)
        {
            string full = ResolveCachePath(lobbyId, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, Convert.FromBase64String(base64));
        }

        public static void WriteFileBase64ToLevelRoot(string rootLevelPath, string relativePath, string base64)
        {
            string full = ResolveLevelPath(rootLevelPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, Convert.FromBase64String(base64));
        }

        public static string ResolveLevelPath(string rootLevelPath, string relativePath)
        {
            if (!IsSafeRelativePath(relativePath))
            {
                throw new InvalidDataException($"Unsafe relative path: {relativePath}");
            }

            string root = Path.GetFullPath(Path.GetDirectoryName(rootLevelPath) ?? string.Empty);
            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Path escapes level root: {relativePath}");
            }

            return full;
        }

        public static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        public static bool IsSafeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string normalized = NormalizeRelativePath(relativePath);
            if (Path.IsPathRooted(normalized) || normalized.Contains(".."))
            {
                return false;
            }

            foreach (char c in Path.GetInvalidPathChars())
            {
                if (normalized.IndexOf(c) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static string SanitizePathPart(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }

        private static string ComputeSha256(string filePath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
