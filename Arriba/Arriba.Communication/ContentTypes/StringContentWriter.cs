// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.ContentTypes
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>
    ///     String content writer.
    /// </summary>
    public sealed class StringContentWriter : IContentWriter
    {
        string IContentWriter.ContentType => "text/plain";

        bool IContentWriter.CanWrite(Type t)
        {
            // We call ToString on everything, always true; 
            return true;
        }

        async Task IContentWriter.WriteAsync(IRequest request, Stream output, object content)
        {
            if (content == null) return;

            using (var writer = new StreamWriter(output))
            {
                var enumberable = content as IEnumerable;
                if (enumberable != null)
                    foreach (var item in enumberable)
                        await writer.WriteLineAsync(item.ToString());
                else
                    await writer.WriteAsync(content.ToString());
            }
        }
    }
}