using System;
using RedLockNet.SERedis;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Redis
{
    /// <summary>
    /// Wraps RedLock / StackExchange Redis client library in interface required by 
    /// RegenerativeCacheManager.
    /// </summary>
    public class RedisDistributedLockFactory : IDistributedLockFactory
    {
        private readonly RedLockNet.IDistributedLockFactory _redLockFactory;

        /// <summary>
        /// Create an instance based on a RedLock.Net lock factory.
        /// </summary>
        /// <param name="redLockFactory">RedLock.Net lock factory</param>
        public RedisDistributedLockFactory(RedLockNet.IDistributedLockFactory redLockFactory)
        {
            _redLockFactory = redLockFactory;
        }

        IDisposable IDistributedLockFactory.CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var redLock = _redLockFactory.CreateLock(lockKey, lockExpiryTime);

            return redLock.IsAcquired ? redLock : null;
        }
    }
}
