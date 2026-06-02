using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unimetry.Internal
{
    internal sealed class ErrorCapture : IDisposable
    {
        private readonly UnimetryOptions options;
        private readonly Action<CapturedError> onCaptured;
        private readonly HashSet<string> recentFingerprints = new();
        private readonly object dedupeGate = new();
        private bool started;

        public ErrorCapture(UnimetryOptions options, Action<CapturedError> onCaptured)
        {
            this.options = options;
            this.onCaptured = onCaptured;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            Application.logMessageReceivedThreaded += HandleLogMessage;
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        }

        public void CaptureManual(Exception exception, string message)
        {
            if (exception == null && string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var captured = BuildCapturedError(
                message ?? exception?.Message ?? "Manual report",
                CapturedErrorSeverity.Exception,
                CapturedErrorSource.Manual,
                exception,
                false);

            Publish(captured);
        }

        public void Dispose()
        {
            if (!started)
            {
                return;
            }

            Application.logMessageReceivedThreaded -= HandleLogMessage;
            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
            TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            started = false;
        }

        private void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            var severity = type switch
            {
                LogType.Assert => CapturedErrorSeverity.Assert,
                LogType.Exception => CapturedErrorSeverity.Exception,
                _ => CapturedErrorSeverity.Error,
            };

            var captured = BuildCapturedError(
                condition,
                severity,
                CapturedErrorSource.UnityLog,
                null,
                false,
                stackTrace);

            Publish(captured);
        }

        private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            var exception = eventArgs.ExceptionObject as Exception;
            var message = exception?.Message ?? eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception";
            var captured = BuildCapturedError(
                message,
                CapturedErrorSeverity.Fatal,
                CapturedErrorSource.UnhandledException,
                exception,
                eventArgs.IsTerminating);

            Publish(captured, bypassDedupe: true);
        }

        private void HandleUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs eventArgs)
        {
            var exception = eventArgs.Exception?.Flatten();
            var captured = BuildCapturedError(
                exception?.Message ?? "Unobserved task exception",
                CapturedErrorSeverity.Exception,
                CapturedErrorSource.UnobservedTaskException,
                exception,
                false);

            eventArgs.SetObserved();
            Publish(captured);
        }

        private CapturedError BuildCapturedError(
            string message,
            CapturedErrorSeverity severity,
            CapturedErrorSource source,
            Exception exception,
            bool isTerminating,
            string stackTraceOverride = null)
        {
            var exceptionType = exception?.GetType().FullName ?? string.Empty;
            var stackTrace = stackTraceOverride ?? StackTraceFormatter.Format(exception, options.MaxStackFrames);
            var thread = Thread.CurrentThread;
            var traceId = IdGenerator.CreateTraceId();
            var spanId = IdGenerator.CreateSpanId();
            var fingerprint = IdGenerator.CreateFingerprint(exceptionType, message, stackTrace);

            return new CapturedError(
                message,
                severity,
                source,
                exceptionType,
                stackTrace,
                thread.Name ?? thread.ManagedThreadId.ToString(),
                isTerminating,
                fingerprint,
                traceId,
                spanId,
                DateTimeOffset.UtcNow);
        }

        private void Publish(CapturedError captured, bool bypassDedupe = false)
        {
            if (!bypassDedupe && ShouldDropDuplicate(captured.Fingerprint))
            {
                return;
            }

            onCaptured?.Invoke(captured);
        }

        private bool ShouldDropDuplicate(string fingerprint)
        {
            lock (dedupeGate)
            {
                if (recentFingerprints.Contains(fingerprint))
                {
                    return true;
                }

                recentFingerprints.Add(fingerprint);
                if (recentFingerprints.Count > 128)
                {
                    recentFingerprints.Clear();
                }

                return false;
            }
        }
    }
}
