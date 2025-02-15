// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NuGet.Insights
{
    public class SpecificTimerExecutionService
    {
        public static readonly string PartitionKey = string.Empty;

        private readonly ServiceClientFactory _serviceClientFactory;
        private readonly IOptions<NuGetInsightsSettings> _options;
        private readonly ITelemetryClient _telemetryClient;
        private readonly ILogger<SpecificTimerExecutionService> _logger;

        public SpecificTimerExecutionService(
            ServiceClientFactory serviceClientFactory,
            IOptions<NuGetInsightsSettings> options,
            ITelemetryClient telemetryClient,
            ILogger<SpecificTimerExecutionService> logger)
        {
            _serviceClientFactory = serviceClientFactory;
            _options = options;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        public async Task InitializeAsync(IEnumerable<ITimer> timers)
        {
            await (await GetTableAsync()).CreateIfNotExistsAsync(retry: true);
            foreach (var timer in timers)
            {
                await timer.InitializeAsync();
            }
        }

        public async Task SetIsEnabledAsync(ITimer timer, bool isEnabled)
        {
            var table = await GetTableAsync();
            var entity = new TimerEntity(timer.Name) { IsEnabled = isEnabled };
            await table.UpsertEntityAsync(entity);
        }

        public async Task<IReadOnlyList<TimerState>> GetStateAsync(IEnumerable<ITimer> timers)
        {
            var pairs = timers
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var isRunningTask = Task.WhenAll(pairs.Select(x => x.IsRunningAsync()));
            var table = await GetTableAsync();
            var entitiesTask = table
                .QueryAsync<TimerEntity>(e => e.PartitionKey == PartitionKey)
                .ToListAsync(_telemetryClient.StartQueryLoopMetrics());

            await Task.WhenAll(isRunningTask, entitiesTask);

            var nameToEntity = (await entitiesTask).ToDictionary(x => x.RowKey);

            return pairs
                .Zip(await isRunningTask, (pair, isRunning) =>
                {
                    nameToEntity.TryGetValue(pair.Name, out var entity);

                    return new TimerState
                    {
                        Name = pair.Name,
                        IsRunning = isRunning,
                        IsEnabledInConfig = pair.IsEnabled,
                        IsEnabledInStorage = entity?.IsEnabled ?? pair.AutoStart,
                        LastExecuted = entity?.LastExecuted,
                        Frequency = pair.Frequency,
                    };
                })
                .ToList();
        }

        public async Task<bool> ExecuteAsync(IEnumerable<ITimer> timers, bool executeNow)
        {
            // Get the existing timer entities.
            var table = await GetTableAsync();
            var entities = await table.QueryAsync<TimerEntity>(x => x.PartitionKey == PartitionKey).ToListAsync(_telemetryClient.StartQueryLoopMetrics());
            var nameToEntity = entities.ToDictionary(x => x.RowKey);

            // Determine what to do for each timer.
            var toExecute = new List<(ITimer timer, TimerEntity entity, Func<Task> persistAsync)>();
            var now = DateTimeOffset.UtcNow;
            foreach (var timer in timers)
            {
                if (!timer.IsEnabled)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it is disabled in config.", timer.Name);
                }
                else if (!nameToEntity.TryGetValue(timer.Name, out var entity))
                {
                    entity = new TimerEntity(timer.Name) { IsEnabled = timer.AutoStart };

                    if (executeNow || entity.IsEnabled)
                    {
                        toExecute.Add((
                            timer,
                            entity,
                            () => table.AddEntityAsync(entity)));
                        _logger.LogInformation("Timer {Name} will be run for the first time.", timer.Name);
                    }
                    else
                    {
                        _logger.LogInformation("Timer {Name} will be initialized without running.", timer.Name);
                        await table.AddEntityAsync(entity);
                    }
                }
                else if (executeNow)
                {
                    _logger.LogInformation("Timer {Name} will be run because it being run on demand.", timer.Name);
                    toExecute.Add((
                        timer,
                        entity,
                        () => table.UpdateEntityAsync(entity, entity.ETag, mode: TableUpdateMode.Replace)));
                }
                else if (!entity.IsEnabled)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it is disabled in storage.", timer.Name);
                }
                else if (!entity.LastExecuted.HasValue)
                {
                    _logger.LogInformation("Timer {Name} will be run because it has never been run before.", timer.Name);
                    toExecute.Add((
                        timer,
                        entity,
                        () => table.UpdateEntityAsync(entity, entity.ETag, mode: TableUpdateMode.Replace)));
                }
                else if ((now - entity.LastExecuted.Value) < timer.Frequency)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it has been executed too recently.", timer.Name);
                }
                else
                {
                    _logger.LogInformation("Timer {Name} will be run because it has hasn't been run recently enough.", timer.Name);
                    toExecute.Add((
                        timer,
                        entity,
                        () => table.UpdateEntityAsync(entity, entity.ETag, mode: TableUpdateMode.Replace)));
                }
            }

            // Execute timers in ordered groups.
            var anyExecuted = false;
            foreach (var group in toExecute.GroupBy(x => x.timer.Order).OrderBy(x => x.Key))
            {
                var executed = await Task.WhenAll(group.Select(x => ExecuteAsync(x.timer, x.entity, x.persistAsync, now)));
                anyExecuted |= executed.Any(x => x);
            }

            return anyExecuted;
        }

        private async Task<bool> ExecuteAsync(ITimer timer, TimerEntity entity, Func<Task> persistAsync, DateTimeOffset now)
        {
            bool executed;
            try
            {
                _telemetryClient.TrackMetric(
                    "Timer.Execute",
                    1,
                    new Dictionary<string, string> { { "Name", entity.GetName() } });

                executed = await timer.ExecuteAsync();
                if (executed)
                {
                    _logger.LogInformation("Timer {Name} was executed successfully.", timer.Name);
                }
                else
                {
                    _logger.LogInformation("Timer {Name} was unable to execute.", timer.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timer {Name} failed with an exception.", timer.Name);
                executed = true; // If a timer fails, still update the timestamp to avoid repeated errors.
            }

            if (executed)
            {
                entity.LastExecuted = now;

                // Update table storage after the execute. In other words, if Table Storage fails, we could run the
                // timer again too frequently.
                await persistAsync();
            }

            return executed;
        }

        private async Task<TableClient> GetTableAsync()
        {
            return (await _serviceClientFactory.GetTableServiceClientAsync())
                .GetTableClient(_options.Value.TimerTableName);
        }
    }
}
