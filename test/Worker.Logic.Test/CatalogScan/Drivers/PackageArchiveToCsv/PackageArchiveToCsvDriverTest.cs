// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Insights.Worker.PackageAssemblyToCsv;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights.Worker.PackageArchiveToCsv
{
    public class PackageArchiveToCsvDriverTest : BaseWorkerLogicIntegrationTest
    {
        public PackageArchiveToCsvDriverTest(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
            : base(output, factory)
        {
            FailFastLogLevel = LogLevel.None;
        }

        public ICatalogLeafToCsvDriver<PackageAssembly> PackageAssemblyToCsv => Host.Services.GetRequiredService<ICatalogLeafToCsvDriver<PackageAssembly>>();
        public ICatalogLeafToCsvDriver<PackageArchiveRecord, PackageArchiveEntry> Target => Host.Services.GetRequiredService<ICatalogLeafToCsvDriver<PackageArchiveRecord, PackageArchiveEntry>>();

        [Fact]
        public async Task ReturnsDeleted()
        {
            var leaf = new CatalogLeafItem
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2017.11.08.17.42.28/nuget.platform.1.0.0.json",
                Type = CatalogLeafType.PackageDelete,
                CommitTimestamp = DateTimeOffset.Parse("2017-11-08T17:42:28.5677911Z"),
                PackageId = "NuGet.Platform",
                PackageVersion = "1.0.0",
            };
            await InitializeAsync(leaf);

            var output = await Target.ProcessLeafAsync(leaf, attemptCount: 1);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Set1.Records);
            Assert.Equal(PackageArchiveResultType.Deleted, record.ResultType);
            var entry = Assert.Single(output.Value.Set2.Records);
            Assert.Equal(PackageArchiveResultType.Deleted, entry.ResultType);
        }

        [Fact]
        public async Task ReturnsEmptyIfMissing()
        {
            // This package was deleted by a subsequent leaf.
            var leaf = new CatalogLeafItem
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2015.06.13.03.41.09/nuget.platform.1.0.0.json",
                Type = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2015-06-13T03:41:09.5185838Z"),
                PackageId = "NuGet.Platform",
                PackageVersion = "1.0.0",
            };
            await InitializeAsync(leaf);

            var output = await Target.ProcessLeafAsync(leaf, attemptCount: 1);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Empty(output.Value.Set1.Records);
            Assert.Empty(output.Value.Set2.Records);
        }

        [Fact]
        public async Task GetsPackageArchiveEntries()
        {
            var leaf = new CatalogLeafItem
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.08.28.22.26.57/loshar.my.package.1.0.0.json",
                Type = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-08-28T22:26:57.4218948Z"),
                PackageId = "Loshar.My.Package",
                PackageVersion = "1.0.0",
            };
            await InitializeAsync(leaf);

            var output = await Target.ProcessLeafAsync(leaf, attemptCount: 1);

            Assert.Equal(DriverResultType.Success, output.Type);
            var archive = Assert.Single(output.Value.Set1.Records);
            Assert.Equal(PackageArchiveResultType.Available, archive.ResultType);
            Assert.Equal(22399, archive.Size);
            Assert.Equal("Iv31+aZ1FrwwrVXrkkw6jw==", archive.MD5);
            Assert.Equal("5mVQoxKAOBG4EfcMtAlbNA9j/BY=", archive.SHA1);
            Assert.Equal("Un5Uw7RzNNFRXRGb4+6+ZB6zAU1+rSJi1UdjwEm+Dos=", archive.SHA256);
            Assert.Equal("0j3nBhz6LgdRGkWED8U9UYpbK06J0adJdmdatPjsDtVrk6TvALANKEBD77uDUs67eIjpa3Bfs9QO/wuVWMrwLw==", archive.SHA512);
            Assert.Equal(22381, archive.OffsetAfterEndOfCentralDirectory);
            Assert.Equal(483u, archive.CentralDirectorySize);
            Assert.Equal(21894u, archive.OffsetOfCentralDirectory);
            Assert.Equal(6, archive.EntryCount);
            Assert.Empty(archive.Comment);

            Assert.Equal(6, output.Value.Set2.Records.Count);
            var entries = output.Value.Set2.Records;

            Assert.Equal(0, entries[0].SequenceNumber);
            Assert.Equal("_rels/.rels", entries[0].Path);
            Assert.Equal(".rels", entries[0].FileName);
            Assert.Equal(".rels", entries[0].FileExtension);
            Assert.Equal("_rels", entries[0].TopLevelFolder);
            Assert.Equal(0, entries[0].Flags);
            Assert.Equal(8, entries[0].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-06T13:48:24.0000000+00:00"), entries[0].LastModified);
            Assert.Equal(3811040653, entries[0].Crc32);
            Assert.Equal(274u, entries[0].CompressedSize);
            Assert.Equal(511u, entries[0].UncompressedSize);
            Assert.Equal(0u, entries[0].LocalHeaderOffset);
            Assert.Empty(entries[0].Comment);

            Assert.Equal(1, entries[1].SequenceNumber);
            Assert.Equal("Loshar.My.Package.nuspec", entries[1].Path);
            Assert.Equal("Loshar.My.Package.nuspec", entries[1].FileName);
            Assert.Equal(".nuspec", entries[1].FileExtension);
            Assert.Null(entries[1].TopLevelFolder);
            Assert.Equal(0, entries[1].Flags);
            Assert.Equal(8, entries[1].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-06T13:48:24.0000000+00:00"), entries[1].LastModified);
            Assert.Equal(3125932907, entries[1].Crc32);
            Assert.Equal(376u, entries[1].CompressedSize);
            Assert.Equal(734u, entries[1].UncompressedSize);
            Assert.Equal(315u, entries[1].LocalHeaderOffset);
            Assert.Empty(entries[1].Comment);

            Assert.Equal(2, entries[2].SequenceNumber);
            Assert.Equal("lib/netstandard2.0/NuGet.Services.EndToEnd.TestPackage.dll", entries[2].Path);
            Assert.Equal("NuGet.Services.EndToEnd.TestPackage.dll", entries[2].FileName);
            Assert.Equal(".dll", entries[2].FileExtension);
            Assert.Equal("lib", entries[2].TopLevelFolder);
            Assert.Equal(0, entries[2].Flags);
            Assert.Equal(8, entries[2].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-06T20:48:20.0000000+00:00"), entries[2].LastModified);
            Assert.Equal(2664195616, entries[2].Crc32);
            Assert.Equal(1559u, entries[2].CompressedSize);
            Assert.Equal(4608u, entries[2].UncompressedSize);
            Assert.Equal(745u, entries[2].LocalHeaderOffset);
            Assert.Empty(entries[2].Comment);

            Assert.Equal(3, entries[3].SequenceNumber);
            Assert.Equal("[Content_Types].xml", entries[3].Path);
            Assert.Equal("[Content_Types].xml", entries[3].FileName);
            Assert.Equal(".xml", entries[3].FileExtension);
            Assert.Null(entries[3].TopLevelFolder);
            Assert.Equal(0, entries[3].Flags);
            Assert.Equal(8, entries[3].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-06T13:48:24.0000000+00:00"), entries[3].LastModified);
            Assert.Equal(3159194846, entries[3].Crc32);
            Assert.Equal(207u, entries[3].CompressedSize);
            Assert.Equal(465u, entries[3].UncompressedSize);
            Assert.Equal(2392u, entries[3].LocalHeaderOffset);
            Assert.Empty(entries[3].Comment);

            Assert.Equal(4, entries[4].SequenceNumber);
            Assert.Equal("package/services/metadata/core-properties/47c2175eaf1d4949936eff3bca7bd113.psmdcp", entries[4].Path);
            Assert.Equal("47c2175eaf1d4949936eff3bca7bd113.psmdcp", entries[4].FileName);
            Assert.Equal(".psmdcp", entries[4].FileExtension);
            Assert.Equal("package", entries[4].TopLevelFolder);
            Assert.Equal(0, entries[4].Flags);
            Assert.Equal(8, entries[4].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-06T13:48:24.0000000+00:00"), entries[4].LastModified);
            Assert.Equal(3933553402, entries[4].Crc32);
            Assert.Equal(408u, entries[4].CompressedSize);
            Assert.Equal(695u, entries[4].UncompressedSize);
            Assert.Equal(2648u, entries[4].LocalHeaderOffset);
            Assert.Empty(entries[4].Comment);

            Assert.Equal(5, entries[5].SequenceNumber);
            Assert.Equal(".signature.p7s", entries[5].Path);
            Assert.Equal(".signature.p7s", entries[5].FileName);
            Assert.Equal(".p7s", entries[5].FileExtension);
            Assert.Null(entries[5].TopLevelFolder);
            Assert.Equal(0, entries[5].Flags);
            Assert.Equal(0, entries[5].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("2018-08-20T20:19:24.0000000+00:00"), entries[5].LastModified);
            Assert.Equal(2180012886, entries[5].Crc32);
            Assert.Equal(18683u, entries[5].CompressedSize);
            Assert.Equal(18683u, entries[5].UncompressedSize);
            Assert.Equal(3167u, entries[5].LocalHeaderOffset);
            Assert.Empty(entries[5].Comment);
        }

        [Fact]
        public async Task AcceptsDuplicateEntries()
        {
            var leaf = new CatalogLeafItem
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2019.12.03.16.44.55/microsoft.extensions.configuration.3.1.0.json",
                Type = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2019-12-03T16:44:55.0668686Z"),
                PackageId = "Microsoft.Extensions.Configuration",
                PackageVersion = "3.1.0",
            };
            await InitializeAsync(leaf);

            var output = await Target.ProcessLeafAsync(leaf, attemptCount: 1);

            Assert.Equal(DriverResultType.Success, output.Type);
            var archive = Assert.Single(output.Value.Set1.Records);
            Assert.Equal(PackageArchiveResultType.Available, archive.ResultType);
            Assert.Equal(69294, archive.Size);
            Assert.Equal("YChbobHPT7otNUCa9TWMqA==", archive.MD5);
            Assert.Equal("Zng0ROCOk8Qtml+wTEUAYtbfhGI=", archive.SHA1);
            Assert.Equal("KI1WXvnF/Xe9cKTdDjzm0vd5h9bmM+3KinuWlsF/X+c=", archive.SHA256);
            Assert.Equal("MUBW1eAqbVex8q0bnCxZ0npzJtiKCMW5OHWKCBYtScmyJ+iTVP9Pv6R9JnnF0UY+PspIkDPNXqo4pxQi/HV+yw==", archive.SHA512);
            Assert.Equal(69276, archive.OffsetAfterEndOfCentralDirectory);
            Assert.Equal(928u, archive.CentralDirectorySize);
            Assert.Equal(68344u, archive.OffsetOfCentralDirectory);
            Assert.Equal(11, archive.EntryCount);
            Assert.Empty(archive.Comment);

            Assert.Equal(11, output.Value.Set2.Records.Count);
            var entries = output.Value.Set2.Records;

            Assert.Equal(6, entries[6].SequenceNumber);
            Assert.Equal("packageIcon.png", entries[6].Path);
            Assert.Equal("packageIcon.png", entries[6].FileName);
            Assert.Equal(".png", entries[6].FileExtension);
            Assert.Null(entries[6].TopLevelFolder);
            Assert.Equal(0, entries[6].Flags);
            Assert.Equal(8, entries[6].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("1980-01-01T00:00:00.0000000+00:00"), entries[6].LastModified);
            Assert.Equal(3714772846, entries[6].Crc32);
            Assert.Equal(5653u, entries[6].CompressedSize);
            Assert.Equal(7006u, entries[6].UncompressedSize);
            Assert.Equal(37399u, entries[6].LocalHeaderOffset);
            Assert.Empty(entries[6].Comment);

            Assert.Equal(7, entries[7].SequenceNumber);
            Assert.Equal("packageIcon.png", entries[7].Path);
            Assert.Equal("packageIcon.png", entries[7].FileName);
            Assert.Equal(".png", entries[7].FileExtension);
            Assert.Null(entries[7].TopLevelFolder);
            Assert.Equal(0, entries[7].Flags);
            Assert.Equal(8, entries[7].CompressionMethod);
            Assert.Equal(DateTimeOffset.Parse("1980-01-01T00:00:00.0000000+00:00"), entries[7].LastModified);
            Assert.Equal(3714772846, entries[7].Crc32);
            Assert.Equal(5653u, entries[7].CompressedSize);
            Assert.Equal(7006u, entries[7].UncompressedSize);
            Assert.Equal(43097u, entries[7].LocalHeaderOffset);
            Assert.Empty(entries[7].Comment);
        }

        private async Task InitializeAsync(CatalogLeafItem leaf)
        {
            await PackageAssemblyToCsv.InitializeAsync();
            await PackageAssemblyToCsv.ProcessLeafAsync(leaf, attemptCount: 1);
            await Target.InitializeAsync();
        }
    }
}
