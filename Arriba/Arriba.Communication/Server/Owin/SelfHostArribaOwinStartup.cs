// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Owin
{
    using System;
    using System.Collections.Generic;

    public abstract class ArribaOwinStartup
    {
        public virtual void Configuration(IAppBuilder app)
        {
            //var host = new Host();
            //host.Add<JsonConverter>(new StringEnumConverter());
            //host.Compose();

            //app.UseCors(CorsOptions.AllowAll)
            //   .UseArriba(host);
        }
    }

    public interface IAppBuilder
    {
        IDictionary<string, object> Properties { get; }

        IAppBuilder Use(Type t, object instance);
    }
}