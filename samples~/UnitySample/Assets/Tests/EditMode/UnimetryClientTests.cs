using System;
using NUnit.Framework;
using UnityEngine;

namespace Unimetry.Tests
{
    public sealed class UnimetryClientTests
    {
        [TearDown]
        public void TearDown()
        {
            UnimetryClient.Shutdown();
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
    }
}
