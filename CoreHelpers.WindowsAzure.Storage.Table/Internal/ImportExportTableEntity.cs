using System;
using System.Collections.Generic;
using System.Text;

namespace CoreHelpers.WindowsAzure.Storage.Table.Internal
{
    internal class ImportExportTableEntity
    {
        public ImportExportTableEntity()
        {
            Properties = new List<ImportExportTablePropertyEntity>();
        }

        public ImportExportTableEntity(string partitionKey, string rowKey) : this()
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }
        public List<ImportExportTablePropertyEntity> Properties { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
    }
}
