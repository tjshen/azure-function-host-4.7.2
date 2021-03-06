// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A PUT request is made against the host system keys resource endpoint", "")]
    public class PutHostSystemKeyScenario : PutFunctionKeysScenario, IClassFixture<PutHostSystemKeyScenario.HostFixture>
    {
        public PutHostSystemKeyScenario(HostFixture fixture)
            : base(fixture)
        {
        }

        public class HostFixture : Fixture
        {
            private readonly string _requestUri = "http://localhost/admin/host/systemkeys/TestKey";

            protected override string RequestUriFormat => _requestUri;
        }
    }
}