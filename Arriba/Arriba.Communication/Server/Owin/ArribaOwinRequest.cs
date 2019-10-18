// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Owin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Principal;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;
    using Communication;

    public class ArribaOwinRequest : IRequest
    {
        private static readonly string[] s_emptyStringArray = new string[0];
        private readonly Lazy<string[]> _acceptLazy;
        private readonly IDictionary<string, object> _environment;
        private readonly Lazy<IValueBag> _headersLazy;
        private readonly Lazy<string> _pathLazy;
        private readonly Lazy<IValueBag> _queryStringLazy;
        private readonly IContentReaderWriterService _readerWriter;

        private readonly Lazy<RequestVerb> _verbLazy;

        public ArribaOwinRequest(IDictionary<string, object> environment, IContentReaderWriterService readerWriter)
        {
            _environment = environment;
            _readerWriter = readerWriter;

            _verbLazy = new Lazy<RequestVerb>(GetVerb);
            _pathLazy = new Lazy<string>(() => _environment.Get<string>("owin.RequestPath"));
            _queryStringLazy = new Lazy<IValueBag>(() =>
                new NameValueCollectionValueBag(
                    HttpUtility.ParseQueryString(_environment.Get<string>("owin.RequestQueryString"), Encoding.UTF8)));
            _headersLazy = new Lazy<IValueBag>(() =>
                new DictionaryValueBag(_environment.Get<IDictionary<string, string[]>>("owin.RequestHeaders")));
            _acceptLazy = new Lazy<string[]>(GetAcceptHeaders);
        }

        public RequestVerb Method => _verbLazy.Value;

        public string Resource => _pathLazy.Value;

        public IValueBag ResourceParameters => _queryStringLazy.Value;

        public IValueBag Headers => _headersLazy.Value;

        public IPrincipal User => _environment.Get<IPrincipal>("server.User");

        public string Origin => _environment.Get<string>("server.RemoteIpAddress");

        public bool HasBody => Headers.Contains("Content-Length") && Headers["Content-Length"] != "0";

        public Stream InputStream => _environment.Get<Stream>("owin.RequestBody");

        public Task<T> ReadBodyAsync<T>()
        {
            var reader = _readerWriter.GetReader<T>(Headers["Content-Type"]);
            return reader.ReadAsync<T>(InputStream);
        }

        public IEnumerable<string> AcceptedResponseTypes => _acceptLazy.Value;

        private RequestVerb GetVerb()
        {
            var verb = _environment.Get<string>("owin.RequestMethod");
            switch (verb)
            {
                case "GET":
                    return RequestVerb.Get;
                case "POST":
                    return RequestVerb.Post;
                case "DELETE":
                    return RequestVerb.Delete;
                case "OPTIONS":
                    return RequestVerb.Options;
                case "PUT":
                    return RequestVerb.Put;
                case "PATCH":
                    return RequestVerb.Patch;
                default:
                    throw new ArgumentException("Unknown HTTP Verb \"" + verb + "\"");
            }
        }

        private string[] GetAcceptHeaders()
        {
            string[] acceptRaw;

            if (Headers.TryGetValues("Accept", out acceptRaw) && acceptRaw != null && acceptRaw.Length > 0)
            {
                var items = acceptRaw.SelectMany(a => a.Split(',')).ToArray();

                for (var i = 0; i < items.Length; i++)
                {
                    var paramsStartIndex = items[i].IndexOf(';');

                    if (paramsStartIndex != -1)
                        // Strip accept params (e.g. q=xxx) 
                        items[i] = items[i].Substring(0, paramsStartIndex).Trim();
                }

                return items;
            }

            return s_emptyStringArray;
        }
    }
}