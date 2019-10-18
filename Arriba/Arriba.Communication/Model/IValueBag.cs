// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System;
    using System.Collections.Generic;

    public interface IValueBag
    {
        string this[string key] { get; }

        IEnumerable<Tuple<string, string>> ValuePairs { get; }
        bool Contains(string key);

        bool TryGetValue(string key, out string value);

        bool TryGetValues(string key, out string[] values);
    }

    public interface IWritableValueBag : IValueBag
    {
        void Add(string key, string value);

        void AddOrUpdate(string key, string value);
    }
}