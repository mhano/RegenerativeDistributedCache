using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.SimpleHelpers;

namespace RegenDistCache.Tests.Helpers
{
    /// <summary>
    /// Caution - disposing any instance of this resets the entire (app domain local) mock of redis
    /// (removes all subscriptions / cache entries).
    /// </summary>
    public class LocalMemMockOfRedis : IExternalCache, IDistributedLockFactory, IFanOutBus, IDisposable
    {
        private static ConcurrentDictionary<string, LocalMemMockOfRedis> MockedInstances = new ConcurrentDictionary<string, LocalMemMockOfRedis>();

        private readonly MemoryCache _memoryCache = new MemoryCache($"{nameof(LocalMemMockOfRedis)}");
        private readonly ConcurrentDictionary<string, ConcurrentBag<Action<string>>> _subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Action<string>>>();

        private LocalMemMockOfRedis() { } // hide ctor

        public static LocalMemMockOfRedis Create(string redisInstanceId)
        {
            lock (typeof(LocalMemMockOfRedis))
            {
                return MockedInstances.GetOrAdd(redisInstanceId, (k) => new LocalMemMockOfRedis());
            }
        }

        public IExternalCache Cache => this;
        public IDistributedLockFactory Lock => this;
        public IFanOutBus Bus => this;

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            var expiry = DateTime.Now.Add(absoluteExpiration);
            _memoryCache.Set(key, new Tuple<DateTime, string>(expiry, val), expiry);
        }

        public string StringGetWithExpiry(string key, out TimeSpan absoluteExpiry)
        {
            var ci = (Tuple<DateTime, string>)_memoryCache.Get(key);
            absoluteExpiry = ci?.Item1.Subtract(DateTime.Now) ?? TimeSpan.MinValue;
            return ci?.Item2;
        }

        public string GetStringStart(string key, int length)
        {
            var cacheVal = (Tuple<DateTime, string>)_memoryCache.Get(key);
            return cacheVal?.Item2?.Substring(0, Math.Min(length, cacheVal.Item2.Length));
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var lck = NamedLock.CreateAndEnter($"{nameof(LocalMemMockOfRedis)}:{nameof(CreateLock)}:{lockKey}", 0);

            if (lck.IsLocked) return lck;

            lck.Dispose();
            return null;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            _subscriptions.GetOrAdd(topicKey, (k) => new ConcurrentBag<Action<string>>())
                .Add(messageReceive);
        }

        public void Publish(string topicKey, string value)
        {
            _subscriptions[topicKey].ToList().ForEach(a => Task.Run(() => a(value)));
        }

        public void Dispose()
        {
            // Reset();
        }
    }
}
