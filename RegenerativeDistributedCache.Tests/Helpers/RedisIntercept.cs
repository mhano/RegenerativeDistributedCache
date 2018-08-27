using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.Redis;
using IDistributedLockFactory = RegenerativeDistributedCache.Interfaces.IDistributedLockFactory;

namespace RegenerativeDistributedCache.Tests.Helpers
{
    internal class RedisIntercept : IExternalCache, IDistributedLockFactory, IFanOutBus, IDisposable
    {
        private readonly BasicRedisWrapper _basicRedisWrapper;

        public IExternalCache Cache => this;
        public IDistributedLockFactory Lock => this;
        public IFanOutBus Bus => this;

        public ConcurrentBag<KeyValuePair<string,string>> CacheSets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,string>> CacheGets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<string> Subscribes = new ConcurrentBag<string>();
        public ConcurrentBag<KeyValuePair<string,string>> Publishes = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,bool>> LockAttempts = new ConcurrentBag<KeyValuePair<string, bool>>();
        public ConcurrentBag<string> ReceivedMessages = new ConcurrentBag<string>();

        public RedisIntercept(string redisConfiguration =  "localhost:6379")
        {
            _basicRedisWrapper = new BasicRedisWrapper(redisConfiguration);
        }

        public void Dispose()
        {
            _basicRedisWrapper?.Dispose();
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            _basicRedisWrapper.Cache.StringSet(key, val, absoluteExpiration);

            CacheSets.Add(new KeyValuePair<string, string>(key, val));
        }

        public string StringGetWithExpiry(string key, out TimeSpan expiry)
        {
            var value = _basicRedisWrapper.Cache.StringGetWithExpiry(key, out expiry);

            CacheGets.Add(new KeyValuePair<string, string>(key, value));

            return value;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var lck = _basicRedisWrapper.Lock.CreateLock(lockKey, lockExpiryTime);

            LockAttempts.Add(new KeyValuePair<string, bool>(lockKey, lck != null));

            return lck;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            Subscribes.Add(topicKey);
            _basicRedisWrapper.Bus.Subscribe(topicKey, value =>
            {
                ReceivedMessages.Add(value);
                messageReceive(value);
            });
        }

        public void Publish(string topicKey, string value)
        {
            Publishes.Add(new KeyValuePair<string, string>(topicKey, value));

            _basicRedisWrapper.Bus.Publish(topicKey, value);
        }
    }
}
