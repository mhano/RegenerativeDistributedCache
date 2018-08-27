using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Tests.Helpers
{
    /// <summary>
    /// Caution - disposing any instance of this resets the entire (app domain local) mock of redis
    /// (removes all subscriptions / cache entries).
    /// </summary>
    public class RedisMock : IExternalCache, IDistributedLockFactory, IFanOutBus, IDisposable
    {
        private static MemoryCache MemCache = new MemoryCache($"{nameof(RedisMock)}");
        private static ConcurrentDictionary<string, ConcurrentBag<Action<string>>> Subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Action<string>>>();

        public IExternalCache Cache => this;
        public IDistributedLockFactory Lock => this;
        public IFanOutBus Bus => this;

        public static void Reset()
        {
            MemCache.Dispose();

            var oldSub = Subscriptions;
            oldSub.Values.ToList().ForEach(v =>
            {
                while (v.TryTake(out Action<string> tr));
            });

            MemCache = new MemoryCache($"{nameof(RedisMock)}");
            Subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Action<string>>>();
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            var expiry = DateTime.Now.Add(absoluteExpiration);
            MemCache.Set(key, new Tuple<DateTime, string>(expiry, val), expiry);
        }

        public string StringGetWithExpiry(string key, out TimeSpan expiry)
        {
            var ci = (Tuple<DateTime, string>)MemCache.Get(key);
            expiry = ci?.Item1.Subtract(DateTime.Now) ?? TimeSpan.MinValue;
            return ci?.Item2;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var lck = SimpleHelpers.NamedLock.CreateAndEnter($"RedisMock", 0);

            if (lck.IsLocked) return lck;

            lck.Dispose();
            return null;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            Subscriptions.GetOrAdd(topicKey, (k) => new ConcurrentBag<Action<string>>())
                .Add(messageReceive);
        }

        public void Publish(string topicKey, string value)
        {
            Subscriptions[topicKey].ToList().ForEach(a => Task.Run(() => a(value)));
        }

        public void Dispose()
        {
            Reset();
        }
    }
}
