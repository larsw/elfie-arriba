// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    using System.IO;
    using System.Threading.Tasks;

    public interface IStreamWriterResponse : IResponse
    {
        string ContentType { get; }

        Task WriteToStreamAsync(Stream outputStream);
    }
}