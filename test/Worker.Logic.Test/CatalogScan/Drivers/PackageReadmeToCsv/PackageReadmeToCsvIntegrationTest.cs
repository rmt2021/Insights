// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights.Worker.PackageReadmeToCsv
{
    public class PackageReadmeToCsvIntegrationTest : BaseCatalogLeafScanToCsvIntegrationTest<PackageReadme>
    {
        private const string PackageReadmeToCsvDir = nameof(PackageReadmeToCsv);
        private const string PackageReadmeToCsv_WithDeleteDir = nameof(PackageReadmeToCsv_WithDelete);
        private const string PackageReadmeToCsv_WithVeryLargeBufferDir = nameof(PackageReadmeToCsv_WithVeryLargeBuffer);

        public class PackageReadmeToCsv : PackageReadmeToCsvIntegrationTest
        {
            public PackageReadmeToCsv(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
                : base(output, factory)
            {
            }

            [Fact]
            public async Task Execute()
            {
                // Arrange
                var min0 = DateTimeOffset.Parse("2022-03-14T23:05:39.6122305Z");
                var max1 = DateTimeOffset.Parse("2022-03-14T23:06:07.7549588Z");
                var max2 = DateTimeOffset.Parse("2022-03-14T23:06:36.1633247Z");

                await CatalogScanService.InitializeAsync();
                await SetCursorAsync(CatalogScanDriverType.LoadPackageReadme, max2);
                await SetCursorAsync(min0);

                // Act
                await UpdateAsync(max1);

                // Assert
                await AssertOutputAsync(PackageReadmeToCsvDir, Step1, 0);
                await AssertOutputAsync(PackageReadmeToCsvDir, Step1, 2);

                // Act
                await UpdateAsync(max2);

                // Assert
                await AssertOutputAsync(PackageReadmeToCsvDir, Step1, 0); // This file is unchanged
                await AssertOutputAsync(PackageReadmeToCsvDir, Step2, 2);
                await AssertBlobCountAsync(DestinationContainerName, 2);
            }
        }

        public class PackageReadmeToCsv_WithDelete : PackageReadmeToCsvIntegrationTest
        {
            public PackageReadmeToCsv_WithDelete(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
                : base(output, factory)
            {
            }

            [Fact]
            public async Task Execute()
            {
                // Arrange
                MakeDeletedPackageAvailable();
                var min0 = DateTimeOffset.Parse("2020-12-20T02:37:31.5269913Z");
                var max1 = DateTimeOffset.Parse("2020-12-20T03:01:57.2082154Z");
                var max2 = DateTimeOffset.Parse("2020-12-20T03:03:53.7885893Z");

                await CatalogScanService.InitializeAsync();
                await SetCursorAsync(CatalogScanDriverType.LoadPackageReadme, max2);
                await SetCursorAsync(min0);

                // Act
                await UpdateAsync(max1);

                // Assert
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step1, 0);
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step1, 1);
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step1, 2);

                // Act
                await UpdateAsync(max2);

                // Assert
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step1, 0); // This file is unchanged.
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step1, 1); // This file is unchanged.
                await AssertOutputAsync(PackageReadmeToCsv_WithDeleteDir, Step2, 2);
            }
        }

        public class PackageReadmeToCsv_WithVeryLargeBuffer : PackageReadmeToCsvIntegrationTest
        {
            public PackageReadmeToCsv_WithVeryLargeBuffer(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
                : base(output, factory)
            {
            }

            [Fact]
            public async Task Execute()
            {
                ConfigureWorkerSettings = x => x.AppendResultStorageBucketCount = 1;

                // Arrange
                var max1 = DateTimeOffset.Parse("2022-03-10T21:32:51.8317694Z"); // PodcastAPI 1.1.1
                var min0 = max1.AddTicks(-1);

                await CatalogScanService.InitializeAsync();
                await SetCursorAsync(CatalogScanDriverType.LoadPackageReadme, max1);
                await SetCursorAsync(min0);

                // Act
                await UpdateAsync(max1);

                // Assert
                await AssertOutputAsync(PackageReadmeToCsv_WithVeryLargeBufferDir, Step1, 0);
            }
        }

        public PackageReadmeToCsvIntegrationTest(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
            : base(output, factory)
        {
        }

        protected override string DestinationContainerName => Options.Value.PackageReadmeContainerName;
        protected override CatalogScanDriverType DriverType => CatalogScanDriverType.PackageReadmeToCsv;
        public override IEnumerable<CatalogScanDriverType> LatestLeavesTypes => new[] { DriverType };
        public override IEnumerable<CatalogScanDriverType> LatestLeavesPerIdTypes => Enumerable.Empty<CatalogScanDriverType>();

        protected override IEnumerable<string> GetExpectedCursorNames()
        {
            return base.GetExpectedCursorNames().Concat(new[] { "CatalogScan-" + CatalogScanDriverType.LoadPackageReadme });
        }

        protected override IEnumerable<string> GetExpectedTableNames()
        {
            return base.GetExpectedTableNames().Concat(new[] { Options.Value.PackageReadmeTableName });
        }
    }
}
