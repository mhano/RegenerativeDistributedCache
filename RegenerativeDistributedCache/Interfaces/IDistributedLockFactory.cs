using System;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Attempt to obtain lock, returns a disposable lock object if acquired, or null if lock wasn't acquired immediately.
    /// Implement as a wrapper on something like RedLock.net - a Redis/RedLock.net implementation is provided in
    /// RegenerativeDistributedCache.Redis.
    /// </summary>
    public interface IDistributedLockFactory
    {
        /// <summary>
        /// Attempt to obtain lock, returns a disposable lock object if acquired, or null if lock wasn't acquired immediately.
        /// Implement as a wrapper on something like RedLock.net.
        /// </summary>
        /// <param name="lockKey">Lock key</param>
        /// <param name="lockExpiryTime">Lock expiry time</param>
        /// <returns>Null if lock couldn't be obtained immediately or a disposable lock (dispose to release)</returns>
        IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime);
    }
}
