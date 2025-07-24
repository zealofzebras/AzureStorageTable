using System;
using Azure.Data.Tables;
using CoreHelpers.WindowsAzure.Storage.Table.Extensions;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using CoreHelpers.WindowsAzure.Storage.Table.Serialization;
using System.Linq;
using CoreHelpers.WindowsAzure.Storage.Table.Internal;

namespace CoreHelpers.WindowsAzure.Storage.Table
{
    public partial class StorageContext : IStorageContext
    {
        public async Task ExportToJsonAsync(string tableName, TextWriter writer, Action<ImportExportOperation> onOperation)
        {
            try
            {
                var tc = GetTableClient(GetTableName(tableName));

                var existsTable = await tc.ExistsAsync();
                if (!existsTable)
                    throw new FileNotFoundException($"Table '{tableName}' does not exist");

                // build the json writer - System.Text.Json uses Utf8JsonWriter which requires a stream
                using var stream = new MemoryStream();
                using var jsonWriter = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
                
                // prepare the array in result
                jsonWriter.WriteStartArray();

                // enumerate all items from a table
                var tablePages = tc.QueryAsync<TableEntity>().AsPages();

                // do the backup
                await foreach (var page in tablePages)
                {
                    if (onOperation != null)
                        onOperation(ImportExportOperation.processingPage);

                    foreach (var entity in page.Values)
                    {
                        if (onOperation != null)
                            onOperation(ImportExportOperation.processingItem);

                        jsonWriter.WriteStartObject();
                        jsonWriter.WriteString(TableConstants.RowKey, entity.RowKey);
                        jsonWriter.WriteString(TableConstants.PartitionKey, entity.PartitionKey);
                        jsonWriter.WritePropertyName(TableConstants.Properties);
                        jsonWriter.WriteStartArray();
                        foreach (var propertyKvp in entity)
                        {
                            if (propertyKvp.Key.Equals(TableConstants.PartitionKey) || propertyKvp.Key.Equals(TableConstants.RowKey) || propertyKvp.Key.Equals("odata.etag") || propertyKvp.Key.Equals(TableConstants.Timestamp))
                                continue;

                            jsonWriter.WriteStartObject();
                            jsonWriter.WriteString(TableConstants.PropertyName, propertyKvp.Key);
                            jsonWriter.WriteNumber(TableConstants.PropertyType, (int)propertyKvp.Value.GetType().GetEdmPropertyType());
                            jsonWriter.WritePropertyName(TableConstants.PropertyValue);

                            switch (propertyKvp.Value.GetType().GetEdmPropertyType())
                            {
                                case ExportEdmType.DateTime:
                                    jsonWriter.WriteStringValue(((DateTimeOffset)propertyKvp.Value).ToUniversalTime().ToString("O"));
                                    break;
                                case ExportEdmType.String:
                                    jsonWriter.WriteStringValue(propertyKvp.Value.ToString());
                                    break;
                                case ExportEdmType.Int32:
                                    jsonWriter.WriteNumberValue((int)propertyKvp.Value);
                                    break;
                                case ExportEdmType.Int64:
                                    jsonWriter.WriteNumberValue((long)propertyKvp.Value);
                                    break;
                                case ExportEdmType.Double:
                                    jsonWriter.WriteNumberValue((double)propertyKvp.Value);
                                    break;
                                case ExportEdmType.Boolean:
                                    jsonWriter.WriteBooleanValue((bool)propertyKvp.Value);
                                    break;
                                case ExportEdmType.Guid:
                                    jsonWriter.WriteStringValue(propertyKvp.Value.ToString());
                                    break;
                                case ExportEdmType.Binary:
                                    jsonWriter.WriteStringValue(Convert.ToBase64String((byte[])propertyKvp.Value));
                                    break;
                                default:
                                    jsonWriter.WriteStringValue(propertyKvp.Value?.ToString() ?? "");
                                    break;
                            }

                            jsonWriter.WriteEndObject();
                        }
                        jsonWriter.WriteEndArray();
                        jsonWriter.WriteEndObject();
                    }

                    if (onOperation != null)
                        onOperation(ImportExportOperation.processedPage);
                }

                // finish the export
                jsonWriter.WriteEndArray();
                jsonWriter.Flush();
                
                // Write the JSON to the TextWriter
                stream.Position = 0;
                using var reader = new StreamReader(stream);
                await writer.WriteAsync(await reader.ReadToEndAsync());
                await writer.FlushAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task ImportFromJsonAsync(string tableName, StreamReader reader, Action<ImportExportOperation> onOperation)
        {
            // get the tableclient
            var tc = GetTableClient(GetTableName(tableName));

            // ensure table exists
            if (!await tc.ExistsAsync())
                await tc.CreateAsync();            

            // store the entities by partition key
            var entityStore = new Dictionary<string, List<TableEntity>>();

            // Read the entire JSON content
            var jsonContent = await reader.ReadToEndAsync();
            
            // Parse the JSON array
            var jsonArray = JsonDocument.Parse(jsonContent);
            
            foreach (var jsonElement in jsonArray.RootElement.EnumerateArray())
            {
                // Deserialize each item to ImportExportTableEntity
                var currentModel = JsonSerializer.Deserialize<ImportExportTableEntity>(jsonElement.GetRawText());

                foreach (var property in currentModel.Properties)
                {
                    if ((ExportEdmType)property.PropertyType == ExportEdmType.String && property.PropertyValue is JsonElement jsonVal && jsonVal.ValueKind == JsonValueKind.String)
                    {
                        var stringValue = jsonVal.GetString();
                        if (DateTime.TryParse(stringValue, out var dateValue))
                        {
                            property.PropertyValue = dateValue.ToString("o");
                        }
                        else
                        {
                            property.PropertyValue = stringValue;
                        }
                    } 
                    else if ((ExportEdmType)property.PropertyType == ExportEdmType.DateTime) 
                    {
                        if (property.PropertyValue is JsonElement jsonDateTime && jsonDateTime.ValueKind == JsonValueKind.String)
                        {
                            if (DateTime.TryParse(jsonDateTime.GetString(), out var dateTime))
                            {
                                property.PropertyValue = dateTime.ToUniversalTime();
                            }
                        }
                        else if (property.PropertyValue is DateTime dt)
                        {
                            property.PropertyValue = dt.ToUniversalTime();
                        }
                    }
                    else if (property.PropertyValue is JsonElement otherJsonVal)
                    {
                        // Handle other JSON element types appropriately
                        switch (otherJsonVal.ValueKind)
                        {
                            case JsonValueKind.String:
                                property.PropertyValue = otherJsonVal.GetString();
                                break;
                            case JsonValueKind.Number:
                                if (otherJsonVal.TryGetInt32(out var intVal))
                                    property.PropertyValue = intVal;
                                else if (otherJsonVal.TryGetInt64(out var longVal))
                                    property.PropertyValue = longVal;
                                else if (otherJsonVal.TryGetDouble(out var doubleVal))
                                    property.PropertyValue = doubleVal;
                                break;
                            case JsonValueKind.True:
                                property.PropertyValue = true;
                                break;
                            case JsonValueKind.False:
                                property.PropertyValue = false;
                                break;
                            case JsonValueKind.Null:
                                property.PropertyValue = null;
                                break;
                        }
                    }
                }

                // convert to table entity
                var tableEntity = GetTableEntity(currentModel);

                // add to the right store
                if (!entityStore.ContainsKey(tableEntity.PartitionKey))
                    entityStore.Add(tableEntity.PartitionKey, new List<TableEntity>());

                // add the entity 
                entityStore[tableEntity.PartitionKey].Add(tableEntity);

                // check if we need to offload this table 
                if (entityStore[tableEntity.PartitionKey].Count == 100)
                {
                    // insert the partition
                    await tc.SubmitTransactionAsync(entityStore[tableEntity.PartitionKey].Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)));
                    
                    // clear
                    entityStore.Remove(tableEntity.PartitionKey);
                }
            }

            // post processing
            foreach (var kvp in entityStore)
            {
                // insert the partition
                await tc.SubmitTransactionAsync(kvp.Value.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)));                    
            }
        }

        private TableEntity GetTableEntity(ImportExportTableEntity data)
        {
            var teBuilder = new TableEntityBuilder();

            teBuilder.AddPartitionKey(data.PartitionKey);
            teBuilder.AddRowKey(data.RowKey);
            
            foreach (var prop in data.Properties)
            {
                teBuilder.AddProperty(prop.PropertyName, prop.PropertyValue);                
            }

            return teBuilder.Build();
        }
    }
}