using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class EventTests
    {
        [SetUp]
        public void SetUp()
        {
            var eventPath = Path.Combine(Application.persistentDataPath, "unimetry", "events.json");
            if (File.Exists(eventPath))
            {
                File.Delete(eventPath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            UnimetryEvent.ClearAttributes();
            UnimetryClient.Shutdown();
        }

        [Test]
        public void Begin_WithoutInitialization_DoesNotThrowOrRecord()
        {
            using (var scope = UnimetryEvent.Begin("disabled"))
            {
                scope.SetTag("ignored", 1);
                scope.SetSeverity(UnimetrySeverity.Error);
            }

            UnimetryEvent.Write("disabled.point");
            UnimetryEvent.Write("disabled.point.warn", UnimetrySeverity.Warn);
            Assert.IsFalse(UnimetryEvent.IsEnabled);
            Assert.AreEqual(0, Copy().Length);
        }

        [Test]
        public void SetSeverity_RecordsWarnAndErrorWithoutAnException()
        {
            var observed = new List<UnimetrySeverity>();
            Initialize(writer: (in UnimetryLogEntry entry) => observed.Add(entry.Severity));

            using (var scope = UnimetryEvent.Begin("master.delivery.failed"))
            {
                scope.SetSeverity(UnimetrySeverity.Error);
                scope.SetTag("reason", "timeout");
            }

            using (var handle = UnimetryEvent.Start("direct.debug"))
            {
                handle.SetSeverity(UnimetrySeverity.Debug);
            }

            UnimetryEvent.Write("cache.evicted", UnimetrySeverity.Warn);
            using (UnimetryEvent.Begin("pooled.after.error"))
            {
            }

            var records = Copy();
            Assert.AreEqual(4, records.Length);
            Assert.AreEqual((int)UnimetrySeverity.Error, records[0].SeverityNumber);
            Assert.IsNull(records[0].ExceptionType);
            Assert.AreEqual("timeout", records[0].Tags[0].Text);
            Assert.AreEqual((int)UnimetrySeverity.Debug, records[1].SeverityNumber);
            Assert.AreEqual((int)UnimetrySeverity.Warn, records[2].SeverityNumber);
            Assert.AreEqual((int)UnimetrySeverity.Info, records[3].SeverityNumber);
            CollectionAssert.AreEqual(
                new[]
                {
                    UnimetrySeverity.Error,
                    UnimetrySeverity.Debug,
                    UnimetrySeverity.Warn,
                    UnimetrySeverity.Info,
                },
                observed);

            var payload = OtlpJsonWriter.BuildLogsPayload(new List<PendingExport>(), records, records.Length, new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-event-test",
            });
            StringAssert.Contains("\"severityText\":\"ERROR\"", payload);
            StringAssert.Contains("\"severityText\":\"WARN\"", payload);
            StringAssert.Contains("\"severityText\":\"DEBUG\"", payload);
            StringAssert.DoesNotContain("exception.type", payload);
        }

        [Test]
        public void SetSeverity_CannotHideARecordedException()
        {
            Initialize();
            using (var scope = UnimetryEvent.Begin("hidden"))
            {
                scope.SetSeverity(UnimetrySeverity.Warn);
                scope.RecordException(new InvalidOperationException("boom"));
                scope.SetSeverity(UnimetrySeverity.Info);
            }

            using (var scope = UnimetryEvent.Begin("fatal"))
            {
                scope.RecordException(new InvalidOperationException("boom"));
                scope.SetSeverity(UnimetrySeverity.Fatal);
            }

            var records = Copy();
            Assert.AreEqual((int)UnimetrySeverity.Error, records[0].SeverityNumber);
            Assert.AreEqual("System.InvalidOperationException", records[0].ExceptionType);
            Assert.AreEqual((int)UnimetrySeverity.Fatal, records[1].SeverityNumber);
        }

        [Test]
        public void SetSeverity_IgnoresValuesOutsideTheDefinedSet()
        {
            Initialize();
            using (var scope = UnimetryEvent.Begin("unknown.scope"))
            {
                scope.SetSeverity(UnimetrySeverity.Error);
                scope.SetSeverity((UnimetrySeverity)2);
            }

            UnimetryEvent.Write("unknown.point", (UnimetrySeverity)4);

            var records = Copy();
            Assert.AreEqual((int)UnimetrySeverity.Error, records[0].SeverityNumber);
            Assert.AreEqual((int)UnimetrySeverity.Info, records[1].SeverityNumber);
        }

        [Test]
        public void Begin_RecordsStartEndAndTypedTags()
        {
            Initialize();
            using (var scope = UnimetryEvent.Begin("manual.tags"))
            {
                var start = EventClock.Timestamp();
                while (EventClock.Timestamp() == start)
                {
                }

                scope.SetTag("name", "alpha");
                scope.SetTag("flag", false);
                scope.SetTag("count", 3);
                scope.SetTag("big", 4L);
                scope.SetTag("ratio", 1.5d);
            }

            var records = Copy();
            Assert.AreEqual(1, records.Length);
            Assert.AreEqual("manual.tags", records[0].Name);
            Assert.Greater(records[0].EndUnixNano, records[0].StartUnixNano);
            Assert.AreEqual((int)UnimetrySeverity.Info, records[0].SeverityNumber);
            Assert.AreEqual(5, records[0].TagCount);
            Assert.AreEqual("alpha", records[0].Tags[0].Text);
            Assert.AreEqual(0L, records[0].Tags[1].Bits);
            Assert.AreEqual(3L, records[0].Tags[2].Bits);
            Assert.AreEqual(4L, records[0].Tags[3].Bits);
            Assert.AreEqual(1.5d, records[0].Tags[4].ReadDouble());
        }

        [Test]
        public void SetAttribute_IsCopiedOntoLaterEvents()
        {
            UnimetryEvent.SetAttribute("user.id", "player-1");
            UnimetryEvent.SetAttribute("session.id", "session-9");
            Initialize();

            UnimetryEvent.Write("ui.button.click");
            using (var scope = UnimetryEvent.Begin("ui.button.play"))
            {
                scope.SetTag("ui.button", "play");
                scope.SetTag("user.id", "override");
            }

            var records = Copy();
            Assert.AreEqual(2, records.Length);
            Assert.AreEqual("player-1", FindCommon(records[0], "user.id").Text);
            Assert.AreEqual("session-9", FindCommon(records[0], "session.id").Text);
            Assert.AreEqual("play", records[1].Tags[0].Text);
            Assert.AreEqual("override", records[1].Tags[1].Text);
            Assert.IsNull(FindCommon(records[1], "user.id").Key);
            Assert.AreEqual("session-9", FindCommon(records[1], "session.id").Text);

            var events = new EventRecord[1];
            events[0] = records[0];
            var payload = OtlpJsonWriter.BuildLogsPayload(new List<PendingExport>(), events, 1, new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-event-test",
            });
            StringAssert.Contains("\"user.id\"", payload);
            StringAssert.Contains("player-1", payload);

            UnimetryEvent.RemoveAttribute("user.id");
            UnimetryEvent.Write("ui.button.after");
            var after = Copy();
            Assert.IsNull(FindCommon(after[2], "user.id").Key);
            Assert.AreEqual("session-9", FindCommon(after[2], "session.id").Text);
        }

        [Test]
        public void Write_UsesTheSameStartAndEnd()
        {
            Initialize();
            UnimetryEvent.Write("manual.point");

            var records = Copy();
            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(records[0].StartUnixNano, records[0].EndUnixNano);
        }

        [UnityTest]
        public IEnumerator Start_CrossesAwait()
        {
            Initialize();
            var task = CrossAwait();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            var records = Copy();
            Assert.AreEqual(1, records.Length);
            Assert.AreEqual("manual.async", records[0].Name);
            Assert.AreEqual(2L, records[0].Tags[0].Bits);
        }

        [Test]
        public void RecordException_MarksTheEventAsError()
        {
            Initialize();
            using (var scope = UnimetryEvent.Begin("manual.fail"))
            {
                try
                {
                    throw new InvalidOperationException("boom");
                }
                catch (InvalidOperationException exception)
                {
                    scope.RecordException(exception);
                }
            }

            var records = Copy();
            Assert.AreEqual((int)UnimetrySeverity.Error, records[0].SeverityNumber);
            Assert.AreEqual("System.InvalidOperationException", records[0].ExceptionType);
            Assert.AreEqual("boom", records[0].ExceptionMessage);
            Assert.IsFalse(records[0].ExceptionEscaped);
        }

        [Test]
        public void RecordException_AppliesSanitizer()
        {
            Initialize(sanitizer: captured => new CapturedError(
                captured.Message,
                captured.Severity,
                captured.Source,
                captured.ExceptionType,
                "redacted-stack",
                captured.ThreadName,
                captured.IsTerminating,
                captured.Fingerprint,
                captured.TraceId,
                captured.SpanId,
                captured.CapturedAtUtc));

            using (var scope = UnimetryEvent.Begin("manual.sanitized"))
            {
                scope.RecordException(new InvalidOperationException("sensitive"));
            }

            Assert.AreEqual("redacted-stack", Copy()[0].ExceptionStack);
        }

        [Test]
        public void CompletedEvent_SurvivesShutdownAndReinitialize()
        {
            Initialize();
            UnimetryEvent.Write("persisted.event");
            UnimetryClient.Shutdown();

            Initialize();

            var records = Copy();
            Assert.AreEqual(1, records.Length);
            Assert.AreEqual("persisted.event", records[0].Name);
        }

        [Test]
        public void Buffer_DropsEventsBeyondCapacity()
        {
            Initialize(maxEventBuffer: 1);
            UnimetryEvent.Write("first");
            UnimetryEvent.Write("second");

            var records = Copy();
            Assert.AreEqual(1, records.Length);
            Assert.AreEqual("first", records[0].Name);
            Assert.GreaterOrEqual(EventPipeline.DroppedCount, 1);
        }

        [Test]
        public void SetTag_DropsAttributesBeyondTheFixedCapacity()
        {
            Initialize();
            using (var scope = UnimetryEvent.Begin("manual.overflow"))
            {
                for (var index = 0; index < 9; index++)
                {
                    scope.SetTag("tag" + index, index);
                }
            }

            var records = Copy();
            Assert.AreEqual(EventBuffer.MaxTags, records[0].TagCount);
            Assert.AreEqual(1, records[0].DroppedTags);
        }

        [Test]
        public void CaptureEventsFalse_StillForwardsErrorsToTheLogWriter()
        {
            var kinds = new List<UnimetryLogKind>();
            Initialize(
                captureEvents: false,
                writer: (in UnimetryLogEntry entry) => kinds.Add(entry.Kind));

            UnimetryEvent.Write("hidden");
            UnimetryClient.Report(new InvalidOperationException("logged error"));

            Assert.AreEqual(0, Copy().Length);
            Assert.AreEqual(1, kinds.Count);
            Assert.AreEqual(UnimetryLogKind.Log, kinds[0]);
        }

        [Test]
        public void WithLog_ReceivesCompletedEvents()
        {
            string name = null;
            long start = 0;
            long end = 0;
            Initialize(writer: (in UnimetryLogEntry entry) =>
            {
                name = entry.Name;
                start = entry.StartUnixNano;
                end = entry.EndUnixNano;
            });

            UnimetryEvent.Write("logged.event");

            Assert.AreEqual("logged.event", name);
            Assert.Greater(start, 0);
            Assert.AreEqual(start, end);
        }

        [Test]
        public void WithConsoleLog_ReportDoesNotRecurse()
        {
            Initialize(console: true);
            LogAssert.Expect(LogType.Error, "[Unimetry] Error console recursion");

            Assert.DoesNotThrow(() =>
                UnimetryClient.Report(new InvalidOperationException("console recursion")));
        }

        [Test]
        public void WithConsoleLog_WritesEventDuration()
        {
            Initialize(console: true);
            LogAssert.Expect(LogType.Log, new Regex(@"\[Unimetry\] console\.event \d+us"));

            UnimetryEvent.Write("console.event");
        }

        [Test]
        public void WithLog_RejectsNullWriter()
        {
            Assert.Throws<ArgumentNullException>(() => new UnimetryOptions().WithLog(null));
        }

        [Test]
        public void WovenMethod_WithoutInitialization_PreservesBehavior()
        {
            Assert.AreEqual(2, EventWeaveFixture.Add(1));
            Assert.AreEqual(0, Copy().Length);
        }

        [Test]
        public void WovenMethod_RecordsNameTagAndRethrows()
        {
            Initialize();
            Assert.AreEqual(42, EventWeaveFixture.Add(41));

            var added = Copy();
            Assert.AreEqual(1, added.Length, "Expected the [Event] weaver to record weave.add.");
            Assert.AreEqual("weave.add", added[0].Name);
            Assert.LessOrEqual(added[0].StartUnixNano, added[0].EndUnixNano);
            Assert.AreEqual(1, added[0].TagCount);
            Assert.AreEqual(41L, added[0].Tags[0].Bits);

            var exception = Assert.Throws<InvalidOperationException>(() => EventWeaveFixture.Fail());
            Assert.AreEqual("weave failed", exception.Message);
            var failed = Copy();
            Assert.AreEqual(2, failed.Length);
            Assert.AreEqual("weave.fail", failed[1].Name);
            Assert.AreEqual((int)UnimetrySeverity.Error, failed[1].SeverityNumber);
            Assert.AreEqual("System.InvalidOperationException", failed[1].ExceptionType);
            Assert.IsTrue(failed[1].ExceptionEscaped);
        }

        [UnityTest]
        public IEnumerator WovenAsyncMethod_RecordsEventAfterAwait()
        {
            Initialize();
            var task = EventWeaveFixture.DelayAdd(7);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            Assert.AreEqual(8, task.Result);
            var records = Copy();
            Assert.AreEqual(1, records.Length, "Expected the [Event] weaver to record weave.delay.");
            Assert.AreEqual("weave.delay", records[0].Name);
            Assert.AreEqual(7L, records[0].Tags[0].Bits);
            Assert.LessOrEqual(records[0].StartUnixNano, records[0].EndUnixNano);
        }

        private static EventTag FindCommon(EventRecord record, string key)
        {
            var count = record.CommonTagCount;
            if (record.CommonTags != null && count > record.CommonTags.Length)
            {
                count = record.CommonTags.Length;
            }

            for (var index = 0; index < count; index++)
            {
                if (record.CommonTags[index].Key == key)
                {
                    return record.CommonTags[index];
                }
            }

            return default;
        }

        private static async Task CrossAwait()
        {
            using (var handle = UnimetryEvent.Start("manual.async"))
            {
                handle.SetTag("step", 2);
                await Task.Yield();
            }
        }

        private static void Initialize(
            int maxEventBuffer = 32,
            bool captureEvents = true,
            bool console = false,
            UnimetryLogWriter writer = null,
            Func<CapturedError, CapturedError> sanitizer = null)
        {
            var options = new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-event-test",
                DeploymentEnvironment = "test",
                FlushInterval = TimeSpan.FromHours(1),
                MaxEventBuffer = maxEventBuffer,
                CaptureEvents = captureEvents,
                Sanitizer = sanitizer,
            };
            if (console)
            {
                options.WithConsoleLog();
            }
            else if (writer != null)
            {
                options.WithLog(writer);
            }

            UnimetryClient.Initialize(options);
        }

        private static EventRecord[] Copy()
        {
            var buffer = new EventRecord[8];
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index].Tags = new EventTag[EventBuffer.MaxTags];
                buffer[index].CommonTags = new EventTag[EventAttributes.MaxCount];
            }

            var count = EventPipeline.CopyPending(buffer);
            var records = new EventRecord[count];
            for (var index = 0; index < count; index++)
            {
                records[index] = buffer[index];
            }

            return records;
        }
    }

    internal static class EventWeaveFixture
    {
        [Event("weave.add")]
        public static int Add([EventTag("operand")] int value)
        {
            return value + 1;
        }

        [Event("weave.fail")]
        public static void Fail()
        {
            throw new InvalidOperationException("weave failed");
        }

        [Event("weave.delay")]
        public static async Task<int> DelayAdd([EventTag("operand")] int value)
        {
            await Task.Yield();
            return value + 1;
        }
    }
}
