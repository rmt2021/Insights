// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NuGet.Insights.Worker.AuxiliaryFileUpdater;
using NuGet.Insights.Worker.BuildVersionSet;

namespace NuGet.Insights.Worker.OwnersToCsv
{
    public class OwnersToCsvUpdater : IAuxiliaryFileUpdater<AsOfData<PackageOwner>>
    {
        private readonly PackageOwnersClient _packageOwnersClient;
        private readonly IOptions<NuGetInsightsWorkerSettings> _options;

        public OwnersToCsvUpdater(
            PackageOwnersClient packageOwnersClient,
            IOptions<NuGetInsightsWorkerSettings> options)
        {
            _packageOwnersClient = packageOwnersClient;
            _options = options;
        }

        public string OperationName => "OwnersToCsv";
        public string BlobName => "owners";
        public string ContainerName => _options.Value.PackageOwnerContainerName;
        public TimeSpan Frequency => _options.Value.OwnersToCsvFrequency;
        public bool HasRequiredConfiguration => _options.Value.OwnersV2Url != null;
        public bool AutoStart => _options.Value.AutoStartOwnersToCsv;
        public Type RecordType => typeof(PackageOwnerRecord);

        public async Task<AsOfData<PackageOwner>> GetDataAsync()
        {
            return await _packageOwnersClient.GetAsync();
        }

        public async Task WriteAsync(IVersionSet versionSet, AsOfData<PackageOwner> data, StreamWriter writer)
        {
            var record = new PackageOwnerRecord { AsOfTimestamp = data.AsOfTimestamp };
            record.WriteHeader(writer);

            var idToOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await foreach (var entry in data.Entries)
            {
                if (!versionSet.DidIdEverExist(entry.Id))
                {
                    continue;
                }

                if (!idToOwners.TryGetValue(entry.Id, out var owners))
                {
                    // Only write when we move to the next ID. This ensures all of the owners of a given ID are in the same record.
                    if (idToOwners.Any())
                    {
                        WriteAndClear(writer, record, idToOwners);
                    }

                    owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    idToOwners.Add(entry.Id, owners);
                }

                owners.Add(entry.Username);
            }

            if (idToOwners.Any())
            {
                WriteAndClear(writer, record, idToOwners);
            }

            // Add IDs that are not mentioned in the data and therefore have no owners. This makes joins on the
            // produced data set easier.
            foreach (var id in versionSet.GetUncheckedIds())
            {
                record.LowerId = id.ToLowerInvariant();
                record.Id = id;
                record.Owners = "[]";
                record.Write(writer);
            }
        }

        private static void WriteAndClear(StreamWriter writer, PackageOwnerRecord record, Dictionary<string, HashSet<string>> idToOwners)
        {
            foreach (var pair in idToOwners)
            {
                record.LowerId = pair.Key.ToLowerInvariant();
                record.Id = pair.Key;
                record.Owners = JsonSerializer.Serialize(pair.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                record.Write(writer);
            }

            idToOwners.Clear();
        }
    }
}
