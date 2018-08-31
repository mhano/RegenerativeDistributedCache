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
    /// WARNING - BASIC REDIS CONNECTION ONLY. Basic wrapper of redis for caching, locking and messaging.
    /// </summary>
    public class BasicRedisWrapper : IDisposable
    {
        private RedisDistributedLockFactory _redisDistributedLockFactory;
        private RedisExternalCache _redisExternalCache;
        private RedisFanOutBus _redisFanOutBus;

        // we dispose any disposables that we create (RedLockFac and Redis Connections)
        private readonly List<IDisposable> _createdDisposables = new List<IDisposable>();

        /// <summary>
        /// Cache interface for RegenerativeCacheManager
        /// </summary>
        public IExternalCache Cache => _redisExternalCache;

        /// <summary>
        /// Lock interface for RegenerativeCacheManager.
        /// </summary>
        public IDistributedLockFactory Lock => _redisDistributedLockFactory;

        /// <summary>
        /// Message bus interface for RegenerativeCacheManager.
        /// </summary>
        public IFanOutBus Bus => _redisFanOutBus;

        /// <summary>
        /// WARNING - BASIC REDIS CONNECTION ONLY. Basic wrapper of redis for caching, locking and messaging.
        /// Uses a single redis connection for caching, locking and messaging (unless told to create three).
        /// 
        /// </summary>
        /// <param name="redisConfiguration">Redis connection string. e.g. "localhost:6379"</param>
        /// <param name="useMultipleRedisConnections">Uses a single redis connection for caching, locking and messaging or use seperate connections for each.</param>
        public BasicRedisWrapper(string redisConfiguration, bool useMultipleRedisConnections = false)
        {
            if (useMultipleRedisConnections)
            {
                Initialise( RedisConnect(redisConfiguration),
                            RedisConnect(redisConfiguration),
                            RedisConnect(redisConfiguration));
            }
            else
            {
                var redisConnection = RedisConnect(redisConfiguration);
                Initialise(redisConnection, redisConnection, redisConnection);
            }
        }

        private ConnectionMultiplexer RedisConnect(string redisConfiguration)
        {
            var conn = ConnectionMultiplexer.Connect(redisConfiguration);
            _createdDisposables.Add(conn);
            return conn;
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
            var redLockMultiPlexers = new List<RedLockMultiplexer>{new RedLockMultiplexer(lockConnection)};
            var redLock = RedLockFactory.Create(redLockMultiPlexers);
            _createdDisposables.Add(redLock);
            _redisDistributedLockFactory = new RedisDistributedLockFactory(redLock);

            _redisExternalCache = new RedisExternalCache(cacheConnection.GetDatabase());

            _redisFanOutBus = new RedisFanOutBus(messagingConnection.GetSubscriber());
        }

        /// <summary>
        /// Disposes any internally created (RedLockFactory and Redis connections)
        /// </summary>
        public void Dispose()
        {
            // ReSharper disable once EmptyGeneralCatchClause
            _createdDisposables.ForEach(d =>{ try { d?.Dispose(); } catch{} });
        }
    }
}
