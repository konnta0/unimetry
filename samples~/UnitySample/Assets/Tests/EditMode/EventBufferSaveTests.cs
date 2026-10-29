using System;
using System.IO;
using NUnit.Framework;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class EventBufferSaveTests
    {
        private static readonly long IntervalTicks =
            (long)(EventBuffer.SaveInterval.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

        private string directory;
        private string storagePath;
        private long nowTicks;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "unimetry-event-buffer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            storagePath = Path.Combine(directory, "events.json");
            nowTicks = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void WritesWithinSaveInterval_AreCoalescedUntilDue()
        {
            var buffer = CreateBuffer();
            buffer.WritePoint("first", 1);
            CollectionAssert.AreEqual(new[] { "first" }, LoadPersistedNames());

            buffer.WritePoint("second", 2);
            buffer.WritePoint("third", 3);
            buffer.SaveIfDue();
            CollectionAssert.AreEqual(new[] { "first" }, LoadPersistedNames());

            nowTicks += IntervalTicks;
            buffer.SaveIfDue();
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, LoadPersistedNames());
        }

        [Test]
        public void SetSaveImmediately_FlushesDeferredEventsAndSavesLaterWrites()
        {
            var buffer = CreateBuffer();
            buffer.WritePoint("first", 1);
            buffer.WritePoint("second", 2);

            buffer.SetSaveImmediately(true);
            CollectionAssert.AreEqual(new[] { "first", "second" }, LoadPersistedNames());

            buffer.WritePoint("third", 3);
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, LoadPersistedNames());
        }

        [Test]
        public void Commit_PersistsRemovalWithoutWaitingForSaveInterval()
        {
            var buffer = CreateBuffer();
            buffer.WritePoint("first", 1);
            buffer.WritePoint("second", 2);

            buffer.Commit(1);

            CollectionAssert.AreEqual(new[] { "second" }, LoadPersistedNames());
        }

        [Test]
        public void DisablingImmediateSave_ResumesCoalescing()
        {
            var buffer = CreateBuffer();
            buffer.SetSaveImmediately(true);
            buffer.WritePoint("first", 1);

            buffer.SetSaveImmediately(false);
            buffer.WritePoint("second", 2);
            CollectionAssert.AreEqual(new[] { "first" }, LoadPersistedNames());

            nowTicks += IntervalTicks;
            buffer.SaveIfDue();
            CollectionAssert.AreEqual(new[] { "first", "second" }, LoadPersistedNames());
        }

        private EventBuffer CreateBuffer()
        {
            return new EventBuffer(8, storagePath: storagePath, clockTicks: () => nowTicks);
        }

        private string[] LoadPersistedNames()
        {
            var loaded = CreateBuffer();
            var records = new EventRecord[8];
            for (var index = 0; index < records.Length; index++)
            {
                records[index].Tags = new EventTag[EventBuffer.MaxTags];
                records[index].CommonTags = new EventTag[EventAttributes.MaxCount];
            }

            var count = loaded.CopyOldest(records);
            var names = new string[count];
            for (var index = 0; index < count; index++)
            {
                names[index] = records[index].Name;
            }

            return names;
        }
    }
}
