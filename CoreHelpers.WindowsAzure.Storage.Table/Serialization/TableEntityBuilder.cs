﻿using Azure;
using Azure.Data.Tables;
using CoreHelpers.WindowsAzure.Storage.Table.Extensions;
using System.Collections.Generic;

namespace CoreHelpers.WindowsAzure.Storage.Table.Serialization
{
    public class TableEntityBuilder
    {
        private IDictionary<string, object> _data = new Dictionary<string, object>();

        public ETag ETag { get; set; }

        public TableEntityBuilder AddPartitionKey(string pkey)
        {
            _data.Add("PartitionKey", pkey);
            return this;
        }

        public TableEntityBuilder AddRowKey(string rkey, nVirtualValueEncoding encoding = nVirtualValueEncoding.None)
        {

            switch (encoding)
            {
                case nVirtualValueEncoding.None:
                    _data.Add("RowKey", rkey);
                    break;
                case nVirtualValueEncoding.Base64:
                    _data.Add("RowKey", rkey.ToBase64());
                    break;
                case nVirtualValueEncoding.Sha256:
                    _data.Add("RowKey", rkey.ToSha256());
                    break;
            }
            
            return this;
        }

        public TableEntityBuilder AddProperty(string property, object value)
        {
            _data.Add(property, value);
            return this;
        }

        public TableEntity Build()
        {
            var entity = new TableEntity(_data);
            if (ETag != null)
                entity.ETag = ETag;
            return entity;
        }
    }
}

