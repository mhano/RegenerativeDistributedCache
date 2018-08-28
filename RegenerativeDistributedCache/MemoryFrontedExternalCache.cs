#region *   License     *
/*
    RegenerativeDistributedCache - MemoryFrontedExternalCache

    Copyright (c) 2018 Mhano Harkness

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

    License: https://www.opensource.org/licenses/mit-license.php
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

using System;
using System.Diagnostics;
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

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(Set)}: {nameof(_externalCache)}.{nameof(_externalCache.StringSet)}(key: {key}, ttl: {absoluteExpiration.TotalSeconds});");

            _externalCache.StringSet($"{_cacheKeyPrefixItem}{key}", val, absoluteExpiration);
        }

        private void StoreLocalMemory(string key, string val, TimeSpan absoluteExpiration)
        {
            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(StoreLocalMemory)}: {nameof(_memoryCache)}.{nameof(_memoryCache.Set)}(key: {key}, ttl: {DateTimeOffset.UtcNow.Add(absoluteExpiration)});");
            _memoryCache.Set(key, val, DateTimeOffset.UtcNow.Add(absoluteExpiration));
        }

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

        public void RemoveLocal(string key)
        {
            var removed = _memoryCache.Remove(key);

            _traceWriter?.Write($"{nameof(MemoryFrontedExternalCache)}: {nameof(RemoveLocal)}: {key}, Removed: {removed != null}");
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
