using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;
using IDistributedLockFactory = RegenerativeDistributedCache.Interfaces.IDistributedLockFactory;

namespace RegenerativeDistributedCache.Tests.Helpers
{
    internal class RedisCacheLocksAndBusMock : IExternalCache, IDistributedLockFactory, IFanoutBus, IDisposable
    {
        private readonly RedLockFactory _redLockFactory;
        private readonly IDatabase _redisDatabase;
        private readonly ISubscriber _redisSubscriber;

        public ConcurrentBag<KeyValuePair<string,string>> CacheSets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,string>> CacheGets = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<string> Subscribes = new ConcurrentBag<string>();
        public ConcurrentBag<KeyValuePair<string,string>> Publishes = new ConcurrentBag<KeyValuePair<string, string>>();
        public ConcurrentBag<KeyValuePair<string,bool>> LockAttempts = new ConcurrentBag<KeyValuePair<string, bool>>();
        public ConcurrentBag<string> ReceivedMessages = new ConcurrentBag<string>();

        public RedisCacheLocksAndBusMock(string redisEndpoint =  "localhost:6379")
        {
            var redisMultiplexers = new List<ConnectionMultiplexer>
            {
                // TODO: should connect to multiple - ConnectionMultiplexer.Connect(""),
                ConnectionMultiplexer.Connect(redisEndpoint),
            };

            // TODO: is there an advantage in seperating these? remove this and the uncomment below.
            List<ConnectionMultiplexer> redisMultiplexersLocking, redisMultiplexersMessaging, redisMultiplexersCaching;
            redisMultiplexersLocking = redisMultiplexersMessaging = redisMultiplexersCaching = redisMultiplexers;

            /* 
            var redisMultiplexersLocking = new List<ConnectionMultiplexer>
            {
                // ConnectionMultiplexer.Connect(""),
                ConnectionMultiplexer.Connect(redisEndpoint),
            };

            var redisMultiplexersCaching = new List<ConnectionMultiplexer>
            {
                ConnectionMultiplexer.Connect(redisEndpoint),
            };

            var redisMultiplexersMessaging = new List<ConnectionMultiplexer>
            {
                ConnectionMultiplexer.Connect(redisEndpoint),
            };
            */

            var redLockMultiplexers = new List<RedLockMultiplexer>(redisMultiplexersLocking.Select(rm => new RedLockMultiplexer(rm)));
            _redLockFactory = RedLockFactory.Create(redLockMultiplexers);

            _redisDatabase = redisMultiplexersCaching[0].GetDatabase();

            _redisSubscriber = redisMultiplexersMessaging[0].GetSubscriber();
        }

        public void Dispose()
        {
            _redLockFactory?.Dispose();
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            _redisDatabase.StringSet(key, val, absoluteExpiration);

            CacheSets.Add(new KeyValuePair<string, string>(key, val));
        }

        public string StringGetWithExpiry(string key, out TimeSpan expiry)
        {
            var result = _redisDatabase.StringGetWithExpiry(key);
            var validResult = result.Value.HasValue && result.Expiry.HasValue;

            expiry = validResult ? result.Expiry.Value : TimeSpan.MinValue;
            var value = validResult ? (string)result.Value : null;

            CacheGets.Add(new KeyValuePair<string, string>(key, value));

            return value;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var redLock =_redLockFactory.CreateLock(lockKey, lockExpiryTime);

            LockAttempts.Add(new KeyValuePair<string, bool>(lockKey, redLock.IsAcquired));

            return redLock.IsAcquired ? redLock : null;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            Subscribes.Add(topicKey);
            _redisSubscriber.Subscribe(topicKey, (ch, value) =>
            {
                ReceivedMessages.Add(value);
                messageReceive(value);
            });
        }

        public void Publish(string topicKey, string value)
        {
            Publishes.Add(new KeyValuePair<string, string>(topicKey, value));

            _redisSubscriber.Publish(topicKey, value);
        }
    }
}
