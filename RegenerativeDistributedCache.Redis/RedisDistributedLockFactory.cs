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

    License: https://www.opensource.org/licenses/mit-license.php
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

using System;
using RedLockNet.SERedis;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Redis
{
    public class RedisDistributedLockFactory : IDistributedLockFactory, IDisposable
    {
        private readonly RedLockFactory _redLockFactory;

        public RedisDistributedLockFactory(RedLockFactory redLockFactory)
        {
            _redLockFactory = redLockFactory;
        }

        public IDisposable CreateLock(string lockKey, TimeSpan lockExpiryTime)
        {
            var redLock = _redLockFactory.CreateLock(lockKey, lockExpiryTime);

            return redLock.IsAcquired ? redLock : null;
        }

        public void Dispose()
        {
            _redLockFactory?.Dispose();
        }
    }
}
