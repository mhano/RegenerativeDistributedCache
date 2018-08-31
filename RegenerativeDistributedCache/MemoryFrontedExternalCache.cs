using System;
using System.Runtime.Caching;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.SimpleHelpers;

namespace RegenerativeDistributedCache
{
    /// <summary>
    /// Wraps a memory cache in front of an external cache (such as redis) to optimise multiple retrievals per machine.
    /// Key space should be unique within app domain and external-cache database but consistent across nodes in a farm.
    /// </summary>
    public class MemoryFrontedExternalCache : IDisposable
    {
        private readonly ITraceWriter _traceWriter;
        private readonly IExternalCache _externalCache;
        private readonly string _cacheKeyPrefixItem;
        private readonly string _lockKeyPrefixExternalRetrieve;

        private readonly MemoryCache _memoryCache;

        /// <summary>
        /// Wraps a memory cache in front of an external cache (such as redis) to optimise multiple retrievals per machine.
        /// Key space should be unique within app domain and external-cache database but consistent across nodes in a farm.
        /// Creates a .net MemoryCache instance named "MemoryFrontedExternalCache_{keyspace}" (usually no configuration of
        /// this is required).
        /// </summary>
        /// <param name="keyspace">Key space must be unique within app domain and external-cache database but consistent across nodes in a farm.</param>
        /// <param name="externalCache">The external distributed/network cache to get/store values from/in.</param>
        /// <param name="traceWriter">Optional trace writer for detailed diagnostics.</param>
        public MemoryFrontedExternalCache(string keyspace, IExternalCache externalCache, ITraceWriter traceWriter = null)
        {
            _memoryCache = new MemoryCache($"{nameof(MemoryFrontedExternalCache)}_{keyspace}");
            _externalCache = externalCache;
            _traceWriter = traceWriter;

            // external cache keys must be unique within an app domain but common across a farm of servers/services (thus includes keyspace but no guid)
            _cacheKeyPrefixItem = $"{nameof(MemoryFrontedExternalCache)}:{keyspace}:Item:";

            // retrieval locks need to be unique within an instance (as NamedLock has a static dictionary of named locks)
            // as multiple of these caches could be setup within an app domain (such as in testing), we need a guid in the key
            // to ensure each one is only creating named locks against itself.
            _lockKeyPrefixExternalRetrieve = $"{nameof(MemoryFrontedExternalCache)}:{keyspace}:Retrieve:{Guid.NewGuid():N}:";
        }

        /// <summary>
        /// Store a value in local memory cache and external network cache.
        /// </summary>
        /// <param name="key">Cache key (unique within external cache)</param>
        /// <param name="val">Value serialised as a string</param>
        /// <param name="absoluteExpiration">Absolute expiration relative to now.</param>
        public void Set(string key, string val, TimeSpan absoluteExpiration)
        {
            StoreLocalMemory(key, val, absoluteExpiration);

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Set)}: {nameof(_externalCache)}.{nameof(_externalCache.StringSet)}(key: {key}, ttl: {absoluteExpiration.TotalSeconds});");

            _externalCache.StringSet($"{_cacheKeyPrefixItem}{key}", val, absoluteExpiration);
        }

        private void StoreLocalMemory(string key, string val, TimeSpan absoluteExpiration)
        {
            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(StoreLocalMemory)}: {nameof(_memoryCache)}.{nameof(_memoryCache.Set)}(key: {key}, ttl: {DateTimeOffset.UtcNow.Add(absoluteExpiration)});");
            _memoryCache.Set(key, val, DateTimeOffset.UtcNow.Add(absoluteExpiration));
        }

        /// <summary>
        /// Returns a value from local memory, failing that retrieves from external/network cache,
        /// stores a copy in local memory (as long as it is not near immediatey expiring) and returns it.
        /// </summary>
        /// <param name="key">Cache key (unique within external cache)</param>
        /// <returns></returns>
        public string Get(string key)
        {
            var cacheVal = _memoryCache.Get(key);
            _traceWriter?.Write($"From Local Cache 1: {key}, val length: {((string)cacheVal)?.Length ?? -1}");
            if (cacheVal != null) return (string)cacheVal;

            // not a perfect named lock but synchronises multiple incomming calls and prevents secondary / concurrent calls
            // to redis.get until the first, then after the redis retrieval is stored temporarily in memory (for 5 seconds)
            // subsoquent calls all get the result from memory (and ultimately fall back to redis if it expires)
            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: Locking local: {_lockKeyPrefixExternalRetrieve}{key}");
            var start = DateTime.Now;
            using (NamedLock.CreateAndEnter($"{_lockKeyPrefixExternalRetrieve}{key}"))
            {
                _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: Locked: {_lockKeyPrefixExternalRetrieve}{key} in {DateTime.Now.Subtract(start).TotalMilliseconds * 1000:#,##0.0}us");
                cacheVal = _memoryCache.Get(key);
                _traceWriter?.Write($"From Local Cache 2: {key}, val length: {((string)cacheVal)?.Length ?? -1}");
                if (cacheVal != null) return (string)cacheVal;

                var timePriorToRedisCall = DateTime.UtcNow;
                var value = _externalCache.StringGetWithExpiry($"{_cacheKeyPrefixItem}{key}", out var expiry);

                _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: {nameof(_externalCache)}.{nameof(_externalCache.StringGetWithExpiry)}(key: {key}, out expiry: {expiry}); => result present: {value != null}");

                if (value != null)
                {
                    var expiryTime = expiry.Subtract(DateTime.UtcNow.Subtract(timePriorToRedisCall));
                    if (expiryTime > TimeSpan.FromSeconds(0))
                    {
                        StoreLocalMemory(key, value, expiryTime);
                    }

                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Return the first n characters of the value of an item in cache or null.
        /// </summary>
        /// <param name="key">Cache key (unique within external cache)</param>
        /// <param name="length">Number of characters to read from the start of the stored value.</param>
        /// <returns>Return the first n characters of the value of an item in cache or null.</returns>
        public string GetStringStart(string key, int length)
        {
            var cacheVal = (string)_memoryCache.Get(key);
            if (cacheVal != null)
            {
                _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(GetStringStart)}: {nameof(_memoryCache)}.{nameof(_memoryCache.Get)}(key: {key}, length: {length}); => result length {cacheVal?.Length ?? -1}, returning {Math.Min(length, (int)cacheVal?.Length)} characters.");
                return cacheVal.Substring(0, length);
            }

            cacheVal = _externalCache.GetStringStart($"{_cacheKeyPrefixItem}{key}", length);

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(GetStringStart)}: {nameof(_externalCache)}.{nameof(_externalCache.GetStringStart)}(key: {key}, length: {length}); => result length {cacheVal?.Length ?? -1}, returning {Math.Min(length, cacheVal?.Length ?? -1)} characters.");

            return cacheVal;
        }

        /// <summary>
        /// Remove from local memory cache only (allows for the lazy fetch of an updated value
        /// from the external/network cache).
        /// </summary>
        /// <param name="key">Cache key (unique within external cache)</param>
        public void RemoveLocal(string key)
        {
            var removed = _memoryCache.Remove(key);

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(RemoveLocal)}: {key}, Removed: {removed != null}");
        }

        /// <summary>
        /// Dispose implemented as internal MemoryCache should be disposed.
        /// </summary>
        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
