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