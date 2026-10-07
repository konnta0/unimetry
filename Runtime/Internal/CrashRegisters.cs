using System.Globalization;
using System.Text;

namespace Unimetry.Internal
{
    internal static class CrashRegisters
    {
        public static readonly int[] X64Offsets =
        {
            0x78, 0x80, 0x88, 0x90, 0x98, 0xA0, 0xA8, 0xB0,
            0xB8, 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8,
        };

        public static readonly string[] Names =
        {
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15", "rip",
        };

        public static string Format(long[] values)
        {
            if (values == null || Names.Length != X64Offsets.Length || values.Length != Names.Length)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Names.Length * 24);
            for (var index = 0; index < Names.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(Names[index]);
                builder.Append("=0x");
                builder.Append(values[index].ToString("x16", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
