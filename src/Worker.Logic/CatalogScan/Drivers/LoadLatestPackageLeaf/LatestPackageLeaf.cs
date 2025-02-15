// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Azure;
using NuGet.Versioning;

namespace NuGet.Insights.Worker.LoadLatestPackageLeaf
{
    public class LatestPackageLeaf : ILatestPackageLeaf, ICatalogLeafItem
    {
        public LatestPackageLeaf()
        {
        }

        public LatestPackageLeaf(ICatalogLeafItem item, int leafRank, int pageRank, string pageUrl)
        {
            PartitionKey = GetPartitionKey(item.PackageId);
            RowKey = GetRowKey(item.PackageVersion);
            Url = item.Url;
            LeafType = item.Type;
            CommitId = item.CommitId;
            CommitTimestamp = item.CommitTimestamp;
            PackageId = item.PackageId;
            PackageVersion = item.PackageVersion;
            LeafRank = leafRank;
            PageRank = pageRank;
            PageUrl = pageUrl;
        }

        public string Url { get; set; }
        public CatalogLeafType LeafType { get; set; }
        public string CommitId { get; set; }
        public DateTimeOffset CommitTimestamp { get; set; }
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public int LeafRank { get; set; }
        public int PageRank { get; set; }
        public string PageUrl { get; set; }

        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        CatalogLeafType ICatalogLeafItem.Type => LeafType;
        DateTimeOffset? IPackageIdentityCommit.CommitTimestamp => CommitTimestamp;

        public string GetLowerId()
        {
            return PartitionKey;
        }

        public string GetLowerVersion()
        {
            return RowKey;
        }

        public static string GetPartitionKey(string id)
        {
            return id.ToLowerInvariant();
        }

        public static string GetRowKey(string version)
        {
            return NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
        }
    }
}
