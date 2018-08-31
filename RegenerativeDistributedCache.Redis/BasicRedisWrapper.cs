using System;
using System.Collections.Generic;
using System.Linq;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;
using IDistributedLockFactory = RegenerativeDistributedCache.Interfaces.IDistributedLockFactory;

namespace RegenerativeDistributedCache.Redis
{
    /// <summary>
    /// WARNING - BASIC / BETA / DEMO Only. Basic wrapper of redis for caching, locking and messaging.
    /// TODO: Should this connect to multiple redis nodes beyond using ConnectionMultiplexer.Connect("")?
    /// TODO: Does this need to be anything more than the below (many examples use lists of multiplexers)?
    /// TODO: is there an advantage in using seperate connections for seperate concerns (caching v messaging)
    /// </summary>
    public class BasicRedisWrapper : IDisposable
    {
        private RedisDistributedLockFactory _redisDistributedLockFactory;
        private RedisExternalCache _redisExternalCache;
        private RedisFanOutBus _redisFanOutBus;

        public IExternalCache Cache => _redisExternalCache;
        public IDistributedLockFactory Lock => _redisDistributedLockFactory;
        public IFanOutBus Bus => _redisFanOutBus;

        /// <summary>
        /// Uses a single redis connection for caching, locking and messaging
        /// </summary>
        /// <param name="redisConfiguration">Redis connection string. e.g. "localhost:6379" </param>
        /// <param name="useMultipleRedisConnections">Uses a single redis connection for caching, locking and messaging or use seperate connections for each.</param>
        public BasicRedisWrapper(string redisConfiguration, bool useMultipleRedisConnections = false)
        {
            if (useMultipleRedisConnections)
            {
                Initialise( ConnectionMultiplexer.Connect(redisConfiguration),
                            ConnectionMultiplexer.Connect(redisConfiguration),
                            ConnectionMultiplexer.Connect(redisConfiguration));
            }
            else
            {
                var redisConnection = ConnectionMultiplexer.Connect(redisConfiguration);
                Initialise(redisConnection, redisConnection, redisConnection);
            }
        }

        /// <summary>
        /// Uses a single redis connection for caching, locking and messaging.
        /// </summary>
        /// <param name="redisConnection"></param>
        public BasicRedisWrapper(IConnectionMultiplexer redisConnection)
        {
            Initialise(redisConnection, redisConnection, redisConnection);
        }

        /// <summary>
        /// Use seperate redis connections for caching, locking and messaging.
        /// </summary>
        /// <param name="cacheConnection"></param>
        /// <param name="lockConnection"></param>
        /// <param name="messagingConnection"></param>
        public BasicRedisWrapper(IConnectionMultiplexer cacheConnection, IConnectionMultiplexer lockConnection, IConnectionMultiplexer messagingConnection)
        {
            Initialise(cacheConnection, lockConnection, messagingConnection);
        }

        private void Initialise(IConnectionMultiplexer cacheConnection, IConnectionMultiplexer lockConnection, IConnectionMultiplexer messagingConnection)
        {
            var redisMultiplexersCache = new List<IConnectionMultiplexer> { cacheConnection, };

            _redisDistributedLockFactory = new RedisDistributedLockFactory(
                RedLockFactory.Create(new List<RedLockMultiplexer>(redisMultiplexersCache.Select(rm => new RedLockMultiplexer(rm)))));

            _redisExternalCache = new RedisExternalCache(lockConnection.GetDatabase());

            _redisFanOutBus = new RedisFanOutBus(messagingConnection.GetSubscriber());
        }

        public void Dispose()
        {
            _redisDistributedLockFactory?.Dispose();
        }
    }
}
