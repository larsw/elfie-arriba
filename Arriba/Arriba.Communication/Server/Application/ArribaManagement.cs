// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Application
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.Threading.Tasks;
    using Authentication;
    using Communication;
    using Communication.Application;
    using Hosting;
    using Model;
    using Model.Column;
    using Model.Query;
    using Model.Security;
    using Monitoring;
    using Types;

    [Export(typeof(IRoutedApplication))]
    internal class ArribaManagement : ArribaApplication
    {
        [ImportingConstructor]
        public ArribaManagement(DatabaseFactory f, ClaimsAuthenticationService auth)
            : base(f, auth)
        {
            // GET - return tables in Database
            Get("", GetTables);

            Get("/allBasics", GetAllBasics);

            Get("/unloadAll", ValidateCreateAccess, UnloadAll);

            // GET /table/foo - Get table information 
            Get("/table/:tableName", ValidateReadAccess, GetTableInformation);

            // POST /table with create table payload (Must be Writer/Owner in security directly in DiskCache folder, or identity running service)
            PostAsync("/table", ValidateCreateAccessAsync, ValidateBodyAsync, CreateNew);

            // POST /table/foo/addcolumns
            PostAsync("/table/:tableName/addcolumns", ValidateWriteAccessAsync, AddColumns);

            // GET /table/foo/save -- TODO: This is not ideal, think of a better pattern 
            Get("/table/:tableName/save", ValidateWriteAccess, Save);

            // Unload/Reload
            Get("/table/:tableName/unload", ValidateWriteAccess, UnloadTable);
            Get("/table/:tableName/reload", ValidateWriteAccess, Reload);

            // DELETE /table/foo 
            Delete("/table/:tableName", ValidateOwnerAccess, Drop);
            Get("/table/:tableName/delete", ValidateOwnerAccess, Drop);

            // POST /table/foo?action=delete
            Get(new RouteSpecification("/table/:tableName", new UrlParameter("action", "delete")), ValidateWriteAccess,
                DeleteRows);
            Post(new RouteSpecification("/table/:tableName", new UrlParameter("action", "delete")), ValidateWriteAccess,
                DeleteRows);

            // POST /table/foo/permissions/user - add permissions 
            PostAsync("/table/:tableName/permissions/:scope", ValidateOwnerAccessAsync, ValidateBodyAsync, Grant);

            // DELETE /table/foo/permissions/user - remove permissions from table 
            DeleteAsync("/table/:tableName/permissions/:scope", ValidateOwnerAccessAsync, ValidateBodyAsync, Revoke);

            // NOTE: _SPECIAL_ permission for localhost users, will override current auth to always be valid.
            // this enables tables recovery from local machine for matching user as the process. 
            // GET /table/foo/permissions  
            Get("/table/:tableName/permissions",
                (c, r) => ValidateTableAccess(c, r, PermissionScope.Reader, true),
                GetTablePermissions);

            // POST /table/foo/permissions  
            PostAsync("/table/:tableName/permissions",
                async (c, r) => await ValidateTableAccessAsync(c, r, PermissionScope.Owner, true),
                SetTablePermissions);
        }

        private IResponse GetTables(IRequestContext ctx, Route route)
        {
            return ArribaResponse.Ok(Database.TableNames);
        }

        private IResponse GetAllBasics(IRequestContext ctx, Route route)
        {
            var hasTables = false;

            var allBasics = new Dictionary<string, TableInformation>();
            foreach (var tableName in Database.TableNames)
            {
                hasTables = true;

                if (HasTableAccess(tableName, ctx.Request.User, PermissionScope.Reader))
                    allBasics[tableName] = GetTableBasics(tableName, ctx);
            }

            // If you didn't have access to any tables, return a distinct result to show Access Denied in the browser
            // but not a 401, because that is eaten by CORS.
            if (allBasics.Count == 0 && hasTables) return ArribaResponse.Ok(null);

            return ArribaResponse.Ok(allBasics);
        }

        private IResponse GetTableInformation(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName)) return Response.NotFound();

            var ti = GetTableBasics(tableName, ctx);
            return ArribaResponse.Ok(ti);
        }

        private TableInformation GetTableBasics(string tableName, IRequestContext ctx)
        {
            var table = Database[tableName];

            var ti = new TableInformation();
            ti.Name = tableName;
            ti.PartitionCount = table.PartitionCount;
            ti.RowCount = table.Count;
            ti.LastWriteTimeUtc = table.LastWriteTimeUtc;
            ti.CanWrite = HasTableAccess(tableName, ctx.Request.User, PermissionScope.Writer);
            ti.CanAdminister = HasTableAccess(tableName, ctx.Request.User, PermissionScope.Owner);

            var restrictedColumns = Database.GetRestrictedColumns(tableName, si => IsInIdentity(ctx.Request.User, si));
            if (restrictedColumns == null)
            {
                ti.Columns = table.ColumnDetails;
            }
            else
            {
                var allowedColumns = new List<ColumnDetails>();
                foreach (var column in table.ColumnDetails)
                    if (!restrictedColumns.Contains(column.Name))
                        allowedColumns.Add(column);
                ti.Columns = allowedColumns;
            }

            return ti;
        }

        private IResponse UnloadTable(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            Database.UnloadTable(tableName);
            return ArribaResponse.Ok("Table unloaded");
        }

        private IResponse UnloadAll(IRequestContext ctx, Route route)
        {
            Database.UnloadAll();
            return ArribaResponse.Ok("All Tables unloaded");
        }

        private IResponse Drop(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);

            if (!Database.TableExists(tableName)) return Response.NotFound();

            using (ctx.Monitor(MonitorEventLevel.Information, "Drop", "Table", tableName))
            {
                Database.DropTable(tableName);
                return ArribaResponse.Ok("Table deleted");
            }
        }

        private IResponse GetTablePermissions(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName))
                return ArribaResponse.NotFound("Table not found to return security for.");

            var security = Database.Security(tableName);
            return ArribaResponse.Ok(security);
        }


        private IResponse DeleteRows(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            var query = SelectQuery.ParseWhere(ctx.Request.ResourceParameters["q"]);

            // Run server correctors
            query = CurrentCorrectors(ctx).Correct(query);

            if (!Database.TableExists(tableName)) return Response.NotFound();

            var table = Database[tableName];
            var result = table.Delete(query);

            return ArribaResponse.Ok(result.Count);
        }

        private async Task<IResponse> SetTablePermissions(IRequestContext request, Route route)
        {
            var security = await request.Request.ReadBodyAsync<SecurityPermissions>();
            var tableName = GetAndValidateTableName(route);

            if (!Database.TableExists(tableName))
                return ArribaResponse.NotFound("Table doesn't exist to update security for.");

            // Reset table permissions and save them
            Database.SetSecurity(tableName, security);
            Database.SaveSecurity(tableName);

            return ArribaResponse.Ok("Security Updated");
        }

        private async Task<IResponse> CreateNew(IRequestContext request, Route routeData)
        {
            var createTable = await request.Request.ReadBodyAsync<CreateTableRequest>();

            if (createTable == null) return ArribaResponse.BadRequest("Invalid body");

            // Does the table already exist? 
            if (Database.TableExists(createTable.TableName)) return ArribaResponse.BadRequest("Table already exists");

            using (request.Monitor(MonitorEventLevel.Information, "Create", "Table", createTable.TableName,
                createTable))
            {
                var table = Database.AddTable(createTable.TableName, createTable.ItemCountLimit);

                // Add columns from request
                table.AddColumns(createTable.Columns);

                // Include permissions from request
                if (createTable.Permissions != null)
                {
                    // Ensure the creating user is always an owner
                    createTable.Permissions.Grant(IdentityScope.User, request.Request.User.Identity.Name,
                        PermissionScope.Owner);

                    Database.SetSecurity(createTable.TableName, createTable.Permissions);
                }

                // Save, so that table existence, column definitions, and permissions are saved
                table.Save();
                Database.SaveSecurity(createTable.TableName);
            }

            return ArribaResponse.Ok(null);
        }

        /// <summary>
        ///     Add requested column(s) to the specified table.
        /// </summary>
        private async Task<IResponse> AddColumns(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);

            using (request.Monitor(MonitorEventLevel.Information, "AddColumn", "Table", tableName))
            {
                if (!Database.TableExists(tableName))
                    return ArribaResponse.NotFound("Table not found to Add Columns to.");

                var table = Database[tableName];

                var columns = await request.Request.ReadBodyAsync<List<ColumnDetails>>();
                table.AddColumns(columns);

                return ArribaResponse.Ok("Added");
            }
        }

        /// <summary>
        ///     Reload the specified table.
        /// </summary>
        private IResponse Reload(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName)) return ArribaResponse.NotFound("Table not found to reload");

            using (request.Monitor(MonitorEventLevel.Information, "Reload", "Table", tableName))
            {
                Database.ReloadTable(tableName);
                return ArribaResponse.Ok("Reloaded");
            }
        }

        /// <summary>
        ///     Saves the specified table.
        /// </summary>
        private IResponse Save(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName)) return ArribaResponse.NotFound("Table not found to save");

            using (request.Monitor(MonitorEventLevel.Information, "Save", "Table", tableName))
            {
                var t = Database[tableName];

                // Verify before saving; don't save if inconsistent
                var d = new ExecutionDetails();
                t.VerifyConsistency(VerificationLevel.Normal, d);

                if (d.Succeeded)
                {
                    t.Save();
                    return ArribaResponse.Ok("Saved");
                }

                return ArribaResponse.Error("Table state inconsistent. Not saving. Restart server to reload. Errors: " +
                                            d.Errors);
            }
        }

        /// <summary>
        ///     Revokes access to a table.
        /// </summary>
        private async Task<IResponse> Revoke(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName))
                return ArribaResponse.NotFound("Table not found to revoke permission on.");

            var identity = await request.Request.ReadBodyAsync<SecurityIdentity>();
            if (string.IsNullOrEmpty(identity.Name))
                return ArribaResponse.BadRequest("Identity name must not be empty");

            PermissionScope scope;
            if (!Enum.TryParse(route["scope"], true, out scope))
                return ArribaResponse.BadRequest("Unknown permission scope {0}", route["scope"]);

            using (request.Monitor(MonitorEventLevel.Information, "RevokePermission", "Table", tableName,
                new {Scope = scope, Identity = identity}))
            {
                var security = Database.Security(tableName);
                security.Revoke(identity, scope);

                // Save permissions
                Database.SaveSecurity(tableName);
            }

            return ArribaResponse.Ok("Revoked");
        }

        /// <summary>
        ///     Grants access to a table.
        /// </summary>
        private async Task<IResponse> Grant(IRequestContext request, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName))
                return ArribaResponse.NotFound("Table not found to grant permission on.");

            var identity = await request.Request.ReadBodyAsync<SecurityIdentity>();
            if (string.IsNullOrEmpty(identity.Name))
                return ArribaResponse.BadRequest("Identity name must not be empty");

            PermissionScope scope;
            if (!Enum.TryParse(route["scope"], true, out scope))
                return ArribaResponse.BadRequest("Unknown permission scope {0}", route["scope"]);

            using (request.Monitor(MonitorEventLevel.Information, "GrantPermission", "Table", tableName,
                new {Scope = scope, Identity = identity}))
            {
                var security = Database.Security(tableName);
                security.Grant(identity.Scope, identity.Name, scope);

                // Save permissions
                Database.SaveSecurity(tableName);
            }

            return ArribaResponse.Ok("Granted");
        }

        private static string SanitizeIdentity(string rawIdentity)
        {
            if (string.IsNullOrEmpty(rawIdentity))
                throw new ArgumentException("Identity must not be empty", "rawIdentity");

            return rawIdentity.Replace("/", "\\");
        }
    }
}