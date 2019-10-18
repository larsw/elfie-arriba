// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System.IO;
    using System.Threading.Tasks;

    internal class StreamResponse : StreamWriterResponse
    {
        public StreamResponse(string contentType, Stream stream)
            : base(contentType)
        {
            Stream = stream;
        }

        public Stream Stream { get; }

        public override Task WriteToStreamAsync(Stream outputStream)
        {
            return Stream.CopyToAsync(outputStream);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;

            if (Stream != null) Stream.Dispose();

            base.Dispose();
        }
    }
}