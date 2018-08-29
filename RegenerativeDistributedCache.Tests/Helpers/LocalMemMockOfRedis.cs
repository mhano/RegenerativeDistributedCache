#region *   License     *
/*
    RegenerativeDistributedCache - Tests

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

    License: https://opensource.org/licenses/mit
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

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
    public class LocalMemMockOfRedis : IExternalCache, IDistributedLockFactory, IFanOutBus, IDisposable
    {
        private static MemoryCache _memoryCache = new MemoryCache($"{nameof(LocalMemMockOfRedis)}");
        private static ConcurrentDictionary<string, ConcurrentBag<Action<string>>> Subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Action<string>>>();

        public IExternalCache Cache => this;
        public IDistributedLockFactory Lock => this;
        public IFanOutBus Bus => this;

        public static void Reset()
        {
            _memoryCache.Dispose();

            var oldSub = Subscriptions;
            oldSub.Values.ToList().ForEach(v =>
            {
                while (v.TryTake(out Action<string> tr));
            });

            _memoryCache = new MemoryCache($"{nameof(LocalMemMockOfRedis)}");
            Subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Action<string>>>();
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            var expiry = DateTime.Now.Add(absoluteExpiration);
            _memoryCache.Set(key, new Tuple<DateTime, string>(expiry, val), expiry);
        }

        public string StringGetWithExpiry(string key, out TimeSpan expiry)
        {
            var ci = (Tuple<DateTime, string>)_memoryCache.Get(key);
            expiry = ci?.Item1.Subtract(DateTime.Now) ?? TimeSpan.MinValue;
            return ci?.Item2;
        }

        public string GetStringStart(string key, int length)
        {
            var cacheVal = (Tuple<DateTime, string>)_memoryCache.Get(key);
            return cacheVal?.Item2?.Substring(0, Math.Min(length, cacheVal.Item2.Length));
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var lck = SimpleHelpers.NamedLock.CreateAndEnter($"{nameof(LocalMemMockOfRedis)}:{nameof(CreateLock)}:{lockKey}", 0);

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
