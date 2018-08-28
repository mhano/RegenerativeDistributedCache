#region *   License     *
/*
    RegenerativeDistributedCache.Redis

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

    License: http://www.opensource.org/licenses/mit-license.php
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

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
