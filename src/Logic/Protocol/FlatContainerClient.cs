// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Protocol;
using NuGet.Versioning;

#nullable enable

namespace NuGet.Insights
{
    public class FlatContainerClient
    {
        private readonly ServiceIndexCache _serviceIndexCache;
        private readonly HttpSource _httpSource;
        private readonly FileDownloader _fileDownloader;
        private readonly IOptions<NuGetInsightsSettings> _options;
        private readonly ILogger<FlatContainerClient> _logger;

        public FlatContainerClient(
            ServiceIndexCache serviceIndexCache,
            HttpSource httpSource,
            FileDownloader fileDownloader,
            IOptions<NuGetInsightsSettings> options,
            ILogger<FlatContainerClient> logger)
        {
            _serviceIndexCache = serviceIndexCache;
            _httpSource = httpSource;
            _fileDownloader = fileDownloader;
            _options = options;
            _logger = logger;
        }

        public async Task<BlobMetadata> GetPackageContentMetadataAsync(string baseUrl, string id, string version)
        {
            var url = GetPackageContentUrl(baseUrl, id, version);
            return await _httpSource.GetBlobMetadataAsync(url, _logger);
        }

        public async Task<string> GetPackageContentUrlAsync(string id, string version)
        {
            var baseUrl = await GetBaseUrlAsync();
            return GetPackageContentUrl(baseUrl, id, version);
        }

        public string GetPackageContentUrl(string baseUrl, string id, string version)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            var url = $"{baseUrl.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";
            return url;
        }

        public async Task<string> GetPackageManifestUrlAsync(string id, string version)
        {
            var baseUrl = await GetBaseUrlAsync();
            return GetPackageManifestUrl(baseUrl, id, version);
        }

        public string GetPackageManifestUrl(string baseUrl, string id, string version)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            var url = $"{baseUrl.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.nuspec";
            return url;
        }

        public async Task<string> GetPackageReadmeUrlAsync(string id, string version)
        {
            var baseUrl = await GetBaseUrlAsync();
            return GetPackageReadmeUrl(baseUrl, id, version);
        }

        public string GetPackageReadmeUrl(string baseUrl, string id, string version)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            var url = $"{baseUrl.TrimEnd('/')}/{lowerId}/{lowerVersion}/readme";
            return url;
        }

        public async Task<(string? ContentType, TempStreamResult Body)?> DownloadPackageIconToFileAsync(string id, string version, CancellationToken token)
        {
            var url = await GetPackageIconUrlAsync(id, version);
            var result = await _fileDownloader.DownloadUrlToFileAsync(url, token);
            if (result is null)
            {
                return null;
            }

            return (result.Value.Headers["Content-Type"].FirstOrDefault(), result.Value.Body);
        }

        public async Task<NuspecContext> GetNuspecContextAsync(string id, string version, CancellationToken token)
        {
            var baseUrl = await GetBaseUrlAsync();
            return await GetNuspecContextAsync(baseUrl, id, version, token);
        }

        public async Task<NuspecContext> GetNuspecContextAsync(string baseUrl, string id, string version, CancellationToken token)
        {
            var url = GetPackageManifestUrl(baseUrl, id, version);

            var nuGetLogger = _logger.ToNuGetLogger();
            return await _httpSource.ProcessStreamAsync(
                new HttpSourceRequest(url, nuGetLogger)
                {
                    IgnoreNotFounds = true,
                },
                networkStream => Task.FromResult(NuspecContext.FromStream(id, version, networkStream, _logger)),
                nuGetLogger,
                token);
        }

        public async Task<string> GetPackageIconUrlAsync(string id, string version)
        {
            var baseUrl = await GetBaseUrlAsync();
            return GetPackageIconUrl(baseUrl, id, version);
        }

        public string GetPackageIconUrl(string baseUrl, string id, string version)
        {
            var lowerId = id.ToLowerInvariant();
            var lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            var url = $"{baseUrl.TrimEnd('/')}/{lowerId}/{lowerVersion}/icon";
            return url;
        }

        public async Task<bool> HasPackageManifestAsync(string baseUrl, string id, string version)
        {
            var url = GetPackageManifestUrl(baseUrl, id, version);
            return await _httpSource.UrlExistsAsync(url, _logger);
        }

        public async Task<bool> HasPackageIconAsync(string baseUrl, string id, string version)
        {
            var url = GetPackageIconUrl(baseUrl, id, version);
            return await _httpSource.UrlExistsAsync(url, _logger);
        }

        public async Task<bool> HasPackageInIndexAsync(string baseUrl, string id, string version)
        {
            var index = await GetIndexAsync(baseUrl, id);
            if (index == null)
            {
                return false;
            }

            var lowerVersion = NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
            return index.Versions.Contains(lowerVersion);
        }

        public async Task<FlatContainerIndex?> GetIndexAsync(string baseUrl, string id)
        {
            var lowerId = id.ToLowerInvariant();
            var packageUrl = $"{baseUrl.TrimEnd('/')}/{lowerId}/index.json";
            return await _httpSource.DeserializeUrlAsync<FlatContainerIndex>(packageUrl, ignoreNotFounds: true, logger: _logger);
        }

        private async Task<string> GetBaseUrlAsync()
        {
            if (_options.Value.FlatContainerBaseUrlOverride != null)
            {
                return _options.Value.FlatContainerBaseUrlOverride;
            }

            return await _serviceIndexCache.GetUrlAsync(ServiceIndexTypes.FlatContainer);
        }
    }
}
