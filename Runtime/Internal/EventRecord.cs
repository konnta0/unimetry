namespace Unimetry.Internal
{
    internal enum EventTagKind : byte
    {
        String = 0,
        Bool = 1,
        Int = 2,
        Long = 3,
        Double = 4,
    }

    internal struct EventTag
    {
        public string Key;
        public EventTagKind Kind;
        public long Bits;
        public string Text;

        public static EventTag FromString(string key, string value)
        {
            return new EventTag
            {
                Key = key,
                Kind = EventTagKind.String,
                Text = value,
            };
        }

        public static EventTag FromBool(string key, bool value)
        {
            return new EventTag
            {
                Key = key,
                Kind = EventTagKind.Bool,
                Bits = value ? 1L : 0L,
            };
        }

        public static EventTag FromInt(string key, int value)
        {
            return new EventTag
            {
                Key = key,
                Kind = EventTagKind.Int,
                Bits = value,
            };
        }

        public static EventTag FromLong(string key, long value)
        {
            return new EventTag
            {
                Key = key,
                Kind = EventTagKind.Long,
                Bits = value,
            };
        }

        public static EventTag FromDouble(string key, double value)
        {
            return new EventTag
            {
                Key = key,
                Kind = EventTagKind.Double,
                Bits = System.BitConverter.DoubleToInt64Bits(value),
            };
        }

        public double ReadDouble()
        {
            return System.BitConverter.Int64BitsToDouble(Bits);
        }
    }

    internal struct EventRecord
    {
        public string Name;
        public long StartUnixNano;
        public long EndUnixNano;
        public int SeverityNumber;
        public string ExceptionType;
        public string ExceptionMessage;
        public string ExceptionStack;
        public bool ExceptionEscaped;
        public int DroppedTags;
        public int TagCount;
        public EventTag[] Tags;
    }
}
