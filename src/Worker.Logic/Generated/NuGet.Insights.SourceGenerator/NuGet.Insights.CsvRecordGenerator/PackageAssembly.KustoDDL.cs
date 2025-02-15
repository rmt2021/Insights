﻿// <auto-generated />

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace NuGet.Insights
{
    static partial class KustoDDL
    {
        public const string PackageAssemblyDefaultTableName = "PackageAssemblies";

        public static readonly IReadOnlyList<string> PackageAssemblyDDL = new[]
        {
            ".drop table __TABLENAME__ ifexists",

            @".create table __TABLENAME__ (
    LowerId: string,
    Identity: string,
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
    EdgeCases: string,
    AssemblyName: string,
    AssemblyVersion: string,
    Culture: string,
    PublicKeyToken: string,
    HashAlgorithm: string,
    HasPublicKey: bool,
    PublicKeyLength: int,
    PublicKeySHA1: string,
    CustomAttributes: dynamic,
    CustomAttributesFailedDecode: dynamic,
    CustomAttributesTotalCount: int,
    CustomAttributesTotalDataLength: int
)",

            ".alter-merge table __TABLENAME__ policy retention softdelete = 30d",

            @".create table __TABLENAME__ ingestion csv mapping 'BlobStorageMapping'
'['
    '{""Column"":""LowerId"",""DataType"":""string"",""Properties"":{""Ordinal"":2}},'
    '{""Column"":""Identity"",""DataType"":""string"",""Properties"":{""Ordinal"":3}},'
    '{""Column"":""Id"",""DataType"":""string"",""Properties"":{""Ordinal"":4}},'
    '{""Column"":""Version"",""DataType"":""string"",""Properties"":{""Ordinal"":5}},'
    '{""Column"":""CatalogCommitTimestamp"",""DataType"":""datetime"",""Properties"":{""Ordinal"":6}},'
    '{""Column"":""Created"",""DataType"":""datetime"",""Properties"":{""Ordinal"":7}},'
    '{""Column"":""ResultType"",""DataType"":""string"",""Properties"":{""Ordinal"":8}},'
    '{""Column"":""Path"",""DataType"":""string"",""Properties"":{""Ordinal"":9}},'
    '{""Column"":""FileName"",""DataType"":""string"",""Properties"":{""Ordinal"":10}},'
    '{""Column"":""FileExtension"",""DataType"":""string"",""Properties"":{""Ordinal"":11}},'
    '{""Column"":""TopLevelFolder"",""DataType"":""string"",""Properties"":{""Ordinal"":12}},'
    '{""Column"":""CompressedLength"",""DataType"":""long"",""Properties"":{""Ordinal"":13}},'
    '{""Column"":""EntryUncompressedLength"",""DataType"":""long"",""Properties"":{""Ordinal"":14}},'
    '{""Column"":""ActualUncompressedLength"",""DataType"":""long"",""Properties"":{""Ordinal"":15}},'
    '{""Column"":""FileSHA256"",""DataType"":""string"",""Properties"":{""Ordinal"":16}},'
    '{""Column"":""EdgeCases"",""DataType"":""string"",""Properties"":{""Ordinal"":17}},'
    '{""Column"":""AssemblyName"",""DataType"":""string"",""Properties"":{""Ordinal"":18}},'
    '{""Column"":""AssemblyVersion"",""DataType"":""string"",""Properties"":{""Ordinal"":19}},'
    '{""Column"":""Culture"",""DataType"":""string"",""Properties"":{""Ordinal"":20}},'
    '{""Column"":""PublicKeyToken"",""DataType"":""string"",""Properties"":{""Ordinal"":21}},'
    '{""Column"":""HashAlgorithm"",""DataType"":""string"",""Properties"":{""Ordinal"":22}},'
    '{""Column"":""HasPublicKey"",""DataType"":""bool"",""Properties"":{""Ordinal"":23}},'
    '{""Column"":""PublicKeyLength"",""DataType"":""int"",""Properties"":{""Ordinal"":24}},'
    '{""Column"":""PublicKeySHA1"",""DataType"":""string"",""Properties"":{""Ordinal"":25}},'
    '{""Column"":""CustomAttributes"",""DataType"":""dynamic"",""Properties"":{""Ordinal"":26}},'
    '{""Column"":""CustomAttributesFailedDecode"",""DataType"":""dynamic"",""Properties"":{""Ordinal"":27}},'
    '{""Column"":""CustomAttributesTotalCount"",""DataType"":""int"",""Properties"":{""Ordinal"":28}},'
    '{""Column"":""CustomAttributesTotalDataLength"",""DataType"":""int"",""Properties"":{""Ordinal"":29}}'
']'",
        };

        public const string PackageAssemblyPartitioningPolicy = @".alter table __TABLENAME__ policy partitioning '{'
  '""PartitionKeys"": ['
    '{'
      '""ColumnName"": ""Identity"",'
      '""Kind"": ""Hash"",'
      '""Properties"": {'
        '""Function"": ""XxHash64"",'
        '""MaxPartitionCount"": 256'
      '}'
    '}'
  ']'
'}'";

        private static readonly bool PackageAssemblyAddTypeToDefaultTableName = AddTypeToDefaultTableName(typeof(NuGet.Insights.Worker.PackageAssemblyToCsv.PackageAssembly), PackageAssemblyDefaultTableName);

        private static readonly bool PackageAssemblyAddTypeToDDL = AddTypeToDDL(typeof(NuGet.Insights.Worker.PackageAssemblyToCsv.PackageAssembly), PackageAssemblyDDL);

        private static readonly bool PackageAssemblyAddTypeToPartitioningPolicy = AddTypeToPartitioningPolicy(typeof(NuGet.Insights.Worker.PackageAssemblyToCsv.PackageAssembly), PackageAssemblyPartitioningPolicy);
    }
}
