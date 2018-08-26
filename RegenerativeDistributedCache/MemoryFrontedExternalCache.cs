using System;
using System.Runtime.Caching;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.SimpleHelpers;

namespace RegenerativeDistributedCache
{
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
        /// </summary>
        /// <param name="keyspace">Key space must be unique within app domain and external-cache database but consistent across nodes in a farm.</param>
        /// <param name="externalCache"></param>
        /// <param name="traceWriter"></param>
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

        public void Set(string key, string val, TimeSpan absoluteExpiration)
        {
            StoreLocalMemory(key, val, absoluteExpiration);

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Set)}: {nameof(_externalCache)}.{nameof(_externalCache.StringSet)}(key: {key}, ttl: {absoluteExpiration.TotalSeconds});", ConsoleColor.Magenta);

            _externalCache.StringSet($"{_cacheKeyPrefixItem}:{key}", val, absoluteExpiration);
        }

        private void StoreLocalMemory(string key, string val, TimeSpan absoluteExpiration)
        {
            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(StoreLocalMemory)}: {nameof(_memoryCache)}.{nameof(_memoryCache.Set)}(key: {key}, ttl: {DateTimeOffset.UtcNow.Add(absoluteExpiration)});", ConsoleColor.Magenta);
            _memoryCache.Set(key, val, DateTimeOffset.UtcNow.Add(absoluteExpiration));
        }

        public string Get(string key)
        {
            var cacheVal = _memoryCache.Get(key);
            // if (cacheVal != null) SynchedConsole.WriteLine($"From Local Cache 1: {key}, val: {cacheVal}", ConsoleColor.White, ConsoleColor.DarkGreen);
            if (cacheVal != null) return (string)cacheVal;

            // not a perfect named lock but synchronises multiple incomming calls and prevents secondary / concurrent calls
            // to redis.get until the first, then after the redis retrieval is stored temporarily in memory (for 5 seconds)
            // subsoquent calls all get the result from memory (and ultimately fall back to redis if it expires)
            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: Locking local: {_lockKeyPrefixExternalRetrieve}{key}", ConsoleColor.White, ConsoleColor.DarkRed);
            var start = DateTime.Now;
            using (NamedLock.CreateAndEnter($"{_lockKeyPrefixExternalRetrieve}{key}"))
            {
                _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: Locked: {_lockKeyPrefixExternalRetrieve}{key} in {DateTime.Now.Subtract(start).TotalMilliseconds * 1000:#,##0.0}us", ConsoleColor.White, ConsoleColor.DarkRed);
                cacheVal = _memoryCache.Get(key);
                // if (cacheVal != null) SynchedConsole.WriteLine($"From Local Cache 2: {key}, val: {cacheVal}", ConsoleColor.White, ConsoleColor.DarkGreen);
                if (cacheVal != null) return (string)cacheVal;

                var timePriorToRedisCall = DateTime.UtcNow;
                var value = _externalCache.StringGetWithExpiry($"{_cacheKeyPrefixItem}:{key}", out var expiry);

                _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Get)}: {nameof(_externalCache)}.{nameof(_externalCache.StringGetWithExpiry)}(key: {key}, out expiry: {expiry}); => result present: {value != null}", ConsoleColor.Yellow);

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

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
