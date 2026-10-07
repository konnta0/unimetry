using System;
using System.IO;
using System.Text;

namespace Unimetry.Internal
{
    internal static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static bool TryWrite(string path, string contents)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var temp = path + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temp, contents ?? string.Empty, Utf8);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }
            catch (Exception)
            {
                TryDelete(temp);
                return false;
            }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Cleanup is best-effort. The next launch ignores files that fail validation.
            }
        }
    }
}
