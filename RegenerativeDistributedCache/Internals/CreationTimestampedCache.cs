#region *   License     *
/*
    RegenerativeDistributedCache

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

namespace RegenerativeDistributedCache.Internals
{
    internal class CreationTimestampedCache : IDisposable
    {
        private readonly MemoryFrontedExternalCache _cache;

        public CreationTimestampedCache(string keyspace, IExternalCache externalCache, ITraceWriter traceWriter = null)
        {
            _cache = new MemoryFrontedExternalCache(keyspace, externalCache, traceWriter);
        }

        public void Set(string key, TimestampedCacheValue val, TimeSpan absoluteExpiration)
        {
            _cache.Set(key, val.ToString(), absoluteExpiration);
        }

        public TimestampedCacheValue Get(string key)
        {
            var value = _cache.Get(key);
            return value == null ? null : TimestampedCacheValue.FromString(value);
        }

        public DateTime? GetCreationTimestamp(string key)
        {
            var value = _cache.GetStringStart(key, TimestampedCacheValue.MaxDateLength);
            return value == null ? (DateTime?)null : TimestampedCacheValue.FromString(value).CreateCommenced;
        }


        public void RemoveLocal(string key)
        {
            _cache.RemoveLocal(key);
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }
    }
}
