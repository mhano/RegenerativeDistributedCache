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
        private readonly RedisDistributedLockFactory _redisDistributedLockFactory;
        private readonly RedisExternalCache _redisExternalCache;
        private readonly RedisFanOutBus _redisFanOutBus;

        public IExternalCache Cache => _redisExternalCache;
        public IDistributedLockFactory Lock => _redisDistributedLockFactory;
        public IFanOutBus Bus => _redisFanOutBus;

        public BasicRedisWrapper(string redisConfiguration = "localhost:6379")
            : this(ConnectionMultiplexer.Connect(redisConfiguration))
        { }

        public BasicRedisWrapper(ConnectionMultiplexer redisConnectionMultiplexer)
        {
            // var redisConnectionMultiplexer = ConnectionMultiplexer.Connect(redisConfiguration);
            var redisMultiplexers = new List<ConnectionMultiplexer> { redisConnectionMultiplexer, };

            _redisDistributedLockFactory = new RedisDistributedLockFactory(
                RedLockFactory.Create(new List<RedLockMultiplexer>(redisMultiplexers.Select(rm => new RedLockMultiplexer(rm)))));

            _redisExternalCache = new RedisExternalCache(redisConnectionMultiplexer.GetDatabase());

            _redisFanOutBus = new RedisFanOutBus(redisConnectionMultiplexer.GetSubscriber());
        }

        public void Dispose()
        {
            _redisDistributedLockFactory?.Dispose();
        }
    }
}
