﻿using System;
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

        void IExternalCache.StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            _redisDatabase.StringSet(key, val, absoluteExpiration);
        }

        string IExternalCache.StringGetWithExpiry(string key, out TimeSpan absoluteExpiry)
        {
            var result = _redisDatabase.StringGetWithExpiry(key);
            var validResult = result.Value.HasValue && result.Expiry.HasValue;

            absoluteExpiry = validResult ? result.Expiry.Value : TimeSpan.MaxValue;
            var value = validResult ? (string)result.Value : null;

            return value;
        }

        string IExternalCache.GetStringStart(string key, int length)
        {
            var result = _redisDatabase.StringGetRange(key, 0, Math.Max(0, length - 1));

            return result.HasValue ? ((string)result).Substring(0, Math.Min(length, ((string)result).Length)) : null;
        }
    }
}
