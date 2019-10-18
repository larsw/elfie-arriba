// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.ContentTypes
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>
    ///     JsonP content writer.
    /// </summary>
    /// <remarks>
    ///     JsonP is used for cross domain json loading, by encoding json output as a function call targetted at a callback url
    ///     parameter.
    /// </remarks>
    public sealed class JsonpContentWriter : IContentWriter
    {
        private const string CallbackNameKey = "callback";
        private readonly JsonContentWriter _jsonWriter;

        public JsonpContentWriter(JsonContentWriter jsonWriter)
        {
            _jsonWriter = jsonWriter;
        }

        public string ContentType => "application/javascript";

        public bool CanWrite(Type t)
        {
            return true;
        }

        public async Task WriteAsync(IRequest request, Stream output, object content)
        {
            var callbackName = request.ResourceParameters[CallbackNameKey];

            if (string.IsNullOrEmpty(callbackName))
                throw new ArgumentException("No callback name specified on request");

            using (var writer = new StreamWriter(output))
            {
                await writer.WriteAsync(callbackName + "(");
                await _jsonWriter.WriteAsyncCore(writer, content);
                await writer.WriteAsync(")");
            }
        }
    }
}