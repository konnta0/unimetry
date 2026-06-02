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
        public void CreateTraceAndSpanIds_HaveExpectedLength()
        {
            var traceId = IdGenerator.CreateTraceId();
            var spanId = IdGenerator.CreateSpanId();

            Assert.AreEqual(32, traceId.Length);
            Assert.AreEqual(16, spanId.Length);
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
