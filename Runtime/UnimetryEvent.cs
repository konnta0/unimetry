using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Unimetry
{
    /// <summary>
    /// Records OpenTelemetry events with a start and end time.
    /// </summary>
    /// <remarks>
    /// When Unimetry is not initialized, or <see cref="UnimetryOptions.CaptureEvents"/> is false,
    /// <see cref="Begin"/>, <see cref="Start"/>, and <see cref="Write"/> return without allocating.
    /// </remarks>
    public static class UnimetryEvent
    {
        /// <summary>
        /// Non-zero while event capture is active. Woven methods branch on this field.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static volatile int EventEnabled;

        /// <summary>
        /// Gets whether event capture is currently recording.
        /// </summary>
        public static bool IsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => EventEnabled != 0;
        }

        /// <summary>
        /// Starts a synchronous event scope. The returned value allocates nothing when capture is disabled.
        /// </summary>
        /// <param name="name">Event name stored in the OTLP <c>eventName</c> field.</param>
        /// <returns>A scope that completes the event when disposed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventScope Begin(string name)
        {
            return new EventScope(Start(name));
        }

        /// <summary>
        /// Starts an event that can cross an await. Returns <see langword="null"/> when capture is disabled,
        /// which <c>using</c> treats as a no-op.
        /// </summary>
        /// <param name="name">Event name stored in the OTLP <c>eventName</c> field.</param>
        /// <returns>A pooled handle, or <see langword="null"/> when capture is disabled.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventHandle Start(string name)
        {
            if (EventEnabled == 0 || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return Internal.EventHandlePool.Rent(name, Internal.EventClock.Timestamp(), Internal.EventClock.UnixNano());
        }

        /// <summary>
        /// Records a point event whose start and end times are the same.
        /// </summary>
        /// <param name="name">Event name stored in the OTLP <c>eventName</c> field.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(string name)
        {
            if (EventEnabled == 0 || string.IsNullOrEmpty(name))
            {
                return;
            }

            Internal.EventPipeline.PublishPoint(name, Internal.EventClock.UnixNano());
        }

        /// <summary>
        /// Starts an event from woven IL.
        /// </summary>
        /// <param name="name">Event name.</param>
        /// <returns>A pooled handle, or <see langword="null"/> when capture is disabled.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static EventHandle WeaveStart(string name)
        {
            return Start(name);
        }

        /// <summary>
        /// Records a string attribute from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveSetTag(EventHandle handle, string key, string value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a boolean attribute from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveSetTag(EventHandle handle, string key, bool value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records an integer attribute from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveSetTag(EventHandle handle, string key, int value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a 64-bit integer attribute from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveSetTag(EventHandle handle, string key, long value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records a floating-point attribute from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="key">Attribute key.</param>
        /// <param name="value">Attribute value.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveSetTag(EventHandle handle, string key, double value)
        {
            if (handle != null)
            {
                handle.SetTag(key, value);
            }
        }

        /// <summary>
        /// Records an exception from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        /// <param name="exception">Exception leaving the woven method.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveRecordException(EventHandle handle, Exception exception)
        {
            if (handle != null)
            {
                handle.RecordException(exception);
                handle.ExceptionEscaped = true;
            }
        }

        /// <summary>
        /// Completes an event from woven IL.
        /// </summary>
        /// <param name="handle">Active event handle.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void WeaveStop(EventHandle handle)
        {
            if (handle != null)
            {
                handle.Dispose();
            }
        }
    }
}
