// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NuGet.Insights.Worker.LoadSymbolPackageArchive
{
    public class LoadSymbolPackageArchiveDriver : ICatalogLeafScanBatchDriver
    {
        private readonly SymbolPackageFileService _symbolPackageFileService;
        private readonly ILogger<LoadSymbolPackageArchiveDriver> _logger;

        public LoadSymbolPackageArchiveDriver(SymbolPackageFileService symbolPackageFileService, ILogger<LoadSymbolPackageArchiveDriver> logger)
        {
            _symbolPackageFileService = symbolPackageFileService;
            _logger = logger;
        }

        public async Task InitializeAsync(CatalogIndexScan indexScan)
        {
            await _symbolPackageFileService.InitializeAsync();
        }

        public Task<CatalogIndexScanResult> ProcessIndexAsync(CatalogIndexScan indexScan)
        {

            return Task.FromResult(CatalogIndexScanResult.ExpandLatestLeaves);
        }

        public Task<CatalogPageScanResult> ProcessPageAsync(CatalogPageScan pageScan)
        {
            throw new NotImplementedException();
        }

        public async Task<BatchMessageProcessorResult<CatalogLeafScan>> ProcessLeavesAsync(IReadOnlyList<CatalogLeafScan> leafScans)
        {
            var failed = new List<CatalogLeafScan>();

            foreach (var group in leafScans.GroupBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                var leafItems = group.Cast<ICatalogLeafItem>().ToList();
                try
                {
                    await _symbolPackageFileService.UpdateBatchAsync(group.Key, leafItems);
                }
                catch (Exception ex) when (leafScans.Count != 1)
                {
                    _logger.LogError(ex, "Updating symbol package file info failed for {Id} with {Count} versions.", group.Key, leafItems.Count);
                    failed.AddRange(group);
                }
            }

            return new BatchMessageProcessorResult<CatalogLeafScan>(failed);
        }

        public Task StartAggregateAsync(CatalogIndexScan indexScan)
        {
            return Task.CompletedTask;
        }

        public Task<bool> IsAggregateCompleteAsync(CatalogIndexScan indexScan)
        {
            return Task.FromResult(true);
        }

        public Task FinalizeAsync(CatalogIndexScan indexScan)
        {
            return Task.CompletedTask;
        }

        public Task StartCustomExpandAsync(CatalogIndexScan indexScan)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsCustomExpandCompleteAsync(CatalogIndexScan indexScan)
        {
            throw new NotImplementedException();
        }
    }
}
