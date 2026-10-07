namespace Unimetry.Internal
{
    internal static class EventPipeline
    {
        private static volatile EventBuffer buffer;
        private static volatile UnimetryLogWriter writer;

        public static void Configure(EventBuffer nextBuffer, UnimetryLogWriter nextWriter, bool captureEvents)
        {
            buffer = nextBuffer;
            writer = nextWriter;
            UnimetryEvent.EventEnabled = captureEvents && nextBuffer != null ? 1 : 0;
        }

        public static void Publish(EventHandle handle, long endUnixNano)
        {
            var activeBuffer = buffer;
            if (activeBuffer != null && handle != null)
            {
                activeBuffer.WriteHandle(handle, endUnixNano);
            }

            var logWriter = writer;
            if (logWriter == null || handle == null)
            {
                return;
            }

            var severity = handle.ExceptionType == null ? UnimetrySeverity.Info : UnimetrySeverity.Error;
            var entry = new UnimetryLogEntry(
                UnimetryLogKind.Event,
                handle.Name,
                handle.Name,
                severity,
                handle.StartUnixNano,
                endUnixNano);
            LogDispatch.Emit(logWriter, in entry);
        }

        public static void PublishPoint(string name, long unixNano)
        {
            var activeBuffer = buffer;
            if (activeBuffer != null)
            {
                activeBuffer.WritePoint(name, unixNano);
            }

            var logWriter = writer;
            if (logWriter == null)
            {
                return;
            }

            var entry = new UnimetryLogEntry(
                UnimetryLogKind.Event,
                name,
                name,
                UnimetrySeverity.Info,
                unixNano,
                unixNano);
            LogDispatch.Emit(logWriter, in entry);
        }

        public static void EmitLog(in UnimetryLogEntry entry)
        {
            LogDispatch.Emit(writer, in entry);
        }

        public static int CopyPending(EventRecord[] destination)
        {
            var activeBuffer = buffer;
            if (activeBuffer == null || destination == null)
            {
                return 0;
            }

            return activeBuffer.CopyOldest(destination);
        }

        public static int DroppedCount
        {
            get
            {
                var activeBuffer = buffer;
                return activeBuffer == null ? 0 : activeBuffer.Dropped;
            }
        }
    }
}
