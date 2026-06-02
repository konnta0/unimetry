using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Unimetry.Internal
{
    internal static class IdGenerator
    {
        public static string CreateTraceId()
        {
            return CreateHexId(16);
        }

        public static string CreateSpanId()
        {
            return CreateHexId(8);
        }

        public static string CreateFingerprint(string exceptionType, string message, string stackTrace)
        {
            var input = string.Concat(exceptionType, "\n", message, "\n", GetStackPrefix(stackTrace));
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        private static string CreateHexId(int byteLength)
        {
            var bytes = new byte[byteLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            var builder = new StringBuilder(byteLength * 2);
            for (var index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("x2"));
            }

            return builder.ToString();
        }

        private static string GetStackPrefix(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return string.Empty;
            }

            var lines = stackTrace.Split('\n');
            var count = Math.Min(lines.Length, 5);
            return string.Join('\n', lines, 0, count);
        }
    }

    internal static class StackTraceFormatter
    {
        public static string Format(Exception exception, int maxFrames)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendException(builder, exception, maxFrames);
            return builder.ToString().TrimEnd();
        }

        public static string Format(string stackTrace, int maxFrames)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return string.Empty;
            }

            var lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var count = Math.Min(lines.Length, maxFrames);
            var builder = new StringBuilder();
            for (var index = 0; index < count; index++)
            {
                builder.AppendLine(lines[index]);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendException(StringBuilder builder, Exception exception, int maxFrames, int depth = 0)
        {
            if (exception == null || depth > 8)
            {
                return;
            }

            if (depth > 0)
            {
                builder.AppendLine($"--- Inner exception ({depth}) ---");
            }

            builder.AppendLine($"{exception.GetType().FullName}: {exception.Message}");
            builder.AppendLine(Format(exception.StackTrace, maxFrames));

            if (exception.InnerException != null)
            {
                AppendException(builder, exception.InnerException, maxFrames, depth + 1);
            }
        }
    }

    internal static class SeverityMapping
    {
        public static (int Number, string Text) FromCapturedError(CapturedErrorSeverity severity)
        {
            return severity switch
            {
                CapturedErrorSeverity.Assert => (17, "ERROR"),
                CapturedErrorSeverity.Error => (17, "ERROR"),
                CapturedErrorSeverity.Exception => (17, "ERROR"),
                CapturedErrorSeverity.Fatal => (21, "FATAL"),
                _ => (17, "ERROR"),
            };
        }
    }
}
