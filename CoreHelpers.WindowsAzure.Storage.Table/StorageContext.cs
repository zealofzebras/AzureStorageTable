﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;
using CoreHelpers.WindowsAzure.Storage.Table.Attributes;

namespace CoreHelpers.WindowsAzure.Storage.Table
{
	public enum nStoreOperation {
		insertOperation, 
		insertOrReplaceOperation,
		mergeOperation,
		mergeOrInserOperation,
		delete
	}

	public class QueryResult<T>
	{
		public IQueryable<T> Items { get; internal set; }
		public TableContinuationToken NextToken { get; internal set; }
	}
	
	public class StorageContext : IDisposable
	{		
		private CloudStorageAccount _storageAccount { get; set; }
		private Dictionary<Type, DynamicTableEntityMapper> _entityMapperRegistry { get; set; } = new Dictionary<Type, DynamicTableEntityMapper>();
		private bool _autoCreateTable { get; set; } = false;
		private IStorageContextDelegate _delegate { get; set; }
		
		public StorageContext(string storageAccountName, string storageAccountKey, string storageEndpointSuffix = null)
		{
			var connectionString = String.Format("DefaultEndpointsProtocol={0};AccountName={1};AccountKey={2}", "https", storageAccountName, storageAccountKey);
			if (!String.IsNullOrEmpty(storageEndpointSuffix))
				connectionString = String.Format("DefaultEndpointsProtocol={0};AccountName={1};AccountKey={2};EndpointSuffix={3}", "https", storageAccountName, storageAccountKey, storageEndpointSuffix);
			
			_storageAccount = CloudStorageAccount.Parse(connectionString);
		}
		
		public StorageContext(StorageContext parentContext)
		{
			_storageAccount = parentContext._storageAccount;
			_entityMapperRegistry = new Dictionary<Type, DynamicTableEntityMapper>(parentContext._entityMapperRegistry);
			this.SetDelegate(parentContext._delegate);
		}

		public void Dispose()
		{
			
		}
		
		public void SetDelegate(IStorageContextDelegate delegateModel)
		{
			_delegate = delegateModel;		
		}
		
		public StorageContext EnableAutoCreateTable() 
		{
			_autoCreateTable = true;
			return this;
		}

		public void AddEntityMapper(Type entityType, DynamicTableEntityMapper entityMapper)
		{
			_entityMapperRegistry.Add(entityType, entityMapper);
		}
        
        public void AddAttributeMapper() 
        {
            AddAttributeMapper(Assembly.GetEntryAssembly());

            /*foreach(var assembly in Assembly.GetEntryAssembly().GetReferencedAssemblies()) {
                AddAttributeMapper(assembly);
            } */          
        }

        internal void AddAttributeMapper(Assembly assembly)
        {
            var typesWithAttribute = assembly.GetTypesWithAttribute(typeof(StorableAttribute));
            foreach(var type in typesWithAttribute) {
                AddAttributeMapper(type);
            }
        }
        
        public void AddAttributeMapper(Type type) 
        {
        	AddAttributeMapper(type, string.Empty);
        }
        
        public void AddAttributeMapper(Type type, String optionalTablenameOverride ) 
        {
    		// get the concrete attribute
            var storableAttribute = type.GetTypeInfo().GetCustomAttribute<StorableAttribute>();                
            if (String.IsNullOrEmpty(storableAttribute.Tablename)) {
                storableAttribute.Tablename = type.Name;
            }

            // store the neded properties
            string partitionKeyFormat = null;
            string rowKeyFormat = null;
                                    
            // get the partitionkey property & rowkey property
            var properties = type.GetRuntimeProperties();
            foreach (var property in properties)
            {
                if (partitionKeyFormat != null && rowKeyFormat != null)
                    break;
                         
                if (partitionKeyFormat == null && property.GetCustomAttribute<PartitionKeyAttribute>() != null)
                    partitionKeyFormat = property.Name;
				
			 	if (rowKeyFormat == null && property.GetCustomAttribute<RowKeyAttribute>() != null)
					rowKeyFormat = property.Name;		               
            }

			// virutal partition key property
			var virtualPartitionKeyAttribute = type.GetTypeInfo().GetCustomAttribute<VirtualPartitionKeyAttribute>();
			if (virtualPartitionKeyAttribute != null && !String.IsNullOrEmpty(virtualPartitionKeyAttribute.PartitionKeyFormat))
				partitionKeyFormat = virtualPartitionKeyAttribute.PartitionKeyFormat;
			
			// virutal row key property
			var virtualRowKeyAttribute = type.GetTypeInfo().GetCustomAttribute<VirtualRowKeyAttribute>();
			if (virtualRowKeyAttribute != null && !String.IsNullOrEmpty(virtualRowKeyAttribute.RowKeyFormat))
				rowKeyFormat = virtualRowKeyAttribute.RowKeyFormat;
				
            // check 
            if (partitionKeyFormat == null || rowKeyFormat == null)
                throw new Exception("Missing Partition or RowKey Attribute");
                
            // build the mapper
            AddEntityMapper(type, new DynamicTableEntityMapper()
            {
                TableName = String.IsNullOrEmpty(optionalTablenameOverride) ? storableAttribute.Tablename : optionalTablenameOverride,
                PartitionKeyFormat = partitionKeyFormat,
                RowKeyFormat = rowKeyFormat
            });         
        } 
               
        public IEnumerable<Type> GetRegisteredMappers() 
        {
			return _entityMapperRegistry.Keys;
        } 
        
        public Task CreateTableAsync(Type entityType, bool ignoreErrorIfExists = true) 
		{
				// Retrieve a reference to the table.
			CloudTable table = GetTableReference(GetTableName(entityType));

			if (ignoreErrorIfExists)
			{
				// Create the table if it doesn't exist.
				return table.CreateIfNotExistsAsync();
			}
			else
			{
				// Create table and throw error
				return table.CreateAsync();
			}
		}
		
		
		public Task CreateTableAsync<T>(bool ignoreErrorIfExists = true)
		{
			return CreateTableAsync(typeof(T), ignoreErrorIfExists);
		}
		
		public void CreateTable<T>(bool ignoreErrorIfExists = true)
		{
			this.CreateTableAsync<T>(ignoreErrorIfExists).GetAwaiter().GetResult();
		}

		public async Task InsertAsync<T>(IEnumerable<T> models) where T : new ()
		{
			await this.StoreAsync(nStoreOperation.insertOperation, models);
		}

		public async Task MergeAsync<T>(IEnumerable<T> models) where T : new()
		{
			await this.StoreAsync(nStoreOperation.mergeOperation, models);
		}

		public async Task InsertOrReplaceAsync<T>(IEnumerable<T> models) where T : new()
		{
			await this.StoreAsync(nStoreOperation.insertOrReplaceOperation, models);
		}
		
		public async Task InsertOrReplaceAsync<T>(T model) where T : new()
		{
			await this.StoreAsync(nStoreOperation.insertOrReplaceOperation, new List<T>() { model });
		}

		public async Task MergeOrInsertAsync<T>(IEnumerable<T> models) where T : new()
		{
			await this.StoreAsync(nStoreOperation.mergeOrInserOperation, models);
		}
		
		public async Task MergeOrInsertAsync<T>(T model) where T : new()
		{
			await this.StoreAsync(nStoreOperation.mergeOrInserOperation, new List<T>() { model });
		}

		public async Task<T> QueryAsync<T>(string partitionKey, string rowKey, int maxItems = 0) where T : new()
		{
			var result = await QueryAsyncInternal<T>(partitionKey, rowKey, maxItems);
			return result.FirstOrDefault<T>();
		}

		public async Task<IQueryable<T>> QueryAsync<T>(string partitionKey,  int maxItems = 0) where T : new()
		{
			return await QueryAsyncInternal<T>(partitionKey, null, maxItems);
		}

		public async Task<IQueryable<T>> QueryAsync<T>(int maxItems = 0) where T: new() 
		{
			return await QueryAsyncInternal<T>(null, null, maxItems);
		}
		
		private string GetTableName<T>() 
		{
			return GetTableName(typeof(T));
		}
		
		private string GetTableName(Type entityType)
		{
			// lookup the entitymapper
			var entityMapper = _entityMapperRegistry[entityType];

			// get the table name
			return entityMapper.TableName;
		}
		
		public async Task StoreAsync<T>(nStoreOperation storaeOperationType, IEnumerable<T> models) where T : new()
		{
			try
			{
				// notify delegate
				if (_delegate != null)
					_delegate.OnStoring(typeof(T), storaeOperationType);
					
				// Retrieve a reference to the table.
				CloudTable table = GetTableReference(GetTableName<T>());

				// Create the batch operation.
				List<TableBatchOperation> batchOperations = new List<TableBatchOperation>();
				
				// Create the first batch
				var currentBatch = new TableBatchOperation();
				batchOperations.Add(currentBatch);

				// lookup the entitymapper
				var entityMapper = _entityMapperRegistry[typeof(T)];

				// define the modelcounter
				int modelCounter = 0;

				// Add all items
				foreach (var model in models)
				{
					switch (storaeOperationType)
					{
						case nStoreOperation.insertOperation:
							currentBatch.Insert(new DynamicTableEntity<T>(model, entityMapper));
							break;
						case nStoreOperation.insertOrReplaceOperation:
							currentBatch.InsertOrReplace(new DynamicTableEntity<T>(model, entityMapper));
							break;
						case nStoreOperation.mergeOperation:
							currentBatch.Merge(new DynamicTableEntity<T>(model, entityMapper));
							break;
						case nStoreOperation.mergeOrInserOperation:
							currentBatch.InsertOrMerge(new DynamicTableEntity<T>(model, entityMapper));
							break;
						case nStoreOperation.delete: 
							currentBatch.Delete(new DynamicTableEntity<T>(model, entityMapper));
							break;
					}

					modelCounter++;

					if (modelCounter % 100 == 0)
					{
						currentBatch = new TableBatchOperation();
						batchOperations.Add(currentBatch);
					}
				}

				// execute 
				foreach (var createdBatch in batchOperations)
				{
					if (createdBatch.Count() > 0)
					{
						await table.ExecuteBatchAsync(createdBatch);

						// notify delegate
						if (_delegate != null)
							_delegate.OnStored(typeof(T), storaeOperationType, createdBatch.Count(), null);
					}
				}
			} 
			catch (StorageException ex) 
			{
				// check the exception
				if (!_autoCreateTable || !ex.Message.StartsWith("0:The table specified does not exist", StringComparison.CurrentCulture))
				{
					// notify delegate
					if (_delegate != null)
						_delegate.OnStored(typeof(T), storaeOperationType, 0, ex);				
					
					throw ex;
				}

				// try to create the table	
				await CreateTableAsync<T>();

				// retry 
				await StoreAsync<T>(storaeOperationType, models);					
			}
		}

		public async Task DeleteAsync<T>(T model) where T: new() 
		{
			await this.StoreAsync(nStoreOperation.delete, new List<T>() { model });
		}
		
		public async Task DeleteAsync<T>(IEnumerable<T> models) where T: new() 
		{
			await this.StoreAsync(nStoreOperation.delete, models);
		}

		private async Task<QueryResult<T>> QueryAsyncInternalSinglePage<T>(string partitionKey, string rowKey, int maxItems = 0, TableContinuationToken continuationToken = null) where T : new()
		{
			try
			{
				// notify delegate
				if (_delegate != null)
					_delegate.OnQuerying(typeof(T), partitionKey, rowKey, maxItems, continuationToken != null);				
					
				// Retrieve a reference to the table.
				CloudTable table = GetTableReference(GetTableName<T>());

				// lookup the entitymapper
				var entityMapper = _entityMapperRegistry[typeof(T)];

				// Construct the query to get all entries
				TableQuery<DynamicTableEntity<T>> query = new TableQuery<DynamicTableEntity<T>>();

				// add partitionkey if exists		
				string partitionKeyFilter = null;
				if (partitionKey != null)
					partitionKeyFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);

				// add row key if exists
				string rowKeyFilter = null;
				if (rowKey != null)
					rowKeyFilter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, rowKey);

				// define the max query items
				if (maxItems > 0)
					query = query.Take(maxItems);
					
				// build the query filter
				if (partitionKey != null && rowKey != null)
					query = query.Where(TableQuery.CombineFilters(partitionKeyFilter, TableOperators.And, rowKeyFilter));
				else if (partitionKey != null && rowKey == null)
					query = query.Where(partitionKeyFilter);										
				else if (partitionKey == null && rowKey != null)
					throw new Exception("PartitionKey must have a value");
				
				// execute the query											
				var queryResult = await table.ExecuteQuerySegmentedAsync(query, continuationToken);
				
				// map all to the original models
				List<T> result = new List<T>();
				foreach (DynamicTableEntity<T> model in queryResult)
					result.Add(model.Model);

				// notify delegate
				if (_delegate != null)
					_delegate.OnQueryed(typeof(T), partitionKey, rowKey, maxItems, continuationToken != null, null);

				// done 
				return new QueryResult<T>() 
				{ 
					Items = result.AsQueryable(), 
					NextToken = queryResult.ContinuationToken 
				};
				
			} catch(Exception e) {
			
				// notify delegate
				if (_delegate != null)
					_delegate.OnQueryed(typeof(T), partitionKey, rowKey, maxItems, continuationToken != null, e);				
				
				// throw exception
				throw e;
			}
		}
		
		private async Task<IQueryable<T>> QueryAsyncInternal<T>(string partitionKey, string rowKey, int maxItems = 0, TableContinuationToken nextToken = null) where T : new()
		{			
			// query the first page
			var result = await QueryAsyncInternalSinglePage<T>(partitionKey, rowKey, maxItems, nextToken);
			
			// check if we have reached the max items
			if (maxItems > 0 && result.Items.Count() >= maxItems)
				return result.Items;
			
			if (result.NextToken != null) 						
				return result.Items.Concat(await this.QueryAsyncInternal<T>(partitionKey, rowKey, maxItems, result.NextToken));
			else 			
				return result.Items;					
		}
	
		private CloudTable GetTableReference(string tableName) {
			
			// create the table client 
			var storageTableClient = _storageAccount.CreateCloudTableClient();

			// Create the table client.
			CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();

			// Retrieve a reference to the table.
			return tableClient.GetTableReference(tableName);
		}
	}
}
