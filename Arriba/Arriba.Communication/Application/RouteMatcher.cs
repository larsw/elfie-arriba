// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Extensions;

    /// <summary>
    ///     Encapsulates a specified route.
    /// </summary>
    internal class RouteMatcher
    {
        private readonly bool _hasUrlParameters;
        private readonly UrlParameter[] _paramMatches;
        private readonly Regex _resourceRegex;
        private readonly string _routeString;


        public RouteMatcher(RequestVerb verb, RouteSpecification route)
        {
            if (route == null) throw new ArgumentNullException("route");

            Verb = verb;
            _routeString = SanatizeRoute(route.ResourceRoute);
            _resourceRegex = new Regex(BuildRouteRegex(_routeString),
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _paramMatches = route.UrlParameters;
            _hasUrlParameters = _paramMatches != null && _paramMatches.Length > 0;

            SortOrder = _routeString.Split('/').Length + 1 + (_hasUrlParameters ? _paramMatches.Length * 3 : 0);
        }

        public int SortOrder { get; }

        public RequestVerb Verb { get; }

        private static string SanatizeRoute(string route)
        {
            route = route.ToLowerInvariant();

            if (!route.StartsWith("/")) route = "/" + route;

            if (route.EndsWith("/")) route = route.Remove(route.Length - 1);

            return route;
        }


        private static string BuildRouteRegex(string route)
        {
            var parts = route.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

            // Base route
            if (parts.Length == 0) return @"^\/$";

            var regexString = new StringBuilder("^"); // Start matching at the start of the string 

            for (var i = 0; i < parts.Length; i++)
            {
                var nodeName = parts[i];

                var isGreedy = nodeName.StartsWith("*");

                if (isGreedy) nodeName = nodeName.Substring(1);

                var isVariable = nodeName.StartsWith(":");
                if (isVariable)
                    // Trim :
                    nodeName = nodeName.Substring(1);

                var isOptional = nodeName.EndsWith("?");
                if (isOptional)
                    // Trim ?
                    nodeName = nodeName.Remove(nodeName.Length - 1);

                if (isOptional && !isVariable)
                    throw new RouteException(string.Format(
                        "Route part \"{0}\" from route \"{1}\" cannot be only optional, it must also be variable",
                        parts[i], route));
                if (isGreedy && !isVariable)
                    throw new RouteException(string.Format(
                        "Route part \"{0}\" from route \"{1}\" cannot be only greed, it must also be variable",
                        parts[i], route));

                // Start a group and match a slash, the slash is inside the group so it can be optional 
                // for optional variables 
                regexString.Append(@"(\/");

                if (isVariable)
                {
                    // Match any text up to the next group thats not a slash 
                    regexString.Append("(?<");
                    regexString.Append(nodeName);
                    regexString.Append(">");

                    if (isGreedy)
                        regexString.Append(".*)"); // Match anything, non greedy (so we don't eat the next slash). 
                    else
                        regexString.Append(
                            @"[^\/]+?)"); // Match anything, non greedy (so we don't eat the next slash). 
                }
                else
                {
                    // Just the node name as a liternal match
                    regexString.Append(nodeName);
                }

                // End the group
                regexString.Append(")");

                if (isOptional) regexString.Append("?");
            }

            // Always allow an optional trailing slash, and just ignore it 
            regexString.Append(@"\/?");
            regexString.Append("$"); // Stop matching at the end of the string  

            return regexString.ToString();
        }

        internal bool TryGetRouteMatch(RequestVerb verb, string uri, IValueBag parameters, out Route data)
        {
            data = null;

            // Check the verb
            if (verb != Verb) return false;

            // Check the route 
            var match = _resourceRegex.Match(uri);

            // Route didnt match, exit early 
            if (!match.Success) return false;

            // Check any required parameters 
            if (_hasUrlParameters)
            {
                string value;

                // Try each parameter match and if any fail, exit
                foreach (var paramMatch in _paramMatches)
                    // Check if the key exists and the parameter value matches the specification 
                    if (!parameters.TryGetValue(paramMatch.Key, out value) || !paramMatch.ValueMatches(value))
                        // Key does not exist 
                        return false;
            }

            // We made it, route matches all requirements 
            data = new Route(match);
            return true;
        }

        public override string ToString()
        {
            return StringExtensions.Format(
                "{0} Route = \"{1}\", Params = ({2}), Regex = \"{3}\"",
                Verb.ToString().ToUpper(),
                _routeString,
                string.Join(", ", _paramMatches.Select(p => p.Key + " = " + p.Value)),
                _resourceRegex.ToString());
        }
    }
}