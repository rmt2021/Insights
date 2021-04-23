﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Knapcode.ExplorePackages.Worker.LoadLatestPackageLeaf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGetPe;

namespace Knapcode.ExplorePackages.Worker.NuGetPackageExplorerToCsv
{
    public class NuGetPackageExplorerToCsvDriver : ICatalogLeafToCsvDriver<NuGetPackageExplorerRecord>, ICsvStorage<NuGetPackageExplorerRecord>
    {
        private readonly CatalogClient _catalogClient;
        private readonly FlatContainerClient _flatContainerClient;
        private readonly HttpSource _httpSource;
        private readonly HttpClient _httpClient;
        private readonly LatestPackageLeafService _latestPackageLeafService;
        private readonly IOptions<ExplorePackagesWorkerSettings> _options;
        private readonly ILogger<NuGetPackageExplorerToCsvDriver> _logger;

        public NuGetPackageExplorerToCsvDriver(
            CatalogClient catalogClient,
            FlatContainerClient flatContainerClient,
            HttpSource httpSource,
            HttpClient httpClient,
            LatestPackageLeafService latestPackageLeafService,
            IOptions<ExplorePackagesWorkerSettings> options,
            ILogger<NuGetPackageExplorerToCsvDriver> logger)
        {
            _catalogClient = catalogClient;
            _flatContainerClient = flatContainerClient;
            _httpSource = httpSource;
            _httpClient = httpClient;
            _latestPackageLeafService = latestPackageLeafService;
            _options = options;
            _logger = logger;
        }

        public bool SingleMessagePerId => false;
        public string ResultsContainerName => _options.Value.NuGetPackageExplorerContainerName;

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task<CatalogLeafItem> MakeReprocessItemOrNullAsync(NuGetPackageExplorerRecord record)
        {
            if (record.ResultType != NuGetPackageExplorerResultType.Failed)
            {
                return null;
            }

            var leaf = await _latestPackageLeafService.GetOrNullAsync(record.Id, record.Version);
            if (leaf == null)
            {
                return null;
            }

            return leaf.ToLeafItem();
        }

        public async Task<DriverResult<CsvRecordSet<NuGetPackageExplorerRecord>>> ProcessLeafAsync(CatalogLeafItem item, int attemptCount)
        {
            var records = await ProcessLeafInternalAsync(item, attemptCount);
            return DriverResult.Success(new CsvRecordSet<NuGetPackageExplorerRecord>(records, PackageRecord.GetBucketKey(item)));
        }

        private async Task<List<NuGetPackageExplorerRecord>> ProcessLeafInternalAsync(CatalogLeafItem item, int attemptCount)
        {
            Guid? scanId = null;
            DateTimeOffset? scanTimestamp = null;
            if (_options.Value.AppendResultUniqueIds)
            {
                scanId = Guid.NewGuid();
                scanTimestamp = DateTimeOffset.UtcNow;
            }

            if (item.Type == CatalogLeafType.PackageDelete)
            {
                var leaf = (PackageDeleteCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(item.Type, item.Url);
                return new List<NuGetPackageExplorerRecord> { new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf) };
            }
            else
            {
                var leaf = (PackageDetailsCatalogLeaf)await _catalogClient.GetCatalogLeafAsync(item.Type, item.Url);

#if !ENABLE_NPE
                // Currently NuGetPackageExplore.Core symbol analysis only works fully on Windows.
                throw new NotSupportedException("This build has the 'ENABLE_NPE' constant and the 'EnableNPE' MSBuild property disabled. This is currently expected when not running on Windows.");
#else
                if (attemptCount > 4)
                {
                    _logger.LogWarning("Package {Id} {Version} failed due to too many attempts.", leaf.PackageId, leaf.PackageVersion);
                    return new List<NuGetPackageExplorerRecord>
                    {
                        new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                        {
                            ResultType = NuGetPackageExplorerResultType.Failed,
                        }
                    };
                }

                var tempDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "npe"));
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var tempPath = Path.Combine(tempDir, StorageUtility.GenerateDescendingId().ToString() + ".nupkg");
                try
                {
                    var contentUrl = await _flatContainerClient.GetPackageContentUrlAsync(leaf.PackageId, leaf.PackageVersion);
                    var nuGetLogger = _logger.ToNuGetLogger();

                    _logger.LogInformation(
                        "Downloading .nupkg for {Id} {Version} on attempt {AttemptCount}.",
                        leaf.PackageId,
                        leaf.PackageVersion,
                        attemptCount);

                    var exists = await _httpSource.ProcessResponseAsync(
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
                            using (var destination = new FileStream(
                                tempPath,
                                FileMode.Create,
                                FileAccess.ReadWrite,
                                FileShare.Read,
                                bufferSize: 4096,
                                FileOptions.Asynchronous))
                            {
                                await destination.SetLengthAndWriteAsync(length);
                                await source.CopyToSlowAsync(
                                    length,
                                    destination,
                                    bufferSize: 4 * 1024 * 1024,
                                    hashAlgorithm: null,
                                    _logger);
                            }

                            return true;
                        },
                        nuGetLogger,
                        CancellationToken.None);

                    if (!exists)
                    {
                        // Ignore packages where the .nupkg is missing. A subsequent scan will produce a deleted record.
                        return new List<NuGetPackageExplorerRecord>();
                    }

                    _logger.LogInformation(
                        "Loading ZIP package for {Id} {Version} on attempt {AttemptCount}.",
                        leaf.PackageId,
                        leaf.PackageVersion,
                        attemptCount);

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
                        return new List<NuGetPackageExplorerRecord>
                        {
                            new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                            {
                                ResultType = NuGetPackageExplorerResultType.InvalidMetadata,
                            }
                        };
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
                                attemptCount);
                            var symbolValidatorTask = symbolValidator.Validate(cts.Token);

                            var resultTask = await Task.WhenAny(delayTask, symbolValidatorTask);
                            if (resultTask == delayTask)
                            {
                                cts.Cancel();

                                if (attemptCount > 3)
                                {
                                    _logger.LogWarning("Package {Id} {Version} had its symbol validation timeout.", leaf.PackageId, leaf.PackageVersion);
                                    return new List<NuGetPackageExplorerRecord>
                                    {
                                        new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                                        {
                                            ResultType = NuGetPackageExplorerResultType.Timeout,
                                        }
                                    };
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
                            attemptCount);

                        await zipPackage.LoadSignatureDataAsync();

                        using var fileStream = zipPackage.GetStream();
                        var baseRecord = new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                        {
                            PackageSize = fileStream.Length,
                            SourceLinkResult = symbolValidatorResult.SourceLinkResult,
                            DeterministicResult = symbolValidatorResult.DeterministicResult,
                            CompilerFlagsResult = symbolValidatorResult.CompilerFlagsResult,
                            IsSignedByAuthor = zipPackage.PublisherSignature != null,
                        };

                        var output = new List<NuGetPackageExplorerRecord>();

                        try
                        {
                            _logger.LogInformation(
                                "Getting all files for {Id} {Version} on attempt {AttemptCount}.",
                                leaf.PackageId,
                                leaf.PackageVersion,
                                attemptCount);

                            foreach (var file in symbolValidator.GetAllFiles())
                            {
                                var compilerFlags = file.DebugData?.CompilerFlags.ToDictionary(k => k.Key, v => v.Value);

                                output.Add(baseRecord with
                                {
                                    Name = file.Name,
                                    Extension = file.Extension,
                                    TargetFramework = file.TargetFramework?.FullName,
                                    TargetFrameworkIdentifier = file.TargetFramework?.Identifier,
                                    TargetFrameworkVersion = file.TargetFramework?.Version.ToString(),
                                    HasCompilerFlags = file.DebugData?.HasCompilerFlags,
                                    HasSourceLink = file.DebugData?.HasSourceLink,
                                    HasDebugInfo = file.DebugData?.HasDebugInfo,
                                    CompilerFlags = compilerFlags != null ? JsonConvert.SerializeObject(compilerFlags) : null,
                                });
                            }
                        }
                        catch (FileNotFoundException ex)
                        {
                            _logger.LogWarning(ex, "Could not get symbol validator files for {Id} {Version}.", leaf.PackageId, leaf.PackageVersion);
                            return new List<NuGetPackageExplorerRecord>
                            {
                                new NuGetPackageExplorerRecord(scanId, scanTimestamp, leaf)
                                {
                                    ResultType = NuGetPackageExplorerResultType.InvalidMetadata,
                                }
                            };
                        }

                        if (!output.Any())
                        {
                            output.Add(baseRecord with
                            {
                                ResultType = NuGetPackageExplorerResultType.NoFiles,
                            });
                        }

                        return output;
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
#endif
            }
        }

        public List<NuGetPackageExplorerRecord> Prune(List<NuGetPackageExplorerRecord> records)
        {
            return PackageRecord.Prune(records);
        }
    }
}
