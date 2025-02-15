// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Azure.Data.Tables;

namespace NuGet.Insights.Worker.LoadPackageVersion
{
    public class PackageVersionStorage : ILatestPackageLeafStorage<PackageVersionEntity>
    {
        private readonly CatalogClient _catalogClient;

        public PackageVersionStorage(
            TableClient tableClient,
            CatalogClient catalogClient)
        {
            Table = tableClient;
            _catalogClient = catalogClient;
        }

        public TableClient Table { get; }
        public string CommitTimestampColumnName => nameof(PackageVersionEntity.CommitTimestamp);

        public string GetPartitionKey(ICatalogLeafItem item)
        {
            return PackageVersionEntity.GetPartitionKey(item.PackageId);
        }

        public string GetRowKey(ICatalogLeafItem item)
        {
            return PackageVersionEntity.GetRowKey(item.PackageVersion);
        }

        public async Task<PackageVersionEntity> MapAsync(ICatalogLeafItem item)
        {
            if (item.Type == CatalogLeafType.PackageDelete)
            {
                return new PackageVersionEntity(
                    item,
                    created: null,
                    listed: null,
                    originalVersion: null,
                    semVerType: null);
            }

            var leaf = (PackageDetailsCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(item);

            return new PackageVersionEntity(
                item,
                leaf.Created,
                leaf.IsListed(),
                leaf.VerbatimVersion,
                leaf.GetSemVerType());
        }
    }
}
