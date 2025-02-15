// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights
{
    public class TimerExecutionServiceTest : IClassFixture<TimerExecutionServiceTest.Fixture>, IAsyncLifetime
    {
        public class TheConstructor : TimerExecutionServiceTest
        {
            public TheConstructor(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public void RejectsDuplicateNames()
            {
                var timer2 = new Mock<ITimer>();
                timer2.Setup(x => x.Name).Returns(TimerName);
                Timers.Add(timer2.Object);

                var ex = Assert.Throws<ArgumentException>(() => Target);

                Assert.Equal($"There are timers with duplicate names: '{TimerName}' (2)", ex.Message);
            }
        }

        public class TheSetIsEnabledMethod : TimerExecutionServiceTest
        {
            public TheSetIsEnabledMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task SetsIsEnabledOnNewEntity(bool isEnabled)
            {
                await Target.SetIsEnabledAsync(TimerName, isEnabled);

                var entities = await GetEntitiesAsync<TableEntity>();
                var entity = Assert.Single(entities);
                Assert.Equal(new[] { "IsEnabled", "odata.etag", "PartitionKey", "RowKey", "Timestamp" }, entity.Keys.OrderBy(x => x).ToArray());
                Assert.Equal(isEnabled, entity.GetBoolean("IsEnabled"));
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task SetsIsEnabledOnExistingEntity(bool isEnabled)
            {
                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                await Target.SetIsEnabledAsync(TimerName, isEnabled);

                var entities = await GetEntitiesAsync<TableEntity>();
                var entity = Assert.Single(entities);
                Assert.Equal(new[] { "IsEnabled", "LastExecuted", "odata.etag", "PartitionKey", "RowKey", "Timestamp" }, entity.Keys.OrderBy(x => x).ToArray());
                Assert.Equal(isEnabled, entity.GetBoolean("IsEnabled"));
                Assert.InRange(entity.GetDateTimeOffset("LastExecuted").Value, before, after);
            }
        }

        public class TheExecuteNowAsyncMethod : TimerExecutionServiceTest
        {
            public TheExecuteNowAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public async Task IgnoresFrequency()
            {
                await Target.ExecuteNowAsync(TimerName);

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteNowAsync(TimerName);
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Exactly(2));
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task RunsEvenIfAlreadyRunning()
            {
                Timer.Setup(x => x.IsRunningAsync()).ReturnsAsync(true);

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteNowAsync(TimerName);
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }
        }

        public class TheExecuteAsyncMethod : TimerExecutionServiceTest
        {
            public TheExecuteAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task ObservesAutoStartProperty(bool autoStart)
            {
                Timer.Setup(x => x.AutoStart).Returns(autoStart);

                await Target.ExecuteAsync();

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Assert.Equal(autoStart, entity.IsEnabled);
            }

            [Fact]
            public async Task CanExecuteNewTimerByDefault()
            {
                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Assert.Equal(TimerName, entity.RowKey);
                Assert.Equal(TimerExecutionService.PartitionKey, entity.PartitionKey);
                Assert.True(entity.IsEnabled);
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task RunsIfAlreadyRunning()
            {
                Timer.Setup(x => x.IsRunningAsync()).ReturnsAsync(true);

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task DoesNotExecuteTimerDisabledFromStorage()
            {
                await Target.SetIsEnabledAsync(TimerName, isEnabled: false);

                await Target.ExecuteAsync();

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Never);
                Assert.Null(entity.LastExecuted);
            }

            [Fact]
            public async Task ExecutesTimerEnabledForStorage()
            {
                await Target.SetIsEnabledAsync(TimerName, isEnabled: true);
                Timer.Setup(x => x.AutoStart).Returns(false);

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task UpdatesLastExecutedWhenExecuteThrows()
            {
                Timer.Setup(x => x.ExecuteAsync()).ThrowsAsync(new InvalidOperationException());

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task DoesNotAddNewTimerWhenExecuteReturnsFalse()
            {
                Timer.Setup(x => x.ExecuteAsync()).ReturnsAsync(false);

                await Target.ExecuteAsync();

                Assert.Empty(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
            }

            [Fact]
            public async Task DoesNotUpdateLastExecutedWhenExecuteReturnsFalse()
            {
                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;
                Timer.Setup(x => x.ExecuteAsync()).ReturnsAsync(false);
                Timer.Setup(x => x.Frequency).Returns(TimeSpan.Zero);

                await Target.ExecuteAsync();

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Exactly(2));
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task DoesNotRunATimerThatHasRunRecently()
            {
                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                await Target.ExecuteAsync();

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Once);
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task RunsATimerAgainIfTheFrequencyAllows()
            {
                await Target.ExecuteAsync();

                Timer.Setup(x => x.Frequency).Returns(TimeSpan.Zero);

                var before = DateTimeOffset.UtcNow;
                await Target.ExecuteAsync();
                var after = DateTimeOffset.UtcNow;

                var entity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Exactly(2));
                Assert.InRange(entity.LastExecuted.Value, before, after);
            }

            [Fact]
            public async Task DoesNotInsertNewTimerDisabledFromConfig()
            {
                Timer.Setup(x => x.IsEnabled).Returns(false);

                await Target.ExecuteAsync();

                Assert.Empty(await GetEntitiesAsync<TimerEntity>());
                Timer.Verify(x => x.ExecuteAsync(), Times.Never);
            }

            [Fact]
            public async Task DoesNotExecuteExistingTimerDisabledFromConfig()
            {
                await Target.ExecuteAsync();
                var existingEntity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Timer.Invocations.Clear();
                Timer.Setup(x => x.IsEnabled).Returns(false);

                await Target.ExecuteAsync();

                var newEntity = Assert.Single(await GetEntitiesAsync<TimerEntity>());
                Assert.Equal(existingEntity.ETag, newEntity.ETag);
                Timer.Verify(x => x.ExecuteAsync(), Times.Never);
            }
        }

        public TimerExecutionServiceTest(Fixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            TimerNamePrefix = StorageUtility.GenerateDescendingId().ToString();
            output.WriteLine("Using timer name prefix: " + TimerNamePrefix);
            TimerName = TimerNamePrefix + "-a";
            Timer = new Mock<ITimer>();
            Timers = new List<ITimer> { Timer.Object };

            Timer.Setup(x => x.Name).Returns(TimerName);
            Timer.Setup(x => x.IsEnabled).Returns(true);
            Timer.Setup(x => x.AutoStart).Returns(true);
            Timer.Setup(x => x.Frequency).Returns(TimeSpan.FromMinutes(5));
            Timer.Setup(x => x.ExecuteAsync()).ReturnsAsync(true);
        }

        private readonly Fixture _fixture;
        private readonly ITestOutputHelper _output;

        public string TimerNamePrefix { get; }
        public string TimerName { get; }
        public Mock<ITimer> Timer { get; }
        public List<ITimer> Timers { get; }

        public TimerExecutionService Target
        {
            get
            {
                var serviceClientFactory = _fixture.GetServiceClientFactory(_output.GetLogger<ServiceClientFactory>());
                return new TimerExecutionService(
                    Timers,
                    _fixture.GetLeaseService(serviceClientFactory),
                    new SpecificTimerExecutionService(
                        serviceClientFactory,
                        _fixture.Options.Object,
                        _output.GetTelemetryClient(),
                        _output.GetLogger<SpecificTimerExecutionService>()),
                    _output.GetLogger<TimerExecutionService>());
            }
        }

        protected async Task<IReadOnlyList<T>> GetEntitiesAsync<T>() where T : class, ITableEntity, new()
        {
            var table = await _fixture.GetTableAsync(_output.GetLogger<ServiceClientFactory>());
            return await table
                .QueryAsync<T>(x => x.PartitionKey == TimerExecutionService.PartitionKey
                                 && x.RowKey.CompareTo(TimerNamePrefix) >= 0
                                 && x.RowKey.CompareTo(TimerNamePrefix + char.MaxValue) < 0)
                .ToListAsync();
        }

        public async Task InitializeAsync()
        {
            await _fixture.GetTableAsync(_output.GetLogger<ServiceClientFactory>());
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public class Fixture : IAsyncLifetime
        {
            private bool _created;

            public Fixture()
            {
                Options = new Mock<IOptions<NuGetInsightsSettings>>();
                Settings = new NuGetInsightsSettings
                {
                    StorageConnectionString = TestSettings.StorageConnectionString,
                    TimerTableName = TestSettings.NewStoragePrefix() + "1t1",
                    LeaseContainerName = TestSettings.NewStoragePrefix() + "1l1",
                };
                Options.Setup(x => x.Value).Returns(() => Settings);
            }

            public Mock<IOptions<NuGetInsightsSettings>> Options { get; }
            public NuGetInsightsSettings Settings { get; }

            public Task InitializeAsync()
            {
                return Task.CompletedTask;
            }

            public async Task<TableClient> GetTableAsync(ILogger<ServiceClientFactory> logger)
            {
                var serviceClientFactory = GetServiceClientFactory(logger);
                var table = (await serviceClientFactory.GetTableServiceClientAsync())
                    .GetTableClient(Options.Object.Value.TimerTableName);

                if (!_created)
                {
                    await GetLeaseService(serviceClientFactory).InitializeAsync();
                    await table.CreateIfNotExistsAsync(retry: true);
                    _created = true;
                }

                return table;
            }

            public AutoRenewingStorageLeaseService GetLeaseService(ServiceClientFactory serviceClientFactory)
            {
                return new AutoRenewingStorageLeaseService(new StorageLeaseService(serviceClientFactory, Options.Object));
            }

            public ServiceClientFactory GetServiceClientFactory(ILogger<ServiceClientFactory> logger)
            {
                return new ServiceClientFactory(Options.Object, logger);
            }

            public async Task DisposeAsync()
            {
                var logger = NullLogger<ServiceClientFactory>.Instance;

                var blobServiceClient = await GetServiceClientFactory(logger).GetBlobServiceClientAsync();
                await blobServiceClient
                    .GetBlobContainerClient(Options.Object.Value.LeaseContainerName)
                    .DeleteIfExistsAsync();

                var table = await GetTableAsync(logger);
                await table.DeleteAsync();
            }
        }
    }
}
