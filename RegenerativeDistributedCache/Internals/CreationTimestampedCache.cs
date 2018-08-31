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

        public DateTime? GetCreationTimestamp(string key)
        {
            var value = _cache.GetStringStart(key, TimestampedCacheValue.MaxDateLength);
            return value == null ? (DateTime?)null : TimestampedCacheValue.FromString(value).CreateCommenced;
        }


        public void RemoveLocal(string key)
        {
            _cache.RemoveLocal(key);
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}
