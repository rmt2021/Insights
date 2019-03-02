﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Knapcode.ExplorePackages.Entities;
using Microsoft.Extensions.Logging;

namespace Knapcode.ExplorePackages.Logic
{
    public class MZipCommitProcessor : ICommitProcessor<PackageEntity, PackageEntity, object>
    {
        private readonly MZipStore _mZipStore;
        private readonly IBatchSizeProvider _batchSizeProvider;
        private readonly ILogger<MZipCommitProcessor> _logger;

        public MZipCommitProcessor(
            MZipStore mZipStore,
            IBatchSizeProvider batchSizeProvider,
            ILogger<MZipCommitProcessor> logger)
        {
            _mZipStore = mZipStore;
            _batchSizeProvider = batchSizeProvider;
            _logger = logger;
        }

        public string CursorName => CursorNames.MZips;

        public IReadOnlyList<string> DependencyCursorNames { get; } = new[]
        {
            CursorNames.NuGetOrg.FlatContainer,
        };

        public int BatchSize => _batchSizeProvider.Get(BatchSizeType.MZips);
        public string SerializeProgressToken(object progressToken) => null;
        public object DeserializeProgressToken(string serializedProgressToken) => null;

        public Task<ItemBatch<PackageEntity, object>> InitializeItemsAsync(
            IReadOnlyList<PackageEntity> entities,
            object progressToken,
            CancellationToken token)
        {
            return Task.FromResult(new ItemBatch<PackageEntity, object>(entities));
        }

        public async Task ProcessBatchAsync(IReadOnlyList<PackageEntity> batch)
        {
            foreach (var package in batch)
            {
                var success = await _mZipStore.StoreMZipAsync(
                   package.PackageRegistration.Id,
                   package.Version,
                   CancellationToken.None);

                if (!success)
                {
                    _logger.LogWarning(
                        "The .mzip for package {Id} {Version} could not be found.",
                        package.PackageRegistration.Id,
                        package.Version);
                }
            }
        }
    }
}
