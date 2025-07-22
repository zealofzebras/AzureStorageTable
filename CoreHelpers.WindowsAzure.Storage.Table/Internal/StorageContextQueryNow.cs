using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using CoreHelpers.WindowsAzure.Storage.Table.Serialization;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace CoreHelpers.WindowsAzure.Storage.Table.Internal
{
    internal class StorageQueryContext<T>
    {
        public StorageContext context { get; set; }
        public string filter { get; set; } = null;
        public int? maxPerPage { get; set; } = null;
        public IEnumerable<string> select { get; set; } = null;
        public CancellationToken cancellationToken { get; set; } = default(CancellationToken);        
    }

    internal class StorageContextQueryIEnumerator<T> : IEnumerator<T> where T: class, new()
    {
        private StorageQueryContext<T> _context;

        private IEnumerator<Azure.Page<TableEntity>> _pageEnumerator;
        private IEnumerator<TableEntity> _inPageEnumerator;
        private int resultItemCounter = 0;

        public StorageContextQueryIEnumerator(StorageQueryContext<T> queryContext)
        {
            _context = queryContext;
        }

        public T Current { get; private set; }

        object IEnumerator.Current => this;

        public void Dispose()
        {
            Reset();                  
        }
        
        public bool MoveNext()
        {
            // initialize the enumerator
            InitializePageEnumeratorIfNeeded();

            // increase the item counter
            resultItemCounter++;

            // evaluate the maxItems
            int maxItemsAllowed = _context.maxPerPage.HasValue && _context.maxPerPage.Value > 0 ? _context.maxPerPage.Value : -1;

            // limit the items to the absolut maximum
            if (maxItemsAllowed > 0 && resultItemCounter > maxItemsAllowed)
                return false;

            // handle the rest internal
            return MoveNextInternal(true);
        }

        private bool MoveNextInternal(bool initialPage)
        {
            try
            {
            
                // check if we need a new page
                if (_inPageEnumerator == null)
                {
                    // notify delegate
                    if (_context.context.GetDelegate() != null)
                        _context.context.GetDelegate().OnQuerying(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage);

                    // go to the next page or end the enumeration
                    if (!_pageEnumerator.MoveNext())
                        return false;                    

                    // get the enumerator of the found items
                    _inPageEnumerator = _pageEnumerator.Current.Values.GetEnumerator();
                }

                // move to the next item
                if (!_inPageEnumerator.MoveNext())
                {

                    // notify delegate
                    if (_context.context.GetDelegate() != null)
                        _context.context.GetDelegate().OnQueryed(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage, null);

                    // move forward
                    _inPageEnumerator = null;
                    return MoveNextInternal(false);
                }

                var entityMapper = _context.context.GetEntityMapper<T>();

                // set the item
                if (entityMapper.TypeField == null)
                    Current = TableEntityDynamic.fromEntity<T>(_inPageEnumerator.Current, entityMapper, _context.context);
                else
                {
                    var entity = _inPageEnumerator.Current;
                    var typeName = entity.GetString(entityMapper.TypeField);
                    Type type = Type.GetType(typeName);
                    MethodInfo method = typeof(TableEntityDynamic).GetMethod(nameof(TableEntityDynamic.fromEntity));
                    MethodInfo genericMethod = method.MakeGenericMethod(type);
                    Current = genericMethod.Invoke(null, [_inPageEnumerator.Current, entityMapper, _context.context] ) as T;
                }

                // done
                return true;

            } catch (Azure.RequestFailedException e)
            {
                // notify delegate
                if (_context.context.GetDelegate() != null)
                    _context.context.GetDelegate().OnQueryed(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage, e);

                if (_context.context.IsAutoCreateTableEnabled() && e.ErrorCode.Equals("TableNotFound"))
                    return false;
                                                     
                // done
                ExceptionDispatchInfo.Capture(e).Throw();
                return false;
            }
        }

        public void Reset()
        {
            if (_pageEnumerator != null)
            {
                _pageEnumerator.Dispose();
                _pageEnumerator = null;
            }

            if (_inPageEnumerator != null)
            {
                _inPageEnumerator.Dispose();
                _inPageEnumerator = null;
            }
        }

        private void InitializePageEnumeratorIfNeeded()
        {
            if (_pageEnumerator != null)
                return;

            // get the table client 
            var tc = _context.context.GetTableClient<T>();

            // evaluate the maxItems
            int? maxPerPage = _context.maxPerPage.HasValue && _context.maxPerPage.Value > 0 ? _context.maxPerPage : null;

            // fix Azurite bug
            var filter = string.IsNullOrWhiteSpace(_context.filter) ? null : _context.filter;
            // start the query
            _pageEnumerator = tc.Query<TableEntity>(filter, maxPerPage, _context.select, _context.cancellationToken).AsPages().GetEnumerator();
            
        }
    }

    internal class StorageContextQueryIEnumerable<T> : IEnumerable<T> where T : class, new()
    {
        private StorageQueryContext<T> _queryContext;

        public StorageContextQueryIEnumerable(StorageQueryContext<T> queryContext)
            => _queryContext = queryContext;       

        public IEnumerator<T> GetEnumerator()
            => new StorageContextQueryIEnumerator<T>(_queryContext);

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();        
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    internal class StorageContextQueryIAsyncEnumerator<T> : IAsyncEnumerator<T> where T : class, new()
    {
        private StorageQueryContext<T> _context;
        private IAsyncEnumerator<Azure.Page<TableEntity>> _pageEnumerator;
        private IEnumerator<TableEntity> _inPageEnumerator;
        private int resultItemCounter = 0;

        public StorageContextQueryIAsyncEnumerator(StorageQueryContext<T> queryContext)
        {
            _context = queryContext;
        }

        public T Current { get; private set; }

        public async ValueTask<bool> MoveNextAsync()
        {
            // initialize the enumerator
            await InitializePageEnumeratorIfNeededAsync();

            // increase the item counter
            resultItemCounter++;

            // evaluate the maxItems
            int maxItemsAllowed = _context.maxPerPage.HasValue && _context.maxPerPage.Value > 0 ? _context.maxPerPage.Value : -1;

            // limit the items to the absolute maximum
            if (maxItemsAllowed > 0 && resultItemCounter > maxItemsAllowed)
                return false;

            // handle the rest internal
            return await MoveNextInternalAsync(true);
        }

        private async ValueTask<bool> MoveNextInternalAsync(bool initialPage)
        {
            try
            {
                // check if we need a new page
                if (_inPageEnumerator == null)
                {
                    // notify delegate
                    if (_context.context.GetDelegate() != null)
                        _context.context.GetDelegate().OnQuerying(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage);

                    // go to the next page or end the enumeration
                    if (!await _pageEnumerator.MoveNextAsync())
                        return false;

                    // get the enumerator of the found items
                    _inPageEnumerator = _pageEnumerator.Current.Values.GetEnumerator();
                }

                // move to the next item
                if (!_inPageEnumerator.MoveNext())
                {
                    // notify delegate
                    if (_context.context.GetDelegate() != null)
                        _context.context.GetDelegate().OnQueryed(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage, null);

                    // move forward
                    _inPageEnumerator?.Dispose();
                    _inPageEnumerator = null;
                    return await MoveNextInternalAsync(false);
                }

                var entityMapper = _context.context.GetEntityMapper<T>();

                // set the item
                if (entityMapper.TypeField == null)
                    Current = TableEntityDynamic.fromEntity<T>(_inPageEnumerator.Current, entityMapper, _context.context);
                else
                {
                    var entity = _inPageEnumerator.Current;
                    var typeName = entity.GetString(entityMapper.TypeField);
                    Type type = Type.GetType(typeName);
                    MethodInfo method = typeof(TableEntityDynamic).GetMethod(nameof(TableEntityDynamic.fromEntity));
                    MethodInfo genericMethod = method.MakeGenericMethod(type);
                    Current = genericMethod.Invoke(null, [_inPageEnumerator.Current, entityMapper, _context.context]) as T;
                }

                // done
                return true;

            }
            catch (Azure.RequestFailedException e)
            {
                // notify delegate
                if (_context.context.GetDelegate() != null)
                    _context.context.GetDelegate().OnQueryed(typeof(T), _context.filter, _context.maxPerPage.HasValue ? _context.maxPerPage.Value : -1, !initialPage, e);

                if (_context.context.IsAutoCreateTableEnabled() && e.ErrorCode.Equals("TableNotFound"))
                    return false;

                // done
                ExceptionDispatchInfo.Capture(e).Throw();
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_pageEnumerator != null)
            {
                await _pageEnumerator.DisposeAsync();
                _pageEnumerator = null;
            }

            if (_inPageEnumerator != null)
            {
                _inPageEnumerator.Dispose();
                _inPageEnumerator = null;
            }
        }

        private Task InitializePageEnumeratorIfNeededAsync()
        {
            if (_pageEnumerator != null)
                return Task.CompletedTask;

            // get the table client 
            var tc = _context.context.GetTableClient<T>();

            // evaluate the maxItems
            int? maxPerPage = _context.maxPerPage.HasValue && _context.maxPerPage.Value > 0 ? _context.maxPerPage : null;

            // fix Azurite bug
            var filter = string.IsNullOrWhiteSpace(_context.filter) ? null : _context.filter;
            // start the query
            _pageEnumerator = tc.QueryAsync<TableEntity>(filter, maxPerPage, _context.select, _context.cancellationToken).AsPages().GetAsyncEnumerator(_context.cancellationToken);

            return Task.CompletedTask;
        }
    }

    internal class StorageContextQueryIAsyncEnumerable<T> : IAsyncEnumerable<T> where T : class, new()
    {
        private StorageQueryContext<T> _queryContext;

        public StorageContextQueryIAsyncEnumerable(StorageQueryContext<T> queryContext)
            => _queryContext = queryContext;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            // Update the cancellation token in the query context
            _queryContext.cancellationToken = cancellationToken;
            return new StorageContextQueryIAsyncEnumerator<T>(_queryContext);
        }
    }
#endif


    public class StorageContextQueryNow<T> : IStorageContextQueryNow<T> where T : class, new()
    {
        protected string _optionalPartitionKey;
        protected string _optionalRowKey;
        protected string _optionalFilter;
        protected int? _optionalMaxItems;

        protected StorageContext _context;
        
        public StorageContextQueryNow(StorageContext context, StorageContextQueryNow<T> src)
        {
            _context = context;

            if (src != null)
            {
                _optionalPartitionKey = src._optionalPartitionKey;
                _optionalRowKey = src._optionalRowKey;
                _optionalFilter = src._optionalFilter;
                _optionalMaxItems = src._optionalMaxItems;
            }
        }

        public StorageContextQueryNow(StorageContext context, StorageContextQueryNow<T> src, string filter, int? maxItems)
            : this(context, src)
        {
            _optionalFilter = filter;
            _optionalMaxItems = maxItems;
        }


        public async Task<IEnumerable<T>> Now()
        {
            // build the query context
            var queryContext = new StorageQueryContext<T>()
            {
                context = _context,
                filter = _optionalFilter,
                maxPerPage = _optionalMaxItems,
                select = null,
                cancellationToken = default(CancellationToken)
            };

            // build the filter correctly
            var filterBuilder = new TableQueryFilterBuilder();

            if (!String.IsNullOrEmpty(_optionalPartitionKey))
                filterBuilder.And("PartitionKey", QueryFilterOperator.Equal, _optionalPartitionKey);                

            if (!String.IsNullOrEmpty(_optionalRowKey))
                filterBuilder.And("RowKey", QueryFilterOperator.Equal, _optionalRowKey);

            if (!String.IsNullOrEmpty(_optionalFilter))
                filterBuilder.Attach(_optionalFilter);

            // set the filter
            queryContext.filter = filterBuilder.Build();

            // build the enumerable
            await Task.CompletedTask;
            return new StorageContextQueryIEnumerable<T>(queryContext);            
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public IAsyncEnumerable<T> AsAsyncEnumerable()
        {
            // build the query context
            var queryContext = new StorageQueryContext<T>()
            {
                context = _context,
                filter = _optionalFilter,
                maxPerPage = _optionalMaxItems,
                select = null,
                cancellationToken = default(CancellationToken)
            };

            // build the filter correctly
            var filterBuilder = new TableQueryFilterBuilder();

            if (!String.IsNullOrEmpty(_optionalPartitionKey))
                filterBuilder.And("PartitionKey", QueryFilterOperator.Equal, _optionalPartitionKey);

            if (!String.IsNullOrEmpty(_optionalRowKey))
                filterBuilder.And("RowKey", QueryFilterOperator.Equal, _optionalRowKey);

            if (!String.IsNullOrEmpty(_optionalFilter))
                filterBuilder.Attach(_optionalFilter);

            // set the filter
            queryContext.filter = filterBuilder.Build();

            // build the async enumerable
            return new StorageContextQueryIAsyncEnumerable<T>(queryContext);
        }
#endif
    }
}

