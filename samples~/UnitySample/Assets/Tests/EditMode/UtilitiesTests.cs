using System;
using NUnit.Framework;
using Unimetry.Internal;

namespace Unimetry.Tests
{
    public sealed class UtilitiesTests
    {
        [Test]
        public void CreateFingerprint_IsStableForSameInput()
        {
            var first = IdGenerator.CreateFingerprint(
                "System.Exception",
                "same message",
                "at Foo()\nat Bar()");

            var second = IdGenerator.CreateFingerprint(
                "System.Exception",
                "same message",
                "at Foo()\nat Bar()");

            Assert.AreEqual(first, second);
            Assert.AreNotEqual(string.Empty, first);
        }

        [Test]
        public void ExportFingerprint_HashesTheSanitizedTextWhenTheInputFingerprintIsCopied()
        {
            const string before = "pre-sanitizer-hash";
            var resolved = ExportFingerprint.Resolve(
                before,
                before,
                "System.InvalidOperationException",
                "open ~/Library/save",
                "at Game.Load()\n~/Library/save:1");

            Assert.AreEqual(
                IdGenerator.CreateFingerprint(
                    "System.InvalidOperationException",
                    "open ~/Library/save",
                    "at Game.Load()\n~/Library/save:1"),
                resolved);
            Assert.AreNotEqual(before, resolved);
        }

        [Test]
        public void ExportFingerprint_KeepsAFingerprintTheSanitizerChose()
        {
            var resolved = ExportFingerprint.Resolve(
                "pre-sanitizer-hash",
                "issue-42",
                "System.InvalidOperationException",
                "open ~/Library/save",
                "at Game.Load()");

            Assert.AreEqual("issue-42", resolved);
        }

        [Test]
        public void ExportFingerprint_HashesWhenTheSanitizerClearsTheFingerprint()
        {
            var resolved = ExportFingerprint.Resolve(
                "pre-sanitizer-hash",
                string.Empty,
                "System.InvalidOperationException",
                "open ~/Library/save",
                string.Empty);

            Assert.AreEqual(
                IdGenerator.CreateFingerprint(
                    "System.InvalidOperationException",
                    "open ~/Library/save",
                    string.Empty),
                resolved);
        }

        [Test]
        public void CreateTraceAndSpanIds_HaveExpectedLength()
        {
            var traceId = IdGenerator.CreateTraceId();
            var spanId = IdGenerator.CreateSpanId();

            Assert.AreEqual(32, traceId.Length);
            Assert.AreEqual(16, spanId.Length);
        }

        [Test]
        public void EventClockUnixNano_ReturnsStrictlyIncreasingValuesWithinTheSameMillisecond()
        {
            const int calls = 10_000;
            var first = EventClock.UnixNano();
            var previous = first;
            var foundSameMillisecond = false;

            for (var index = 1; index < calls; index++)
            {
                var current = EventClock.UnixNano();
                Assert.Greater(current, previous);
                if (current / 1_000_000L == previous / 1_000_000L)
                {
                    foundSameMillisecond = true;
                }

                previous = current;
            }

            Assert.IsTrue(foundSameMillisecond);
            Assert.Less(previous - first, TimeSpan.FromSeconds(1).Ticks * 100L);
        }

        [Test]
        public void StackTraceFormatter_TruncatesFrames()
        {
            var stackTrace = "frame0\nframe1\nframe2\nframe3\nframe4";
            var formatted = StackTraceFormatter.Format(stackTrace, maxFrames: 3);

            StringAssert.Contains("frame0", formatted);
            StringAssert.Contains("frame2", formatted);
            StringAssert.DoesNotContain("frame3", formatted);
        }
    }
}
