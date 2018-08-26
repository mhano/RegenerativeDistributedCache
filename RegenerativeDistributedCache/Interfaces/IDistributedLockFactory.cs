using System;

namespace RegenerativeDistributedCache.Interfaces
{
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
