﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Knapcode.ExplorePackages.Entities;
using Knapcode.MiniZip;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Knapcode.ExplorePackages.Logic
{
    public class PackageService : IPackageService
    {
        private static readonly IMapper Mapper;
        private readonly PackageCommitEnumerator _enumerator;
        private readonly ICommitCondition _commitCondition;
        private readonly EntityContextFactory _entityContextFactory;
        private readonly ILogger<PackageService> _logger;

        static PackageService()
        {
            var mapperConfiguration = new MapperConfiguration(expression =>
            {
                expression
                    .CreateMap<PackageArchiveEntity, PackageArchiveEntity>()
                    .ForMember(x => x.PackageKey, x => x.Ignore())
                    .ForMember(x => x.Package, x => x.Ignore())
                    .ForMember(x => x.PackageEntries, x => x.Ignore());

                expression
                    .CreateMap<PackageEntryEntity, PackageEntryEntity>()
                    .ForMember(x => x.PackageEntryKey, x => x.Ignore())
                    .ForMember(x => x.PackageKey, x => x.Ignore())
                    .ForMember(x => x.PackageArchive, x => x.Ignore())
                    .ForMember(x => x.Index, x => x.Ignore());
            });

            Mapper = mapperConfiguration.CreateMapper();
        }

        public PackageService(
            PackageCommitEnumerator enumerator,
            ICommitCondition commitCondition,
            EntityContextFactory entityContextFactory,
            ILogger<PackageService> logger)
        {
            _enumerator = enumerator;
            _commitCondition = commitCondition;
            _entityContextFactory = entityContextFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyDictionary<string, PackageRegistrationEntity>> AddPackageRegistrationsAsync(
            IEnumerable<string> ids,
            bool includeCatalogPackageRegistrations,
            bool includePackages)
        {
            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                var registrations = await AddPackageRegistrationsAsync(
                    entityContext,
                    ids,
                    includeCatalogPackageRegistrations,
                    includePackages);

                await entityContext.SaveChangesAsync();

                return registrations;
            }
        }

        public async Task<PackageEntity> GetPackageOrNullAsync(string id, string version)
        {
            var normalizedVersion = NuGetVersion.Parse(version).ToNormalizedString();
            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                return await entityContext
                    .Packages
                    .Include(x => x.PackageRegistration)
                    .Include(x => x.V2Package)
                    .Include(x => x.CatalogPackage)
                    .Where(x => x.PackageRegistration.Id == id && x.Version == normalizedVersion)
                    .FirstOrDefaultAsync();
            }
        }

        public async Task<IReadOnlyList<PackageEntity>> GetBatchAsync(IReadOnlyList<PackageIdentity> identities)
        {
            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                var identityStrings = identities
                    .Select(x => $"{x.Id}/{x.Version}")
                    .ToList();

                return await entityContext
                    .Packages
                    .Include(x => x.PackageRegistration)
                    .Where(x => identityStrings.Contains(x.Identity))
                    .ToListAsync();
            }
        }

        private async Task<IReadOnlyDictionary<string, PackageRegistrationEntity>> AddPackageRegistrationsAsync(
            IEntityContext entityContext,
            IEnumerable<string> ids,
            bool includeCatalogPackageRegistrations,
            bool includePackages)
        {
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

            IQueryable<PackageRegistrationEntity> existingRegistrationsQueryable = entityContext
                .PackageRegistrations;

            if (includeCatalogPackageRegistrations)
            {
                existingRegistrationsQueryable = existingRegistrationsQueryable
                    .Include(x => x.CatalogPackageRegistration);
            }

            if (includePackages)
            {
                existingRegistrationsQueryable = existingRegistrationsQueryable
                    .Include(x => x.Packages)
                    .ThenInclude(x => x.CatalogPackage);
            }

            var existingRegistrations = await existingRegistrationsQueryable
                .Where(x => idSet.Contains(x.Id))
                .ToListAsync();

            idSet.ExceptWith(existingRegistrations.Select(x => x.Id));

            var newRegistrations = idSet
                .Select(x => new PackageRegistrationEntity
                {
                    Id = x,
                    Packages = new List<PackageEntity>(),
                })
                .ToList();

            await entityContext.AddRangeAsync(newRegistrations);

            return existingRegistrations
                .Concat(newRegistrations)
                .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<IReadOnlyDictionary<string, long>>  AddOrUpdatePackagesAsync<T>(
            QueryEntities<PackageEntity> getPackages,
            IEnumerable<T> foreignPackages,
            Func<IEnumerable<T>, IEnumerable<T>> sort,
            Func<T, string> getId,
            Func<T, string> getVersion,
            Func<PackageEntity, T, Task> initializePackageFromForeignAsync,
            Func<PackageEntity, T, Task> updatePackageFromForeignAsync,
            Action<IEntityContext, PackageEntity, PackageEntity> updateExistingPackage,
            bool includeCatalogPackageRegistrations)
        {
            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                var packageRegistrations = await AddPackageRegistrationsAsync(
                    entityContext,
                    foreignPackages.Select(getId),
                    includeCatalogPackageRegistrations: includeCatalogPackageRegistrations,
                    includePackages: false);

                // Create a mapping from package identity to latest package.
                var identityToLatest = new Dictionary<string, PackageEntity>(StringComparer.OrdinalIgnoreCase);
                foreach (var foreignPackage in foreignPackages)
                {
                    var id = getId(foreignPackage);
                    var version = NuGetVersion.Parse(getVersion(foreignPackage)).ToNormalizedString();
                    var identity = $"{id}/{version}";
                    
                    if (!identityToLatest.TryGetValue(identity, out var latestPackage))
                    {
                        latestPackage = new PackageEntity
                        {
                            PackageRegistration = packageRegistrations[id],
                            Version = version,
                            Identity = identity,
                        };

                        await initializePackageFromForeignAsync(latestPackage, foreignPackage);

                        identityToLatest[latestPackage.Identity] = latestPackage;
                    }
                    else
                    {
                        // TODO: remove this step and only use updateExistingPackage, somehow.
                        await updatePackageFromForeignAsync(latestPackage, foreignPackage);
                    }
                }

                var getExistingStopwatch = Stopwatch.StartNew();
                var identities = identityToLatest.Keys.ToList();

                var existingPackages = await getPackages(entityContext)
                    .Where(p => identities.Contains(p.Identity))
                    .ToListAsync();

                _logger.LogInformation(
                    "Got {ExistingCount} existing {TypeName} instances. {ElapsedMilliseconds}ms",
                    existingPackages.Count,
                    typeof(T).Name,
                    getExistingStopwatch.ElapsedMilliseconds);

                // Keep track of the resulting package keys.
                var identityToPackageKey = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

                // Update existing records.
                foreach (var existingPackage in existingPackages)
                {
                    var latestPackage = identityToLatest[existingPackage.Identity];
                    identityToLatest.Remove(existingPackage.Identity);

                    updateExistingPackage(entityContext, existingPackage, latestPackage);

                    identityToPackageKey.Add(existingPackage.Identity, existingPackage.PackageKey);
                }

                // Add new records.
                await entityContext.Packages.AddRangeAsync(identityToLatest.Values);

                // Commit the changes.
                var commitStopwatch = Stopwatch.StartNew();
                var changes = await entityContext.SaveChangesAsync();
                _logger.LogInformation(
                    "Committed {Changes} {TypeName} changes. {ElapsedMilliseconds}ms",
                    changes,
                    typeof(T).Name,
                    commitStopwatch.ElapsedMilliseconds);

                // Add the new package keys.
                foreach (var pair in identityToLatest)
                {
                    identityToPackageKey.Add(pair.Key, pair.Value.PackageKey);
                }

                return identityToPackageKey;
            }
        }

        public async Task AddOrUpdatePackagesAsync(IEnumerable<PackageArchiveMetadata> metadataSequence)
        {
            await AddOrUpdatePackagesAsync(
                x => x
                    .Packages
                    .Include(y => y.PackageArchive)
                    .ThenInclude(y => y.PackageEntries),
                metadataSequence,
                x => x,
                d => d.Id,
                d => d.Version,
                (p, f) =>
                {
                    p.PackageArchive = Initialize(new PackageArchiveEntity(), f);
                    return Task.CompletedTask;
                },
                (p, f) =>
                {
                    Initialize(p.PackageArchive, f);
                    return Task.CompletedTask;
                },
                (c, pe, pl) =>
                {
                    if (pe.PackageArchive == null)
                    {
                        pe.PackageArchive = pl.PackageArchive;
                    }
                    else
                    {
                        Update(c, pe.PackageArchive, pl.PackageArchive);
                    }
                },
                includeCatalogPackageRegistrations: false);
        }

        private void Update(IEntityContext entityContext, PackageArchiveEntity existing, PackageArchiveEntity latest)
        {
            Mapper.Map(latest, existing);

            latest.PackageEntries.Sort((a, b) => a.Index.CompareTo(b.Index));
            existing.PackageEntries.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            for (var index = 0; index < latest.PackageEntries.Count; index++)
            {
                var latestEntry = latest.PackageEntries[index];

                if (index < existing.PackageEntries.Count)
                {
                    var existingEntry = existing.PackageEntries[index];

                    if (existingEntry.Index != (ulong)index)
                    {
                        throw new InvalidOperationException("One of the package archive entries seems to have an incorrect index.");
                    }

                    Mapper.Map(latestEntry, existingEntry);
                }
                else if (index == existing.PackageEntries.Count)
                {
                    latestEntry.PackageKey = existing.PackageKey;
                    latestEntry.PackageArchive = existing;

                    existing.PackageEntries.Add(latestEntry);

                    entityContext.PackageEntries.Add(latestEntry);
                }
                else
                {
                    throw new InvalidOperationException("The list of existing package entries should have grown.");
                }
            }

            while (latest.PackageEntries.Count < existing.PackageEntries.Count)
            {
                var last = existing.PackageEntries.Last();
                existing.PackageEntries.RemoveAt(existing.PackageEntries.Count - 1);
                entityContext.PackageEntries.Remove(last);
            }
        }

        private PackageArchiveEntity Initialize(PackageArchiveEntity entity, PackageArchiveMetadata metadata)
        {
            entity.Size = metadata.Size;
            entity.EntryCount = metadata.ZipDirectory.Entries.Count;

            entity.CentralDirectorySize = metadata.ZipDirectory.CentralDirectorySize;
            entity.Comment = metadata.ZipDirectory.Comment;
            entity.CommentSize = metadata.ZipDirectory.CommentSize;
            entity.DiskWithStartOfCentralDirectory = metadata.ZipDirectory.DiskWithStartOfCentralDirectory;
            entity.EntriesForWholeCentralDirectory = metadata.ZipDirectory.EntriesForWholeCentralDirectory;
            entity.EntriesInThisDisk = metadata.ZipDirectory.EntriesInThisDisk;
            entity.NumberOfThisDisk = metadata.ZipDirectory.NumberOfThisDisk;
            entity.OffsetAfterEndOfCentralDirectory = metadata.ZipDirectory.OffsetAfterEndOfCentralDirectory;
            entity.OffsetOfCentralDirectory = metadata.ZipDirectory.OffsetOfCentralDirectory;

            entity.Zip64CentralDirectorySize = metadata.ZipDirectory.Zip64?.CentralDirectorySize;
            entity.Zip64DiskWithStartOfCentralDirectory = metadata.ZipDirectory.Zip64?.DiskWithStartOfCentralDirectory;
            entity.Zip64DiskWithStartOfEndOfCentralDirectory = metadata.ZipDirectory.Zip64?.DiskWithStartOfEndOfCentralDirectory;
            entity.Zip64EndOfCentralDirectoryOffset = metadata.ZipDirectory.Zip64?.EndOfCentralDirectoryOffset;
            entity.Zip64EntriesForWholeCentralDirectory = metadata.ZipDirectory.Zip64?.EntriesForWholeCentralDirectory;
            entity.Zip64EntriesInThisDisk = metadata.ZipDirectory.Zip64?.EntriesInThisDisk;
            entity.Zip64NumberOfThisDisk = metadata.ZipDirectory.Zip64?.NumberOfThisDisk;
            entity.Zip64OffsetAfterEndOfCentralDirectoryLocator = metadata.ZipDirectory.Zip64?.OffsetAfterEndOfCentralDirectoryLocator;
            entity.Zip64OffsetOfCentralDirectory = metadata.ZipDirectory.Zip64?.OffsetOfCentralDirectory;
            entity.Zip64SizeOfCentralDirectoryRecord = metadata.ZipDirectory.Zip64?.SizeOfCentralDirectoryRecord;
            entity.Zip64TotalNumberOfDisks = metadata.ZipDirectory.Zip64?.TotalNumberOfDisks;
            entity.Zip64VersionMadeBy = metadata.ZipDirectory.Zip64?.VersionMadeBy;
            entity.Zip64VersionToExtract = metadata.ZipDirectory.Zip64?.VersionToExtract;

            // Make sure the entries are sorted by index.
            if (entity.PackageEntries == null)
            {
                entity.PackageEntries = new List<PackageEntryEntity>();
            }

            entity.PackageEntries.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            for (var index = 0; index < metadata.ZipDirectory.Entries.Count; index++)
            {
                var entryMetadata = metadata.ZipDirectory.Entries[index];
                PackageEntryEntity entryEntity;
                if (index < entity.PackageEntries.Count)
                {
                    entryEntity = entity.PackageEntries[index];

                    if (entryEntity.Index != (ulong)index)
                    {
                        throw new InvalidOperationException("One of the package archive entries seems to have an incorrect index.");
                    }
                }
                else if(index == entity.PackageEntries.Count)
                {
                    entryEntity = new PackageEntryEntity
                    {
                        PackageArchive = entity,
                        Index = (ulong)index,
                    };

                    entity.PackageEntries.Add(entryEntity);
                }
                else
                {
                    throw new InvalidOperationException("The list of existing package entries should have grown.");
                }

                Initialize(entryEntity, entryMetadata);
            }

            entity
                .PackageEntries
                .RemoveAll(x => x.Index >= (ulong)metadata.ZipDirectory.Entries.Count);

            return entity;
        }

        private PackageEntryEntity Initialize(PackageEntryEntity entity, CentralDirectoryHeader metadata)
        {
            entity.Comment = metadata.Comment;
            entity.CommentSize = metadata.CommentSize;
            entity.CompressedSize = metadata.CompressedSize;
            entity.CompressionMethod = metadata.CompressionMethod;
            entity.Crc32 = metadata.Crc32;
            entity.DiskNumberStart = metadata.DiskNumberStart;
            entity.ExternalAttributes = metadata.ExternalAttributes;
            entity.ExtraField = metadata.ExtraField;
            entity.ExtraFieldSize = metadata.ExtraFieldSize;
            entity.Flags = metadata.Flags;
            entity.InternalAttributes = metadata.InternalAttributes;
            entity.LastModifiedDate = metadata.LastModifiedDate;
            entity.LastModifiedTime = metadata.LastModifiedTime;
            entity.LocalHeaderOffset = metadata.LocalHeaderOffset;
            entity.Name = metadata.Name;
            entity.NameSize = metadata.NameSize;
            entity.UncompressedSize = metadata.UncompressedSize;
            entity.VersionMadeBy = metadata.VersionMadeBy;
            entity.VersionToExtract = metadata.VersionToExtract;

            return entity;
        }

        public async Task AddOrUpdatePackagesAsync(IEnumerable<PackageDownloads> packageDownloads)
        {
            var identityToPackageDownloads = packageDownloads
                .GroupBy(x => new PackageIdentity(x.Id, x.Version))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Downloads).First());

            var identityToPackageKey = await AddOrUpdatePackagesAsync(identityToPackageDownloads.Keys);

            var packageKeyDownloadPairs = identityToPackageDownloads
                .Select(x => KeyValuePairFactory.Create(identityToPackageKey[x.Key.Value], x.Value.Downloads))
                .OrderBy(x => x.Key)
                .ToList();

            var changeCount = 0;
            var stopwatch = Stopwatch.StartNew();

            using (var entityContext = await _entityContextFactory.GetAsync())
            using (var connection = entityContext.Database.GetDbConnection())
            {
                await _commitCondition.VerifyAsync();
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    var command = connection.CreateCommand();

                    command.Transaction = transaction;

                    if (entityContext is SqlServerEntityContext)
                    {
                        command.CommandText = @"
                            MERGE PackageDownloads pd
                            USING (VALUES (@PackageKey, @Downloads)) AS i(PackageKey, Downloads)
                            ON pd.PackageKey = i.PackageKey
                            WHEN MATCHED THEN
                                UPDATE SET pd.Downloads = i.Downloads
                            WHEN NOT MATCHED THEN
                                INSERT (PackageKey, Downloads) VALUES (i.PackageKey, i.Downloads);";
                    }
                    else if (entityContext is SqliteEntityContext)
                    {
                        command.CommandText = @"
                            INSERT OR REPLACE INTO PackageDownloads (PackageKey, Downloads)
                            VALUES (@PackageKey, @Downloads)";
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    var packageKeyParameter = command.CreateParameter();
                    packageKeyParameter.ParameterName = "PackageKey";
                    packageKeyParameter.DbType = DbType.Int64;
                    command.Parameters.Add(packageKeyParameter);

                    var downloadsParameter = command.CreateParameter();
                    downloadsParameter.ParameterName = "Downloads";
                    downloadsParameter.DbType = DbType.Int64;
                    command.Parameters.Add(downloadsParameter);

                    foreach (var pair in packageKeyDownloadPairs)
                    {
                        packageKeyParameter.Value = pair.Key;
                        downloadsParameter.Value = pair.Value;
                        changeCount += await command.ExecuteNonQueryAsync();
                    }

                    transaction.Commit();
                }
            }

            _logger.LogInformation(
                "Committed {ChangeCount} package download changes. {ElapsedMilliseconds}ms.",
                changeCount,
                stopwatch.ElapsedMilliseconds);
        }

        public async Task<IReadOnlyDictionary<string, long>> AddOrUpdatePackagesAsync(IEnumerable<PackageIdentity> identities)
        {
            return await AddOrUpdatePackagesAsync(
                x => x.Packages,
                identities,
                x => x.OrderBy(y => y.Id).ThenBy(y => y.Version),
                x => x.Id,
                x => x.Version,
                (p, i) =>
                {
                    return Task.CompletedTask;
                },
                (p, i) =>
                {
                    return Task.CompletedTask;
                },
                (c, pe, pl) => {},
                includeCatalogPackageRegistrations: false);
        }

        public async Task AddOrUpdatePackagesAsync(IEnumerable<V2Package> v2Packages)
        {
            await AddOrUpdatePackagesAsync(
                x => x
                    .Packages
                    .Include(y => y.V2Package),
                v2Packages,
                x => x.OrderBy(v2 => v2.Created),
                v2 => v2.Id,
                v2 => v2.Version,
                (p, v2) =>
                {
                    p.V2Package = ToEntity(v2);
                    return Task.CompletedTask;
                },
                (p, v2) =>
                {
                    var e = ToEntity(v2);
                    if (e.LastUpdatedTimestamp < p.V2Package.LastUpdatedTimestamp)
                    {
                        return Task.CompletedTask;
                    }

                    p.V2Package.CreatedTimestamp = e.CreatedTimestamp;
                    p.V2Package.LastEditedTimestamp = e.LastEditedTimestamp;
                    p.V2Package.PublishedTimestamp = e.PublishedTimestamp;
                    p.V2Package.LastUpdatedTimestamp = e.LastUpdatedTimestamp;
                    p.V2Package.Listed = e.Listed;

                    return Task.CompletedTask;
                },
                (c, pe, pl) =>
                {
                    if (pe.V2Package == null)
                    {
                        pe.V2Package = pl.V2Package;
                    }
                    else if (pe.V2Package.LastUpdatedTimestamp <= pl.V2Package.LastUpdatedTimestamp)
                    {
                        pe.V2Package.CreatedTimestamp = pl.V2Package.CreatedTimestamp;
                        pe.V2Package.LastEditedTimestamp = pl.V2Package.LastEditedTimestamp;
                        pe.V2Package.PublishedTimestamp = pl.V2Package.PublishedTimestamp;
                        pe.V2Package.LastUpdatedTimestamp = pl.V2Package.LastUpdatedTimestamp;
                        pe.V2Package.Listed = pl.V2Package.Listed;
                    }
                },
                includeCatalogPackageRegistrations: false);
        }

        private static V2PackageEntity ToEntity(V2Package v2)
        {
            return new V2PackageEntity
            {
                CreatedTimestamp = v2.Created.UtcTicks,
                LastEditedTimestamp = v2.LastEdited?.UtcTicks,
                PublishedTimestamp = v2.Published.UtcTicks,
                LastUpdatedTimestamp = v2.LastUpdated.UtcTicks,
                Listed = v2.Listed,
            };
        }

        public async Task SetDeletedPackagesAsUnlistedInV2Async(IEnumerable<CatalogLeafItem> entries)
        {
            var identities = entries
                .Where(x => x.IsPackageDelete())
                .Select(x => $"{x.PackageId}/{x.ParsePackageVersion().ToNormalizedString()}")
                .Distinct()
                .ToList();
            _logger.LogInformation("Found {Count} catalog leaves containing deleted packages.", identities.Count);

            if (!identities.Any())
            {
                _logger.LogInformation("No updates necessary.");
                return;
            }

            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                var selectStopwatch = Stopwatch.StartNew();
                var existingPackages = await entityContext
                    .Packages
                    .Include(x => x.V2Package)
                    .Include(x => x.CatalogPackage)
                    .Where(x => x.V2Package != null)
                    .Where(p => identities.Contains(p.Identity))
                    .ToListAsync();
                _logger.LogInformation(
                    "Found {Count} corresponding V2 packages. {ElapsedMilliseconds}ms",
                    existingPackages.Count,
                    selectStopwatch.ElapsedMilliseconds);

                foreach (var existingPackage in existingPackages)
                {
                    // Only unlist the V2 package if the catalog package is deleted. It's possible that we are
                    // processing an old delete entry in the catalog that was superceded by the package being
                    // re -pushed. This is the "delete then recreate" flow which should be very rare but does happen
                    // in practice.
                    if (existingPackage.CatalogPackage.Deleted)
                    {
                        existingPackage.V2Package.Listed = false;
                    }
                }

                var commitStopwatch = Stopwatch.StartNew();
                var changeCount = await entityContext.SaveChangesAsync();
                _logger.LogInformation(
                    "Committed {Count} changes. {ElapsedMilliseconds}ms",
                    changeCount,
                    commitStopwatch.ElapsedMilliseconds);
            }
        }

        public async Task<IReadOnlyList<PackageEntity>> GetPackagesWithDependenciesAsync(IReadOnlyList<PackageIdentity> identities)
        {
            using (var entityContext = await _entityContextFactory.GetAsync())
            {
                var identityValues = identities
                    .Select(x => x.Value)
                    .ToList();

                var packages = await entityContext
                    .Packages
                    .Include(x => x.PackageDependencies)
                    .ThenInclude(x => x.DependencyPackageRegistration)
                    .Where(x => identityValues.Contains(x.Identity))
                    .ToListAsync();

                var missingIdentityValues = identityValues
                    .Except(packages.Select(x => x.Identity), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x);

                if (missingIdentityValues.Any())
                {
                    _logger.LogError("The following package identities do not exist: {MissingIdentityValues}", missingIdentityValues);
                    throw new InvalidOperationException("Some packages do not exist.");
                }

                return packages;
            }
        }


        /// <summary>
        /// Adds the provided catalog entries to the database. Catalog entries are processed in the order provided.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, long>> AddOrUpdatePackagesAsync(
            IEnumerable<CatalogLeafItem> entries,
            IReadOnlyDictionary<CatalogLeafItem, bool> entryToListed)
        {
            return await AddOrUpdatePackagesAsync(
                x => x
                    .Packages
                    .Include(y => y.CatalogPackage),
                entryToListed.Keys,
                x => x.OrderBy(c => c.CommitTimestamp),
                c => c.PackageId,
                c => c.ParsePackageVersion().ToNormalizedString(),
                (p, cp) =>
                {
                    // Initialize the catalog package registration.
                    if (p.PackageRegistration.CatalogPackageRegistration == null)
                    {
                        p.PackageRegistration.CatalogPackageRegistration = new CatalogPackageRegistrationEntity
                        {
                            PackageRegistrationKey = p.PackageRegistration.PackageRegistrationKey,
                            FirstCommitTimestamp = cp.CommitTimestamp.UtcTicks,
                            LastCommitTimestamp = cp.CommitTimestamp.UtcTicks,
                        };
                    }

                    // Initialize the catalog package.
                    p.CatalogPackage = new CatalogPackageEntity
                    {
                        Deleted = cp.IsPackageDelete(),
                        FirstCommitTimestamp = cp.CommitTimestamp.UtcTicks,
                        LastCommitTimestamp = cp.CommitTimestamp.UtcTicks,
                        Listed = entryToListed[cp],
                    };
                    return Task.CompletedTask;
                },
                (p, cp) =>
                {
                    // Update the catalog package registration.
                    p.PackageRegistration.CatalogPackageRegistration.FirstCommitTimestamp = Math.Min(
                        p.PackageRegistration.CatalogPackageRegistration.FirstCommitTimestamp,
                        cp.CommitTimestamp.UtcTicks);

                    p.PackageRegistration.CatalogPackageRegistration.FirstCommitTimestamp = Math.Max(
                        p.PackageRegistration.CatalogPackageRegistration.LastCommitTimestamp,
                        cp.CommitTimestamp.UtcTicks);

                    // Update the catalog package.
                    if (p.CatalogPackage.LastCommitTimestamp < cp.CommitTimestamp.UtcTicks)
                    {
                        p.CatalogPackage.Deleted = cp.IsPackageDelete();
                        p.CatalogPackage.Listed = entryToListed[cp];
                    }

                    p.CatalogPackage.FirstCommitTimestamp = Math.Min(
                        p.CatalogPackage.FirstCommitTimestamp,
                        cp.CommitTimestamp.UtcTicks);

                    p.CatalogPackage.LastCommitTimestamp = Math.Max(
                        p.CatalogPackage.LastCommitTimestamp,
                        cp.CommitTimestamp.UtcTicks);

                    return Task.CompletedTask;
                },
                (c, pe, pl) =>
                {
                    // Update the catalog package registration.
                    if (pe.PackageRegistration.CatalogPackageRegistration == null)
                    {
                        pe.PackageRegistration.CatalogPackageRegistration = pl.PackageRegistration.CatalogPackageRegistration;
                    }
                    else
                    {
                        pe.PackageRegistration.CatalogPackageRegistration.FirstCommitTimestamp = Math.Min(
                           pe.PackageRegistration.CatalogPackageRegistration.FirstCommitTimestamp,
                           pl.CatalogPackage.FirstCommitTimestamp);

                        pe.PackageRegistration.CatalogPackageRegistration.LastCommitTimestamp = Math.Max(
                            pe.PackageRegistration.CatalogPackageRegistration.LastCommitTimestamp,
                            pl.CatalogPackage.LastCommitTimestamp);
                    }

                    // Update the catalog package.
                    if (pe.CatalogPackage == null)
                    {
                        pe.CatalogPackage = pl.CatalogPackage;
                    }
                    else
                    {
                        if (pe.CatalogPackage.LastCommitTimestamp < pl.CatalogPackage.LastCommitTimestamp)
                        {
                            pe.CatalogPackage.Deleted = pl.CatalogPackage.Deleted;
                            pe.CatalogPackage.Listed = pl.CatalogPackage.Listed;
                        }

                        pe.CatalogPackage.FirstCommitTimestamp = Math.Min(
                            pe.CatalogPackage.FirstCommitTimestamp,
                            pl.CatalogPackage.FirstCommitTimestamp);

                        pe.CatalogPackage.LastCommitTimestamp = Math.Max(
                            pe.CatalogPackage.LastCommitTimestamp,
                            pl.CatalogPackage.LastCommitTimestamp);
                    }
                },
                includeCatalogPackageRegistrations: true);
        }
    }
}
