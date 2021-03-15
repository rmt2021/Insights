﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;

namespace Knapcode.ExplorePackages
{
    public class TimerExecutionService
    {
        public static readonly string PartitionKey = string.Empty;

        private readonly IReadOnlyDictionary<string, ITimer> _nameToTimer;
        private readonly ServiceClientFactory _serviceClientFactory;
        private readonly AutoRenewingStorageLeaseService _leaseService;
        private readonly IOptions<ExplorePackagesSettings> _options;
        private readonly ITelemetryClient _telemetryClient;
        private readonly ILogger<TimerExecutionService> _logger;

        public TimerExecutionService(
            ServiceClientFactory serviceClientFactory,
            IEnumerable<ITimer> timers,
            AutoRenewingStorageLeaseService leaseService,
            IOptions<ExplorePackagesSettings> options,
            ITelemetryClient telemetryClient,
            ILogger<TimerExecutionService> logger)
        {
            var timerList = timers.ToList();
            var duplicateNames = timerList
                .GroupBy(x => x.Name)
                .Where(g => g.Count() > 1)
                .Select(g => $"'{g.Key}' ({g.Count()})")
                .ToList();
            if (duplicateNames.Any())
            {
                throw new ArgumentException("There are timers with duplicate names: " + string.Join(", ", duplicateNames));
            }

            _nameToTimer = timerList.ToDictionary(x => x.Name);
            _serviceClientFactory = serviceClientFactory;
            _leaseService = leaseService;
            _options = options;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await _leaseService.InitializeAsync();
            await GetTable().CreateIfNotExistsAsync(retry: true);
            foreach (var timer in _nameToTimer.Values)
            {
                await timer.InitializeAsync();
            }
        }

        public async Task<IReadOnlyList<TimerState>> GetStateAsync()
        {
            var pairs = _nameToTimer.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            
            var isRunningTask = Task.WhenAll(pairs.Select(x => x.Value.IsRunningAsync()));
            var entitiesTask = GetTable().GetEntitiesAsync<TimerEntity>(PartitionKey, _telemetryClient.StartQueryLoopMetrics());

            await Task.WhenAll(isRunningTask, entitiesTask);

            var nameToEntity = (await entitiesTask).ToDictionary(x => x.RowKey);

            return pairs
                .Zip(await isRunningTask, (pair, isRunning) =>
                {
                    nameToEntity.TryGetValue(pair.Key, out var entity);

                    return new TimerState
                    {
                        Name = pair.Key,
                        IsRunning = isRunning,
                        IsEnabledInConfig = pair.Value.IsEnabled,
                        IsEnabledInStorage = entity?.IsEnabled,
                        LastExecuted = entity?.LastExecuted,
                        Frequency = pair.Value.Frequency,
                    };
                })
                .ToList();
        }

        public async Task SetIsEnabled(string timerName, bool isEnabled)
        {
            GetTimer(timerName);
            var table = GetTable();
            var entity = new TimerEntity(timerName) { IsEnabled = isEnabled };
            await table.ExecuteAsync(TableOperation.InsertOrMerge(entity));
        }

        private ITimer GetTimer(string timerName)
        {
            if (!_nameToTimer.TryGetValue(timerName, out var timer))
            {
                throw new ArgumentException("The provided timer name is not recognized.", nameof(timerName));
            }

            return timer;
        }

        public async Task ExecuteNowAsync(string timerName)
        {
            await ExecuteAsync(new HashSet<string> { timerName }, isEnabledDefault: true, executeNow: true);
        }

        public async Task ExecuteAsync(bool isEnabledDefault)
        {
            await using (var lease = await _leaseService.TryAcquireAsync(nameof(TimerExecutionService)))
            {
                if (!lease.Acquired)
                {
                    _logger.LogInformation("Another thread is executing this method.");
                    return;
                }

                await ExecuteAsync(timerNames: null, isEnabledDefault, executeNow: false);
            }
        }

        private async Task ExecuteAsync(ISet<string> timerNames, bool isEnabledDefault, bool executeNow)
        {
            if (timerNames != null)
            {
                // Validate the provided timer names.
                foreach (var timerName in timerNames)
                {
                    GetTimer(timerName);
                }
            }

            // Get the existing timer entities.
            var table = GetTable();
            var entities = await table.GetEntitiesAsync<TimerEntity>(
                PartitionKey,
                _telemetryClient.StartQueryLoopMetrics());
            var nameToEntity = entities.ToDictionary(x => x.RowKey);

            // Determine what to do for each timer.
            var toExecute = new List<(ITimer timer, TimerEntity entity)>();
            var batch = new TableBatchOperation();
            foreach (var timer in _nameToTimer.Values)
            {
                if (timerNames != null && !timerNames.Contains(timer.Name))
                {
                    continue;
                }

                if (!timer.IsEnabled)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it is disabled in config.", timer.Name);
                }
                else if (!nameToEntity.TryGetValue(timer.Name, out var entity))
                {
                    entity = new TimerEntity(timer.Name) { IsEnabled = isEnabledDefault };
                    batch.Insert(entity);

                    if (isEnabledDefault)
                    {
                        toExecute.Add((timer, entity));
                        _logger.LogInformation("Timer {Name} will be run for the first time.", timer.Name);
                    }
                    else
                    {
                        _logger.LogInformation("Timer {Name} will be initialized without running.", timer.Name);
                    }
                }
                else if (!entity.IsEnabled)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it is disabled in storage.", timer.Name);
                }
                else if (!entity.LastExecuted.HasValue)
                {
                    _logger.LogInformation("Timer {Name} will be run because it has never been run before.", timer.Name);
                    toExecute.Add((timer, entity));
                    batch.Replace(entity);
                }
                else if (!executeNow && (DateTimeOffset.UtcNow - entity.LastExecuted.Value) < timer.Frequency)
                {
                    _logger.LogInformation("Timer {Name} will not be run because it has been executed too recently.", timer.Name);
                }
                else
                {
                    _logger.LogInformation("Timer {Name} will be run because it has hasn't been run recently enough.", timer.Name);
                    toExecute.Add((timer, entity));
                    batch.Replace(entity);
                }
            }

            // Execute all timers.
            await Task.WhenAll(toExecute.Select(x => ExecuteAsync(x.timer, x.entity)));

            if (batch.Count > 0)
            {
                // Update table storage after the execute. In other words, if Table Storage fails, we could run the timers
                // too frequently.
                await table.ExecuteBatchAsync(batch);
            }
        }

        private async Task ExecuteAsync(ITimer timer, TimerEntity entity)
        {
            try
            {
                await timer.ExecuteAsync();
                _logger.LogInformation("Timer {Name} was executed successfully.", timer.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timer {Name} failed to execute.", timer.Name);
            }

            entity.LastExecuted = DateTimeOffset.UtcNow;
        }

        private CloudTable GetTable()
        {
            return _serviceClientFactory
                .GetStorageAccount()
                .CreateCloudTableClient()
                .GetTableReference(_options.Value.TimerTableName);
        }
    }
}
