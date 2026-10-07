using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class CrashArtifactStore
    {
        public const int SchemaVersion = 1;
        public const int MaxRetained = 8;
        public const int MaxFileBytes = 256 * 1024;

        private readonly string directory;

        public CrashArtifactStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Crash directory is required.", nameof(directory));
            }

            this.directory = directory;
        }

        public string DirectoryPath => directory;

        public static bool IsArtifactId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 32)
            {
                return false;
            }

            for (var index = 0; index < id.Length; index++)
            {
                var character = id[index];
                var hex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsSafeDumpName(string id, string fileName)
        {
            return IsArtifactId(id)
                && !string.IsNullOrEmpty(fileName)
                && string.Equals(fileName, id + ".dmp", StringComparison.Ordinal);
        }

        public string ArtifactPath(string id)
        {
            return Path.Combine(directory, id + ".crash.json");
        }

        public string MinidumpPath(string id)
        {
            return Path.Combine(directory, id + ".dmp");
        }

        public bool EnsureDirectory()
        {
            return TryGetDirectory(create: true);
        }

        public bool TryWrite(CrashArtifact artifact)
        {
            if (artifact == null || !IsArtifactId(artifact.id))
            {
                return false;
            }

            if (!TryGetDirectory(create: true))
            {
                return false;
            }

            if (artifact.capturedAtUnixMs <= 0)
            {
                artifact.capturedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            artifact.Clamp();
            var json = JsonUtility.ToJson(artifact);
            if (string.IsNullOrEmpty(json) || json.Length > MaxFileBytes)
            {
                return false;
            }

            if (!AtomicFile.TryWrite(ArtifactPath(artifact.id), json))
            {
                return false;
            }

            Trim();
            return true;
        }

        public List<CrashArtifact> ReadPending()
        {
            var results = new List<CrashArtifact>();
            if (!TryGetDirectory(create: false))
            {
                return results;
            }

            string[] paths;
            try
            {
                paths = Directory.GetFiles(directory, "*.crash.json");
            }
            catch (Exception)
            {
                return results;
            }

            for (var index = 0; index < paths.Length; index++)
            {
                if (!TryRead(paths[index], out var artifact))
                {
                    AtomicFile.TryDelete(paths[index]);
                    continue;
                }

                results.Add(artifact);
            }

            results.Sort(static (left, right) => left.capturedAtUnixMs.CompareTo(right.capturedAtUnixMs));
            return results;
        }

        public bool HasDump(CrashArtifact artifact)
        {
            if (artifact == null || !IsSafeDumpName(artifact.id, artifact.minidumpFile) || artifact.minidumpBytes <= 0)
            {
                return false;
            }

            try
            {
                var info = new FileInfo(MinidumpPath(artifact.id));
                if (!info.Exists || info.Length != artifact.minidumpBytes || info.Length > 32 * 1024 * 1024)
                {
                    return false;
                }

                return (info.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void DeleteJson(CrashArtifact artifact)
        {
            if (artifact == null || !IsArtifactId(artifact.id))
            {
                return;
            }

            AtomicFile.TryDelete(ArtifactPath(artifact.id));
        }

        public void DeleteAll()
        {
            if (!TryGetDirectory(create: false))
            {
                return;
            }

            DeleteMatching("*.crash.json");
            DeleteMatching("*.dmp");
            DeleteMatching("*.tmp");
        }

        private void Trim()
        {
            TrimGroup("*.crash.json", deleteDump: true);
            TrimGroup("*.dmp", deleteDump: false);
        }

        private void TrimGroup(string pattern, bool deleteDump)
        {
            string[] paths;
            try
            {
                paths = Directory.GetFiles(directory, pattern);
            }
            catch (Exception)
            {
                return;
            }

            if (paths.Length <= MaxRetained)
            {
                return;
            }

            try
            {
                Array.Sort(paths, static (left, right) => File.GetLastWriteTimeUtc(left).CompareTo(File.GetLastWriteTimeUtc(right)));
            }
            catch (Exception)
            {
                return;
            }
            var extra = paths.Length - MaxRetained;
            for (var index = 0; index < extra; index++)
            {
                var path = paths[index];
                AtomicFile.TryDelete(path);
                if (!deleteDump)
                {
                    continue;
                }

                var name = Path.GetFileName(path);
                if (name != null && name.EndsWith(".crash.json", StringComparison.Ordinal) && name.Length == 32 + ".crash.json".Length)
                {
                    var id = name.Substring(0, 32);
                    if (IsArtifactId(id))
                    {
                        AtomicFile.TryDelete(MinidumpPath(id));
                    }
                }
            }
        }

        private void DeleteMatching(string pattern)
        {
            string[] paths;
            try
            {
                paths = Directory.GetFiles(directory, pattern);
            }
            catch (Exception)
            {
                return;
            }

            for (var index = 0; index < paths.Length; index++)
            {
                var name = Path.GetFileName(paths[index]);
                if (!IsManagedName(name, pattern))
                {
                    continue;
                }

                AtomicFile.TryDelete(paths[index]);
            }
        }

        private static bool IsManagedName(string name, string pattern)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (string.Equals(pattern, "*.tmp", StringComparison.Ordinal))
            {
                return name.EndsWith(".tmp", StringComparison.Ordinal);
            }

            if (string.Equals(pattern, "*.dmp", StringComparison.Ordinal))
            {
                return name.Length == 32 + 4 && name.EndsWith(".dmp", StringComparison.Ordinal) && IsArtifactId(name.Substring(0, 32));
            }

            return name.Length == 32 + ".crash.json".Length
                && name.EndsWith(".crash.json", StringComparison.Ordinal)
                && IsArtifactId(name.Substring(0, 32));
        }

        private bool TryRead(string path, out CrashArtifact artifact)
        {
            artifact = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes)
                {
                    return false;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                var name = Path.GetFileName(path);
                if (name == null || name.Length != 32 + ".crash.json".Length || !name.EndsWith(".crash.json", StringComparison.Ordinal))
                {
                    return false;
                }

                var id = name.Substring(0, 32);
                if (!IsArtifactId(id))
                {
                    return false;
                }

                var json = File.ReadAllText(path);
                artifact = JsonUtility.FromJson<CrashArtifact>(json);
                if (artifact == null || artifact.schema != SchemaVersion || !string.Equals(artifact.id, id, StringComparison.Ordinal))
                {
                    artifact = null;
                    return false;
                }

                artifact.Clamp();
                return true;
            }
            catch (Exception)
            {
                artifact = null;
                return false;
            }
        }

        private bool TryGetDirectory(bool create)
        {
            try
            {
                if (create)
                {
                    Directory.CreateDirectory(directory);
                }

                var info = new DirectoryInfo(directory);
                if (!info.Exists)
                {
                    return false;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
