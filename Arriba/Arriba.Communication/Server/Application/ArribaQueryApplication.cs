// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Composition;
    using System.Linq;
    using System.Threading.Tasks;
    using Authentication;
    using Communication;
    using Communication.Application;
    using Hosting;
    using Model;
    using Model.Expressions;
    using Model.Query;
    using Model.Security;
    using Monitoring;
    using Serialization;
    using Serialization.Csv;
    using Structures;

    /// <summary>
    ///     Arriba restful application for query operations.
    /// </summary>
    [Export(typeof(IRoutedApplication))]
    internal class ArribaQueryApplication : ArribaApplication
    {
        private const string DefaultFormat = "dictionary";

        [ImportingConstructor]
        public ArribaQueryApplication(DatabaseFactory f, ClaimsAuthenticationService auth)
            : base(f, auth)
        {
            // /table/foo?type=select
            GetAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "select")),
                ValidateReadAccessAsync, Select);
            PostAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "select")),
                ValidateReadAccessAsync, Select);

            // /table/foo?type=distinct
            GetAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "distinct")),
                ValidateReadAccessAsync, Distinct);
            PostAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "distinct")),
                ValidateReadAccessAsync, Distinct);

            // /table/foo?type=aggregate
            GetAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "aggregate")),
                ValidateReadAccessAsync, Aggregate);
            PostAsync(new RouteSpecification("/table/:tableName", new UrlParameter("action", "aggregate")),
                ValidateReadAccessAsync, Aggregate);

            GetAsync(new RouteSpecification("/allCount"), AllCount);
            GetAsync(new RouteSpecification("/suggest"), Suggest);
        }

        private async Task<IResponse> Select(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName)) return ArribaResponse.NotFound("Table not found to select from.");

            var outputFormat = ctx.Request.ResourceParameters["fmt"];

            var p = await ParametersFromQueryStringAndBody(ctx);
            var query = SelectQueryFromRequest(Database, p);
            query.TableName = tableName;

            var table = Database[tableName];
            SelectResult result = null;

            // If no columns were requested or this is RSS, get only the ID column
            if (query.Columns == null || query.Columns.Count == 0 ||
                string.Equals(outputFormat, "rss", StringComparison.OrdinalIgnoreCase))
                query.Columns = new[] {table.IDColumn.Name};

            // Read Joins, if passed
            var wrappedQuery = WrapInJoinQueryIfFound(query, Database, p);

            var correctors = CurrentCorrectors(ctx);
            using (ctx.Monitor(MonitorEventLevel.Verbose, "Correct", "Table", tableName, query.Where.ToString()))
            {
                // Run server correctors
                wrappedQuery.Correct(correctors);
            }

            using (ctx.Monitor(MonitorEventLevel.Information, "Select", "Table", tableName, query.Where.ToString()))
            {
                // Run the query
                result = Database.Query(wrappedQuery, si => IsInIdentity(ctx.Request.User, si));
            }

            // Canonicalize column names (if query successful)
            if (result.Details.Succeeded) query.Columns = result.Values.Columns.Select(cd => cd.Name).ToArray();

            // Format the result in the return format
            switch ((outputFormat ?? "").ToLowerInvariant())
            {
                case "":
                case "json":
                    return ArribaResponse.Ok(result);
                case "csv":
                    return ToCsvResponse(result, $"{tableName}-{DateTime.Now:yyyyMMdd}.csv");
                case "rss":
                    return ToRssResponse(result, "", query.TableName + ": " + query.Where,
                        ctx.Request.ResourceParameters["iURL"]);
                default:
                    throw new ArgumentException($"OutputFormat [fmt] passed, '{outputFormat}', was invalid.");
            }
        }

        private SelectQuery SelectQueryFromRequest(Database db, NameValueCollection p)
        {
            var query = new SelectQuery();

            query.Where = SelectQuery.ParseWhere(p["q"]);
            query.OrderByColumn = p["ob"];
            query.Columns = ReadParameterSet(p, "c", "cols");

            var take = p["t"];
            if (!string.IsNullOrEmpty(take)) query.Count = ushort.Parse(take);

            var sortOrder = p["so"] ?? "";
            switch (sortOrder.ToLowerInvariant())
            {
                case "":
                case "asc":
                    query.OrderByDescending = false;
                    break;
                case "desc":
                    query.OrderByDescending = true;
                    break;
                default:
                    throw new ArgumentException($"SortOrder [so] passed, '{sortOrder}' was not 'asc' or 'desc'.");
            }

            var highlightString = p["h"];
            if (!string.IsNullOrEmpty(highlightString))
                // Set the end highlight string to the start highlight string if it is not set. 
                query.Highlighter = new Highlighter(highlightString, p["h2"] ?? highlightString);

            return query;
        }

        /// <summary>
        ///     Read a set of parameters into a List (C1=X&C2=Y&C3=Z) => { "X", "Y", "Z" }.
        /// </summary>
        /// <param name="request">IRequest to read from</param>
        /// <param name="baseName">Parameter name before numbered suffix ('C' -> look for 'C1', 'C2', ...)</param>
        /// <returns>List&lt;string&gt; containing values for the parameter set, if any are found, otherwise an empty list.</returns>
        protected static List<string> ReadParameterSet(NameValueCollection parameters, string baseName)
        {
            var result = new List<string>();

            var i = 1;
            while (true)
            {
                var value = parameters[baseName + i];
                if (string.IsNullOrEmpty(value)) break;

                result.Add(value);
                ++i;
            }

            return result;
        }

        /// <summary>
        ///     Read a set of parameters into a List, allowing a single comma-delimited fallback value.
        ///     (C1=X&C2=Y&C3=Z or Cols=X,Y,Z) => { "X", "Y", "Z" }
        /// </summary>
        /// <param name="request">IRequest to read from</param>
        /// <param name="nameIfSeparate">
        ///     Parameter name prefix if parameters are passed separately ('C' -> look for 'C1', 'C2',
        ///     ...)
        /// </param>
        /// <param name="nameIfDelimited">Parameter name if parameters are passed together comma delimited ('Cols')</param>
        /// <returns>List&lt;string&gt; containing values for the parameter set, if any are found, otherwise an empty list.</returns>
        protected static List<string> ReadParameterSet(NameValueCollection parameters, string nameIfSeparate,
            string nameIfDelimited)
        {
            var result = ReadParameterSet(parameters, nameIfSeparate);

            if (result.Count == 0)
            {
                var delimitedValue = parameters[nameIfDelimited];
                if (!string.IsNullOrEmpty(delimitedValue)) result = new List<string>(delimitedValue.Split(','));
            }

            return result;
        }

        private static IQuery<T> WrapInJoinQueryIfFound<T>(IQuery<T> primaryQuery, Database db, NameValueCollection p)
        {
            var joins = new List<SelectQuery>();

            var joinQueries = ReadParameterSet(p, "q");
            var joinTables = ReadParameterSet(p, "t");

            for (var queryIndex = 0; queryIndex < Math.Min(joinQueries.Count, joinTables.Count); ++queryIndex)
                joins.Add(new SelectQuery
                    {Where = SelectQuery.ParseWhere(joinQueries[queryIndex]), TableName = joinTables[queryIndex]});

            if (joins.Count == 0)
                return primaryQuery;
            return new JoinQuery<T>(db, primaryQuery, joins);
        }

        private static IResponse ToCsvResponse(SelectResult result, string fileName)
        {
            const string outputMimeType = "text/csv; encoding=utf-8";

            var resp = new StreamWriterResponse(outputMimeType, async s =>
            {
                var context = new SerializationContext(s);
                var items = result.Values;

                // ***Crazy Excel Business***
                // This is pretty ugly. If the first 2 chars in a CSV file as ID, then excel is  thinks the file is a SYLK 
                // file not a CSV File (!) and will alert the user. Excel does not care about output mime types. 
                // 
                // To work around this, and have a _nice_ experience for csv export, we'll modify 
                // the first column name to " ID" to trick Excel. It's not perfect, but it'll do.
                // 
                // As a mitigation for round-tripping, the CsvReader will trim column names. Sigh. 
                var columns = new List<string>();

                foreach (var column in items.Columns)
                    if (columns.Count == 0 && column.Name.Equals("ID", StringComparison.OrdinalIgnoreCase))
                        columns.Add(" ID");
                    else
                        columns.Add(column.Name);

                var writer = new CsvWriter(context, columns);

                for (var row = 0; row < items.RowCount; ++row)
                {
                    for (var col = 0; col < items.ColumnCount; ++col) writer.AppendValue(items[row, col]);

                    writer.AppendRowSeparator();
                }

                context.Writer.Flush();
                await s.FlushAsync();
            });

            resp.AddHeader("Content-Disposition", string.Concat("attachment;filename=\"", fileName, "\";"));

            return resp;
        }

        private static IResponse ToRssResponse(SelectResult result, string rssUrl, string query,
            string itemUrlWithoutId)
        {
            var utcNow = DateTime.UtcNow;

            const string outputMimeType = "application/rss+xml; encoding=utf-8";

            var resp = new StreamWriterResponse(outputMimeType, async s =>
            {
                var context = new SerializationContext(s);
                var w = new RssWriter(context);

                var queryBB = (ByteBlock) query;
                w.WriteRssHeader(queryBB, queryBB, rssUrl, utcNow, TimeSpan.FromHours(1));

                ByteBlock baseLink = itemUrlWithoutId;
                var items = result.Values;
                for (var row = 0; row < items.RowCount; ++row)
                {
                    var id = ConvertToByteBlock(items[row, 0]);
                    w.WriteItem(id, id, id, baseLink, utcNow);
                }

                w.WriteRssFooter();

                context.Writer.Flush();
                await s.FlushAsync();
            });

            return resp;
        }

        private static ByteBlock ConvertToByteBlock(object value)
        {
            if (value == null) return ByteBlock.Zero;

            if (value is ByteBlock)
                return (ByteBlock) value;
            if (value is string)
                return (ByteBlock) value;
            return (ByteBlock) value.ToString();
        }

        private async Task<IResponse> AllCount(IRequestContext ctx, Route route)
        {
            var p = await ParametersFromQueryStringAndBody(ctx);

            var queryString = p["q"] ?? "";
            var result = new AllCountResult(queryString);

            // Build a Count query
            IQuery<AggregationResult> query = new AggregationQuery("count", null, queryString);

            // Wrap in Joins, if found
            query = WrapInJoinQueryIfFound(query, Database, p);

            // Run server correctors
            using (ctx.Monitor(MonitorEventLevel.Verbose, "Correct", "AllCount", detail: query.Where.ToString()))
            {
                query.Correct(CurrentCorrectors(ctx));
            }

            // Accumulate Results for each table
            var user = ctx.Request.User;
            using (ctx.Monitor(MonitorEventLevel.Information, "AllCount", "AllCount", detail: query.Where.ToString()))
            {
                var defaultWhere = query.Where;

                foreach (var tableName in Database.TableNames)
                    if (HasTableAccess(tableName, user, PermissionScope.Reader))
                    {
                        query.TableName = tableName;
                        query.Where = defaultWhere;

                        var tableCount = Database.Query(query, si => IsInIdentity(ctx.Request.User, si));

                        if (!tableCount.Details.Succeeded || tableCount.Values == null)
                            result.ResultsPerTable.Add(new CountResult(tableName, 0, true, false));
                        else
                            result.ResultsPerTable.Add(new CountResult(tableName, (ulong) tableCount.Values[0, 0], true,
                                tableCount.Details.Succeeded));
                    }
                    else
                    {
                        result.ResultsPerTable.Add(new CountResult(tableName, 0, false, false));
                    }
            }

            // Sort results so that succeeding tables are first and are subsorted by count [descending]
            result.ResultsPerTable.Sort((left, right) =>
            {
                var order = right.Succeeded.CompareTo(left.Succeeded);
                if (order != 0) return order;

                return right.Count.CompareTo(left.Count);
            });

            return ArribaResponse.Ok(result);
        }

        private async Task<IResponse> Suggest(IRequestContext ctx, Route route)
        {
            var p = await ParametersFromQueryStringAndBody(ctx);

            var query = p["q"];
            var selectedTable = p["t"];
            var user = ctx.Request.User;

            IntelliSenseResult result = null;

            using (ctx.Monitor(MonitorEventLevel.Verbose, "Suggest", "Suggest", detail: query))
            {
                // Get all available tables
                var tables = new List<Table>();
                foreach (var tableName in Database.TableNames)
                    if (HasTableAccess(tableName, user, PermissionScope.Reader))
                        if (string.IsNullOrEmpty(selectedTable) ||
                            selectedTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                            tables.Add(Database[tableName]);

                // Get IntelliSense results and return
                var qi = new QueryIntelliSense();
                result = qi.GetIntelliSenseItems(query, tables);
            }

            return ArribaResponse.Ok(result);
        }

        private IResponse Query<T>(IRequestContext ctx, Route route, IQuery<T> query, NameValueCollection p)
        {
            var wrappedQuery = WrapInJoinQueryIfFound(query, Database, p);

            // Ensure the table exists and set it on the query
            var tableName = GetAndValidateTableName(route);
            if (!Database.TableExists(tableName)) return ArribaResponse.NotFound("Table not found to query.");

            query.TableName = tableName;

            // Correct the query with default correctors
            using (ctx.Monitor(MonitorEventLevel.Verbose, "Correct", "Table", tableName, query.Where.ToString()))
            {
                query.Correct(CurrentCorrectors(ctx));
            }

            // Execute and return results for the query
            using (ctx.Monitor(MonitorEventLevel.Information, query.GetType().Name, "Table", tableName,
                query.Where.ToString()))
            {
                var result = Database.Query(wrappedQuery, si => IsInIdentity(ctx.Request.User, si));
                return ArribaResponse.Ok(result);
            }
        }

        private async Task<IResponse> Aggregate(IRequestContext ctx, Route route)
        {
            var p = await ParametersFromQueryStringAndBody(ctx);
            IQuery<AggregationResult> query = BuildAggregateFromContext(ctx, p);
            return Query(ctx, route, query, p);
        }

        private AggregationQuery BuildAggregateFromContext(IRequestContext ctx, NameValueCollection p)
        {
            var aggregationFunction = p["a"] ?? "count";
            var columnName = p["col"];
            var queryString = p["q"];

            var query = new AggregationQuery();
            query.Aggregator = AggregationQuery.BuildAggregator(aggregationFunction);
            query.AggregationColumns = string.IsNullOrEmpty(columnName) ? null : new[] {columnName};

            using (ctx.Monitor(MonitorEventLevel.Verbose, "Arriba.ParseQuery",
                string.IsNullOrEmpty(queryString) ? "<none>" : queryString))
            {
                query.Where = string.IsNullOrEmpty(queryString)
                    ? new AllExpression()
                    : SelectQuery.ParseWhere(queryString);
            }

            for (var dimensionPrefix = 'd';; ++dimensionPrefix)
            {
                var dimensionParts = ReadParameterSet(p, dimensionPrefix.ToString());
                if (dimensionParts.Count == 0) break;

                if (dimensionParts.Count == 1 && dimensionParts[0].EndsWith(">"))
                    query.Dimensions.Add(
                        new DistinctValueDimension(QueryParser.UnwrapColumnName(dimensionParts[0].TrimEnd('>'))));
                else
                    query.Dimensions.Add(new AggregationDimension("", dimensionParts));
            }

            return query;
        }

        private async Task<IResponse> Distinct(IRequestContext ctx, Route route)
        {
            var p = await ParametersFromQueryStringAndBody(ctx);
            IQuery<DistinctResult> query = BuildDistinctFromContext(ctx, p);
            return Query(ctx, route, query, p);
        }

        private DistinctQuery BuildDistinctFromContext(IRequestContext ctx, NameValueCollection p)
        {
            var query = new DistinctQueryTop();
            query.Column = p["col"];
            if (string.IsNullOrEmpty(query.Column))
                throw new ArgumentException("Distinct Column [col] must be passed.");

            var queryString = p["q"];
            using (ctx.Monitor(MonitorEventLevel.Verbose, "Arriba.ParseQuery",
                string.IsNullOrEmpty(queryString) ? "<none>" : queryString))
            {
                query.Where = string.IsNullOrEmpty(queryString)
                    ? new AllExpression()
                    : SelectQuery.ParseWhere(queryString);
            }

            var take = p["t"];
            if (!string.IsNullOrEmpty(take)) query.Count = ushort.Parse(take);

            return query;
        }
    }
}