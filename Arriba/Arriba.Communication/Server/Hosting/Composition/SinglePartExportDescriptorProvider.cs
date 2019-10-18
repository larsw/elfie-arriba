// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Composition.Hosting.Core;
    using System.Linq;

    /// <summary>
    ///     Base class for single instance export providers.
    /// </summary>
    internal abstract class SinglePartExportDescriptorProvider : ExportDescriptorProvider
    {
        private readonly string _contractName;
        private readonly Type _contractType;

        protected SinglePartExportDescriptorProvider(Type contractType, string contractName,
            IDictionary<string, object> metadata)
        {
            if (contractType == null) throw new ArgumentNullException("contractType");

            _contractType = contractType;
            _contractName = contractName;
            Metadata = metadata ?? new Dictionary<string, object>();
        }

        protected IDictionary<string, object> Metadata { get; }

        protected bool IsSupportedContract(CompositionContract contract)
        {
            if (contract.ContractType != _contractType ||
                contract.ContractName != _contractName)
                return false;

            if (contract.MetadataConstraints != null)
            {
                var subsetOfConstraints = contract.MetadataConstraints.Where(c => Metadata.ContainsKey(c.Key))
                    .ToDictionary(c => c.Key, c => Metadata[c.Key]);
                var constrainedSubset = new CompositionContract(contract.ContractType, contract.ContractName,
                    subsetOfConstraints.Count == 0 ? null : subsetOfConstraints);

                if (!contract.Equals(constrainedSubset))
                    return false;
            }

            return true;
        }
    }
}