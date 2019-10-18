// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Monitoring;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    internal class RequestContext : IRequestContext
    {
        private static readonly JsonSerializerSettings s_jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly List<MonitorEventScope> _events = new List<MonitorEventScope>();
        private readonly EventPublisherSource _eventSource;

        public RequestContext(IRequest request)
        {
            var defaults = new MonitorEventEntry
            {
                Level = MonitorEventLevel.Verbose,
                Source = "HTTP",
                User = request.User.Identity.Name,
                Detail = null
            };

            Request = request;
            _eventSource = EventPublisher.CreateEventSource(defaults);
        }

        public IRequest Request { get; }

        public IDictionary<string, double> TraceTimings
        {
            get
            {
                return _events.GroupBy(e => e.Start.Name)
                    .ToDictionary(e => e.Key,
                        e => e.Sum(s => s.Stop == null ? s.CurrentRuntime : s.Stop.RuntimeMilliseconds),
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        public IDisposable Monitor(MonitorEventLevel level, string name, string type = null, string identity = null,
            object detail = null)
        {
            // TODO: Consider making detail evaluation lazy. 
            var detailValue = string.Empty;

            if (detail != null)
                // Attempt to serialize  
                detailValue = JsonConvert.SerializeObject(detail, s_jsonSettings);

            var evt = _eventSource.RaiseScope(level, type, identity, name, detail: detailValue);
            _events.Add(evt);
            return evt;
        }
    }
}