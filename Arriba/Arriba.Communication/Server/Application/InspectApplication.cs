// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Application
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.Diagnostics;
    using Authentication;
    using Communication;
    using Communication.Application;
    using Hosting;

    internal class InspectApplication : ArribaApplication
    {
        [ImportingConstructor]
        public InspectApplication(DatabaseFactory f, ClaimsAuthenticationService auth)
            : base(f, auth)
        {
            Get("/inspect/memory", Memory);
            Get("/inspect/machine", Machine);
        }

        private ArribaResponse Memory(IRequestContext request, Route routedata)
        {
            var currentProcess = Process.GetCurrentProcess();

            return ArribaResponse.Ok(new Dictionary<string, object>(5)
            {
                {"totalGCBytes", GC.GetTotalMemory(false)},
                {"totalProcessBytes", currentProcess.WorkingSet64},
                {"environmentWorkingSet", Environment.WorkingSet}
            });
        }

        private ArribaResponse Machine(IRequestContext _, Route data)
        {
            return ArribaResponse.Ok(new
            {
                Environment.MachineName,
                OsVersion = Environment.OSVersion,
                OsBitness = Environment.Is64BitOperatingSystem ? 64 : 32,
                ProcessBitness = Environment.Is64BitProcess ? 64 : 32,
                Environment.ProcessorCount
            });
        }
    }
}