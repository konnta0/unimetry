using System.Globalization;

namespace Unimetry.Internal
{
    internal static class CrashSignal
    {
        public static string Name(uint code)
        {
            switch (code)
            {
                case 0xC0000005:
                    return "ACCESS_VIOLATION";
                case 0xC0000006:
                    return "IN_PAGE_ERROR";
                case 0xC000001D:
                    return "ILLEGAL_INSTRUCTION";
                case 0xC0000025:
                    return "NONCONTINUABLE_EXCEPTION";
                case 0xC0000026:
                    return "INVALID_DISPOSITION";
                case 0xC000008C:
                    return "ARRAY_BOUNDS_EXCEEDED";
                case 0xC0000094:
                    return "INTEGER_DIVIDE_BY_ZERO";
                case 0xC0000096:
                    return "PRIVILEGED_INSTRUCTION";
                case 0xC00000FD:
                    return "STACK_OVERFLOW";
                case 0x80000002:
                    return "DATATYPE_MISALIGNMENT";
                case 0x80000003:
                    return "BREAKPOINT";
                case 0x80000004:
                    return "SINGLE_STEP";
                case 0xE06D7363:
                    return "CXX_EXCEPTION";
                default:
                    return "NATIVE_EXCEPTION";
            }
        }

        public static string Hex(ulong value)
        {
            return "0x" + value.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static string Describe(
            uint code,
            ulong exceptionAddress,
            int parameterCount,
            ulong information0,
            ulong information1)
        {
            var name = Name(code);
            var summary = string.Concat("Native crash ", name, " (", Hex(code), ") at ", Hex(exceptionAddress));
            if (code == 0xC0000005 && parameterCount >= 2)
            {
                string operation;
                switch (information0)
                {
                    case 0:
                        operation = "reading";
                        break;
                    case 1:
                        operation = "writing";
                        break;
                    case 8:
                        operation = "executing";
                        break;
                    default:
                        operation = "accessing";
                        break;
                }

                summary = string.Concat(summary, " ", operation, " ", Hex(information1));
            }

            return summary;
        }
    }
}
