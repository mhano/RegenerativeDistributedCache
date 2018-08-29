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

    License: https://opensource.org/licenses/mit
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

using System;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;

namespace RegenerativeDistributedCache.Redis
{
    public class RedisExternalCache : IExternalCache
    {
        private readonly IDatabase _redisDatabase;

        public RedisExternalCache(IDatabase redisDatabase)
        {
            _redisDatabase = redisDatabase;
        }

        public void StringSet(string key, string val, TimeSpan absoluteExpiration)
        {
            _redisDatabase.StringSet(key, val, absoluteExpiration);
        }

        public string StringGetWithExpiry(string key, out TimeSpan expiry)
        {
            var result = _redisDatabase.StringGetWithExpiry(key);
            var validResult = result.Value.HasValue && result.Expiry.HasValue;

            expiry = validResult ? result.Expiry.Value : TimeSpan.MaxValue;
            var value = validResult ? (string)result.Value : null;

            return value;
        }

        public string GetStringStart(string key, int length)
        {
            var result = _redisDatabase.StringGetRange(key, 0, Math.Max(0, length - 1));

            return result.HasValue ? ((string)result).Substring(0, Math.Min(length, ((string)result).Length)) : null;
        }
    }
}
