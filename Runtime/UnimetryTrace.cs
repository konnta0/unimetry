using System;
using System.Threading;
using Unimetry.Internal;

namespace Unimetry
{
    /// <summary>
    /// W3C trace context and gameplay spans exported as OpenTelemetry traces.
    /// </summary>
    public static class UnimetryTrace
    {
        /// <summary>
        /// Starts a gameplay span. Completing the returned object exports it.
        /// When Unimetry is not initialized the span is local and is not exported.
        /// </summary>
        /// <param name="name">Span name, for example <c>match.load</c>.</param>
        /// <returns>A span that ends when disposed.</returns>
        public static UnimetrySpan Start(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return UnimetrySpan.Inactive;
            }

            return TraceContext.Start(name);
        }

        /// <summary>
        /// Stores a W3C <c>traceparent</c> value as the current parent context.
        /// </summary>
        /// <param name="header">A header such as <c>00-{trace}-{span}-01</c>.</param>
        /// <returns><c>true</c> when the header is accepted.</returns>
        public static bool ExtractTraceParent(string header)
        {
            return TraceContext.ExtractTraceParent(header);
        }

        /// <summary>
        /// Sets baggage copied onto later span attributes. An empty value removes the key.
        /// At most eight entries are kept. This is not written to resource attributes.
        /// </summary>
        /// <param name="key">Baggage key, for example <c>user.id</c> or <c>session.id</c>.</param>
        /// <param name="value">Baggage value.</param>
        public static void SetBaggage(string key, string value)
        {
            TraceContext.SetBaggage(key, value);
        }
    }

    /// <summary>
    /// A gameplay span started by <see cref="UnimetryTrace.Start"/>.
    /// </summary>
    public sealed class UnimetrySpan : IDisposable
    {
        private readonly long startUnixNano;
        private int disposed;

        internal UnimetrySpan(string name, string traceId, string spanId, string parentSpanId, long startUnixNano)
        {
            Name = name ?? string.Empty;
            TraceId = traceId ?? string.Empty;
            SpanId = spanId ?? string.Empty;
            ParentSpanId = parentSpanId ?? string.Empty;
            this.startUnixNano = startUnixNano;
        }

        internal static UnimetrySpan Inactive { get; } = new UnimetrySpan(string.Empty, string.Empty, string.Empty, string.Empty, 0);

        /// <summary>Span name.</summary>
        public string Name { get; }

        /// <summary>Hex-encoded 32-character trace identifier.</summary>
        public string TraceId { get; }

        /// <summary>Hex-encoded 16-character span identifier.</summary>
        public string SpanId { get; }

        /// <summary>Parent span identifier. Empty when this span starts a new trace.</summary>
        public string ParentSpanId { get; }

        internal long StartUnixNano => startUnixNano;

        /// <summary>Ends the span and queues it for export.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            TraceContext.Complete(this);
        }
    }
}
