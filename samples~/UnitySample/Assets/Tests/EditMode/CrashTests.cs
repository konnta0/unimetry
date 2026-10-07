using System;
using System.IO;
using NUnit.Framework;
using Unimetry.Internal;
using UnityEngine;

namespace Unimetry.Tests
{
    public sealed class CrashTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Application.temporaryCachePath, "unimetry-crash-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            var queuePath = Path.Combine(Application.persistentDataPath, "unimetry", "queue.json");
            if (File.Exists(queuePath))
            {
                File.Delete(queuePath);
            }
        }

        [Test]
        public void Store_RoundTripsArtifactAndDropsForeignFiles()
        {
            var store = new CrashArtifactStore(root);
            var id = new string('a', 32);
            var written = store.TryWrite(new CrashArtifact
            {
                id = id,
                capturedAtUnixMs = 1_700_000_000_000L,
                signal = "0xc0000005",
                exceptionType = "ACCESS_VIOLATION",
                message = "previous native crash",
                threadName = "12",
                registers = "rax=0x1",
                managedStack = "at Game()",
                breadcrumbs = "1000\tmenu",
                deviceModel = "TestPC",
                osType = "windows",
                buildId = "build-1",
                minidumpFile = "../secret.dmp",
                minidumpBytes = 99,
            });

            Assert.IsTrue(written);
            File.WriteAllText(Path.Combine(root, "notes.txt"), "keep");
            var mismatched = Path.Combine(root, new string('b', 32) + ".crash.json");
            File.WriteAllText(mismatched, "{\"schema\":1,\"id\":\"" + new string('c', 32) + "\"}");

            var pending = store.ReadPending();

            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(id, pending[0].id);
            Assert.AreEqual(1_700_000_000_000L, pending[0].capturedAtUnixMs);
            Assert.AreEqual("previous native crash", pending[0].message);
            Assert.AreEqual(string.Empty, pending[0].minidumpFile);
            Assert.AreEqual(0, pending[0].minidumpBytes);
            Assert.IsTrue(File.Exists(Path.Combine(root, "notes.txt")));
            Assert.IsFalse(File.Exists(mismatched));
        }

        [Test]
        public void Store_ReadsPosixSignalArtifact()
        {
            var store = new CrashArtifactStore(root);
            var id = "000000006ac676a200000a016aa83bcb";
            var json = "{\"schema\":1,\"id\":\"" + id
                + "\",\"capturedAtUnixMs\":1700000000000,\"signal\":\"0x0000000b\",\"exceptionType\":\"SIGSEGV\",\"message\":\"Native crash SIGSEGV\",\"threadName\":\"1\",\"registers\":\"pc=0x10\",\"managedStack\":\"\",\"breadcrumbs\":\"1000\\thello\",\"deviceModel\":\"TestMac\",\"osType\":\"darwin\",\"buildId\":\"build-test\",\"minidumpFile\":\"\",\"minidumpBytes\":0}";
            File.WriteAllText(Path.Combine(root, id + ".crash.json"), json);

            var pending = store.ReadPending();

            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("SIGSEGV", pending[0].exceptionType);
            Assert.AreEqual("0x0000000b", pending[0].signal);
            Assert.AreEqual("darwin", pending[0].osType);
            Assert.AreEqual("build-test", pending[0].buildId);
            Assert.AreEqual(1700000000000L, pending[0].capturedAtUnixMs);
        }

        [Test]
        public void Store_TrimsOldestArtifact()
        {
            var store = new CrashArtifactStore(root);
            for (var index = 0; index < CrashArtifactStore.MaxRetained; index++)
            {
                var id = HexId(index + 1);
                Assert.IsTrue(store.TryWrite(new CrashArtifact
                {
                    id = id,
                    capturedAtUnixMs = 1_700_000_000_000L + index,
                    message = "item-" + index.ToString(),
                }));
                File.SetLastWriteTimeUtc(store.ArtifactPath(id), DateTime.UtcNow.AddMinutes(-30 + index));
            }

            var newest = HexId(CrashArtifactStore.MaxRetained + 1);
            Assert.IsTrue(store.TryWrite(new CrashArtifact
            {
                id = newest,
                capturedAtUnixMs = 1_700_000_000_000L,
                message = "newest",
            }));

            Assert.IsFalse(File.Exists(store.ArtifactPath(HexId(1))));
            Assert.IsTrue(File.Exists(store.ArtifactPath(newest)));
            Assert.AreEqual(CrashArtifactStore.MaxRetained, Directory.GetFiles(root, "*.crash.json").Length);
        }

        [Test]
        public void BreadcrumbLog_DropsExpiredAndOldestEntries()
        {
            long now = 1_000_000;
            var path = Path.Combine(root, "breadcrumbs.txt");
            var log = new BreadcrumbLog(2, TimeSpan.FromSeconds(5), path, () => now);

            log.Add("one\nline");
            log.Add("two");
            now += 10_000;
            log.Add("three");

            var text = log.CaptureText(now);

            StringAssert.DoesNotContain("one", text);
            StringAssert.DoesNotContain("two", text);
            StringAssert.Contains("three", text);
            var snapshot = File.ReadAllText(path);
            StringAssert.DoesNotContain("one", snapshot);
            StringAssert.Contains("three", snapshot);
        }

        [Test]
        public void BreadcrumbLog_OverwritesOldestWhenFull()
        {
            long now = 5_000;
            var log = new BreadcrumbLog(2, TimeSpan.FromMinutes(1), Path.Combine(root, "breadcrumbs.txt"), () => now);

            log.Add("one");
            log.Add("two");
            log.Add("three");
            var text = log.CaptureText(now);

            StringAssert.DoesNotContain("one", text);
            var two = text.IndexOf("two", StringComparison.Ordinal);
            var three = text.IndexOf("three", StringComparison.Ordinal);
            Assert.GreaterOrEqual(two, 0);
            Assert.Greater(three, two);
        }

        [Test]
        public void Ingest_EnqueuesCrashLogAndStripsUnsafeDumpName()
        {
            var store = new CrashArtifactStore(root);
            var id = new string('d', 32);
            Assert.IsTrue(store.TryWrite(new CrashArtifact
            {
                id = id,
                capturedAtUnixMs = 1_700_000_000_000L,
                signal = CrashSignal.Hex(0xC0000005),
                exceptionType = "ACCESS_VIOLATION",
                message = "previous native crash",
                breadcrumbs = "entered match",
                deviceModel = "TestPC",
                osType = "windows",
                buildId = "build-9",
                minidumpFile = id + ".dmp",
                minidumpBytes = 128,
            }));
            File.WriteAllBytes(store.MinidumpPath(id), new byte[128]);

            DeleteQueueFile();
            var queue = new PersistentQueue(8);
            var options = new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-test",
                ExportErrorSpans = true,
                Sanitizer = Redact,
            };

            var count = CrashIngest.EnqueuePending(store, queue, options);

            Assert.AreEqual(1, count);
            Assert.IsFalse(File.Exists(store.ArtifactPath(id)));
            var batch = queue.DequeueBatch(4);
            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual(CrashRecord.Type, batch[0].RecordType);
            Assert.AreEqual("redacted", batch[0].Message);
            Assert.AreEqual("redacted", batch[0].Breadcrumbs);
            Assert.AreEqual(id + ".dmp", batch[0].MinidumpFile);
            Assert.AreEqual(128, batch[0].MinidumpBytes);
            Assert.AreEqual(CrashRecord.SpanName, batch[0].SpanName);
            Assert.AreEqual(1_700_000_000_000L * 1_000_000L, batch[0].CapturedAtUnixNano);

            var logs = OtlpJsonWriter.BuildLogsPayload(batch, options);
            StringAssert.Contains("\"unimetry.record_type\"", logs);
            StringAssert.Contains("\"crash.signal\"", logs);
            StringAssert.Contains("c0000005", logs);
            StringAssert.Contains("\"device.model\"", logs);
            StringAssert.Contains("TestPC", logs);
            StringAssert.Contains("\"os.type\"", logs);
            StringAssert.Contains("\"app.build_id\"", logs);
            StringAssert.Contains("\"severityText\":\"FATAL\"", logs);
            StringAssert.Contains("\"crash.minidump_file\"", logs);
            StringAssert.Contains(id + ".dmp", logs);
            StringAssert.DoesNotContain("previous native crash", logs);
            StringAssert.DoesNotContain("secret", logs);

            var unsafeExport = CrashIngest.ToPendingExport(new CrashArtifact
            {
                id = id,
                capturedAtUnixMs = 1_700_000_000_000L,
                message = "boom",
                minidumpFile = "../secret.dmp",
                minidumpBytes = 12,
            }, new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
            });
            Assert.AreEqual(string.Empty, unsafeExport.MinidumpFile);
            Assert.AreEqual(0, unsafeExport.MinidumpBytes);

            var traces = OtlpJsonWriter.BuildTracesPayload(batch, options);
            StringAssert.Contains("\"name\":\"unity.crash\"", traces);
        }

        [Test]
        public void CrashSignal_DescribesAccessViolation()
        {
            var text = CrashSignal.Describe(0xC0000005, 0x10, 2, 0, 0x20);

            StringAssert.Contains("ACCESS_VIOLATION", text);
            StringAssert.Contains("reading", text);
            StringAssert.Contains("0x0000000000000020", text);
        }

        [Test]
        public void CrashRegisters_FormatsRipAtDocumentedOffset()
        {
            Assert.AreEqual(CrashRegisters.Names.Length, CrashRegisters.X64Offsets.Length);
            Assert.AreEqual(0xF8, CrashRegisters.X64Offsets[CrashRegisters.X64Offsets.Length - 1]);
            Assert.AreEqual("rip", CrashRegisters.Names[CrashRegisters.Names.Length - 1]);

            var values = new long[CrashRegisters.Names.Length];
            values[0] = 0x11;
            values[values.Length - 1] = 0x22;
            var formatted = CrashRegisters.Format(values);

            StringAssert.Contains("rax=0x0000000000000011", formatted);
            StringAssert.Contains("rip=0x0000000000000022", formatted);
        }

        private static CapturedError Redact(CapturedError captured)
        {
            return new CapturedError(
                "redacted",
                captured.Severity,
                captured.Source,
                captured.ExceptionType,
                captured.StackTrace,
                captured.ThreadName,
                captured.IsTerminating,
                captured.Fingerprint,
                captured.TraceId,
                captured.SpanId,
                captured.CapturedAtUtc);
        }

        private static string HexId(int value)
        {
            var hex = value.ToString("x");
            return new string('0', 32 - hex.Length) + hex;
        }

        private static void DeleteQueueFile()
        {
            var queuePath = Path.Combine(Application.persistentDataPath, "unimetry", "queue.json");
            if (File.Exists(queuePath))
            {
                File.Delete(queuePath);
            }
        }
    }
}
