// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Authentication
{
    using System;
    using System.Composition;
    using System.Diagnostics;
    using System.Security.Claims;
    using System.Security.Principal;

    /// <summary>
    ///     Windows authentication utilities.
    /// </summary>
    [Export(typeof(ClaimsAuthenticationService))]
    [Shared]
    internal class ClaimsAuthenticationService : IDisposable
    {
        private readonly RuntimeCache _cache = new RuntimeCache("Arriba.ClaimsAuthentication");
        private readonly TimeSpan _defaultTimeToLive = TimeSpan.FromMinutes(15);

        public void Dispose()
        {
            _cache.Dispose();
        }

        /// <summary>
        ///     Determines whether the specified user is within the specified security group.
        /// </summary>
        /// <param name="principal">User principal to check.</param>
        /// <param name="roleName">Role to validate.</param>
        /// <returns>True if the user is in the specified role, otherwise false.</returns>
        public bool IsUserInGroup(IPrincipal principal, string roleName)
        {
            if (principal == null) throw new ArgumentNullException(nameof(principal));

            if (roleName == null) throw new ArgumentNullException(nameof(roleName));

            if (roleName.Length == 0) throw new ArgumentException("Role name should not be empty", nameof(roleName));

            var cPrincipal = principal as ClaimsPrincipal;

            if (cPrincipal.Identity == null || string.IsNullOrEmpty(cPrincipal.Identity.Name) ||
                !principal.Identity.IsAuthenticated) return false;

            // Cachekey should be in the form of UserInGroup:{Identity}:{Role}
            var cacheKey = string.Concat("UserInGroup:", principal.Identity.Name, ":", roleName);

            Debug.Assert(cacheKey.Contains(principal.Identity.Name));
            Debug.Assert(cacheKey.Contains(roleName));

            return _cache.GetOrAdd(
                cacheKey,
                () => principal.IsInRole(roleName),
                _defaultTimeToLive);
        }
    }
}