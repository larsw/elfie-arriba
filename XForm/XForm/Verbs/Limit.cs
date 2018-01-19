﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

using XForm.Data;
using XForm.Query;

namespace XForm.Verbs
{
    internal class LimitCommandBuilder : IVerbBuilder
    {
        public string Verb => "limit";
        public string Usage => "limit {RowLimit} {ColLimit?}";

        public IDataBatchEnumerator Build(IDataBatchEnumerator source, XDatabaseContext context)
        {
            int rowLimit = context.Parser.NextInteger();
            int colLimit = (context.Parser.HasAnotherArgument ? context.Parser.NextInteger() : -1);
            return new Limit(source, rowLimit, colLimit);
        }
    }

    public class Limit : DataBatchEnumeratorWrapper
    {
        private int _colCountLimit;
        private int _rowCountLimit;
        private int _rowCountSoFar;
        private IReadOnlyList<ColumnDetails> _columns;

        public Limit(IDataBatchEnumerator source, int rowLimit, int colLimit = -1) : base(source)
        {
            _rowCountLimit = (rowLimit > 0 ? rowLimit : int.MaxValue);
            _colCountLimit = colLimit;
            _columns = (_colCountLimit > 0 ? base.Columns.Take(colLimit).ToList() : base.Columns);
        }

        public override IReadOnlyList<ColumnDetails> Columns => _columns;

        public override void Reset()
        {
            base.Reset();
            _rowCountSoFar = 0;
        }

        public override int Next(int desiredCount)
        {
            if (_rowCountSoFar >= _rowCountLimit) return 0;
            if (_rowCountSoFar + desiredCount > _rowCountLimit) desiredCount = _rowCountLimit - _rowCountSoFar;

            int sourceCount = _source.Next(desiredCount);
            _rowCountSoFar += sourceCount;

            return sourceCount;
        }
    }
}
