// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System;
    using System.Collections.Specialized;

    /// <summary>
    ///     Default implementation of IResponse
    /// </summary>
    public class Response : IResponse
    {
        private readonly Lazy<IWritableValueBag> _headersLazy =
            new Lazy<IWritableValueBag>(() => new NameValueCollectionValueBag(new NameValueCollection()));

        private readonly object _responseBody;

        public Response(ResponseStatus status)
        {
            Status = status;
        }

        public Response(ResponseStatus status, object body)
            : this(status)
        {
            _responseBody = body;
        }

        public static IResponse NotHandled { get; } = new Response(ResponseStatus.NotHandled, null);

        public ResponseStatus Status { get; }

        public virtual object ResponseBody => GetResponseBody();

        public IWritableValueBag Headers => _headersLazy.Value;

        public void AddHeader(string key, string value)
        {
            _headersLazy.Value.Add(key, value);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual object GetResponseBody()
        {
            return _responseBody;
        }

        internal static IResponse Error(object body)
        {
            return new Response(ResponseStatus.Error, body);
        }

        internal static IResponse NotFound(object body)
        {
            return new Response(ResponseStatus.NotFound, body);
        }

        internal static IResponse NotFound()
        {
            return new Response(ResponseStatus.NotFound, null);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}