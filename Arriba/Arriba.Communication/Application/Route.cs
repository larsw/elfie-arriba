// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System.Text.RegularExpressions;

    /// <summary>
    ///     Route properties container.
    /// </summary>
    public class Route
    {
        private readonly Match _match;

        public Route(Match regexMatch)
        {
            // TODO: Complete member initialization
            _match = regexMatch;
        }

        public string this[string index] => GetPart(index);

        public string GetPart(string partName)
        {
            var group = _match.Groups[partName.ToLowerInvariant()];

            if (group == null) return null;

            return group.Value;
        }
    }
}