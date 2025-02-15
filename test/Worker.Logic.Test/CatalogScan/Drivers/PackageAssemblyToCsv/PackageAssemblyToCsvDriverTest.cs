// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights.Worker.PackageAssemblyToCsv
{
    public class PackageAssemblyToCsvDriverTest : BaseWorkerLogicIntegrationTest
    {
        public PackageAssemblyToCsvDriverTest(ITestOutputHelper output, DefaultWebApplicationFactory<StaticFilesStartup> factory)
            : base(output, factory)
        {
            FailFastLogLevel = LogLevel.None;
            AssertLogLevel = LogLevel.None;
        }

        public ICatalogLeafToCsvDriver<PackageAssembly> Target => Host.Services.GetRequiredService<ICatalogLeafToCsvDriver<PackageAssembly>>();

        [Fact]
        public async Task HandlesInvalidPublicKey()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.10.11.03.47.42/sharepointpnpcoreonline.2.21.1712.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-10-11T03:47:42.1892419Z"),
                PackageId = "SharePointPnPCoreOnline",
                PackageVersion = "2.21.1712",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.PublicKeyToken_Security));
            Assert.True(record.HasPublicKey);
            Assert.Null(record.PublicKeyToken);
            Assert.Equal("Gt7fjUhg9y4YDKVONm/Pm3ykcVw=", record.PublicKeySHA1);
            Assert.Equal(288, record.PublicKeyLength);
        }

        [Fact]
        public async Task HandlesResourceAssembly()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.10.22.19.27.07/humanizer.core.zh-cn.2.2.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-10-22T19:27:07.7689589Z"),
                PackageId = "Humanizer.Core.zh-CN",
                PackageVersion = "2.2.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            Assert.Equal("zh-CN", record.Culture);
        }

        [Fact]
        public async Task HandlesRefAssembly()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.13.22.09.48/system.runtime.handles.4.3.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-13T22:09:48.4043804Z"),
                PackageId = "System.Runtime.Handles",
                PackageVersion = "4.3.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            Assert.True(customAttributes.TryGetProperty("ReferenceAssembly", out var _));
        }

        [Fact]
        public async Task HandlesUnoptimizedAssembly()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.07.01.04.25.53/txtcsvhelper.1.2.8.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-07-01T04:25:53.5053628Z"),
                PackageId = "TxtCsvHelper",
                PackageVersion = "1.2.8",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            var debuggingModes = (DebuggableAttribute.DebuggingModes)customAttributes.GetProperty("Debuggable")[0].GetProperty("0").GetInt32();
            Assert.True(debuggingModes.HasFlag(DebuggableAttribute.DebuggingModes.DisableOptimizations));
        }

        [Fact]
        public async Task HandlesCustomAttributeArgumentArrayWithCorruptedLength()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.03.26.13.21.32/kentico.xperience.aspnet.mvc5.libraries.13.0.18.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-03-26T13:21:32.0964414Z"),
                PackageId = "Kentico.Xperience.AspNet.Mvc5.Libraries",
                PackageVersion = "13.0.18",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(5, output.Value.Records.Count);
            var record = output.Value.Records[0];
            Assert.Equal("lib/NET48/Kentico.Content.Web.Mvc.dll", record.Path);
            Assert.Contains("RegisterPageBuilderLocalizationResource", record.CustomAttributesFailedDecode);
        }

        [Fact]
        public async Task HandlesCustomAttributeWithBrokenMethodName()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2020.02.23.11.43.38/citizenfx.framework.client.0.1.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2020-02-23T11:43:38.8666289Z"),
                PackageId = "CitizenFX.Framework.Client",
                PackageVersion = "0.1.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(20, output.Value.Records.Count);
            var record = output.Value.Records[4];
            Assert.Equal("ref/net452/mscorlib.dll", record.Path);
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.CustomAttributes_BrokenMethodDefinitionName));
        }

        [Fact]
        public async Task HandlesOptimizedAssembly()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.07.12.03.40.51/txtcsvhelper.1.2.9.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-07-12T03:40:51.9884006Z"),
                PackageId = "TxtCsvHelper",
                PackageVersion = "1.2.9",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            var debuggingModes = (DebuggableAttribute.DebuggingModes)customAttributes.GetProperty("Debuggable")[0].GetProperty("0").GetInt32();
            Assert.False(debuggingModes.HasFlag(DebuggableAttribute.DebuggingModes.DisableOptimizations));
        }

        [Fact]
        public async Task HandlesFailedDecodeOfSecurityRulesAttribute()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.08.13.18.49.00/ewl.69.0.0-pr00249.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-07-14T19:04:24.75Z"),
                PackageId = "Ewl",
                PackageVersion = "69.0.0-pr00249",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(43, output.Value.Records.Count);
            var record = output.Value.Records[0];
            Assert.Equal("Development Utility/Aspose.PDF.dll", record.Path);
            Assert.Equal("[\"SecurityRules\"]", record.CustomAttributesFailedDecode);
        }

        [Fact]
        public async Task HandlesDuplicateCustomAttributeArgumentNames()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.11.02.02.58.09/realm.3.0.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-11-02T02:58:09.1470115Z"),
                PackageId = "Realm",
                PackageVersion = "3.0.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(7, output.Value.Records.Count);
            var record = output.Value.Records[1];
            Assert.Equal("lib/netstandard1.4/Realm.Sync.dll", record.Path);
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.CustomAttributes_DuplicateArgumentName));
        }

        [Fact]
        public async Task HandlesCustomAttributeWithCorruptMethodInsteadOfType()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.04.13.08.32.33/gembox.document.33.0.1173-hotfix.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-04-13T08:32:33.3109749Z"),
                PackageId = "GemBox.Document",
                PackageVersion = "33.0.1173-hotfix",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(4, output.Value.Records.Count);
            var record = output.Value.Records[0];
            Assert.Equal("lib/net35/GemBox.Document.dll", record.Path);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            Assert.Equal("StandardFonts/", customAttributes.GetProperty("\u0002   ")[0].GetProperty("0").GetString());
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.CustomAttributes_MethodDefinition));
        }

        [Fact]
        public async Task HandlesCustomAttributeWithManyMethodsInsteadOfTypes()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.13.22.10.24/system.runtime.4.3.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-13T22:10:24.250425Z"),
                PackageId = "System.Runtime",
                PackageVersion = "4.3.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(7, output.Value.Records.Count);
            var record = output.Value.Records[3];
            Assert.Equal("ref/netstandard1.0/System.Runtime.dll", record.Path);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            Assert.Equal(
                new[] { "AllowPartiallyTrustedCallers", "AssemblyCompany", "AssemblyCopyright", "AssemblyDefaultAlias", "AssemblyDelaySign", "AssemblyDescription", "AssemblyFileVersion", "AssemblyInformationalVersion", "AssemblyKeyFile", "AssemblyProduct", "AssemblyTitle", "CLSCompliant", "CompilationRelaxations", "ReferenceAssembly", "RuntimeCompatibility" },
                customAttributes.EnumerateObject().Select(x => x.Name).ToArray());
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.CustomAttributes_MethodDefinition));
        }

        [Fact]
        public async Task HandlesCustomAttributeConstructorWithTypeDefinitionInsteadOfTypeReference()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.10.27.13.06.16/quickgraph.3.6.61119.7.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-10-27T13:06:16.1670248Z"),
                PackageId = "QuickGraph",
                PackageVersion = "3.6.61119.7",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(8, output.Value.Records.Count);
            var record = output.Value.Records[1];
            Assert.Equal("lib/net4/CodeContracts/QuickGraph.Contracts.dll", record.Path);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            Assert.Equal(
                new[] { "AssemblyCompany", "AssemblyConfiguration", "AssemblyCopyright", "AssemblyDelaySign", "AssemblyDescription", "AssemblyKeyName", "AssemblyProduct", "AssemblyTitle", "AssemblyTrademark", "ContractDeclarativeAssembly", "ContractReferenceAssembly", "Debuggable", "Extension", "RuntimeCompatibility", "TargetFramework" },
                customAttributes.EnumerateObject().Select(x => x.Name).ToArray());
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.CustomAttributes_TypeDefinitionConstructor));
        }

        [Fact]
        public async Task HandlesCustomAttributesWithBadValueBlobs()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.19.06.05.27/awesomesocket.1.2.0.1.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-19T06:05:27.3158923Z"),
                PackageId = "AwesomeSocket",
                PackageVersion = "1.2.0.1",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(6, output.Value.Records.Count);
            var record = output.Value.Records[1];
            Assert.Equal("bin/Debug/SockLibNG.dll", record.Path);
            Assert.Equal("[\"\",\"Runtime.InteropServices\",\"blyDescription\",\"bute\",\"ces\",\"eByte\",\"emblyCopyright\",\"ionRelaxations\",\"ssemblyCompany\",\"ssemblyProduct\",\"ssemblyVersion\",\"stem.Runtime.Versioning\",\"ty\",\"untime.CompilerServices\",\"yConfiguration\"]", record.CustomAttributesFailedDecode);
        }

        [Fact]
        public async Task HandlesDuplicateAssemblyAttributes()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2021.02.23.22.21.34/moq.4.16.1.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2021-02-23T22:21:34.6558267Z"),
                PackageId = "Moq",
                PackageVersion = "4.16.1",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = output.Value.Records[0];
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            var customAttributes = JsonSerializer.Deserialize<JsonElement>(record.CustomAttributes);
            Assert.Equal(2, customAttributes.GetProperty("InternalsVisibleTo").GetArrayLength());
            Assert.Equal("Moq.Tests, PublicKey=00240000048000009400000006020000002400005253413100040000010001009f7a95086500f8f66d892174803850fed9c22225c2ccfff21f39c8af8abfa5415b1664efd0d8e0a6f7f2513b1c11659bd84723dc7900c3d481b833a73a2bcf1ed94c16c4be64d54352c86956c89930444e9ac15124d3693e3f029818e8410f167399d6b995324b635e95353ba97bfab856abbaeb9b40c9b160070c6325e22ddc", customAttributes.GetProperty("InternalsVisibleTo")[0].GetProperty("0").GetString());
            Assert.Equal("DynamicProxyGenAssembly2,PublicKey=0024000004800000940000000602000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605d02baa56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667bb4f64bcb8e2121380fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6a7d3113e92484cf7045cc7", customAttributes.GetProperty("InternalsVisibleTo")[1].GetProperty("0").GetString());
        }

        [Fact]
        public async Task HandlesInvalidCultureWhenReadingAssemblyName()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.18.08.44.52/enyutrynuget.1.0.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-18T08:44:52.1808182Z"),
                PackageId = "EnyuTryNuget",
                PackageVersion = "1.0.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            var record = Assert.Single(output.Value.Records);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.Name_CultureNotFoundException));
            Assert.Equal("EnyuTryNuget", record.Culture);
            Assert.False(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.Name_FileLoadException));
        }

        [Fact]
        public async Task HandlesFileLoadExceptionWhenReadingAssemblyName()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.11.04.41.19/getaddress.azuretablestorage.1.0.0.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-11T04:41:19.7918122Z"),
                PackageId = "getAddress.AzureTableStorage",
                PackageVersion = "1.0.0",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.Equal(2, output.Value.Records.Count);
            var record = output.Value.Records[1];
            Assert.Equal("lib/net451/getAddress,Azure.4.5.1.dll", record.Path);
            Assert.Equal(PackageAssemblyResultType.ValidAssembly, record.ResultType);
            Assert.True(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.Name_FileLoadException));
            Assert.False(record.EdgeCases.Value.HasFlag(PackageAssemblyEdgeCases.Name_CultureNotFoundException));
        }

        [Fact]
        public async Task HandlesInvalidZipEntry()
        {
            var leaf = new CatalogLeafScan
            {
                Url = "https://api.nuget.org/v3/catalog0/data/2018.12.14.10.06.47/microsoft.dotnet.interop.1.0.0-prerelease-0002.json",
                LeafType = CatalogLeafType.PackageDetails,
                CommitTimestamp = DateTimeOffset.Parse("2018-12-14T10:06:47.962422Z"),
                PackageId = "Microsoft.DotNet.Interop",
                PackageVersion = "1.0.0-prerelease-0002",
            };
            await Target.InitializeAsync();

            var output = await Target.ProcessLeafAsync(leaf);

            Assert.Equal(DriverResultType.Success, output.Type);
            Assert.All(output.Value.Records, x => Assert.Equal(PackageAssemblyResultType.InvalidZipEntry, x.ResultType));
        }

        [Fact]
        public async Task HandlesMaxWriterConcurrency()
        {
            // Arrange
            TempStreamDirectory tempDir = null;
            ConfigureSettings = x =>
            {
                x.MaxTempMemoryStreamSize = 0;
                tempDir = x.TempDirectories[0];
                tempDir.Path = Path.GetFullPath(tempDir.Path);
                tempDir.MaxConcurrentWriters = 1;
            };
            await Host.Services.GetRequiredService<StorageLeaseService>().InitializeAsync();
            await Target.InitializeAsync();

            using (var serviceScope = Host.Services.CreateScope())
            {
                var leaseScopeA = serviceScope.ServiceProvider.GetRequiredService<TempStreamLeaseScope>();
                await using var ownershipA = leaseScopeA.TakeOwnership();
                Assert.True(await leaseScopeA.WaitAsync(tempDir));

                var leaseScopeB = Host.Services.GetRequiredService<TempStreamLeaseScope>();
                await using var ownershipB = leaseScopeB.TakeOwnership();
                var leaf = new CatalogLeafScan
                {
                    Url = "https://api.nuget.org/v3/catalog0/data/2018.12.14.10.06.47/microsoft.dotnet.interop.1.0.0-prerelease-0002.json",
                    LeafType = CatalogLeafType.PackageDetails,
                    CommitTimestamp = DateTimeOffset.Parse("2018-12-14T10:06:47.962422Z"),
                    PackageId = "Microsoft.DotNet.Interop",
                    PackageVersion = "1.0.0-prerelease-0002",
                };

                // Act
                var output = await Target.ProcessLeafAsync(leaf);

                // Assert
                Assert.Equal(DriverResultType.TryAgainLater, output.Type);
            }
        }
    }
}
