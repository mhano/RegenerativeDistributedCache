using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.Redis;
using IDistributedLockFactory = RegenerativeDistributedCache.Interfaces.IDistributedLockFactory;

namespace RegenDistCache.Tests.Helpers
{
    /// <summary>
    /// Caution - disposing any instance of this (which has been setup to use the app domain level mock
    /// of redis by passing "mock" to ctor) resets the entire (app domain local) mock of redis
    /// (removes all subscriptions / cache entries).
    /// </summary>
    internal class RedisInterceptOrMock : IExternalCache, IDistributedLockFactory, IFanOutBus, IDisposable
    {
        private readonly BasicRedisWrapper _basicRedisWrapper;
        private readonly LocalMemMockOfRedis _redisMock;

        public IExternalCache Cache => this;
        public IDistributedLockFactory Lock => this;
        public IFanOutBus Bus => this;

        public ConcurrentBag<KeyValuePair<string,string>> CacheSets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,string>> CacheGets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,string>> CacheGetStringStarts = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<string> Subscribes = new ConcurrentBag<string>();
        public ConcurrentBag<KeyValuePair<string,string>> Publishes = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,bool>> LockAttempts = new ConcurrentBag<KeyValuePair<string, bool>>();
        public ConcurrentBag<string> ReceivedMessages = new ConcurrentBag<string>();
        
        /// <summary>
        /// Create an interceptor based on real redis connection(s) or a local in memory redis mock.
        /// Note: local in memory redis mock does not support "multiple connections" (there is no actual connecting going on)
        /// </summary>
        /// <param name="redisConfiguration">redis connection config (e.g. "localhost:6379") or "mock-{uniqueid}"</param>
        /// <param name="useMultipleRedisConnections">use a single redis connection vs multiple for different concerns (locking, caching, messaging)</param>
        public RedisInterceptOrMock(string redisConfiguration, bool useMultipleRedisConnections = false)
        {
            if (redisConfiguration.ToLowerInvariant().StartsWith("mock-"))
            {
                if (useMultipleRedisConnections)
                {
                    throw new ArgumentException($"Mock redis and useMultipleRedisConnections is non-sensical");
                }

                if (redisConfiguration.ToLowerInvariant() == "mock-")
                {
                    throw new ArgumentException($"Mock redis id should be supplied (per fake redis instance)");
                }

                _basicRedisWrapper = null;
                _redisMock = LocalMemMockOfRedis.Create(redisConfiguration);
            }
            else
            {
                _basicRedisWrapper =  new BasicRedisWrapper(redisConfiguration, useMultipleRedisConnections);
                _redisMock = null;
            }
        }

        public void Dispose()
        {
            (_basicRedisWrapper as IDisposable)?.Dispose();
            _redisMock?.Dispose();
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            (_redisMock?.Cache ?? _basicRedisWrapper.Cache).StringSet(key, val, absoluteExpiration);

            CacheSets.Add(new KeyValuePair<string, string>(key, val));
        }

        public string StringGetWithExpiry(string key, out TimeSpan absoluteExpiry)
        {
            var value = (_redisMock?.Cache ?? _basicRedisWrapper.Cache).StringGetWithExpiry(key, out absoluteExpiry);

            CacheGets.Add(new KeyValuePair<string, string>(key, value));

            return value;
        }

        public string GetStringStart(string key, int length)
        {
            var value = (_redisMock?.Cache ?? _basicRedisWrapper.Cache).GetStringStart(key, length);

            CacheGetStringStarts.Add(new KeyValuePair<string, string>(key, value));

            return value;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var lck = (_redisMock?.Lock ?? _basicRedisWrapper.Lock).CreateLock(lockKey, lockExpiryTime);

            LockAttempts.Add(new KeyValuePair<string, bool>(lockKey, lck != null));

            return lck;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            Subscribes.Add(topicKey);
            (_redisMock?.Bus ?? _basicRedisWrapper.Bus).Subscribe(topicKey, value =>
            {
                ReceivedMessages.Add(value);
                messageReceive(value);
            });
        }

        public void Publish(string topicKey, string value)
        {
            Publishes.Add(new KeyValuePair<string, string>(topicKey, value));

            (_redisMock?.Bus ?? _basicRedisWrapper.Bus).Publish(topicKey, value);
        }
    }
}
