// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Principal;
    using System.Threading.Tasks;

    /// <summary>
    ///     Modifies the request verb for a request.
    /// </summary>
    internal class ModifiedVerbRequest : IRequest
    {
        private readonly IRequest _request;

        public ModifiedVerbRequest(IRequest request, RequestVerb verb)
        {
            _request = request;
            Method = verb;
        }

        public RequestVerb Method { get; }

        public string Resource => _request.Resource;

        public IPrincipal User => _request.User;

        public bool HasBody => _request.HasBody;

        public Task<T> ReadBodyAsync<T>()
        {
            return _request.ReadBodyAsync<T>();
        }


        public IValueBag ResourceParameters => _request.ResourceParameters;

        public IValueBag Headers => _request.Headers;

        public Stream InputStream => _request.InputStream;


        public IEnumerable<string> AcceptedResponseTypes => _request.AcceptedResponseTypes;


        public string Origin => _request.Origin;
    }
}