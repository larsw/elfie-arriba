// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Server.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Composition.Convention;
    using System.Composition.Hosting;
    using System.Linq;
    using Client;
    using Communication;
    using Model;
    using Newtonsoft.Json;

    public class Host : IDisposable
    {
        private readonly ContainerConfiguration _configuration;
        private CompositionHost _container;

        public Host()
        {
            var conventions = new ConventionBuilder();
            conventions.ForTypesDerivedFrom<IChannel>()
                .ExportInterfaces()
                .Shared();

            conventions.ForTypesDerivedFrom<IContentReader>()
                .Export<IContentReader>()
                .Shared();

            conventions.ForTypesDerivedFrom<IContentWriter>()
                .Export()
                .Export<IContentWriter>()
                .Shared();

            conventions.ForTypesDerivedFrom<IApplication>()
                .ExportInterfaces()
                .Shared();

            conventions.ForTypesDerivedFrom<JsonConverter>()
                .Export<JsonConverter>()
                .Shared();

            //                       Arriba.dll              Arriba.Client                  Arriba.Communication           Arriba.Server
            var assemblies = new[]
            {
                typeof(Table).Assembly, typeof(ArribaClient).Assembly, typeof(IApplication).Assembly,
                typeof(Host).Assembly
            };

            _configuration = new ContainerConfiguration().WithAssemblies(assemblies.Distinct(), conventions);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Compose()
        {
            _container = _configuration.CreateContainer();
        }

        public void Add<TContract>(TContract value)
        {
            _configuration.WithExport(value);
        }

        public void AddConfigurationValue<T>(string name, T value)
        {
            _configuration.WithExport(value, name);
        }

        public T GetService<T>()
        {
            return _container.GetExport<T>();
        }

        public IEnumerable<T> GetServices<T>()
        {
            return _container.GetExports<T>();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            if (_container != null) _container.Dispose();
        }
    }
}