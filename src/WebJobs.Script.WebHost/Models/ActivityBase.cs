// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ActivityBase
    {
        public DateTime EventTimeStamp { get; set; }

        public string Tenant { get; set; }
    }
}
