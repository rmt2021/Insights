// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace NuGet.Insights.Worker.CatalogDataToCsv
{
    public class CatalogDataToCsvDriver :
        ICatalogLeafToCsvDriver<PackageDeprecationRecord, PackageVulnerabilityRecord, CatalogLeafItemRecord>,
        ICsvResultStorage<PackageDeprecationRecord>,
        ICsvResultStorage<PackageVulnerabilityRecord>,
        ICsvResultStorage<CatalogLeafItemRecord>
    {
        private readonly CatalogClient _catalogClient;
        private readonly IOptions<NuGetInsightsWorkerSettings> _options;

        public CatalogDataToCsvDriver(
            CatalogClient catalogClient,
            IOptions<NuGetInsightsWorkerSettings> options)
        {
            _catalogClient = catalogClient;
            _options = options;
        }

        public bool SingleMessagePerId => false;
        string ICsvResultStorage<PackageDeprecationRecord>.ResultContainerName => _options.Value.PackageDeprecationContainerName;
        string ICsvResultStorage<PackageVulnerabilityRecord>.ResultContainerName => _options.Value.PackageVulnerabilityContainerName;
        string ICsvResultStorage<CatalogLeafItemRecord>.ResultContainerName => _options.Value.CatalogLeafItemContainerName;

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task<DriverResult<CsvRecordSets<PackageDeprecationRecord, PackageVulnerabilityRecord, CatalogLeafItemRecord>>> ProcessLeafAsync(
            CatalogLeafScan leafScan)
        {
            (var deprecation, var vulnerabilities, var leafRecord) = await ProcessLeafInternalAsync(leafScan);
            var bucketKey = PackageRecord.GetBucketKey(leafScan);
            return DriverResult.Success(new CsvRecordSets<PackageDeprecationRecord, PackageVulnerabilityRecord, CatalogLeafItemRecord>(
                new CsvRecordSet<PackageDeprecationRecord>(bucketKey, new[] { deprecation }),
                new CsvRecordSet<PackageVulnerabilityRecord>(bucketKey, vulnerabilities),
                new CsvRecordSet<CatalogLeafItemRecord>(bucketKey, new[] { leafRecord })));
        }

        private async Task<(PackageDeprecationRecord, IReadOnlyList<PackageVulnerabilityRecord>, CatalogLeafItemRecord)> ProcessLeafInternalAsync(
            CatalogLeafScan leafScan)
        {
            var scanId = Guid.NewGuid();
            var scanTimestamp = DateTimeOffset.UtcNow;

            if (leafScan.LeafType == CatalogLeafType.PackageDelete)
            {
                var leaf = (PackageDeleteCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(leafScan.LeafType, leafScan.Url);
                return (
                    new PackageDeprecationRecord(scanId, scanTimestamp, leaf),
                    new[] { new PackageVulnerabilityRecord(scanId, scanTimestamp, leaf) },
                    new CatalogLeafItemRecord(leaf, leafScan.PageUrl)
                );
            }
            else
            {
                var leaf = (PackageDetailsCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(leafScan.LeafType, leafScan.Url);
                return (
                    GetDeprecation(scanId, scanTimestamp, leaf),
                    GetVulnerabilities(scanId, scanTimestamp, leaf),
                    new CatalogLeafItemRecord(leaf, leafScan.PageUrl)
                );
            }
        }

        private PackageDeprecationRecord GetDeprecation(Guid scanId, DateTimeOffset scanTimestamp, PackageDetailsCatalogLeaf leaf)
        {
            if (leaf.Deprecation is null)
            {
                return new PackageDeprecationRecord(scanId, scanTimestamp, leaf)
                {
                    ResultType = PackageDeprecationResultType.NotDeprecated,
                };
            }

            return new PackageDeprecationRecord(scanId, scanTimestamp, leaf)
            {
                ResultType = PackageDeprecationResultType.Deprecated,
                Message = leaf.Deprecation.Message,
                Reasons = leaf.Deprecation.Reasons != null ? JsonSerializer.Serialize(leaf.Deprecation.Reasons) : null,
                AlternatePackageId = leaf.Deprecation.AlternatePackage?.Id,
                AlternateVersionRange = leaf.Deprecation.AlternatePackage?.Range,
            };
        }

        private List<PackageVulnerabilityRecord> GetVulnerabilities(Guid scanId, DateTimeOffset scanTimestamp, PackageDetailsCatalogLeaf leaf)
        {
            var output = new List<PackageVulnerabilityRecord>();
            if (leaf.Vulnerabilities is null || leaf.Vulnerabilities.Count == 0)
            {
                output.Add(new PackageVulnerabilityRecord(scanId, scanTimestamp, leaf)
                {
                    ResultType = PackageVulnerabilityResultType.NotVulnerable,
                });
            }
            else
            {
                foreach (var vulnerability in leaf.Vulnerabilities)
                {
                    var match = Regex.Match(vulnerability.AtId, @"#vulnerability/GitHub/(?<GitHubDatabaseKey>\d+)$");
                    if (!match.Success)
                    {
                        throw new InvalidDataException($"The vulnerability @id '{vulnerability.AtId}' value does not have a recognized GitHub database key.");
                    }

                    output.Add(new PackageVulnerabilityRecord(scanId, scanTimestamp, leaf)
                    {
                        ResultType = PackageVulnerabilityResultType.Vulnerable,
                        GitHubDatabaseKey = int.Parse(match.Groups["GitHubDatabaseKey"].Value),
                        AdvisoryUrl = vulnerability.AdvisoryUrl,
                        Severity = int.Parse(vulnerability.Severity),
                    });
                }
            }

            return output;
        }

        public List<PackageDeprecationRecord> Prune(List<PackageDeprecationRecord> records, bool isFinalPrune)
        {
            return PackageRecord.Prune(records, isFinalPrune);
        }

        public List<PackageVulnerabilityRecord> Prune(List<PackageVulnerabilityRecord> records, bool isFinalPrune)
        {
            return PackageRecord.Prune(records, isFinalPrune);
        }

        public List<CatalogLeafItemRecord> Prune(List<CatalogLeafItemRecord> records, bool isFinalPrune)
        {
            return records
                .Distinct()
                .OrderBy(x => x.CommitTimestamp)
                .ThenBy(x => x.Identity, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task<(ICatalogLeafItem LeafItem, string PageUrl)> MakeReprocessItemOrNullAsync(PackageDeprecationRecord record)
        {
            throw new NotImplementedException();
        }

        public Task<(ICatalogLeafItem LeafItem, string PageUrl)> MakeReprocessItemOrNullAsync(PackageVulnerabilityRecord record)
        {
            throw new NotImplementedException();
        }

        public Task<(ICatalogLeafItem LeafItem, string PageUrl)> MakeReprocessItemOrNullAsync(CatalogLeafItemRecord record)
        {
            throw new NotImplementedException();
        }
    }
}
