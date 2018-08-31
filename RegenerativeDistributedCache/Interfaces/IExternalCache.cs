using System;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Represents and external cache (such as redis) as required by RegenerativeCacheManager.
    /// Implement this interface to use an alternative underlying network/distributed caching 
    /// mechanism. A Redis implementation is provided in RegenerativeDistributedCache.Redis.
    /// </summary>
    public interface IExternalCache
    {
        /// <summary>
        /// Store a value in cache for RegenerativeCacheManager
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="val">value to store in cache (a serialised object)</param>
        /// <param name="absoluteExpiration">Absolute time (relative to now) that the item is kept in cache.</param>
        void StringSet(string key, string val, TimeSpan absoluteExpiration);

        /// <summary>
        /// Return the cached item with it's absolute absoluteExpiry time (relative to now).
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="absoluteExpiry">Absolute absoluteExpiry time (relative to now)</param>
        /// <returns>Null if not found or string value</returns>
        string StringGetWithExpiry(string key, out TimeSpan absoluteExpiry);

        /// <summary>
        /// Return the first n characters of the value of an item in cache
        /// </summary>
        /// <param name="key">Cache key (RegenerativeCacheManager adds a prefix including keyspace to concern specific keys given to it)</param>
        /// <param name="length">Number of characters to read from the start of the stored value</param>
        /// <returns>Null if not found or string value</returns>
        string GetStringStart(string key, int length);
    }
}
