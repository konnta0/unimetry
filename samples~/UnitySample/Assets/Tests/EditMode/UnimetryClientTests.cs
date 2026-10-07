using System;
using System.IO;
using NUnit.Framework;
using Unimetry.Internal;
using UnityEngine;

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
    }
}
