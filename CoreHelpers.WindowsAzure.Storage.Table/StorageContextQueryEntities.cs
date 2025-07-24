using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreHelpers.WindowsAzure.Storage.Table.Internal;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Threading;
#endif

namespace CoreHelpers.WindowsAzure.Storage.Table
{
    public partial class StorageContext : IStorageContext
    {
        public async Task<T> QueryAsync<T>(string partitionKey, string rowKey, int maxItems = 0) where T : class, new()
             => (await Query<T>().InPartition(partitionKey).GetItem(rowKey).LimitTo(maxItems).Now()).FirstOrDefault<T>();

        public async Task<IEnumerable<T>> QueryAsync<T>(string partitionKey, IEnumerable<QueryFilter> queryFilters, int maxItems = 0) where T : class, new()
            => await Query<T>().InPartition(partitionKey).Filter(queryFilters).LimitTo(maxItems).Now();

        public async Task<IEnumerable<T>> QueryAsync<T>(string partitionKey, int maxItems = 0) where T : class, new()
            => await Query<T>().InPartition(partitionKey).LimitTo(maxItems).Now();

        public async Task<IEnumerable<T>> QueryAsync<T>(int maxItems = 0) where T : class, new()
            => await Query<T>().InPartition(null).LimitTo(maxItems).Now();

        public IStorageContextQueryWithPartitionKey<T> Query<T>() where T : class, new()
        {
            return new StorageContextQueryWithPartitionKey<T>(this);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public IAsyncEnumerable<T> QueryAsyncEnumerable<T>(string partitionKey, int maxItems = 0) where T : class, new()
            => Query<T>().InPartition(partitionKey).LimitTo(maxItems).AsAsyncEnumerable();

        public IAsyncEnumerable<T> QueryAsyncEnumerable<T>(string partitionKey, IEnumerable<QueryFilter> queryFilters, int maxItems = 0) where T : class, new()
            => Query<T>().InPartition(partitionKey).Filter(queryFilters).LimitTo(maxItems).AsAsyncEnumerable();

        public IAsyncEnumerable<T> QueryAsyncEnumerable<T>(int maxItems = 0) where T : class, new()
            => Query<T>().InPartition(null).LimitTo(maxItems).AsAsyncEnumerable();
#endif
    }
}