// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Storage
{
    internal class WebJobsStorageDelegatingHandler : DelegatingHandler
    {
        private const int MaxConnectionsPerServer = 50;
        private bool _isInnerHandlerConfigured = false;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_isInnerHandlerConfigured)
            {
                InitializeInnerHandler();

                _isInnerHandlerConfigured = true;
            }

            return base.SendAsync(request, cancellationToken);
        }

        private void InitializeInnerHandler()
        {
            try
            {
                // Storage ensures that the inner handler is an HttpClientHandler
                if (!(InnerHandler is HttpClientHandler innerHandler))
                {
                    throw new InvalidOperationException("The inner handler has not been initialized by the Storage SDK.");
                }

                innerHandler.MaxConnectionsPerServer = MaxConnectionsPerServer;
            }
            catch (InvalidOperationException)
            {
                // This exception is thrown if there's a race and this was set by another thread.
            }
        }
    }
}
