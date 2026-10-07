namespace Unimetry.Internal
{
    internal static class EventAttributes
    {
        public const int MaxCount = 8;
        private const int MaxKeyLength = 256;
        private const int MaxValueLength = 4096;

        private static readonly object Gate = new object();
        private static readonly EventTag[] Items = new EventTag[MaxCount];
        private static int count;

        public static void Set(string key, string value)
        {
            if (!TryPrepareKey(ref key))
            {
                return;
            }

            if (value == null)
            {
                Remove(key);
                return;
            }

            Set(EventTag.FromString(key, Bound(value)));
        }

        public static void Set(string key, bool value)
        {
            if (!TryPrepareKey(ref key))
            {
                return;
            }

            Set(EventTag.FromBool(key, value));
        }

        public static void Set(string key, int value)
        {
            if (!TryPrepareKey(ref key))
            {
                return;
            }

            Set(EventTag.FromInt(key, value));
        }

        public static void Set(string key, long value)
        {
            if (!TryPrepareKey(ref key))
            {
                return;
            }

            Set(EventTag.FromLong(key, value));
        }

        public static void Set(string key, double value)
        {
            if (!TryPrepareKey(ref key))
            {
                return;
            }

            Set(EventTag.FromDouble(key, value));
        }

        public static void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (Gate)
            {
                for (var index = 0; index < count; index++)
                {
                    if (!string.Equals(Items[index].Key, key, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    for (var shift = index; shift < count - 1; shift++)
                    {
                        Items[shift] = Items[shift + 1];
                    }

                    count--;
                    Items[count] = default;
                    return;
                }
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                for (var index = 0; index < count; index++)
                {
                    Items[index] = default;
                }

                count = 0;
            }
        }

        public static void Apply(ref EventRecord slot, int explicitCount)
        {
            if (slot.CommonTags == null)
            {
                return;
            }

            lock (Gate)
            {
                var copied = 0;
                for (var index = 0; index < count && copied < slot.CommonTags.Length; index++)
                {
                    if (ContainsKey(slot.Tags, explicitCount, Items[index].Key))
                    {
                        continue;
                    }

                    slot.CommonTags[copied] = Items[index];
                    copied++;
                }

                slot.CommonTagCount = copied;
            }
        }

        private static void Set(EventTag tag)
        {
            lock (Gate)
            {
                for (var index = 0; index < count; index++)
                {
                    if (!string.Equals(Items[index].Key, tag.Key, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Items[index] = tag;
                    return;
                }

                if (count >= Items.Length)
                {
                    return;
                }

                Items[count] = tag;
                count++;
            }
        }

        private static bool TryPrepareKey(ref string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (key.Length > MaxKeyLength)
            {
                key = key.Substring(0, MaxKeyLength);
            }

            return true;
        }

        private static string Bound(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= MaxValueLength)
            {
                return value;
            }

            return value.Substring(0, MaxValueLength);
        }

        private static bool ContainsKey(EventTag[] tags, int count, string key)
        {
            if (tags == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (count > tags.Length)
            {
                count = tags.Length;
            }

            for (var index = 0; index < count; index++)
            {
                if (string.Equals(tags[index].Key, key, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
