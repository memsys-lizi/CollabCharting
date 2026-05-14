using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

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

        public static byte[] ReadFileBytes(string rootLevelPath, string relativePath)
        {
            string full = ResolveLevelPath(rootLevelPath, relativePath);
            return File.ReadAllBytes(full);
        }

        public static List<ResourceManifestEntry> CollectRequiredFiles(CollabOperationBatch batch, string rootLevelPath)
        {
            TryCollectRequiredFiles(batch, rootLevelPath, out List<ResourceManifestEntry> files, out _);
            return files;
        }

        public static bool TryCollectRequiredFiles(
            CollabOperationBatch batch,
            string rootLevelPath,
            out List<ResourceManifestEntry> requiredFiles,
            out List<string> missingFiles)
        {
            var files = new Dictionary<string, ResourceManifestEntry>(StringComparer.OrdinalIgnoreCase);
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (batch == null || string.IsNullOrWhiteSpace(rootLevelPath) || !File.Exists(rootLevelPath))
            {
                requiredFiles = files.Values.ToList();
                missingFiles = missing.ToList();
                return missingFiles.Count == 0;
            }

            foreach (CollabAtomicOperation op in batch.Ops)
            {
                CollectRequiredFiles(op, rootLevelPath, files, missing);
            }

            requiredFiles = files.Values.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToList();
            missingFiles = missing.ToList();
            return missingFiles.Count == 0;
        }

        public static bool LevelRootHasFiles(string rootLevelPath, IEnumerable<ResourceManifestEntry> files, out List<string> missingFiles)
        {
            missingFiles = new List<string>();
            foreach (ResourceManifestEntry file in files)
            {
                if (file == null || !IsSafeRelativePath(file.RelativePath))
                {
                    missingFiles.Add(file?.RelativePath ?? "<invalid>");
                    continue;
                }

                string full = ResolveLevelPath(rootLevelPath, file.RelativePath);
                if (!File.Exists(full))
                {
                    missingFiles.Add(file.RelativePath);
                    continue;
                }

                FileInfo info = new FileInfo(full);
                if (info.Length != file.Size || !string.Equals(ComputeSha256(full), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    missingFiles.Add(file.RelativePath);
                }
            }

            return missingFiles.Count == 0;
        }

        public static bool CacheHasFiles(string lobbyId, IEnumerable<ResourceManifestEntry> files, out List<string> missingFiles)
        {
            missingFiles = new List<string>();
            foreach (ResourceManifestEntry file in files)
            {
                if (file == null || !IsSafeRelativePath(file.RelativePath))
                {
                    missingFiles.Add(file?.RelativePath ?? "<invalid>");
                    continue;
                }

                string full = ResolveCachePath(lobbyId, file.RelativePath);
                if (!File.Exists(full))
                {
                    missingFiles.Add(file.RelativePath);
                    continue;
                }

                FileInfo info = new FileInfo(full);
                if (info.Length != file.Size || !string.Equals(ComputeSha256(full), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    missingFiles.Add(file.RelativePath);
                }
            }

            return missingFiles.Count == 0;
        }

        private static void CollectRequiredFiles(
            CollabAtomicOperation op,
            string rootLevelPath,
            Dictionary<string, ResourceManifestEntry> files,
            ISet<string> missingFiles)
        {
            if (op == null || op.Payload == null || op.Kind.EndsWith(".remove", StringComparison.Ordinal))
            {
                return;
            }

            if (op.Payload["changes"] is JArray changes)
            {
                foreach (JToken change in changes)
                {
                    if (change.Value<bool?>("NewExists") == true)
                    {
                        CollectRequiredFiles(change["NewValue"], rootLevelPath, files, missingFiles);
                    }
                }

                return;
            }

            if (op.Kind.EndsWith(".add", StringComparison.Ordinal))
            {
                CollectRequiredFiles(op.Payload["item"], rootLevelPath, files, missingFiles);
                return;
            }

            if (op.Kind == "decoration.reorder")
            {
                CollectRequiredFiles(op.Payload["items"], rootLevelPath, files, missingFiles);
                return;
            }

            CollectRequiredFiles(op.Payload["newValue"] ?? op.Payload["value"] ?? op.Payload["values"], rootLevelPath, files, missingFiles);
        }

        private static void CollectRequiredFiles(
            JToken? token,
            string rootLevelPath,
            Dictionary<string, ResourceManifestEntry> files,
            ISet<string> missingFiles)
        {
            if (token == null)
            {
                return;
            }

            if (token.Type == JTokenType.String)
            {
                TryAddRequiredFile(rootLevelPath, token.Value<string>() ?? string.Empty, files, missingFiles);
                return;
            }

            foreach (JToken child in token.Children())
            {
                CollectRequiredFiles(child, rootLevelPath, files, missingFiles);
            }
        }

        private static void TryAddRequiredFile(
            string rootLevelPath,
            string value,
            Dictionary<string, ResourceManifestEntry> files,
            ISet<string> missingFiles)
        {
            string relativePath = NormalizeRelativePath(value);
            if (!IsSafeRelativePath(relativePath) || !HasSyncableFileExtension(relativePath))
            {
                return;
            }

            string full = ResolveLevelPath(rootLevelPath, relativePath);
            if (!File.Exists(full))
            {
                Main.Mod?.Logger.Warning($"Collab operation references missing resource: {relativePath}");
                missingFiles.Add(relativePath);
                return;
            }

            FileInfo info = new FileInfo(full);
            files[relativePath] = new ResourceManifestEntry
            {
                RelativePath = relativePath,
                Size = info.Length,
                Sha256 = ComputeSha256(full)
            };
        }

        public static void WriteFileBase64(string lobbyId, string relativePath, string base64)
        {
            string full = ResolveCachePath(lobbyId, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, Convert.FromBase64String(base64));
        }

        public static void WriteFileBytes(string lobbyId, string relativePath, byte[] bytes)
        {
            string full = ResolveCachePath(lobbyId, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, bytes);
        }

        public static void WriteFileBase64ToLevelRoot(string rootLevelPath, string relativePath, string base64)
        {
            string full = ResolveLevelPath(rootLevelPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, Convert.FromBase64String(base64));
        }

        public static void WriteFileBytesToLevelRoot(string rootLevelPath, string relativePath, byte[] bytes)
        {
            string full = ResolveLevelPath(rootLevelPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? string.Empty);
            File.WriteAllBytes(full, bytes);
        }

        public static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
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

        private static bool HasSyncableFileExtension(string relativePath)
        {
            string ext = Path.GetExtension(relativePath).ToLowerInvariant();
            switch (ext)
            {
                case ".adofai":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".webp":
                case ".ogg":
                case ".mp3":
                case ".wav":
                case ".flac":
                case ".mp4":
                case ".webm":
                case ".mov":
                    return true;
                default:
                    return false;
            }
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
