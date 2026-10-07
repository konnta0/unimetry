using System;
using Unimetry.Internal;

namespace Unimetry
{
    /// <summary>
    /// Pooled event scope that can cross an await. Use <see cref="UnimetryEvent.Begin"/> on synchronous hot paths.
    /// </summary>
    /// <remarks>
    /// A handle is single-threaded. Do not call <see cref="SetTag"/> concurrently with <see cref="Dispose"/>.
    /// </remarks>
    public sealed class EventHandle : IDisposable
    {
        internal string Name;
        internal long StartTimestamp;
        internal long StartUnixNano;
        internal int TagCount;
        internal int DroppedTags;
        internal string ExceptionType;
        internal string ExceptionMessage;
        internal string ExceptionStack;
        internal bool ExceptionEscaped;
        internal EventTag[] Tags;
        internal bool Active;

        internal EventHandle()
        {
            Tags = new EventTag[EventBuffer.MaxTags];
        }

        /// <summary>
        /// Records a string attribute. Keys longer than 256 characters and values longer than 4096 characters are truncated.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, string value)
        {
            if (!TryReserve(ref key))
            {
                return;
            }

            Tags[TagCount] = EventTag.FromString(key, Bound(value));
            TagCount++;
        }

        /// <summary>
        /// Records a boolean attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, bool value)
        {
            if (!TryReserve(ref key))
            {
                return;
            }

            Tags[TagCount] = EventTag.FromBool(key, value);
            TagCount++;
        }

        /// <summary>
        /// Records an integer attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, int value)
        {
            if (!TryReserve(ref key))
            {
                return;
            }

            Tags[TagCount] = EventTag.FromInt(key, value);
            TagCount++;
        }

        /// <summary>
        /// Records a 64-bit integer attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, long value)
        {
            if (!TryReserve(ref key))
            {
                return;
            }

            Tags[TagCount] = EventTag.FromLong(key, value);
            TagCount++;
        }

        /// <summary>
        /// Records a floating-point attribute.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, double value)
        {
            if (!TryReserve(ref key))
            {
                return;
            }

            Tags[TagCount] = EventTag.FromDouble(key, value);
            TagCount++;
        }

        /// <summary>
        /// Marks the event as an error and stores exception semantic-convention fields.
        /// </summary>
        /// <param name="exception">Exception leaving the instrumented region.</param>
        public void RecordException(Exception exception)
        {
            if (!Active || exception == null)
            {
                return;
            }

            ExceptionType = exception.GetType().FullName ?? string.Empty;
            ExceptionMessage = Bound(exception.Message);
            ExceptionStack = Bound(exception.StackTrace);
            ExceptionEscaped = false;
        }

        /// <summary>
        /// Completes the event and returns this handle to the pool.
        /// </summary>
        public void Dispose()
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            try
            {
                var endUnixNano = EventClock.AddElapsedNanos(
                    StartUnixNano,
                    StartTimestamp,
                    EventClock.Timestamp());
                EventPipeline.Publish(this, endUnixNano);
            }
            finally
            {
                EventHandlePool.Return(this);
            }
        }

        internal void Activate(string name, long startTimestamp, long startUnixNano)
        {
            Name = name;
            StartTimestamp = startTimestamp;
            StartUnixNano = startUnixNano;
            TagCount = 0;
            DroppedTags = 0;
            ExceptionType = null;
            ExceptionMessage = null;
            ExceptionStack = null;
            ExceptionEscaped = false;
            Active = true;
        }

        internal void Clear()
        {
            Name = null;
            StartTimestamp = 0;
            StartUnixNano = 0;
            TagCount = 0;
            DroppedTags = 0;
            ExceptionType = null;
            ExceptionMessage = null;
            ExceptionStack = null;
            ExceptionEscaped = false;
            Active = false;
            for (var index = 0; index < Tags.Length; index++)
            {
                Tags[index] = default;
            }
        }

        private bool TryReserve(ref string key)
        {
            if (!Active || string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (key.Length > 256)
            {
                key = key.Substring(0, 256);
            }

            if (TagCount >= Tags.Length)
            {
                DroppedTags++;
                return false;
            }

            return true;
        }

        private static string Bound(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= 4096)
            {
                return value;
            }

            return value.Substring(0, 4096);
        }
    }

    /// <summary>
    /// Stack-only event scope for synchronous code. Disposing a default scope does nothing and allocates nothing.
    /// </summary>
    public readonly ref struct EventScope
    {
        private readonly EventHandle handle;

        internal EventScope(EventHandle handle)
        {
            this.handle = handle;
        }

        /// <summary>
        /// Records a string attribute when this scope is active.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, string value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a boolean attribute when this scope is active.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, bool value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records an integer attribute when this scope is active.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, int value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a 64-bit integer attribute when this scope is active.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, long value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a floating-point attribute when this scope is active.
        /// </summary>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        public void SetTag(string key, double value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Marks the event as an error when this scope is active.
        /// </summary>
        /// <param name="exception">Exception leaving the instrumented region.</param>
        public void RecordException(Exception exception)
        {
            if (handle != null)
            {
                handle.RecordException(exception);
            }
        }

        /// <summary>
        /// Completes the event. A default scope returns immediately.
        /// </summary>
        public void Dispose()
        {
            if (handle != null)
            {
                handle.Dispose();
            }
        }
    }
}
