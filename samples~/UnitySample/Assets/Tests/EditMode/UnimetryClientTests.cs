using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
#if UNIMETRY_UNITASK
using System.Reflection;
using Cysharp.Threading.Tasks;
#endif
using NUnit.Framework;
using Unimetry.Internal;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unimetry.Tests
{
    public sealed class UnimetryClientTests
    {
        [TearDown]
        public void TearDown()
        {
            UnimetryClient.Shutdown();
            var root = Path.Combine(Application.persistentDataPath, "unimetry");
            var queuePath = Path.Combine(root, "queue.json");
            if (File.Exists(queuePath))
            {
                File.Delete(queuePath);
            }

            var eventPath = Path.Combine(root, "events.json");
            if (File.Exists(eventPath))
            {
                File.Delete(eventPath);
            }

            var breadcrumbs = Path.Combine(root, "breadcrumbs.txt");
            if (File.Exists(breadcrumbs))
            {
                File.Delete(breadcrumbs);
            }

            var crashes = Path.Combine(root, "crashes");
            if (Directory.Exists(crashes))
            {
                Directory.Delete(crashes, true);
            }
        }

        [Test]
        public void Initialize_ReportsManualExceptionWithoutThrowing()
        {
            UnimetryClient.Initialize(new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-client-test",
                DeploymentEnvironment = "test",
                FlushInterval = TimeSpan.FromHours(1),
            });

            Assert.IsTrue(UnimetryClient.IsInitialized);
            Assert.DoesNotThrow(() =>
                UnimetryClient.Report(new InvalidOperationException("client test exception")));
        }

        [Test]
        public void Initialize_ThrowsWhenEndpointMissing()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                UnimetryClient.Initialize(new UnimetryOptions
                {
                    Endpoint = string.Empty,
                    ServiceName = "invalid",
                }));

            StringAssert.Contains("endpoint", exception.Message);
        }

        [Test]
        public void AddBreadcrumb_BeforeInitialize_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => UnimetryClient.AddBreadcrumb("early"));
        }

        [Test]
        public void Initialize_ConfiguresMetricCaptureAndShutdownDisablesIt()
        {
            var options = CreateOptions(captureNativeCrashes: false);
            options.CaptureMetrics = true;

            UnimetryClient.Initialize(options);
            Assert.IsTrue(UnimetryMetric.IsEnabled);

            UnimetryClient.Shutdown();
            Assert.IsFalse(UnimetryMetric.IsEnabled);

            options.CaptureMetrics = false;
            UnimetryClient.Initialize(options);
            Assert.IsFalse(UnimetryMetric.IsEnabled);
        }

#if UNIMETRY_UNITASK
        [Test]
        public void AsyncApis_ExposeTaskAndUniTaskVariants()
        {
            Assert.AreEqual(
                typeof(Task),
                typeof(UnimetryClient).GetMethod(
                    nameof(UnimetryClient.FlushAsync),
                    BindingFlags.Public | BindingFlags.Static)?.ReturnType);
            Assert.AreEqual(
                typeof(UniTask),
                typeof(UnimetryClient).GetMethod(
                    nameof(UnimetryClient.FlushUniTaskAsync),
                    BindingFlags.Public | BindingFlags.Static)?.ReturnType);
            Assert.AreEqual(
                typeof(Task),
                typeof(UnimetryClient).GetMethod(
                    nameof(UnimetryClient.ShutdownAsync),
                    BindingFlags.Public | BindingFlags.Static)?.ReturnType);
            Assert.AreEqual(
                typeof(UniTask),
                typeof(UnimetryClient).GetMethod(
                    nameof(UnimetryClient.ShutdownUniTaskAsync),
                    BindingFlags.Public | BindingFlags.Static)?.ReturnType);
        }
#endif

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator TaskAsyncApis_CanBeCalledFromAWorkerThread()
        {
            UnimetryClient.Initialize(CreateOptions(captureNativeCrashes: false));
            UnimetryClient.Report(new InvalidOperationException("worker-thread flush"));

            var flush = Task.Run(() => UnimetryClient.FlushAsync());
            while (!flush.IsCompleted)
            {
                yield return null;
            }

            Assert.IsFalse(flush.IsFaulted, flush.Exception?.ToString());

            var shutdown = Task.Run(() => UnimetryClient.ShutdownAsync());
            while (!shutdown.IsCompleted)
            {
                yield return null;
            }

            Assert.IsFalse(shutdown.IsFaulted, shutdown.Exception?.ToString());
            Assert.IsFalse(UnimetryClient.IsInitialized);
        }

        [Test]
        public void Initialize_WhenNativeCrashesDisabled_DeletesStoredArtifacts()
        {
            var store = new CrashArtifactStore(CrashDirectory());
            var id = new string('e', 32);
            Assert.IsTrue(store.TryWrite(new CrashArtifact
            {
                id = id,
                capturedAtUnixMs = 1_700_000_000_000L,
                message = "revoke me",
            }));

            UnimetryClient.Initialize(CreateOptions(captureNativeCrashes: false));

            Assert.IsFalse(File.Exists(store.ArtifactPath(id)));
        }

        [Test]
        public void Initialize_IngestsPreviousNativeCrash()
        {
            var store = new CrashArtifactStore(CrashDirectory());
            var id = new string('f', 32);
            Assert.IsTrue(store.TryWrite(new CrashArtifact
            {
                id = id,
                capturedAtUnixMs = 1_700_000_000_000L,
                signal = "0xc0000005",
                exceptionType = "ACCESS_VIOLATION",
                message = "previous native crash",
            }));

            UnimetryClient.Initialize(CreateOptions(captureNativeCrashes: true));
            UnimetryClient.AddBreadcrumb("entered match");

            Assert.IsFalse(File.Exists(store.ArtifactPath(id)));
            var queuePath = Path.Combine(Application.persistentDataPath, "unimetry", "queue.json");
            var stored = File.ReadAllBytes(queuePath);
            Assert.GreaterOrEqual(stored.Length, 4);
            Assert.AreEqual((byte)'U', stored[0]);
            Assert.AreEqual((byte)'M', stored[1]);
            Assert.AreEqual((byte)'Q', stored[2]);
            Assert.AreEqual((byte)'1', stored[3]);
            var reloaded = new PersistentQueue(32);
            var batch = reloaded.DequeueBatch(32);
            var found = false;
            for (var index = 0; index < batch.Count; index++)
            {
                if (batch[index].Message == "previous native crash" && batch[index].Source == "NativeCrash")
                {
                    found = true;
                }
            }

            Assert.IsTrue(found);
            Assert.IsTrue(File.Exists(Path.Combine(Application.persistentDataPath, "unimetry", "breadcrumbs.txt")));
        }

        [Test]
        public void Initialize_RejectsInvalidBreadcrumbCapacity()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                UnimetryClient.Initialize(new UnimetryOptions
                {
                    Endpoint = "http://127.0.0.1:9",
                    BreadcrumbCapacity = 0,
                }));

            StringAssert.Contains("BreadcrumbCapacity", exception.Message);
        }

        [Test]
        public void ErrorFilter_CanDisableUnityLogCaptureOnly()
        {
            var options = CreateOptions(captureNativeCrashes: false);
            options.ErrorFilter = captured => captured.Source != CapturedErrorSource.UnityLog;
            UnimetryClient.Initialize(options);

            LogAssert.Expect(LogType.Error, "filtered unity error");
            Debug.LogError("filtered unity error");
            UnimetryClient.Report(new InvalidOperationException("manual remains"));

            var batch = new PersistentQueue(32).DequeueBatch(32);
            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual(CapturedErrorSource.Manual.ToString(), batch[0].Source);
        }

        [Test]
        public void SetResourceAttribute_SnapshotsIdentityAtRecordTime()
        {
            var options = CreateOptions(captureNativeCrashes: false);
            options.ResourceAttributes = new System.Collections.Generic.Dictionary<string, string>
            {
                ["app.user_id"] = "account-a",
            };
            UnimetryClient.Initialize(options);
            UnimetryClient.Report(new InvalidOperationException("from account a"));

            UnimetryClient.SetResourceAttribute("app.user_id", "account-b");
            UnimetryClient.Report(new ArgumentException("from account b"));
            var span = UnimetryTrace.Start("account-b-span");
            UnimetryClient.SetResourceAttribute("app.user_id", "account-c");
            span.Dispose();

            var batch = new PersistentQueue(32).DequeueBatch(32);
            Assert.AreEqual(3, batch.Count);
            Assert.AreEqual("account-a", FindResource(batch[0], "app.user_id"));
            Assert.AreEqual("account-b", FindResource(batch[1], "app.user_id"));
            Assert.AreEqual("account-b", FindResource(batch[2], "app.user_id"));
        }

        private static UnimetryOptions CreateOptions(bool captureNativeCrashes)
        {
            return new UnimetryOptions
            {
                Endpoint = "http://127.0.0.1:9",
                ServiceName = "unimetry-client-test",
                DeploymentEnvironment = "test",
                FlushInterval = TimeSpan.FromHours(1),
                CaptureNativeCrashes = captureNativeCrashes,
            };
        }

        private static string CrashDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "unimetry", "crashes");
        }

        private static string FindResource(PendingExport export, string key)
        {
            var attributes = export.Resource?.Attributes;
            if (attributes == null)
            {
                return null;
            }

            for (var index = 0; index < attributes.Length; index++)
            {
                if (attributes[index].Key == key)
                {
                    return attributes[index].Value;
                }
            }

            return null;
        }
    }
}
