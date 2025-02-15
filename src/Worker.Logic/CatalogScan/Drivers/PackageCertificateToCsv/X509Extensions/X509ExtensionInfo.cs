// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace NuGet.Insights.Worker.PackageCertificateToCsv
{
    public class X509ExtensionInfo
    {
        public X509ExtensionInfo(X509Extension extension, bool recognized)
        {
            Oid = extension.Oid.Value;
            Critical = extension.Critical;
            Recognized = recognized;
            RawDataLength = extension.RawData.Length;
            RawData = extension.RawData.ToBase64();
        }

        public string Oid { get; }
        public bool Critical { get; }
        public bool Recognized { get; }
        public int RawDataLength { get; }
        public string RawData { get; }
    }
}
