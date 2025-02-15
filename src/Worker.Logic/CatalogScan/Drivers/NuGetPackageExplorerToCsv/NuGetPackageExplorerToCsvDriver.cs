// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGetPe;

namespace NuGet.Insights.Worker.NuGetPackageExplorerToCsv
{
    public class NuGetPackageExplorerToCsvDriver :
        ICatalogLeafToCsvDriver<NuGetPackageExplorerRecord, NuGetPackageExplorerFile>,
        ICsvResultStorage<NuGetPackageExplorerRecord>,
        ICsvResultStorage<NuGetPackageExplorerFile>
    {
        private readonly CatalogClient _catalogClient;
        private readonly FlatContainerClient _flatContainerClient;
        private readonly HttpSource _httpSource;
        private readonly HttpClient _httpClient;
        private readonly IOptions<NuGetInsightsWorkerSettings> _options;
        private readonly ILogger<NuGetPackageExplorerToCsvDriver> _logger;

        public NuGetPackageExplorerToCsvDriver(
            CatalogClient catalogClient,
            FlatContainerClient flatContainerClient,
            HttpSource httpSource,
            HttpClient httpClient,
            IOptions<NuGetInsightsWorkerSettings> options,
            ILogger<NuGetPackageExplorerToCsvDriver> logger)
        {
            _catalogClient = catalogClient;
            _flatContainerClient = flatContainerClient;
            _httpSource = httpSource;
            _httpClient = httpClient;
            _options = options;
            _logger = logger;
        }

        public bool SingleMessagePerId => false;
        string ICsvResultStorage<NuGetPackageExplorerRecord>.ResultContainerName => _options.Value.NuGetPackageExplorerContainerName;
        string ICsvResultStorage<NuGetPackageExplorerFile>.ResultContainerName => _options.Value.NuGetPackageExplorerFileContainerName;

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<(ICatalogLeafItem LeafItem, string PageUrl)> MakeReprocessItemOrNullAsync(NuGetPackageExplorerRecord record)
        {
            throw new NotImplementedException();
        }

        public Task<(ICatalogLeafItem LeafItem, string PageUrl)> MakeReprocessItemOrNullAsync(NuGetPackageExplorerFile record)
        {
            throw new NotImplementedException();
        }

        public async Task<DriverResult<CsvRecordSets<NuGetPackageExplorerRecord, NuGetPackageExplorerFile>>> ProcessLeafAsync(CatalogLeafScan leafScan)
        {
            (var record, var files) = await ProcessLeafInternalAsync(leafScan);
            var bucketKey = PackageRecord.GetBucketKey(leafScan);
            return DriverResult.Success(new CsvRecordSets<NuGetPackageExplorerRecord, NuGetPackageExplorerFile>(
                new CsvRecordSet<NuGetPackageExplorerRecord>(bucketKey, record != null ? new[] { record } : Array.Empty<NuGetPackageExplorerRecord>()),
                new CsvRecordSet<NuGetPackageExplorerFile>(bucketKey, files ?? Array.Empty<NuGetPackageExplorerFile>())));
        }

        private async Task<(NuGetPackageExplorerRecord, IReadOnlyList<NuGetPackageExplorerFile>)> ProcessLeafInternalAsync(CatalogLeafScan leafScan)
        {
            var scanId = Guid.NewGuid();
            var scanTimestamp = DateTimeOffset.UtcNow;

            if (leafScan.LeafType == CatalogLeafType.PackageDelete)
            {
                var leaf = (PackageDeleteCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(leafScan.LeafType, leafScan.Url);
                return (
                    new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf),
                    new[] { new NuGetPackageExplorerFile(scanId, scanTimestamp, leaf) }
                );
            }
            else
            {
                var leaf = (PackageDetailsCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(leafScan.LeafType, leafScan.Url);

                var tempDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "npe"));
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var tempPath = Path.Combine(tempDir, StorageUtility.GenerateDescendingId().ToString() + ".nupkg");
                try
                {
                    var exists = await DownloadToFileAsync(leaf, leafScan.AttemptCount, tempPath);
                    if (!exists)
                    {
                        // Ignore packages where the .nupkg is missing. A subsequent scan will produce a deleted record.
                        return (null, null);
                    }

                    _logger.LogInformation(
                        "Loading ZIP package for {Id} {Version} on attempt {AttemptCount}.",
                        leaf.PackageId,
                        leaf.PackageVersion,
                        leafScan.AttemptCount);

                    ZipPackage zipPackage;
                    try
                    {
                        zipPackage = new ZipPackage(tempPath);
                    }
                    catch (Exception ex) when (ex is InvalidDataException
                                            || ex is ArgumentException
                                            || ex is PackagingException
                                            || ex is XmlException
                                            || ex is InvalidOperationException
                                            || ex.Message.Contains("Enabling license acceptance requires a license or a licenseUrl to be specified.")
                                            || ex.Message.Contains("Authors is required.")
                                            || ex.Message.Contains("Description is required.")
                                            || ex.Message.Contains("Url cannot be empty.")
                                            || (ex.Message.Contains("Assembly reference ") && ex.Message.Contains(" contains invalid characters.")))
                    {
                        _logger.LogWarning(ex, "Package {Id} {Version} had invalid metadata.", leaf.PackageId, leaf.PackageVersion);
                        return MakeSingleItem(scanId, scanTimestamp, leaf, NuGetPackageExplorerResultType.InvalidMetadata);
                    }

                    using (zipPackage)
                    {
                        var symbolValidator = new SymbolValidator(zipPackage, zipPackage.Source, rootFolder: null, _httpClient);

                        SymbolValidatorResult symbolValidatorResult;
                        using (var cts = new CancellationTokenSource())
                        {
                            var delayTask = Task.Delay(TimeSpan.FromMinutes(4), cts.Token);
                            _logger.LogInformation(
                                "Starting symbol validation for {Id} {Version} on attempt {AttemptCount}.",
                                leaf.PackageId,
                                leaf.PackageVersion,
                                leafScan.AttemptCount);
                            var symbolValidatorTask = symbolValidator.Validate(cts.Token);

                            var resultTask = await Task.WhenAny(delayTask, symbolValidatorTask);
                            if (resultTask == delayTask)
                            {
                                cts.Cancel();

                                if (leafScan.AttemptCount > 3)
                                {
                                    _logger.LogWarning("Package {Id} {Version} had its symbol validation timeout.", leaf.PackageId, leaf.PackageVersion);
                                    return MakeSingleItem(scanId, scanTimestamp, leaf, NuGetPackageExplorerResultType.Timeout);
                                }
                                else
                                {
                                    throw new TimeoutException("The NuGetPackageExplorer symbol validator task timed out.");
                                }
                            }
                            else
                            {
                                symbolValidatorResult = await symbolValidatorTask;
                                cts.Cancel();
                            }
                        }

                        _logger.LogInformation(
                            "Loading signature data for {Id} {Version} on attempt {AttemptCount}.",
                            leaf.PackageId,
                            leaf.PackageVersion,
                            leafScan.AttemptCount);

                        await zipPackage.LoadSignatureDataAsync();

                        using var fileStream = zipPackage.GetStream();
                        var record = new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                        {
                            SourceLinkResult = symbolValidatorResult.SourceLinkResult,
                            DeterministicResult = symbolValidatorResult.DeterministicResult,
                            CompilerFlagsResult = symbolValidatorResult.CompilerFlagsResult,
                            IsSignedByAuthor = zipPackage.PublisherSignature != null,
                        };

                        var files = new List<NuGetPackageExplorerFile>();

                        try
                        {
                            _logger.LogInformation(
                                "Getting all files for {Id} {Version} on attempt {AttemptCount}.",
                                leaf.PackageId,
                                leaf.PackageVersion,
                                leafScan.AttemptCount);

                            foreach (var file in symbolValidator.GetAllFiles())
                            {
                                var compilerFlags = file.DebugData?.CompilerFlags.ToDictionary(k => k.Key, v => v.Value);

                                var sourceUrls = file.DebugData?.Sources.Where(x => x.Url != null).Select(x => x.Url);
                                var sourceUrlRepoInfo = sourceUrls != null ? SourceUrlRepoParser.GetSourceRepoInfo(sourceUrls) : null;

                                files.Add(new NuGetPackageExplorerFile(scanId, scanTimestamp, leaf)
                                {
                                    Path = file.Path,
                                    Extension = file.Extension,
                                    HasCompilerFlags = file.DebugData?.HasCompilerFlags,
                                    HasSourceLink = file.DebugData?.HasSourceLink,
                                    HasDebugInfo = file.DebugData?.HasDebugInfo,
                                    PdbType = file.DebugData?.PdbType,
                                    CompilerFlags = compilerFlags != null ? JsonSerializer.Serialize(compilerFlags, JsonSerializerOptions) : null,
                                    SourceUrlRepoInfo = sourceUrlRepoInfo != null ? JsonSerializer.Serialize(sourceUrlRepoInfo, JsonSerializerOptions) : null,
                                });
                            }
                        }
                        catch (Exception ex) when (ex is FileNotFoundException || ex is FormatException)
                        {
                            _logger.LogWarning(ex, "Could not get symbol validator files for {Id} {Version}.", leaf.PackageId, leaf.PackageVersion);
                            return MakeSingleItem(scanId, scanTimestamp, leaf, NuGetPackageExplorerResultType.InvalidMetadata);
                        }

                        if (files.Count == 0)
                        {
                            record.ResultType = NuGetPackageExplorerResultType.NothingToValidate;

                            // Add a marker "nothing to validate" record to the files table so that all tables have the
                            // same set of identities.
                            files.Add(new NuGetPackageExplorerFile(scanId, scanTimestamp, leaf)
                            {
                                ResultType = NuGetPackageExplorerResultType.NothingToValidate,
                            });
                        }

                        return (record, files);
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch (Exception ex)
                        {
                            // Best effort.
                            _logger.LogError(ex, "Could not delete {TempPath} during NuGet Package Explorer clean up.", tempPath);
                        }
                    }
                }
            }
        }

        private async Task<bool> DownloadToFileAsync(PackageDetailsCatalogLeaf leaf, int attemptCount, string path)
        {
            var contentUrl = await _flatContainerClient.GetPackageContentUrlAsync(leaf.PackageId, leaf.PackageVersion);
            var nuGetLogger = _logger.ToNuGetLogger();

            _logger.LogInformation(
                "Downloading .nupkg for {Id} {Version} on attempt {AttemptCount}.",
                leaf.PackageId,
                leaf.PackageVersion,
                attemptCount);

            return await _httpSource.ProcessResponseAsync(
                new HttpSourceRequest(contentUrl, nuGetLogger) { IgnoreNotFounds = true },
                async response =>
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return false;
                    }

                    response.EnsureSuccessStatusCode();

                    var length = response.Content.Headers.ContentLength.Value;

                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var hash = IncrementalHash.CreateAll())
                    using (var destination = new FileStream(
                        path,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        bufferSize: 4096,
                        FileOptions.Asynchronous))
                    {
                        await destination.SetLengthAndWriteAsync(length);
                        await source.CopyToSlowAsync(
                            destination,
                            length,
                            bufferSize: 4 * 1024 * 1024,
                            hashAlgorithm: hash,
                            logger: _logger);
                    }

                    return true;
                },
                nuGetLogger,
                CancellationToken.None);
        }

        private static (NuGetPackageExplorerRecord, NuGetPackageExplorerFile[]) MakeSingleItem(
            Guid scanId,
            DateTimeOffset scanTimestamp,
            PackageDetailsCatalogLeaf leaf,
            NuGetPackageExplorerResultType type)
        {
            return (
                new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf) { ResultType = type },
                new[] { new NuGetPackageExplorerFile(scanId, scanTimestamp, leaf) { ResultType = type } }
            );
        }

        public List<NuGetPackageExplorerRecord> Prune(List<NuGetPackageExplorerRecord> records, bool isFinalPrune)
        {
            return PackageRecord.Prune(records, isFinalPrune);
        }

        public List<NuGetPackageExplorerFile> Prune(List<NuGetPackageExplorerFile> records, bool isFinalPrune)
        {
            return PackageRecord.Prune(records, isFinalPrune);
        }

        private static JsonSerializerOptions JsonSerializerOptions { get; } = new JsonSerializerOptions
        {
            Converters =
            {
                new SourceUrlRepoJsonConverter(),
            },
        };

        private class SourceUrlRepoJsonConverter : JsonConverter<SourceUrlRepo>
        {
            public override SourceUrlRepo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, SourceUrlRepo value, JsonSerializerOptions options)
            {
                // Each non-abstract class descending from SourceUrlRepo should be in this switch to allow serialization.
                switch (value)
                {
                    case GitHubSourceRepo gitHub:
                        JsonSerializer.Serialize(writer, gitHub, options);
                        break;
                    case InvalidSourceRepo invalid:
                        JsonSerializer.Serialize(writer, invalid, options);
                        break;
                    case UnknownSourceRepo unknown:
                        JsonSerializer.Serialize(writer, unknown, options);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}
