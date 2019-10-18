// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Arriba.Communication
{
    /// <summary>
    ///     Modifies a response to have no response body.
    /// </summary>
    internal class NullBodyResponse : IResponse
    {
        private readonly IResponse _response;

        public NullBodyResponse(IResponse response)
        {
            _response = response;
        }

        public bool Handled => true;

        public ResponseStatus Status => _response.Status;

        public object ResponseBody => null;

        public IWritableValueBag Headers => _response.Headers;

        public void AddHeader(string key, string value)
        {
            _response.Headers.Add(key, value);
        }

        public void Dispose()
        {
        }
    }
}