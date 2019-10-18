// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication.Application
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class RouteException : Exception
    {
        public RouteException()
        {
        }

        public RouteException(string message) : base(message)
        {
        }

        public RouteException(string message, Exception inner) : base(message, inner)
        {
        }

        protected RouteException(
            SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
        }
    }
}