// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server
{
    using System;
    using System.Composition;
    using System.Net;
    using System.Security.Principal;
    using System.Threading.Tasks;
    using Authentication;
    using Communication;
    using Communication.Application;
    using Hosting;
    using Model;
    using Model.Correctors;
    using Model.Security;
    using Monitoring;

    internal abstract class ArribaApplication : RoutedApplication<IResponse>
    {
        protected static readonly ArribaResponse ContinueToNextHandlerResponse = null;
        private readonly ClaimsAuthenticationService _claimsAuth;
        private readonly ComposedCorrector _correctors;

        [ImportingConstructor]
        protected ArribaApplication(DatabaseFactory factory, ClaimsAuthenticationService claimsAuth)
        {
            EventSource = EventPublisher.CreateEventSource(GetType().Name);
            Database = factory.GetDatabase();
            _claimsAuth = claimsAuth;

            // Cache correctors which aren't request specific
            // Cache the People table so that it isn't reloaded for every request.
            // TODO: Need to make configurable by table.
            _correctors = new ComposedCorrector(new TodayCorrector(), new UserAliasCorrector(Database["People"]));
        }

        public override string Name => GetType().Name;

        protected SecureDatabase Database { get; }

        public EventPublisherSource EventSource { get; }

        /// <summary>
        ///     Return the current Corrector(s) to use to correct queries
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        protected ICorrector CurrentCorrectors(IRequestContext ctx)
        {
            // Add the 'MeCorrector' for the requesting user (must be first, to chain with the UserAliasCorrector)
            return new ComposedCorrector(new MeCorrector(ctx.Request.User.Identity.Name), _correctors);
        }

        protected Task<IResponse> ValidateBodyAsync(IRequestContext ctx, Route route)
        {
            if (!ctx.Request.HasBody)
                return Task.FromResult<IResponse>(ArribaResponse.BadRequest("Request must have content body"));

            return Task.FromResult<IResponse>(null);
        }

        protected Task<IResponse> ValidateReadAccessAsync(IRequestContext ctx, Route route)
        {
            return Task.FromResult(ValidateReadAccess(ctx, route));
        }

        protected Task<IResponse> ValidateWriteAccessAsync(IRequestContext ctx, Route route)
        {
            return Task.FromResult(ValidateWriteAccess(ctx, route));
        }

        protected Task<IResponse> ValidateOwnerAccessAsync(IRequestContext ctx, Route route)
        {
            return Task.FromResult(ValidateOwnerAccess(ctx, route));
        }

        protected Task<IResponse> ValidateCreateAccessAsync(IRequestContext ctx, Route route)
        {
            return Task.FromResult(ValidateCreateAccess(ctx, route));
        }

        protected IResponse ValidateCreateAccess(IRequestContext ctx, Route route)
        {
            var hasPermission = false;

            var security = Database.DatabasePermissions();
            if (!security.HasTableAccessSecurity)
                // If there's no security, table create is only allowed if the service is running as the same user
                hasPermission = ctx.Request.User.Identity.Name.Equals(WindowsIdentity.GetCurrent().Name);
            else
                // Otherwise, check for writer or better permissions at the DB level
                hasPermission = HasPermission(security, ctx.Request.User, PermissionScope.Writer);

            if (!hasPermission)
                return ArribaResponse.Forbidden($"Create Table access denied for {ctx.Request.User.Identity.Name}.");
            return ContinueToNextHandlerResponse;
        }

        protected IResponse ValidateReadAccess(IRequestContext ctx, Route routeData)
        {
            return ValidateTableAccess(ctx, routeData, PermissionScope.Reader);
        }

        protected IResponse ValidateWriteAccess(IRequestContext ctx, Route routeData)
        {
            return ValidateTableAccess(ctx, routeData, PermissionScope.Writer);
        }

        protected IResponse ValidateOwnerAccess(IRequestContext ctx, Route routeData)
        {
            return ValidateTableAccess(ctx, routeData, PermissionScope.Owner);
        }

        protected Task<IResponse> ValidateTableAccessAsync(IRequestContext ctx, Route routeData, PermissionScope scope,
            bool overrideLocalHostSameUser = false)
        {
            return Task.FromResult(ValidateTableAccess(ctx, routeData, scope, overrideLocalHostSameUser));
        }

        protected IResponse ValidateTableAccess(IRequestContext ctx, Route routeData, PermissionScope scope,
            bool overrideLocalHostSameUser = false)
        {
            var tableName = GetAndValidateTableName(routeData);
            if (!Database.TableExists(tableName)) return ArribaResponse.NotFound("Table requested does not exist.");

            var currentUser = ctx.Request.User;

            // If we are asked if override auth, check if the request was made from a loopback address (local) and the 
            // current process identity matches the request identity
            if (overrideLocalHostSameUser && IsRequestOriginLoopback(ctx.Request) &&
                IsProcessUserSame(currentUser.Identity))
            {
                // Log for auditing that we skipped out on checking table auth. 
                EventSource.Raise(MonitorEventLevel.Warning,
                    MonitorEventOpCode.Mark,
                    "Table",
                    tableName,
                    "Authentication Override",
                    ctx.Request.User.Identity.Name,
                    "Skipping table authentication for local loopback user on request");

                return ContinueToNextHandlerResponse;
            }

            if (!HasTableAccess(tableName, currentUser, scope))
                return ArribaResponse.Forbidden($"Access to {tableName} denied for {currentUser.Identity.Name}.");
            return ContinueToNextHandlerResponse;
        }

        protected bool HasTableAccess(string tableName, IPrincipal currentUser, PermissionScope scope)
        {
            var security = Database.Security(tableName);

            // No security? Allowed.
            if (!security.HasTableAccessSecurity) return true;

            // Otherwise check permissions
            return HasPermission(security, currentUser, scope);
        }

        protected bool HasPermission(SecurityPermissions security, IPrincipal currentUser, PermissionScope scope)
        {
            // No user identity? Forbidden! 
            if (currentUser == null || !currentUser.Identity.IsAuthenticated) return false;

            // Try user first, cheap check. 
            if (security.IsIdentityInPermissionScope(IdentityScope.User, currentUser.Identity.Name, scope)) return true;

            // See if the user is in any allowed groups.
            foreach (var group in security.GetScopeIdentities(scope, IdentityScope.Group))
                if (_claimsAuth.IsUserInGroup(currentUser, group.Name))
                    return true;

            return false;
        }

        protected bool IsInIdentity(IPrincipal currentUser, SecurityIdentity targetUserOrGroup)
        {
            if (targetUserOrGroup.Scope == IdentityScope.User)
                return targetUserOrGroup.Name.Equals(currentUser.Identity.Name, StringComparison.OrdinalIgnoreCase);
            return _claimsAuth.IsUserInGroup(currentUser, targetUserOrGroup.Name);
        }

        /// <summary>
        ///     Validates the current process identity matches the given identity.
        /// </summary>
        /// <param name="identity">Identity to validate.</param>
        /// <returns>true if same user otherwise false.</returns>
        private static bool IsProcessUserSame(IIdentity identity)
        {
            var processUserName =
                (string.IsNullOrEmpty(Environment.UserDomainName) ? string.Empty : Environment.UserDomainName + @"\") +
                Environment.UserName;
            return string.Equals(identity.Name, processUserName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Validates that the given request was made from a loopback address (localhost).
        /// </summary>
        /// <param name="request">Request to validate.</param>
        /// <returns>true if request was made from a loopback address, otherwise false. </returns>
        private static bool IsRequestOriginLoopback(IRequest request)
        {
            IPAddress ipAddress;
            return IPAddress.TryParse(request.Origin, out ipAddress) && IPAddress.IsLoopback(ipAddress);
        }

        protected string GetAndValidateTableName(Route route)
        {
            var tableName = route["tableName"];

            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("No table name specified in route");

            return tableName;
        }

        protected override IResponse OnAfterProcess(IRequestContext request, IResponse response)
        {
            var arribaResponse = response as ArribaResponse;

            if (arribaResponse != null)
                // Add the request trace timings to the response trace timings so they can be output to the client.
                arribaResponse.ResponseBody.TraceTimings = request.TraceTimings;

            // Always no cache for arriba responses 
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate"); // HTTP 1.1
            response.AddHeader("Pragma", "no-cache"); // HTTP 1.0
            response.AddHeader("Expires", "0"); // Proxies

            // Allow cross-domain use [server:80 to server:42784]
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Credentials", "true");

            return response;
        }
    }
}