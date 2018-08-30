#region *   License     *
/*
    RegenerativeDistributedCache - Tests

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
using System.Collections.Generic;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Redis;

namespace RegenerativeDistributedCache.Tests.Helpers
{
    public class TestMachineHasRedis
    {
        private const string LocalRedis = "localhost"; // :6379

        private static readonly object LocalRedisTestSynch = new object();
        private static bool _localRedisTested = false;
        private static bool _localRedisAvailable = false;

        private static readonly Dictionary<string,string> TestMachinesWithRedisAccess = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // list of machines with access to a redis instance (allows you to skip the test and see approach)
            { "sydcn1012", LocalRedis },
        };

        /// <param name="redisConnection">Redis static (hard coded) connection string for the test machine.</param>
        /// <param name="machineName">Defaults to Environment.MachineName</param>
        /// <returns></returns>
        public static bool GetRedisConnection(out string redisConnection, string machineName = null)
        {
            return 
                TestMachinesWithRedisAccess.TryGetValue(machineName ?? Environment.MachineName, out redisConnection)
                || RedisAvailable(out redisConnection);
        }

        private static bool RedisAvailable(out string redisConnection)
        {
            redisConnection = LocalRedis;
            if (_localRedisTested) return _localRedisAvailable;

            lock (LocalRedisTestSynch)
            {
                if (_localRedisTested) return _localRedisAvailable;

                try
                {
                    using (var basicRedis = new BasicRedisWrapper(redisConnection, false))
                    {
                        var topic = $"{typeof(TestMachineHasRedis).FullName}:Bus:{Guid.NewGuid():N}";
                        var msg = $"testmsg{Guid.NewGuid():N}";

                        string recvdMsg = null;
                        basicRedis.Bus.Subscribe(topic, s => recvdMsg = s);
                        basicRedis.Bus.Publish(topic, msg);

                        using (var lck = basicRedis.Lock.CreateLock($"{typeof(TestMachineHasRedis).FullName}:Lock:{Guid.NewGuid():N}", TimeSpan.Zero))
                        {
                            var cacheKey = $"{typeof(TestMachineHasRedis).FullName}:Cache:{Guid.NewGuid():N}";
                            var cacheVal = $"{typeof(TestMachineHasRedis).FullName}:Cache:{Guid.NewGuid():N}";
                            basicRedis.Cache.StringSet(cacheKey, cacheVal, TimeSpan.FromSeconds(3));
                            var cacheValGot = basicRedis.Cache.GetStringStart(cacheKey, 200);
                            var cacheValGot2 = basicRedis.Cache.GetStringStart(cacheKey, 10);

                            var end = DateTime.Now.AddSeconds(3);
                            while (DateTime.Now < end && recvdMsg == null) Task.Delay(50).Wait();

                            _localRedisAvailable = lck != null &&
                                                 cacheValGot == cacheVal &&
                                                 cacheVal.StartsWith(cacheValGot2) &&
                                                 cacheValGot2.Length == 10 &&
                                                 recvdMsg == msg;
                        }
                    }
                }
                catch
                {
                    _localRedisAvailable = false;
                }

                _localRedisTested = true;
            }

            return _localRedisAvailable;
        }
    }
}