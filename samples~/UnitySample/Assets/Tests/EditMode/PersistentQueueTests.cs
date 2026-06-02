using System.IO;
using NUnit.Framework;
using Unimetry.Internal;
using UnityEngine;

namespace Unimetry.Tests
{
    public sealed class PersistentQueueTests
    {
        [TearDown]
        public void TearDown()
        {
            var queuePath = Path.Combine(Application.persistentDataPath, "unimetry", "queue.json");
            if (File.Exists(queuePath))
            {
                File.Delete(queuePath);
            }
        }

        [Test]
        public void EnqueueAndDequeueBatch_PreservesInsertionOrder()
        {
            var queue = new PersistentQueue(maxQueueSize: 8);
            queue.Enqueue(CreatePending("first"));
            queue.Enqueue(CreatePending("second"));

            var batch = queue.DequeueBatch(batchSize: 1);

            Assert.AreEqual(1, batch.Count);
            Assert.AreEqual("first", batch[0].Message);
            Assert.AreEqual(1, queue.Count);
        }

        [Test]
        public void RequeueFront_RestoresFailedBatch()
        {
            var queue = new PersistentQueue(maxQueueSize: 8);
            queue.Enqueue(CreatePending("retry-me"));

            var batch = queue.DequeueBatch(batchSize: 1);
            queue.RequeueFront(batch);

            Assert.AreEqual(1, queue.Count);
            var restored = queue.DequeueBatch(batchSize: 1);
            Assert.AreEqual("retry-me", restored[0].Message);
        }

        [Test]
        public void Enqueue_TrimsWhenMaxQueueSizeExceeded()
        {
            var queue = new PersistentQueue(maxQueueSize: 2);
            queue.Enqueue(CreatePending("one"));
            queue.Enqueue(CreatePending("two"));
            queue.Enqueue(CreatePending("three"));

            Assert.AreEqual(2, queue.Count);
            var batch = queue.DequeueBatch(batchSize: 2);
            Assert.AreEqual("two", batch[0].Message);
            Assert.AreEqual("three", batch[1].Message);
        }

        private static PendingExport CreatePending(string message)
        {
            return new PendingExport
            {
                Message = message,
                Severity = CapturedErrorSeverity.Error.ToString(),
                Source = CapturedErrorSource.Manual.ToString(),
                TraceId = IdGenerator.CreateTraceId(),
                SpanId = IdGenerator.CreateSpanId(),
                CapturedAtUnixNano = 1_700_000_000_000_000_000L,
            };
        }
    }
}
