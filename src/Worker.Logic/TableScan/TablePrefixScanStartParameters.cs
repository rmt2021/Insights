// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace NuGet.Insights.Worker
{
    public class TablePrefixScanStartParameters
    {
        [JsonPropertyName("sf")]
        public int SegmentsPerFirstPrefix { get; set; }

        [JsonPropertyName("ss")]
        public int SegmentsPerSubsequentPrefix { get; set; }
    }
}
