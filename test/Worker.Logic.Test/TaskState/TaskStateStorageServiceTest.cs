// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights.Worker
{
    public class TaskStateStorageServiceTest : IClassFixture<TaskStateStorageServiceTest.Fixture>, IAsyncLifetime
    {
        public class TheGetAsyncMethod : TaskStateStorageServiceTest
        {
            public TheGetAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public async Task ReturnsNullForMissingTaskState()
            {
                var result = await Target.GetAsync(new TaskStateKey(StorageSuffix, PartitionKey, "foo"));

                Assert.Null(result);
            }
        }

        public class TheAddAsyncMethod : TaskStateStorageServiceTest
        {
            public TheAddAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public async Task CanAddMoreThan100TaskStatesAtOnce()
            {
                var rowKeys = Enumerable.Range(100, 101).Select(x => x.ToString()).ToList();

                await Target.AddAsync(StorageSuffix, PartitionKey, rowKeys);

                var taskStates = await GetEntitiesAsync<TaskState>();
                Assert.Equal(rowKeys, taskStates.Select(x => x.RowKey).ToList());
                Assert.All(taskStates, x =>
                {
                    Assert.Equal(StorageSuffix, x.StorageSuffix);
                    Assert.Equal(PartitionKey, x.PartitionKey);
                    Assert.Null(x.Parameters);
                    Assert.NotEqual(default, x.ETag);
                });
            }

            [Fact]
            public async Task DoesNotConflictWithExisting()
            {
                var rowKeysA = Enumerable.Range(10, 10).Select(x => x.ToString()).ToList();
                var rowKeysB = Enumerable.Range(10, 20).Select(x => x.ToString()).ToList();

                await Target.AddAsync(StorageSuffix, PartitionKey, rowKeysA);
                await Target.AddAsync(StorageSuffix, PartitionKey, rowKeysB);

                var taskStates = await GetEntitiesAsync<TaskState>();
                Assert.Equal(rowKeysB, taskStates.Select(x => x.RowKey).ToList());
                Assert.All(taskStates, x =>
                {
                    Assert.Equal(StorageSuffix, x.StorageSuffix);
                    Assert.Equal(PartitionKey, x.PartitionKey);
                    Assert.Null(x.Parameters);
                    Assert.NotEqual(default, x.ETag);
                });
            }

            [Fact]
            public async Task HasExpectedPropertiesWithoutParameters()
            {
                await Target.AddAsync(new TaskStateKey(StorageSuffix, PartitionKey, "foo"));

                var entities = await GetEntitiesAsync<TableEntity>();
                var entity = Assert.Single(entities);
                Assert.Equal(new[] { "odata.etag", "PartitionKey", "RowKey", "StorageSuffix", "Timestamp" }, entity.Keys.OrderBy(x => x).ToArray());
            }

            [Fact]
            public async Task HasExpectedPropertiesWithParameters()
            {
                await Target.AddAsync(StorageSuffix, PartitionKey, new[] { new TaskState(StorageSuffix, PartitionKey, "bar") { Parameters = "baz" } });

                var entities = await GetEntitiesAsync<TableEntity>();
                var entity = Assert.Single(entities);
                Assert.Equal(new[] { "odata.etag", "Parameters", "PartitionKey", "RowKey", "StorageSuffix", "Timestamp" }, entity.Keys.OrderBy(x => x).ToArray());
            }

            [Fact]
            public async Task CanAddTaskStateInstance()
            {
                var input = new TaskState(StorageSuffix, PartitionKey, "foo");

                await Target.AddAsync(input);

                var output = await Target.GetAsync(input.GetKey());
                Assert.Equal(output.ETag, input.ETag);
            }
        }

        public class TheUpdateAsyncMethod : TaskStateStorageServiceTest
        {
            public TheUpdateAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public async Task UpdatesContent()
            {
                var input = new TaskState(StorageSuffix, PartitionKey, "foo") { Parameters = "a" };
                await Target.AddAsync(input);
                input.Parameters = "b";

                await Target.UpdateAsync(input);

                var output = await Target.GetAsync(input.GetKey());
                Assert.Equal(output.ETag, input.ETag);
                Assert.Equal("b", output.Parameters);
            }

            [Fact]
            public async Task CanClearParameters()
            {
                var input = new TaskState(StorageSuffix, PartitionKey, "foo") { Parameters = "a" };
                await Target.AddAsync(input);
                input.Parameters = null;

                await Target.UpdateAsync(input);

                var output = await Target.GetAsync(input.GetKey());
                Assert.Null(output.Parameters);
            }

            [Fact]
            public async Task FailsIfETagChanged()
            {
                var input = new TaskState(StorageSuffix, PartitionKey, "foo") { Parameters = "a" };
                await Target.AddAsync(input);
                await Target.UpdateAsync(new TaskState(StorageSuffix, PartitionKey, "foo") { Parameters = "a", ETag = input.ETag });
                input.Parameters = "b";

                var ex = await Assert.ThrowsAsync<RequestFailedException>(() => Target.UpdateAsync(input));
                Assert.Equal(HttpStatusCode.PreconditionFailed, (HttpStatusCode)ex.Status);
            }

            [Fact]
            public async Task FailsIfDeleted()
            {
                var input = new TaskState(StorageSuffix, PartitionKey, "foo") { Parameters = "a" };
                await Target.AddAsync(input);
                await Target.DeleteAsync(input);

                var ex = await Assert.ThrowsAsync<RequestFailedException>(() => Target.UpdateAsync(input));
                Assert.Equal(HttpStatusCode.NotFound, (HttpStatusCode)ex.Status);
            }
        }

        public class TheGetByRowKeyPrefixAsyncMethod : TaskStateStorageServiceTest
        {
            public TheGetByRowKeyPrefixAsyncMethod(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
            {
            }

            [Fact]
            public async Task ReturnsNothingWhenNoMatch()
            {
                await Target.AddAsync(new TaskState(StorageSuffix, PartitionKey, "foo"));

                var actual = await Target.GetByRowKeyPrefixAsync(StorageSuffix, PartitionKey, "bar");

                Assert.Empty(actual);
            }

            [Fact]
            public async Task AllowsEmptyPrefix()
            {
                await Target.AddAsync(new TaskState(StorageSuffix, PartitionKey, "foo"));

                var actual = await Target.GetByRowKeyPrefixAsync(StorageSuffix, PartitionKey, "");

                Assert.Equal("foo", Assert.Single(actual).RowKey);
            }

            [Fact]
            public async Task ReturnsAllMatches()
            {
                var rowKeys = Enumerable.Range(0, 10).Select(x => $"foo{x}").ToList();
                await Target.AddAsync(StorageSuffix, PartitionKey, rowKeys);

                var actual = await Target.GetByRowKeyPrefixAsync(StorageSuffix, PartitionKey, "foo");

                Assert.Equal(rowKeys, actual.Select(x => x.RowKey).ToList());
                Assert.All(actual, x => Assert.Equal(x.PartitionKey, PartitionKey));
                Assert.All(actual, x => Assert.Equal(x.StorageSuffix, StorageSuffix));
            }
        }

        public TaskStateStorageServiceTest(Fixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            PartitionKey = StorageUtility.GenerateDescendingId().ToString();
        }

        private readonly Fixture _fixture;
        private readonly ITestOutputHelper _output;

        public string StorageSuffix => _fixture.StorageSuffix;
        public string PartitionKey { get; }
        public TaskStateStorageService Target => new TaskStateStorageService(
            _fixture.GetServiceClientFactory(_output.GetLogger<ServiceClientFactory>()),
            _output.GetTelemetryClient(),
            _fixture.Options.Object);

        protected async Task<IReadOnlyList<T>> GetEntitiesAsync<T>() where T : class, ITableEntity, new()
        {
            var table = await _fixture.GetTableAsync(_output.GetLogger<ServiceClientFactory>());
            return await table
                .QueryAsync<T>(x => x.PartitionKey == PartitionKey)
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
            public bool _created;

            public Fixture()
            {
                Options = new Mock<IOptions<NuGetInsightsWorkerSettings>>();
                Settings = new NuGetInsightsWorkerSettings
                {
                    StorageConnectionString = TestSettings.StorageConnectionString,
                    TaskStateTableName = TestSettings.NewStoragePrefix() + "1ts1",
                };
                Options.Setup(x => x.Value).Returns(() => Settings);
                StorageSuffix = "suffix";
            }

            public Mock<IOptions<NuGetInsightsWorkerSettings>> Options { get; }
            public NuGetInsightsWorkerSettings Settings { get; }
            public string StorageSuffix { get; }

            public Task InitializeAsync()
            {
                return Task.CompletedTask;
            }

            public ServiceClientFactory GetServiceClientFactory(ILogger<ServiceClientFactory> logger)
            {
                return new ServiceClientFactory(Options.Object, logger);
            }

            public async Task DisposeAsync()
            {
                await (await GetTableAsync(NullLogger<ServiceClientFactory>.Instance)).DeleteAsync();
            }

            public async Task<TableClient> GetTableAsync(ILogger<ServiceClientFactory> logger)
            {
                var table = (await GetServiceClientFactory(logger).GetTableServiceClientAsync())
                    .GetTableClient(Options.Object.Value.TaskStateTableName + StorageSuffix);

                if (!_created)
                {
                    await table.CreateIfNotExistsAsync();
                    _created = true;
                }

                return table;
            }
        }
    }
}
