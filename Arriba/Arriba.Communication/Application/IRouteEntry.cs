// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System.Threading.Tasks;

    /// <summary>
    ///     Encapsulates a routes matching and processing functionality.
    /// </summary>
    internal interface IRouteHandler
    {
        RouteMatcher Matcher { get; }

        Task<IResponse> TryHandleAsync(IRequestContext ctx, Route data);
    }
}