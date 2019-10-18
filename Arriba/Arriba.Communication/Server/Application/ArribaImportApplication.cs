// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Application
{
    using System;
    using System.Collections.Generic;
    using System.Composition;
    using System.Linq;
    using System.Threading.Tasks;
    using Authentication;
    using Communication;
    using Communication.Application;
    using Hosting;
    using Model;
    using Model.Column;
    using Monitoring;
    using Serialization.Csv;
    using Structures;

    [Export(typeof(IRoutedApplication))]
    internal class ArribaImportApplication : ArribaApplication
    {
        private const int BatchSize = 100;

        [ImportingConstructor]
        public ArribaImportApplication(DatabaseFactory f, ClaimsAuthenticationService auth)
            : base(f, auth)
        {
            // POST /table/foo?type=csv -- Import CSV data 
            Post(new RouteSpecification("/table/:tableName", new UrlParameter("type", "csv")), ValidateWriteAccess,
                CsvAppend);

            // POST /table/foo?type=block -- Import as DataBlock format
            PostAsync(new RouteSpecification("/table/:tableName", new UrlParameter("type", "block")),
                ValidateWriteAccessAsync, DataBlockAppendAsync);

            // POST /table/foo?type=json -- Post many objects
            PostAsync(new RouteSpecification("/table/:tableName", new UrlParameter("type", "json")),
                ValidateWriteAccessAsync, JSONArrayAppendAsync);

            // POST /sample?type=csv -- Import CSV data 
            Post(new RouteSpecification("/sample", new UrlParameter("type", "csv")), CsvSample);
        }

        private IResponse CsvSample(IRequestContext ctx, Route route)
        {
            if (!ctx.Request.HasBody) return ArribaResponse.BadRequest("Empty request body");

            var result = new SampleResult();

            var config = new CsvReaderSettings {DisposeStream = true, HasHeaders = true};
            using (var reader = new CsvReader(ctx.Request.InputStream, config))
            {
                // Read the CSV fragment into a DataBlock
                var block = reader.ReadAsDataBlockBatch(10000, true).FirstOrDefault();

                if (block == null) return ArribaResponse.BadRequest("No result content found.");

                // Count the rows actually returned
                result.RowCount = block.RowCount + 1;

                // Insert only the first 100 rows and not the last (partial) row
                block.SetRowCount(Math.Min(block.RowCount - 1, 100));

                // Build a table with the sample
                var sample = new Table("Sample", 100);
                sample.AddOrUpdate(block, new AddOrUpdateOptions {AddMissingColumns = true});

                // Return the created columns in the order they appeared in the CSV
                result.Columns = sample.ColumnDetails.OrderBy(cd => block.IndexOfColumn(cd.Name)).ToList();

                // Return the columns and row count from the sample
                return ArribaResponse.Ok(result);
            }
        }

        private IResponse CsvAppend(IRequestContext ctx, Route route)
        {
            if (!ctx.Request.HasBody) return ArribaResponse.BadRequest("Empty request body");

            var tableName = GetAndValidateTableName(route);
            var table = Database[tableName];

            if (table == null) return ArribaResponse.BadRequest("Table {0} is not loaded or does not exist", tableName);

            var response = new ImportResponse();
            response.TableName = tableName;

            var config = new CsvReaderSettings {DisposeStream = true, HasHeaders = true};

            using (ctx.Monitor(MonitorEventLevel.Information, "Import.Csv", "Table", tableName))
            {
                using (var reader = new CsvReader(ctx.Request.InputStream, config))
                {
                    response.Columns = reader.ColumnNames;

                    foreach (var blockBatch in reader.ReadAsDataBlockBatch(BatchSize))
                    {
                        response.RowCount += blockBatch.RowCount;
                        table.AddOrUpdate(blockBatch, new AddOrUpdateOptions {AddMissingColumns = true});
                    }
                }
            }

            return ArribaResponse.Ok(response);
        }

        private async Task<IResponse> DataBlockAppendAsync(IRequestContext ctx, Route route)
        {
            var tableName = GetAndValidateTableName(route);
            var table = Database[tableName];

            if (table == null) return ArribaResponse.BadRequest("Table {0} is not loaded or does not exist", tableName);

            using (ctx.Monitor(MonitorEventLevel.Information, "Import.DataBlock", "Table", tableName))
            {
                var block = await ctx.Request.ReadBodyAsync<DataBlock>();
                table.AddOrUpdate(block, new AddOrUpdateOptions {AddMissingColumns = true});

                var response = new ImportResponse();
                response.TableName = tableName;
                response.Columns = block.Columns.Select(cd => cd.Name).ToArray();
                response.RowCount = block.RowCount;
                return ArribaResponse.Ok(response);
            }
        }

        private async Task<IResponse> JSONArrayAppendAsync(IRequestContext ctx, Route route)
        {
            var content = ctx.Request.Headers["Content-Type"];

            if (string.IsNullOrEmpty(content) ||
                !string.Equals(content, "application/json", StringComparison.OrdinalIgnoreCase))
                return ArribaResponse.BadRequest("Content-Type of {0} was not expected", content);
            if (!ctx.Request.HasBody) return ArribaResponse.BadRequest("Empty request body");

            var tableName = GetAndValidateTableName(route);
            var table = Database[tableName];

            if (table == null) return ArribaResponse.BadRequest("Table {0} is not loaded or does not exist", tableName);

            var rows = await ctx.Request.ReadBodyAsync<List<Dictionary<string, object>>>();

            var detail = new
            {
                RequestSize = ctx.Request.Headers["Content-Length"],
                RowCount = rows.Count
            };

            using (ctx.Monitor(MonitorEventLevel.Information, "Import.JsonObjectArray", "Table", tableName, detail))
            {
                // Read column names from JSON
                var columnDetails = new Dictionary<string, ColumnDetails>();
                foreach (var row in rows)
                foreach (var property in row)
                    if (property.Value != null && !columnDetails.ContainsKey(property.Key))
                    {
                        var colDetail = new ColumnDetails(property.Key);
                        columnDetails.Add(property.Key, colDetail);
                    }

                var columns = columnDetails.Values.ToArray();

                // Insert the data in batches 
                var block = new DataBlock(columns, BatchSize);
                for (var batchOffset = 0; batchOffset < rows.Count; batchOffset += BatchSize)
                {
                    var rowsLeft = rows.Count - batchOffset;
                    var rowsInBatch = BatchSize;

                    if (rowsLeft < BatchSize)
                    {
                        block = new DataBlock(columns, rowsLeft);
                        rowsInBatch = rowsLeft;
                    }

                    for (var blockRowIndex = 0; blockRowIndex < rowsInBatch; ++blockRowIndex)
                    {
                        var sourceRowIndex = blockRowIndex + batchOffset;

                        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                        {
                            object value = null;

                            if (rows[sourceRowIndex].TryGetValue(columns[columnIndex].Name, out value))
                                block[blockRowIndex, columnIndex] = value;
                        }
                    }

                    table.AddOrUpdate(block, new AddOrUpdateOptions {AddMissingColumns = true});
                }

                using (ctx.Monitor(MonitorEventLevel.Verbose, "table.save"))
                {
                    table.Save();
                }

                return ArribaResponse.Created(new ImportResponse
                {
                    TableName = table.Name,
                    RowCount = rows.Count(),
                    Columns = columns.Select(cd => cd.Name).ToArray()
                });
            }
        }

        private class SampleResult
        {
            public ICollection<ColumnDetails> Columns;
            public int RowCount;
        }

        private class ImportResponse
        {
            public string[] Columns;
            public int RowCount;
            public string TableName;
        }
    }
}