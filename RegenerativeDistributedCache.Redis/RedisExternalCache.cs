using System;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;

namespace RegenerativeDistributedCache.Redis
{
    /// <summary>
    /// Provides a redis implemention of the external/network cache interface required 
    /// by RegenerativeCacheManager.
    /// </summary>
    public class RedisExternalCache : IExternalCache
    {
        private readonly IDatabase _redisDatabase;

        /// <summary>
        /// Provides a redis implemention of the external/network cache interface required 
        /// by RegenerativeCacheManager based on provided StackExchange.Redis IDatabase provided.
        /// </summary>
        /// <param name="redisDatabase">Redis database/connection to use.</param>
        public RedisExternalCache(IDatabase redisDatabase)
        {
            _redisDatabase = redisDatabase;
        }

        /// <summary>
        /// From RegenerativeDistributedCache -> IExternalCache - Store a value in cache for RegenerativeCacheManager
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="val">value to store in cache (a serialised object)</param>
        /// <param name="absoluteExpiration">Absolute time (relative to now) that the item is kept in cache.</param>
        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            _redisDatabase.StringSet(key, val, absoluteExpiration);
        }

        /// <summary>
        /// From RegenerativeDistributedCache -> IExternalCache - Return the cached item with it's absolute absoluteExpiry time (relative to now).
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="absoluteExpiry">Absolute absoluteExpiry time (relative to now)</param>
        /// <returns>Null if not found or string value</returns>
        public string StringGetWithExpiry(string key, out TimeSpan absoluteExpiry)
        {
            var result = _redisDatabase.StringGetWithExpiry(key);
            var validResult = result.Value.HasValue && result.Expiry.HasValue;

            absoluteExpiry = validResult ? result.Expiry.Value : TimeSpan.MaxValue;
            var value = validResult ? (string)result.Value : null;

            return value;
        }

        /// <summary>
        /// From RegenerativeDistributedCache -> IExternalCache - Return the first n characters of the value of an item in cache
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="length">Number of characters to read from the start of the stored value</param>
        /// <returns>Null if not found or string value</returns>
        public string GetStringStart(string key, int length)
        {
            var result = _redisDatabase.StringGetRange(key, 0, Math.Max(0, length - 1));

            return result.HasValue ? ((string)result).Substring(0, Math.Min(length, ((string)result).Length)) : null;
        }
    }
}
