// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class HostPerformanceManagerTests
    {
        private string _performanceCountersValue;
        private TestLogger _logger;
        private HostPerformanceManager _performanceManager;
        private Mock<IServiceProvider> _serviceProviderMock;

        public HostPerformanceManagerTests()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _performanceCountersValue = string.Empty;
            _logger = new TestLogger("Test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.DynamicSku);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName)).Returns(() => _performanceCountersValue);
            var options = new HostHealthMonitorOptions();
            _serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            _performanceManager = new HostPerformanceManager(mockEnvironment.Object, new OptionsWrapper<HostHealthMonitorOptions>(options), _serviceProviderMock.Object);
        }

        [Fact]
        public void HostHealthMonitorOptions_FormatsCorrectly()
        {
            var options = new HostHealthMonitorOptions();
            var result = JObject.Parse(options.Format()).ToString(Formatting.None);
            Assert.Equal("{\"Enabled\":true,\"HealthCheckInterval\":\"00:00:10\",\"HealthCheckWindow\":\"00:02:00\",\"CounterThreshold\":0.8}", result);
        }

        [Fact]
        public void GetCounters_ReturnsExpectedResult()
        {
            _performanceCountersValue = "{\"userTime\": 30000000,\"kernelTime\": 16562500,\"pageFaults\": 131522,\"processes\": 1,\"processLimit\": 32,\"threads\": 32,\"threadLimit\": 512,\"connections\": 4,\"connectionLimit\": 300,\"activeConnections\": 25,\"activeConnectionLimit\": 600,\"sections\": 3,\"sectionLimit\": 256,\"namedPipes\": 0,\"namedPipeLimit\": 128,\"remoteDirMonitors\": 5,\"remoteDirMonitorLimit\": 500,\"readIoOperations\": 675,\"writeIoOperations\": 18,\"otherIoOperations\": 9721,\"readIoBytes\": 72585119,\"writeIoBytes\": 5446,\"otherIoBytes\": 393926,\"privateBytes\": 33759232,\"handles\": 987,\"contextSwitches\": 15535,\"remoteOpens\": 250}";
            var counters = _performanceManager.GetPerformanceCounters(_logger);
            Assert.Equal(counters.PageFaults, 131522);

            // verify garbage characters are trimmed
            _performanceCountersValue += "猅";
            counters = _performanceManager.GetPerformanceCounters(_logger);
            Assert.Equal(counters.PageFaults, 131522);

            _performanceCountersValue = "{}";
            counters = _performanceManager.GetPerformanceCounters(_logger);
            Assert.Equal(counters.PageFaults, 0);

            _performanceCountersValue = "this is not json";
            counters = _performanceManager.GetPerformanceCounters(_logger);
            Assert.Null(counters);
            var error = _logger.GetLogMessages().Last();
            Assert.Equal("Failed to deserialize application performance counters. JSON Content: \"this is not json\"", error.FormattedMessage);
        }

        [Theory]
        [InlineData(ThrottleState.Disabled, false)]
        [InlineData(ThrottleState.Unknown, false)]
        [InlineData(ThrottleState.Enabled, true)]
        public async Task ProcessThresholdsExceeded_InProc_ReturnsExpectedResult(ThrottleState throttleState, bool expected)
        {
            var status = new ConcurrencyThrottleAggregateStatus
            {
                State = throttleState
            };
            if (throttleState == ThrottleState.Enabled)
            {
                status.EnabledThrottles = new List<string> { "CPU", "Memory" }.AsReadOnly();
            }
            var mockConcurrencyThrottleManager = new Mock<IConcurrencyThrottleManager>(MockBehavior.Strict);
            mockConcurrencyThrottleManager.Setup(p => p.GetStatus()).Returns(status);

            var mockScriptHostManager = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostManagerServiceProviderMock = mockScriptHostManager.As<IServiceProvider>();
            scriptHostManagerServiceProviderMock.Setup(p => p.GetService(typeof(IFunctionInvocationDispatcherFactory))).Returns(null);
            scriptHostManagerServiceProviderMock.Setup(p => p.GetService(typeof(IConcurrencyThrottleManager))).Returns(mockConcurrencyThrottleManager.Object);
            _serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(mockScriptHostManager.Object);

            Collection<string> exceededCounters = new Collection<string>();
            bool result = await _performanceManager.ProcessThresholdsExceeded(_logger);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(ThrottleState.Disabled, false)]
        [InlineData(ThrottleState.Unknown, false)]
        [InlineData(ThrottleState.Enabled, true)]
        public async Task ProcessThresholdsExceeded_OutOfProc_ReturnsExpectedResult(ThrottleState throttleState, bool expected)
        {
            var status = new ConcurrencyThrottleAggregateStatus
            {
                State = throttleState
            };
            if (throttleState == ThrottleState.Enabled)
            {
                status.EnabledThrottles = new List<string> { "CPU", "Memory" }.AsReadOnly();
            }
            var mockConcurrencyThrottleManager = new Mock<IConcurrencyThrottleManager>(MockBehavior.Strict);
            mockConcurrencyThrottleManager.Setup(p => p.GetStatus()).Returns(status);

            var workerStatuses = new Dictionary<string, WorkerStatus>();
            for (int i = 0; i < 3; i++)
            {
                var workerStatus = new WorkerStatus
                {
                    Latency = TimeSpan.FromMilliseconds(25)
                };
                workerStatuses.Add(Guid.NewGuid().ToString(), workerStatus);
            }

            var mockDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            mockDispatcher.SetupGet(p => p.State).Returns(FunctionInvocationDispatcherState.Initialized);
            mockDispatcher.Setup(p => p.GetWorkerStatusesAsync()).ReturnsAsync(workerStatuses);
            var mockDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            mockDispatcherFactory.Setup(p => p.GetFunctionDispatcher()).Returns(mockDispatcher.Object);
            var mockScriptHostManager = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostManagerServiceProviderMock = mockScriptHostManager.As<IServiceProvider>();
            scriptHostManagerServiceProviderMock.Setup(p => p.GetService(typeof(IFunctionInvocationDispatcherFactory))).Returns(mockDispatcherFactory.Object);
            scriptHostManagerServiceProviderMock.Setup(p => p.GetService(typeof(IConcurrencyThrottleManager))).Returns(mockConcurrencyThrottleManager.Object);
            _serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(mockScriptHostManager.Object);

            Collection<string> exceededCounters = new Collection<string>();
            bool result = await _performanceManager.ProcessThresholdsExceeded(_logger);
            Assert.Equal(expected, result);

            mockDispatcher.VerifyAll();
        }

        [Theory]
        [InlineData(0, 100, 0.60F, false)]
        [InlineData(60, 100, 0.60F, false)]
        [InlineData(61, 100, 0.60F, true)]
        [InlineData(100, 100, 0.60F, true)]
        [InlineData(101, 100, 0.60F, true)]
        [InlineData(101, 0, 0.60F, false)]
        [InlineData(101, -1, 0.60F, false)]
        public void ThresholdExceeded_ReturnsExpectedValue(int currentValue, int limit, float threshold, bool expected)
        {
            Assert.Equal(expected, HostPerformanceManager.ThresholdExceeded("Test", currentValue, limit, threshold));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(240, false)]
        [InlineData(241, true)]
        [InlineData(290, true)]
        [InlineData(300, true)]
        [InlineData(310, true)]
        public void PerformanceCounterThresholdsExceeded_Connections_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Connections = currentValue,
                ConnectionLimit = 300
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Connections", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(480, false)]
        [InlineData(481, true)]
        [InlineData(500, true)]
        [InlineData(600, true)]
        [InlineData(610, true)]
        public void PerformanceCounterThresholdsExceeded_ActiveConnections_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                ActiveConnections = currentValue,
                ActiveConnectionLimit = 600
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("ActiveConnections", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(400, false)]
        [InlineData(401, true)]
        [InlineData(500, true)]
        [InlineData(600, true)]
        public void PerformanceCounterThresholdsExceeded_RemoteDirMonitors_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                RemoteDirMonitors = currentValue,
                RemoteDirMonitorLimit = 500
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("RemoteDirMonitors", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(409, false)]
        [InlineData(410, true)]
        [InlineData(500, true)]
        [InlineData(512, true)]
        [InlineData(513, true)]
        public void PerformanceCounterThresholdsExceeded_Threads_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Threads = currentValue,
                ThreadLimit = 512
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Threads", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(25, false)]
        [InlineData(26, true)]
        [InlineData(30, true)]
        [InlineData(32, true)]
        [InlineData(33, true)]
        public void PerformanceCounterThresholdsExceeded_Processes_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Processes = currentValue,
                ProcessLimit = 32
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Processes", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(102, false)]
        [InlineData(103, true)]
        [InlineData(120, true)]
        [InlineData(128, true)]
        [InlineData(129, true)]
        public void PerformanceCounterThresholdsExceeded_NamedPipes_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                NamedPipes = currentValue,
                NamedPipeLimit = 128
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("NamedPipes", exceededCounters[0]);
            }
        }

        [Fact]
        public void PerformanceCounterThresholdsExceeded_MultipleExceededThrottles_ReturnsExpectedResults()
        {
            var counters = new ApplicationPerformanceCounters
            {
                NamedPipes = 130,
                NamedPipeLimit = 128,
                Processes = 40,
                ProcessLimit = 32,
                Threads = 600,
                ThreadLimit = 512
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(true, HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters));
            Assert.Equal(3, exceededCounters.Count);
            Assert.Equal("Threads, Processes, NamedPipes", string.Join(", ", exceededCounters));
        }
    }
}
