using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace NuGet
{
    public sealed class MemoryCache : IDisposable
    {
        private static readonly Lazy<MemoryCache> _instance = new Lazy<MemoryCache>(() => new MemoryCache());
        // Interval to wait before cleaning up expired items
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(10);

        // Cache keys are case-sensitive
        private readonly ConcurrentDictionary<object, CacheItem> _cache = new ConcurrentDictionary<object, CacheItem>();
        private readonly Timer _timer;

        internal MemoryCache()
        {
            _timer = new Timer(RemoveExpiredEntries, null, _cleanupInterval, _cleanupInterval);
        }

        internal static MemoryCache Instance
        {
            get
            {
                return _instance.Value;
            }
        }

        internal T GetOrAdd<T>(object cacheKey, Func<T> factory, TimeSpan expiration, bool absoluteExpiration = false) where T : class
        {
            // Although this method would return values that have expired while also elavating them to unexpired entries,
            // none of the data that we cache is time sensitive. At worst, an item will be cached for an extra _cleanupInterval duration.

            CacheItem cacheFactory = new CacheItem(factory, expiration, absoluteExpiration);

            var cachedItem = _cache.GetOrAdd(cacheKey, cacheFactory);

            // Increase the expiration time
            cachedItem.UpdateUsage(expiration);

            return (T)cachedItem.Value;
        }

        internal bool TryGetValue<T>(object cacheKey, out T value) where T : class
        {
            CacheItem cacheItem;
            if (_cache.TryGetValue(cacheKey, out cacheItem))
            {
                value = (T)cacheItem.Value;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }

        internal void Remove(object cacheKey)
        {
            CacheItem item;
            _cache.TryRemove(cacheKey, out item);
        }

        private void RemoveExpiredEntries(object state)
        {
            // Remove all the expired ones
            var keys = _cache.Keys;
            foreach (var key in keys)
            {
                CacheItem cacheItem;
                if (_cache.TryGetValue(key, out cacheItem) && cacheItem.Expired)
                {
                    // Note: It is entirely possible that someone reads the value between the time we read Expired on the CacheItem 
                    // and we call TryRemove. However we are fine with cache misses at these boundary values. If in the future the nature
                    // of this cache changes, use the method prescribed at http://blogs.msdn.com/b/pfxteam/archive/2011/04/02/10149222.aspx
                    _cache.TryRemove(key, out cacheItem);
                }
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
            }
        }

        private sealed class CacheItem
        {
            private readonly Lazy<object> _valueFactory;
            private readonly bool _absoluteExpiration;
            private long _expires;

            public CacheItem(Func<object> valueFactory, TimeSpan expires, bool absoluteExpiration)
            {
                _valueFactory = new Lazy<object>(valueFactory);
                _absoluteExpiration = absoluteExpiration;
                _expires = DateTime.UtcNow.Ticks + expires.Ticks;
            }

            public object Value
            {
                get
                {
                    return _valueFactory.Value;
                }
            }

            public void UpdateUsage(TimeSpan slidingExpiration)
            {
                if (!_absoluteExpiration)
                {
                    _expires = DateTime.UtcNow.Ticks + slidingExpiration.Ticks;
                }
            }

            public bool Expired
            {
                get
                {
                    long ticks = DateTime.UtcNow.Ticks;
                    long expires = Interlocked.Read(ref _expires);
                    // > is atomic on primitive types
                    return ticks > expires;
                }
            }
        }
    }
}
