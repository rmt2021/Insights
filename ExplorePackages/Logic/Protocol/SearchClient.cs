﻿using System;
using System.IO;
using System.Threading.Tasks;
using Knapcode.ExplorePackages.Support;
using NuGet.Common;
using NuGet.Protocol;

namespace Knapcode.ExplorePackages.Logic
{
    public class SearchClient
    {
        private readonly HttpSource _httpSource;
        private readonly ILogger _log;

        public SearchClient(HttpSource httpSource, ILogger log)
        {
            _httpSource = httpSource;
            _log = log;
        }

        public async Task<bool> HasPackageAsync(string baseUrl, string id, string version)
        {
            var query = $"packageid:{id} version:{version}";
            var url = $"{baseUrl.TrimEnd('/')}/search/query?q={Uri.EscapeDataString(query)}&take=1&ignoreFilter=true&semVerLevel=2.0.0";

            var result = await _httpSource.DeserializeUrlAsync<V2SearchResult>(
                url,
                ignoreNotFounds: false,
                log: _log);

            if (result.TotalHits == 0)
            {
                return false;
            }
            else if (result.TotalHits == 1)
            {
                return true;
            }

            throw new InvalidDataException("The count returned by V2 search should be either 0 or 1.");
        }
    }
}
