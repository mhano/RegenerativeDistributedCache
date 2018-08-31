using System;
using RedLockNet.SERedis;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Redis
{
    public class RedisDistributedLockFactory : IDistributedLockFactory, IDisposable
    {
        private readonly RedLockFactory _redLockFactory;

        public RedisDistributedLockFactory(RedLockFactory redLockFactory)
        {
            _redLockFactory = redLockFactory;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var redLock = _redLockFactory.CreateLock(lockKey, lockExpiryTime);

            return redLock.IsAcquired ? redLock : null;
        }

        public void Dispose()
        {
            _redLockFactory?.Dispose();
        }
    }
}
