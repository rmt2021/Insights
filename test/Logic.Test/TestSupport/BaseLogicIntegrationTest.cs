// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Insights
{
    public abstract class BaseLogicIntegrationTest : IClassFixture<DefaultWebApplicationFactory<StaticFilesStartup>>, IAsyncLifetime
    {
        static BaseLogicIntegrationTest()
        {
            var oldTemp = Environment.GetEnvironmentVariable("TEMP");
            var newTemp = Path.GetFullPath(Path.Join(oldTemp, "NuGet.Insights.Temp"));
            Directory.CreateDirectory(newTemp);
            Environment.SetEnvironmentVariable("TEMP", newTemp);
            Environment.SetEnvironmentVariable("TMP", newTemp);
        }

        public const string ProgramName = "NuGet.Insights.Logic.Test";
        public const string TestData = "TestData";
        public const string Step1 = "Step1";
        public const string Step2 = "Step2";
        public const string Step3 = "Step3";

        /// <summary>
        /// This should only be on when generating new test data locally. It should never be checked in as true.
        /// </summary>
        protected static readonly bool OverwriteTestData = false;

        private readonly Lazy<IHost> _lazyHost;

        public BaseLogicIntegrationTest(
            ITestOutputHelper output,
            DefaultWebApplicationFactory<StaticFilesStartup> factory)
        {
            Output = output;
            StoragePrefix = TestSettings.NewStoragePrefix();
            HttpMessageHandlerFactory = new TestHttpMessageHandlerFactory();

            var currentDirectory = Directory.GetCurrentDirectory();
            var testWebHostBuilder = factory.WithWebHostBuilder(b => b
                .ConfigureLogging(b => b.SetMinimumLevel(LogLevel.Error))
                .UseContentRoot(currentDirectory)
                .UseWebRoot(currentDirectory));
            TestDataHttpClient = testWebHostBuilder.CreateClient();
            LogLevelToCount = new ConcurrentDictionary<LogLevel, int>();

            _lazyHost = new Lazy<IHost>(() => GetHost(output));
        }

        private IHost GetHost(ITestOutputHelper output)
        {
            var hostBuilder = new HostBuilder();

            hostBuilder
                .ConfigureServices(serviceCollection =>
                {
                    serviceCollection.AddNuGetInsights(ProgramName);

                    serviceCollection.AddSingleton((INuGetInsightsHttpMessageHandlerFactory)HttpMessageHandlerFactory);

                    serviceCollection.AddTransient<ITelemetryClient>(s => new LoggerTelemetryClient(s.GetRequiredService<ILogger<LoggerTelemetryClient>>()));

                    serviceCollection.AddLogging(o =>
                    {
                        o.SetMinimumLevel(LogLevel.Trace);
                        o.AddProvider(new XunitLoggerProvider(output, LogLevel.Trace, LogLevelToCount, FailFastLogLevel, LogMessages));
                    });

                    serviceCollection.Configure((Action<NuGetInsightsSettings>)ConfigureDefaultsAndSettings);
                });

            ConfigureHostBuilder(hostBuilder);

            return hostBuilder.Build();
        }

        protected LogLevel FailFastLogLevel { get; set; } = LogLevel.Error;
        protected LogLevel AssertLogLevel { get; set; } = LogLevel.Warning;

        protected virtual void ConfigureHostBuilder(IHostBuilder hostBuilder)
        {
        }

        protected void ConfigureDefaultsAndSettings(NuGetInsightsSettings x)
        {
            x.StorageConnectionString = TestSettings.StorageConnectionString;
            x.StorageBlobReadSharedAccessSignature = TestSettings.StorageBlobReadSharedAccessSignature;

            x.LeaseContainerName = $"{StoragePrefix}1l1";
            x.PackageArchiveTableName = $"{StoragePrefix}1pa1";
            x.SymbolPackageArchiveTableName = $"{StoragePrefix}1sa1";
            x.PackageManifestTableName = $"{StoragePrefix}1pm1";
            x.PackageReadmeTableName = $"{StoragePrefix}1prm1";
            x.PackageHashesTableName = $"{StoragePrefix}1ph1";
            x.OwnerToSubjectReferenceTableName = $"{StoragePrefix}1ro2s1";
            x.SubjectToOwnerReferenceTableName = $"{StoragePrefix}1rs2o1";
            x.TimerTableName = $"{StoragePrefix}1t1";

            if (ConfigureSettings != null)
            {
                ConfigureSettings(x);
            }

            AssertStoragePrefix(x);
        }

        protected void AssertStoragePrefix(object x)
        {
            // Verify all container names are prefixed, so that parallel tests and cleanup work properly.
            var storageNameProperties = x
                .GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.Name.EndsWith("QueueName") || x.Name.EndsWith("TableName") || x.Name.EndsWith("ContainerName"));
            var storageNames = new HashSet<string>();
            foreach (var property in storageNameProperties)
            {
                var value = (string)property.GetMethod.Invoke(x, null);
                Assert.StartsWith(StoragePrefix, value);
                Assert.DoesNotContain(value, storageNames); // Make sure there are no duplicates
                storageNames.Add(value);
            }
        }

        public ITestOutputHelper Output { get; }
        public string StoragePrefix { get; }
        public TestHttpMessageHandlerFactory HttpMessageHandlerFactory { get; }
        public HttpClient TestDataHttpClient { get; }
        public ConcurrentDictionary<LogLevel, int> LogLevelToCount { get; }
        public Action<NuGetInsightsSettings> ConfigureSettings { get; set; }
        public IHost Host => _lazyHost.Value;
        public ServiceClientFactory ServiceClientFactory => Host.Services.GetRequiredService<ServiceClientFactory>();
        public ITelemetryClient TelemetryClient => Host.Services.GetRequiredService<ITelemetryClient>();
        public ILogger Logger => Host.Services.GetRequiredService<ILogger<BaseLogicIntegrationTest>>();
        public ConcurrentQueue<string> LogMessages { get; } = new ConcurrentQueue<string>();

        protected async Task AssertBlobCountAsync(string containerName, int expected)
        {
            var client = await ServiceClientFactory.GetBlobServiceClientAsync();
            var container = client.GetBlobContainerClient(containerName);
            var blobs = await container.GetBlobsAsync().ToListAsync();
            Assert.Equal(expected, blobs.Count);
        }

        protected async Task AssertCsvBlobAsync<T>(string containerName, string testName, string stepName, string fileName, string blobName) where T : ICsvRecord
        {
            Assert.EndsWith(".csv.gz", blobName);
            var actual = await AssertBlobAsync(containerName, testName, stepName, fileName, blobName, gzip: true);
            var headerFactory = Activator.CreateInstance<T>();
            var stringWriter = new StringWriter { NewLine = "\n" };
            headerFactory.WriteHeader(stringWriter);
            Assert.StartsWith(stringWriter.ToString(), actual);
        }

        protected async Task<BlobClient> GetBlobAsync(string containerName, string blobName)
        {
            var client = await ServiceClientFactory.GetBlobServiceClientAsync();
            var container = client.GetBlobContainerClient(containerName);
            return container.GetBlobClient(blobName);
        }

        protected async Task<string> AssertBlobAsync(string containerName, string testName, string stepName, string fileName, string blobName, bool gzip = false)
        {
            var blob = await GetBlobAsync(containerName, blobName);

            string actual;
            if (gzip)
            {
                if (fileName == null)
                {
                    fileName = blobName.Substring(0, blobName.Length - ".gz".Length);
                }

                Assert.EndsWith(".gz", blobName);

                using var destStream = new MemoryStream();
                using BlobDownloadInfo downloadInfo = await blob.DownloadAsync();
                await downloadInfo.Content.CopyToAsync(destStream);
                destStream.Position = 0;

                Assert.Contains(StorageUtility.RawSizeBytesMetadata, downloadInfo.Details.Metadata);
                var uncompressedLength = long.Parse(downloadInfo.Details.Metadata[StorageUtility.RawSizeBytesMetadata]);

                using var gzipStream = new GZipStream(destStream, CompressionMode.Decompress);
                using var decompressedStream = new MemoryStream();
                await gzipStream.CopyToAsync(decompressedStream);
                decompressedStream.Position = 0;

                Assert.Equal(uncompressedLength, decompressedStream.Length);

                using var reader = new StreamReader(decompressedStream);
                actual = await reader.ReadToEndAsync();
            }
            else
            {
                if (fileName == null)
                {
                    fileName = blobName;
                }

                using BlobDownloadInfo downloadInfo = await blob.DownloadAsync();
                using var reader = new StreamReader(downloadInfo.Content);
                actual = await reader.ReadToEndAsync();
            }

            var testDataFile = Path.Combine(TestData, testName, stepName, fileName);
            if (OverwriteTestData)
            {
                OverwriteTestDataAndCopyToSource(testDataFile, actual);
            }
            var expected = File.ReadAllText(testDataFile);
            Assert.Equal(expected, actual);

            return actual;
        }

        protected static void OverwriteTestDataAndCopyToSource(string testDataFile, string actual)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(testDataFile));
            File.WriteAllText(testDataFile, actual);

            var sourcePath = Path.GetFullPath(testDataFile);
            var projectDir = sourcePath.Contains("Worker.Logic.Test") ? "Worker.Logic.Test" : "Logic.Test";
            var repoDir = TestSettings.GetRepositoryRoot();
            var destPath = Path.Combine(repoDir, "test", projectDir, testDataFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

            File.Copy(sourcePath, destPath, overwrite: true);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public virtual async Task DisposeAsync()
        {
            try
            {
                // Global assertions
                AssertOnlyInfoLogsOrLess();
            }
            finally
            {
                // Clean up
                var blobServiceClient = await ServiceClientFactory.GetBlobServiceClientAsync();
                var containerItems = await blobServiceClient.GetBlobContainersAsync(prefix: StoragePrefix).ToListAsync();
                foreach (var containerItem in containerItems)
                {
                    await blobServiceClient.DeleteBlobContainerAsync(containerItem.Name);
                }

                var queueServiceClient = await ServiceClientFactory.GetQueueServiceClientAsync();
                var queueItems = await queueServiceClient.GetQueuesAsync(prefix: StoragePrefix).ToListAsync();
                foreach (var queueItem in queueItems)
                {
                    await queueServiceClient.DeleteQueueAsync(queueItem.Name);
                }

                var tableServiceClient = await ServiceClientFactory.GetTableServiceClientAsync();
                var tableItems = await tableServiceClient.QueryAsync(prefix: StoragePrefix).ToListAsync();
                foreach (var tableItem in tableItems)
                {
                    await tableServiceClient.DeleteTableAsync(tableItem.Name);
                }
            }
        }

        private void AssertOnlyInfoLogsOrLess()
        {
            var warningOrGreater = LogLevelToCount
                .Where(x => x.Key >= AssertLogLevel)
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Key)
                .ToList();
            foreach ((var logLevel, var count) in warningOrGreater)
            {
                Logger.LogInformation("There were {Count} {LogLevel} log messages.", count, logLevel);
            }
            Assert.Empty(warningOrGreater);
        }

        public static string SerializeTestJson(object obj)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters =
                {
                    new JsonStringEnumConverter(),
                },
            });

            return json.Replace("\r\n", "\n");
        }

        public static HttpRequestMessage Clone(HttpRequestMessage req)
        {
            var clone = new HttpRequestMessage(req.Method, req.RequestUri)
            {
                Content = req.Content,
                Version = req.Version
            };

            foreach (var header in req.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        public delegate Task<HttpResponseMessage> GetResponseAsync(CancellationToken token);
        public delegate Task<HttpResponseMessage> SendMessageAsync(HttpRequestMessage request, CancellationToken token);
        public delegate Task<HttpResponseMessage> SendMessageWithBaseAsync(HttpRequestMessage request, SendMessageAsync baseSendAsync, CancellationToken token);

        public class TestHttpMessageHandlerFactory : INuGetInsightsHttpMessageHandlerFactory
        {
            public SendMessageWithBaseAsync OnSendAsync { get; set; }

            public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new ConcurrentQueue<HttpRequestMessage>();

            public ConcurrentQueue<HttpResponseMessage> Responses { get; } = new ConcurrentQueue<HttpResponseMessage>();

            public DelegatingHandler Create()
            {
                return new TestHttpMessageHandler(async (req, baseSendAsync, token) =>
                {
                    if (OnSendAsync != null)
                    {
                        return await OnSendAsync(req, baseSendAsync, token);
                    }

                    return null;
                }, Requests, Responses);
            }
        }

        public class TestHttpMessageHandler : DelegatingHandler
        {
            private readonly SendMessageWithBaseAsync _onSendAsync;
            private readonly ConcurrentQueue<HttpRequestMessage> _requestQueue;
            private readonly ConcurrentQueue<HttpResponseMessage> _responseQueue;

            public TestHttpMessageHandler(
                SendMessageWithBaseAsync onSendAsync,
                ConcurrentQueue<HttpRequestMessage> requestQueue,
                ConcurrentQueue<HttpResponseMessage> responseQueue)
            {
                _onSendAsync = onSendAsync;
                _requestQueue = requestQueue;
                _responseQueue = responseQueue;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                _requestQueue.Enqueue(request);

                var response = await _onSendAsync(request, base.SendAsync, token);
                if (response != null)
                {
                    _responseQueue.Enqueue(response);
                    return response;
                }

                response = await base.SendAsync(request, token);
                _responseQueue.Enqueue(response);
                return response;
            }
        }

        public class TestServiceClientFactory : ServiceClientFactory
        {
            public TestServiceClientFactory(
                HttpClientHandler httpClientHandler,
                IOptions<NuGetInsightsSettings> options,
                ILogger<ServiceClientFactory> logger) : base(options, logger)
            {
                HttpClientHandler = httpClientHandler;
            }

            public HttpClientHandler HttpClientHandler { get; }
            public TestHttpMessageHandlerFactory HandlerFactory { get; } = new TestHttpMessageHandlerFactory();

            protected override HttpPipelineTransport GetHttpPipelineTransport()
            {
                var testHandler = HandlerFactory.Create();
                testHandler.InnerHandler = HttpClientHandler;
                return new HttpClientTransport(testHandler);
            }
        }
    }
}
