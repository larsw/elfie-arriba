// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.ContentTypes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Extensions;

    /// <summary>
    ///     Content reading and writing service.
    /// </summary>
    internal class ContentReaderWriterService : IContentReaderWriterService
    {
        internal readonly Dictionary<string, IContentReader> readers =
            new Dictionary<string, IContentReader>(StringComparer.OrdinalIgnoreCase);

        internal readonly Dictionary<string, IContentWriter> writers =
            new Dictionary<string, IContentWriter>(StringComparer.OrdinalIgnoreCase);

        public IContentReader GetReader<T>(string contentType)
        {
            IContentReader reader;

            if (!readers.TryGetValue(contentType, out reader))
                throw new NotSupportedException(
                    string.Format("No content type reader registered for content type \"{0}\"", contentType));

            if (!reader.CanRead<T>())
                throw new NotSupportedException(string.Format(
                    "Content type reader found for \"{0}\" but it is unable to deserialize to type \"{1}", contentType,
                    typeof(T)));

            return reader;
        }

        public IContentWriter GetWriter(string contentType, object content)
        {
            IContentWriter writer;

            if (!writers.TryGetValue(contentType, out writer))
                throw new NotSupportedException(
                    StringExtensions.Format("No content type writer registered for content type \"{0}", contentType));

            if (!writer.CanWrite(content.GetType()))
                throw new NotSupportedException(StringExtensions.Format(
                    "Content type writer for \"{0}\" but is unable to serialize type \"{1}\"", contentType,
                    content.GetType().Name));

            return writer;
        }

        public IContentWriter GetWriter(IEnumerable<string> contentTypes, object content)
        {
            var outputType = content.GetType();

            foreach (var contentType in contentTypes)
            {
                var actualContentType = contentType;

                IContentWriter candidiate;

                if (!writers.TryGetValue(actualContentType, out candidiate) ||
                    !candidiate.CanWrite(outputType)) continue;

                return candidiate;
            }

            throw new NotSupportedException(string.Format(
                "No content type writer found matching content types \"{0}\" and output type \"{1}\"",
                string.Join(",", contentTypes), outputType));
        }

        public IContentWriter GetWriter(IEnumerable<string> contentTypes, string defaultContentType, object content)
        {
            return GetWriter(contentTypes.Concat(new[] {defaultContentType}), content);
        }

        public IContentWriter GetWriter(string contentType, string defaultContentType, object content)
        {
            return GetWriter(new[] {contentType, defaultContentType}, content);
        }

        public void AddReader(IContentReader reader)
        {
            foreach (var contentType in reader.ContentTypes) readers.Add(contentType, reader);
        }

        public void AddWriter(IContentWriter writer)
        {
            writers.Add(writer.ContentType, writer);
        }
    }
}