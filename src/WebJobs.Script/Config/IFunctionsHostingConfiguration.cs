﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal interface IFunctionsHostingConfiguration
    {
        bool FunctionsWorkerDynamicConcurrencyEnabled { get; }
    }
}
