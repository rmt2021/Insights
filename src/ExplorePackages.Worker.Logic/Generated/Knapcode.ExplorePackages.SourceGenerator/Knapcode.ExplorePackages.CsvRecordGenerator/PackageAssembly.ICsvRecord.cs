﻿// <auto-generated />

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Knapcode.ExplorePackages;

namespace Knapcode.ExplorePackages.Worker.FindPackageAssembly
{
    /* Kusto DDL:

    .drop table JverPackageAssemblies;

    .create table JverPackageAssemblies (
        ScanId: guid,
        ScanTimestamp: datetime,
        Id: string,
        Version: string,
        CatalogCommitTimestamp: datetime,
        Created: datetime,
        ResultType: string,
        Path: string,
        FileName: string,
        FileExtension: string,
        TopLevelFolder: string,
        CompressedLength: long,
        EntryUncompressedLength: long,
        ActualUncompressedLength: long,
        FileSHA256: string,
        HasException: bool,
        AssemblyName: string,
        AssemblyVersion: string,
        Culture: string,
        AssemblyNameHasCultureNotFoundException: bool,
        AssemblyNameHasFileLoadException: bool,
        PublicKeyToken: string,
        PublicKeyTokenHasSecurityException: bool,
        HashAlgorithm: string,
        HasPublicKey: bool,
        PublicKeyLength: int,
        PublicKeySHA1: string
    );

    .create table JverPackageAssemblies ingestion csv mapping 'JverPackageAssemblies_mapping'
    '['
        '{"Column":"ScanId","DataType":"guid","Properties":{"Ordinal":0}},'
        '{"Column":"ScanTimestamp","DataType":"datetime","Properties":{"Ordinal":1}},'
        '{"Column":"Id","DataType":"string","Properties":{"Ordinal":2}},'
        '{"Column":"Version","DataType":"string","Properties":{"Ordinal":3}},'
        '{"Column":"CatalogCommitTimestamp","DataType":"datetime","Properties":{"Ordinal":4}},'
        '{"Column":"Created","DataType":"datetime","Properties":{"Ordinal":5}},'
        '{"Column":"ResultType","DataType":"string","Properties":{"Ordinal":6}},'
        '{"Column":"Path","DataType":"string","Properties":{"Ordinal":7}},'
        '{"Column":"FileName","DataType":"string","Properties":{"Ordinal":8}},'
        '{"Column":"FileExtension","DataType":"string","Properties":{"Ordinal":9}},'
        '{"Column":"TopLevelFolder","DataType":"string","Properties":{"Ordinal":10}},'
        '{"Column":"CompressedLength","DataType":"long","Properties":{"Ordinal":11}},'
        '{"Column":"EntryUncompressedLength","DataType":"long","Properties":{"Ordinal":12}},'
        '{"Column":"ActualUncompressedLength","DataType":"long","Properties":{"Ordinal":13}},'
        '{"Column":"FileSHA256","DataType":"string","Properties":{"Ordinal":14}},'
        '{"Column":"HasException","DataType":"bool","Properties":{"Ordinal":15}},'
        '{"Column":"AssemblyName","DataType":"string","Properties":{"Ordinal":16}},'
        '{"Column":"AssemblyVersion","DataType":"string","Properties":{"Ordinal":17}},'
        '{"Column":"Culture","DataType":"string","Properties":{"Ordinal":18}},'
        '{"Column":"AssemblyNameHasCultureNotFoundException","DataType":"bool","Properties":{"Ordinal":19}},'
        '{"Column":"AssemblyNameHasFileLoadException","DataType":"bool","Properties":{"Ordinal":20}},'
        '{"Column":"PublicKeyToken","DataType":"string","Properties":{"Ordinal":21}},'
        '{"Column":"PublicKeyTokenHasSecurityException","DataType":"bool","Properties":{"Ordinal":22}},'
        '{"Column":"HashAlgorithm","DataType":"string","Properties":{"Ordinal":23}},'
        '{"Column":"HasPublicKey","DataType":"bool","Properties":{"Ordinal":24}},'
        '{"Column":"PublicKeyLength","DataType":"int","Properties":{"Ordinal":25}},'
        '{"Column":"PublicKeySHA1","DataType":"string","Properties":{"Ordinal":26}}'
    ']'

    */
    partial record PackageAssembly
    {
        public int FieldCount => 27;

        public void Write(List<string> fields)
        {
            fields.Add(ScanId.ToString());
            fields.Add(CsvUtility.FormatDateTimeOffset(ScanTimestamp));
            fields.Add(Id);
            fields.Add(Version);
            fields.Add(CsvUtility.FormatDateTimeOffset(CatalogCommitTimestamp));
            fields.Add(CsvUtility.FormatDateTimeOffset(Created));
            fields.Add(ResultType.ToString());
            fields.Add(Path);
            fields.Add(FileName);
            fields.Add(FileExtension);
            fields.Add(TopLevelFolder);
            fields.Add(CompressedLength.ToString());
            fields.Add(EntryUncompressedLength.ToString());
            fields.Add(ActualUncompressedLength.ToString());
            fields.Add(FileSHA256);
            fields.Add(CsvUtility.FormatBool(HasException));
            fields.Add(AssemblyName);
            fields.Add(AssemblyVersion?.ToString());
            fields.Add(Culture);
            fields.Add(CsvUtility.FormatBool(AssemblyNameHasCultureNotFoundException));
            fields.Add(CsvUtility.FormatBool(AssemblyNameHasFileLoadException));
            fields.Add(PublicKeyToken);
            fields.Add(CsvUtility.FormatBool(PublicKeyTokenHasSecurityException));
            fields.Add(HashAlgorithm.ToString());
            fields.Add(CsvUtility.FormatBool(HasPublicKey));
            fields.Add(PublicKeyLength.ToString());
            fields.Add(PublicKeySHA1);
        }

        public void Write(TextWriter writer)
        {
            writer.Write(ScanId);
            writer.Write(',');
            writer.Write(CsvUtility.FormatDateTimeOffset(ScanTimestamp));
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, Id);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, Version);
            writer.Write(',');
            writer.Write(CsvUtility.FormatDateTimeOffset(CatalogCommitTimestamp));
            writer.Write(',');
            writer.Write(CsvUtility.FormatDateTimeOffset(Created));
            writer.Write(',');
            writer.Write(ResultType);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, Path);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, FileName);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, FileExtension);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, TopLevelFolder);
            writer.Write(',');
            writer.Write(CompressedLength);
            writer.Write(',');
            writer.Write(EntryUncompressedLength);
            writer.Write(',');
            writer.Write(ActualUncompressedLength);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, FileSHA256);
            writer.Write(',');
            writer.Write(CsvUtility.FormatBool(HasException));
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, AssemblyName);
            writer.Write(',');
            writer.Write(AssemblyVersion);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, Culture);
            writer.Write(',');
            writer.Write(CsvUtility.FormatBool(AssemblyNameHasCultureNotFoundException));
            writer.Write(',');
            writer.Write(CsvUtility.FormatBool(AssemblyNameHasFileLoadException));
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, PublicKeyToken);
            writer.Write(',');
            writer.Write(CsvUtility.FormatBool(PublicKeyTokenHasSecurityException));
            writer.Write(',');
            writer.Write(HashAlgorithm);
            writer.Write(',');
            writer.Write(CsvUtility.FormatBool(HasPublicKey));
            writer.Write(',');
            writer.Write(PublicKeyLength);
            writer.Write(',');
            CsvUtility.WriteWithQuotes(writer, PublicKeySHA1);
            writer.WriteLine();
        }

        public async Task WriteAsync(TextWriter writer)
        {
            await writer.WriteAsync(ScanId.ToString());
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatDateTimeOffset(ScanTimestamp));
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, Id);
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, Version);
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatDateTimeOffset(CatalogCommitTimestamp));
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatDateTimeOffset(Created));
            await writer.WriteAsync(',');
            await writer.WriteAsync(ResultType.ToString());
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, Path);
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, FileName);
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, FileExtension);
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, TopLevelFolder);
            await writer.WriteAsync(',');
            await writer.WriteAsync(CompressedLength.ToString());
            await writer.WriteAsync(',');
            await writer.WriteAsync(EntryUncompressedLength.ToString());
            await writer.WriteAsync(',');
            await writer.WriteAsync(ActualUncompressedLength.ToString());
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, FileSHA256);
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatBool(HasException));
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, AssemblyName);
            await writer.WriteAsync(',');
            await writer.WriteAsync(AssemblyVersion?.ToString());
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, Culture);
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatBool(AssemblyNameHasCultureNotFoundException));
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatBool(AssemblyNameHasFileLoadException));
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, PublicKeyToken);
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatBool(PublicKeyTokenHasSecurityException));
            await writer.WriteAsync(',');
            await writer.WriteAsync(HashAlgorithm.ToString());
            await writer.WriteAsync(',');
            await writer.WriteAsync(CsvUtility.FormatBool(HasPublicKey));
            await writer.WriteAsync(',');
            await writer.WriteAsync(PublicKeyLength.ToString());
            await writer.WriteAsync(',');
            await CsvUtility.WriteWithQuotesAsync(writer, PublicKeySHA1);
            await writer.WriteLineAsync();
        }

        public PackageAssembly Read(Func<string> getNextField)
        {
            return new PackageAssembly
            {
                ScanId = CsvUtility.ParseNullable(getNextField(), Guid.Parse),
                ScanTimestamp = CsvUtility.ParseNullable(getNextField(), CsvUtility.ParseDateTimeOffset),
                Id = getNextField(),
                Version = getNextField(),
                CatalogCommitTimestamp = CsvUtility.ParseDateTimeOffset(getNextField()),
                Created = CsvUtility.ParseNullable(getNextField(), CsvUtility.ParseDateTimeOffset),
                ResultType = Enum.Parse<PackageAssemblyResultType>(getNextField()),
                Path = getNextField(),
                FileName = getNextField(),
                FileExtension = getNextField(),
                TopLevelFolder = getNextField(),
                CompressedLength = CsvUtility.ParseNullable(getNextField(), long.Parse),
                EntryUncompressedLength = CsvUtility.ParseNullable(getNextField(), long.Parse),
                ActualUncompressedLength = CsvUtility.ParseNullable(getNextField(), long.Parse),
                FileSHA256 = getNextField(),
                HasException = bool.Parse(getNextField()),
                AssemblyName = getNextField(),
                AssemblyVersion = CsvUtility.ParseReference(getNextField(), System.Version.Parse),
                Culture = getNextField(),
                AssemblyNameHasCultureNotFoundException = CsvUtility.ParseNullable(getNextField(), bool.Parse),
                AssemblyNameHasFileLoadException = CsvUtility.ParseNullable(getNextField(), bool.Parse),
                PublicKeyToken = getNextField(),
                PublicKeyTokenHasSecurityException = CsvUtility.ParseNullable(getNextField(), bool.Parse),
                HashAlgorithm = CsvUtility.ParseNullable(getNextField(), Enum.Parse<System.Reflection.AssemblyHashAlgorithm>),
                HasPublicKey = CsvUtility.ParseNullable(getNextField(), bool.Parse),
                PublicKeyLength = CsvUtility.ParseNullable(getNextField(), int.Parse),
                PublicKeySHA1 = getNextField(),
            };
        }
    }
}
