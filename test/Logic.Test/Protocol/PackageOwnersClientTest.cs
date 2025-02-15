// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights
{
    public class PackageOwnersClientTest : BaseLogicIntegrationTest
    {
        private const string OwnersToCsvDir = "OwnersToCsv";

        public PackageOwnersClientTest(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory) : base(output, factory)
        {
        }

        [Fact]
        public async Task ExecuteAsync()
        {
            // Arrange
            var url = $"http://localhost/{TestData}/{OwnersToCsvDir}/{Step1}/owners.v2.json";
            ConfigureSettings = x => x.OwnersV2Url = url;
            HttpMessageHandlerFactory.OnSendAsync = async (req, _, _) =>
            {
                if (req.RequestUri.AbsolutePath.EndsWith("/owners.v2.json"))
                {
                    var newReq = Clone(req);
                    newReq.RequestUri = new Uri(url);
                    return await TestDataHttpClient.SendAsync(newReq);
                }

                return null;
            };

            // Set the Last-Modified date
            var asOfTimestamp = DateTime.Parse("2021-01-14T18:00:00Z");
            var downloadsFile = new FileInfo(Path.Combine(TestData, OwnersToCsvDir, Step1, "owners.v2.json"))
            {
                LastWriteTimeUtc = asOfTimestamp,
            };

            var client = Host.Services.GetRequiredService<PackageOwnersClient>();

            // Act
            await using var set = await client.GetAsync();

            // Assert
            Assert.Equal(asOfTimestamp, set.AsOfTimestamp);
            Assert.Equal(url, set.Url);
            Assert.Equal("\"1d6ea9f13639114\"", set.ETag);
            var owners = new List<PackageOwner>();
            await foreach (var owner in set.Entries)
            {
                owners.Add(owner);
            }

            Assert.Equal(
                new[]
                {
                    new PackageOwner("Microsoft.Extensions.Logging", "Microsoft"),
                    new PackageOwner("Microsoft.Extensions.Logging", "aspnet"),
                    new PackageOwner("Microsoft.Extensions.Logging", "dotnetframework"),
                    new PackageOwner("Knapcode.TorSharp", "joelverhagen"),
                    new PackageOwner("Castle.Core", "castleproject"),
                    new PackageOwner("Newtonsoft.Json", "newtonsoft"),
                    new PackageOwner("Newtonsoft.Json", "jamesnk"),
                },
                owners.ToArray());
        }
    }
}
