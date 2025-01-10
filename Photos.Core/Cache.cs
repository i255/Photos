using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class Cache<T, Q> 
        where T : notnull 
        where Q : class
    {
        class CacheItem
        {
            public CacheItem(Q value)
            {
                this.value = value;
                timestamp = DateTime.UtcNow;
            }

            public Q value;
            public DateTime timestamp;
        }

        Dictionary<T, CacheItem> _cache = new();
        public int Limit;

        public Cache(int limit)
        {
            Limit = limit;
        }

        public Q Get(T key)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var val))
                {
                    val.timestamp = DateTime.UtcNow;
                    return val.value;
                }

                return null;
            }
        }

        public void Remove(T key)
        {
            lock (_cache)
                _cache.Remove(key);
        }

        public void Put(T key, Q val)
        {
            lock (_cache)
            {
                var n = new CacheItem(val);
                if (!_cache.TryAdd(key, n))
                {
                    Dispose(_cache[key]);
                    _cache[key] = n;
                }

                if (_cache.Count > Limit)
                {
                    var toRemove = Math.Max(1, _cache.Count / 8);
                    foreach (var item in _cache.OrderBy(x => x.Value.timestamp).Take(toRemove).ToList())
                    {
                        _cache.Remove(item.Key);
                        Dispose(item.Value);
                    }

                }
            }
        }

        public void Clear()
        {
            lock (_cache)
            {
                foreach (var item in _cache)
                    Dispose(item.Value);
                _cache.Clear();
            }
        }

        private void Dispose(Cache<T, Q>.CacheItem item)
        {
            OnDispose?.Invoke(item.value);
        }

        public bool Contains(T key)
        {
            lock (_cache)
                return _cache.ContainsKey(key);
        }

        public Action<Q> OnDispose;
    }
}
