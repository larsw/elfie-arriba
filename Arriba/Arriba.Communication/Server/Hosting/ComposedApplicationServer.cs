// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.Linq;
    using Communication;

    /// <summary>
    ///     Represents an application server that fulfills its dependencies via composition.
    /// </summary>
    [Export(typeof(ComposedApplicationServer))]
    [Shared]
    public class ComposedApplicationServer : ApplicationServer
    {
        [ImportingConstructor]
        public ComposedApplicationServer(
            [ImportMany] IEnumerable<IApplication> applications,
            [ImportMany] IEnumerable<IContentReader> readers,
            [ImportMany] IEnumerable<IContentWriter> writers,
            [ImportMany] IEnumerable<IChannel> channels)
        {
            if (!applications.Any()) throw new ArgumentException("No applications registered");

            //if(!channels.Any())
            //{
            //    throw new ArgumentException("No channels registered");
            //}

            foreach (var application in applications) RegisterApplication(application);

            foreach (var reader in readers) RegisterContentReader(reader);

            foreach (var writer in writers) RegisterContentWriter(writer);

            foreach (var channel in channels) RegisterRequestChannel(channel);
        }
    }
}