using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoreHelpers.WindowsAzure.Storage.Table
{
    public interface IStorageContextQueryNow<T>
    {
        Task<IEnumerable<T>> Now();

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        IAsyncEnumerable<T> AsAsyncEnumerable();
#endif
    }

    public interface IStorageContextQueryWithFilter<T> : IStorageContextQueryNow<T>
    {
        IStorageContextQueryWithFilter<T> Filter(string filter);
        IStorageContextQueryWithFilter<T> Filter(IEnumerable<QueryFilter> filters);        

        IStorageContextQueryWithFilter<T> LimitTo(int maxItems);
    }
   
    public interface IStorageContextQueryWithRowKey<T> : IStorageContextQueryWithFilter<T>
    {
        IStorageContextQueryWithFilter<T> GetItem(string rowKey);
    }

    public interface IStorageContextQueryWithPartitionKey<T> : IStorageContextQueryNow<T>
    {
        IStorageContextQueryWithRowKey<T> InPartition(string partitionKey);

        IStorageContextQueryWithRowKey<T> InAllPartitions();
    }
}

