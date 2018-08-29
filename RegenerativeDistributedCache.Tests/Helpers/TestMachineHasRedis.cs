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

namespace RegenerativeDistributedCache.Tests.Helpers
{
    public class TestMachineHasRedis
    {
        private const string LocalRedis = "localhost:6379";

        private static readonly Dictionary<string,string> TestMachinesWithRedisAccess = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // list of machines with access to a redis instance
            { "sydcn1012", LocalRedis },
        };

        /// <param name="redisConnection">Redis static (hard coded) connection string for the test machine.</param>
        /// <param name="machineName">Defaults to Environment.MachineName</param>
        /// <returns></returns>
        public static bool GetRedisConnection(out string redisConnection, string machineName = null)
        {
            return TestMachinesWithRedisAccess.TryGetValue(machineName ?? Environment.MachineName, out redisConnection);
        }
    }
}