// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace NuGet.Insights.Worker.PackageAssemblyToCsv
{
    public partial record PackageAssembly : PackageRecord, ICsvRecord
    {
        public PackageAssembly()
        {
        }

        public PackageAssembly(Guid scanId, DateTimeOffset scanTimestamp, PackageDeleteCatalogLeaf leaf)
            : base(scanId, scanTimestamp, leaf)
        {
            ResultType = PackageAssemblyResultType.Deleted;
        }

        public PackageAssembly(Guid scanId, DateTimeOffset scanTimestamp, PackageDetailsCatalogLeaf leaf, PackageAssemblyResultType resultType)
            : base(scanId, scanTimestamp, leaf)
        {
            ResultType = resultType;
        }

        public PackageAssemblyResultType ResultType { get; set; }

        public string Path { get; set; }
        public string FileName { get; set; }
        public string FileExtension { get; set; }
        public string TopLevelFolder { get; set; }

        public long? CompressedLength { get; set; }
        public long? EntryUncompressedLength { get; set; }

        public long? ActualUncompressedLength { get; set; }
        public string FileSHA256 { get; set; }

        public PackageAssemblyEdgeCases? EdgeCases { get; set; }
        public string AssemblyName { get; set; }
        public Version AssemblyVersion { get; set; }
        public string Culture { get; set; }

        public string PublicKeyToken { get; set; }

        public AssemblyHashAlgorithm? HashAlgorithm { get; set; }

        public bool? HasPublicKey { get; set; }
        public int? PublicKeyLength { get; set; }
        public string PublicKeySHA1 { get; set; }

        [KustoType("dynamic")]
        public string CustomAttributes { get; set; }

        [KustoType("dynamic")]
        public string CustomAttributesFailedDecode { get; set; }

        public int? CustomAttributesTotalCount { get; set; }
        public int? CustomAttributesTotalDataLength { get; set; }
    }
}
