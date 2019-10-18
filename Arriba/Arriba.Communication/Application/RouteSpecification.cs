// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    ///     Complex specification of route matching.
    /// </summary>
    public class RouteSpecification
    {
        public RouteSpecification(string route, params UrlParameter[] urlParameters)
        {
            ResourceRoute = route;
            UrlParameters = urlParameters;
        }

        public UrlParameter[] UrlParameters { get; }

        public string ResourceRoute { get; }
    }

    /// <summary>
    ///     Route matching specication for url parameters
    /// </summary>
    public class UrlParameter
    {
        private readonly Regex _valueRegex;

        public UrlParameter(string key)
            : this(key, null)
        {
        }

        public UrlParameter(string key, string value)
            : this(key, value, false)
        {
        }

        public UrlParameter(string key, string value, bool isRegex)
        {
            Key = key;
            Value = value;
            ValueIsRegex = isRegex;

            if (isRegex) _valueRegex = new Regex(value, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public string Key { get; }

        public string Value { get; }

        private bool ValueIsRegex { get; }

        internal bool ValueMatches(string value)
        {
            if (Value == null)
                // Nothing defined == match any 
                return true;
            if (_valueRegex == null)
                // Case insenstive match
                return string.Equals(value, Value, StringComparison.OrdinalIgnoreCase);
            if (value == null)
                // Empty value passed, but attempted to use regex 
                return false;

            // Use Regex
            return _valueRegex.IsMatch(value);
        }
    }
}