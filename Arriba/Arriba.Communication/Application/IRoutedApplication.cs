// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System.Collections.Generic;

    internal interface IRoutedApplication
    {
        string Name { get; }

        IEnumerable<IRouteHandler> RouteEnteries { get; }
    }
}