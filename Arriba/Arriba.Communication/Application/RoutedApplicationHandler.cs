// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System.Collections.Generic;
    using System.Composition;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    ///     Application implementation that delegates to IRoutedApplication implementations.
    /// </summary>
    [Export(typeof(IApplication))]
    internal class RoutedApplicationHandler : IApplication
    {
        private readonly List<IRoutedApplication> _apps = new List<IRoutedApplication>();

        private readonly Dictionary<RequestVerb, IRouteHandler[]> _routeLookup =
            new Dictionary<RequestVerb, IRouteHandler[]>();

        [ImportingConstructor]
        public RoutedApplicationHandler([ImportMany] IEnumerable<IRoutedApplication> routes)
        {
            _apps.AddRange(routes);

            UpdateRoutes();
        }

        public string Name
        {
            get { return "RoutedApplication(" + string.Join(", ", _apps.Select(s => s.Name)) + ")"; }
        }

        public async Task<IResponse> TryProcessAsync(IRequestContext ctx)
        {
            Route data;

            IRouteHandler[] candidateRoutes;

            // Get the entries for the verbs
            if (_routeLookup.TryGetValue(ctx.Request.Method, out candidateRoutes) && candidateRoutes != null)
                foreach (var route in candidateRoutes)
                    if (route.Matcher.TryGetRouteMatch(ctx.Request.Method, ctx.Request.Resource,
                        ctx.Request.ResourceParameters, out data))
                    {
                        var response = await route.TryHandleAsync(ctx, data);
                        if (response != null && response.Status != ResponseStatus.NotHandled) return response;
                    }

            // DEBUG: Breakpoint here to debug route matching failures.
            return Response.NotHandled;
        }

        private void UpdateRoutes()
        {
            _routeLookup.Clear();

            // Build a lookup of VERB to *sorted* set of route entries. 
            foreach (var verbSet in _apps.SelectMany(a => a.RouteEnteries).GroupBy(e => e.Matcher.Verb))
                _routeLookup.Add(verbSet.Key, verbSet.OrderByDescending(v => v.Matcher.SortOrder).ToArray());
        }
    }
}