// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Owin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Hosting;

    internal static class OwinExtensions
    {
        private const string AppDisposingKey = "host.OnAppDisposing";

        public static IAppBuilder UseArriba(this IAppBuilder builder, Host host)
        {
            HookDisposal(builder, host);
            return builder.Use(typeof(ArribaOwinHost), host);
        }

        private static void HookDisposal(IAppBuilder builder, IDisposable disposable)
        {
            if (!builder.Properties.ContainsKey(AppDisposingKey)) return;

            var appDisposing = builder.Properties[AppDisposingKey] as CancellationToken?;

            if (appDisposing.HasValue) appDisposing.Value.Register(disposable.Dispose);
        }

        public static T Get<T>(this IDictionary<string, object> env, string key)
        {
            object value;
            return env.TryGetValue(key, out value) && value is T ? (T) value : default;
        }
    }
}