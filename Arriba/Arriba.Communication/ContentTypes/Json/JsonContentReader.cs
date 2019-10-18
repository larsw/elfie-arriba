﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.ContentTypes
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    /// <summary>
    ///     Json content reader
    /// </summary>
    public sealed class JsonContentReader : IContentReader
    {
        private readonly JsonSerializerSettings _settings;

        public JsonContentReader(IEnumerable<JsonConverter> converters)
        {
            _settings = new JsonSerializerSettings();
#if DEBUG
            _settings.Formatting = Formatting.Indented;
#endif

            // Enable "PropertyName" to be output and read as "propertyName"
            _settings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            foreach (var converter in converters) _settings.Converters.Add(converter);
        }

        IEnumerable<string> IContentReader.ContentTypes
        {
            get
            {
                yield return "application/json";
                yield return "application/javascript";
                yield return "text/plain;charset=UTF-8";
            }
        }

        bool IContentReader.CanRead<T>()
        {
            // Supports any type. 
            return true;
        }

        async Task<T> IContentReader.ReadAsync<T>(Stream input)
        {
            using (var reader = new StreamReader(input))
            {
                var value = await reader.ReadToEndAsync();
                var result = JsonConvert.DeserializeObject<T>(value, _settings);
                return result;
            }
        }
    }
}