using System;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Internals
{
    internal class CreationTimestampedCache : IDisposable
    {
        private readonly MemoryFrontedExternalCache _cache;

        public CreationTimestampedCache(string keyspace, IExternalCache externalCache, ITraceWriter traceWriter = null)
        {
            _cache = new MemoryFrontedExternalCache(keyspace, externalCache, traceWriter);
        }

        public void Set(string key, TimestampedCacheValue val, TimeSpan absoluteExpiration)
        {
            _cache.Set(key, val.ToString(), absoluteExpiration);
        }

        public TimestampedCacheValue Get(string key)
        {
            var value = _cache.Get(key);
            return value == null ? null : TimestampedCacheValue.FromString(value);
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}
