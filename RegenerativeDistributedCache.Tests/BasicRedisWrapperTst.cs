using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Redis;
using RegenDistCache.Tests.Helpers;
using RegenDistCache.Tests.DynamicSkippableTests;
using Xunit;
using Xunit.Abstractions;

namespace RegenDistCache.Tests
{
    public class BasicRedisWrapperTst
    {
        private readonly ITestOutputHelper _output;
        public BasicRedisWrapperTst(ITestOutputHelper output)
        { _output = output; }

        [SkippableFact]
        public void Locks()
        {
            var redisConnection = TestMachineHasRedis.GetTestEnvironmentRedis();

            var testId = Guid.NewGuid();
            using (var basicRedis1 = new BasicRedisWrapper(redisConnection, false))
            using (var basicRedis2 = new BasicRedisWrapper(redisConnection, false))
            using (var basicRedis3 = new BasicRedisWrapper(redisConnection, false))
            {
                using (var lck1A = basicRedis1.Lock.CreateLock($"{typeof(TestMachineHasRedis).FullName}:Lock1:{testId:N}", TimeSpan.FromSeconds(30)))
                using (var lck2 = basicRedis2.Lock.CreateLock($"{typeof(TestMachineHasRedis).FullName}:Lock2:{testId:N}", TimeSpan.FromSeconds(30)))
                using (var lck1B = basicRedis3.Lock.CreateLock($"{typeof(TestMachineHasRedis).FullName}:Lock1:{testId:N}", TimeSpan.FromSeconds(30)))
                {
                    Assert.NotNull(lck1A);
                    Assert.NotNull(lck2);
                    Assert.Null(lck1B);
                }
            }
        }

        [SkippableFact]
        public void Caching()
        {
            var redisConnection = TestMachineHasRedis.GetTestEnvironmentRedis();

            using (var basicRedis1 = new BasicRedisWrapper(redisConnection, false))
            using (var basicRedis2 = new BasicRedisWrapper(redisConnection, false))
            {
                var cacheKey = $"{typeof(TestMachineHasRedis).FullName}:Cache:{Guid.NewGuid():N}";
                var cacheKeyMissing = $"{typeof(TestMachineHasRedis).FullName}:CacheMissing:{Guid.NewGuid():N}";
                var cacheVal1 = $"{typeof(TestMachineHasRedis).FullName}:CacheVal1:{Guid.NewGuid():N}";
                var cacheVal2 = $"{typeof(TestMachineHasRedis).FullName}:CacheVal2:{Guid.NewGuid():N}";

                // basic write/read
                basicRedis1.Cache.StringSet(cacheKey, cacheVal1, TimeSpan.FromSeconds(3));
                var node2Val1 = basicRedis2.Cache.StringGetWithExpiry(cacheKey, out TimeSpan node2Val1Expires1);
                var node2Subval1 = basicRedis2.Cache.GetStringStart(cacheKey, 10);
                var node1Subval1 = basicRedis1.Cache.GetStringStart(cacheKey, 10);
                var node1Val1 = basicRedis1.Cache.StringGetWithExpiry(cacheKey, out TimeSpan node1Val1Expires1);

                Assert.Equal(node2Val1, node1Val1);
                Assert.Equal(node2Subval1, node1Subval1);
                Assert.NotEqual(node1Val1, node1Subval1);
                Assert.StartsWith(node1Subval1, node1Val1);

                basicRedis2.Cache.StringSet(cacheKey, cacheVal2, TimeSpan.FromSeconds(3));
                var node2Val2 = basicRedis2.Cache.StringGetWithExpiry(cacheKey, out TimeSpan node2Val2Expires1);
                var node2Subval2 = basicRedis2.Cache.GetStringStart(cacheKey, 10);
                var node1Val2 = basicRedis1.Cache.StringGetWithExpiry(cacheKey, out TimeSpan node1Val2Expires1);
                var node1Subval2 = basicRedis1.Cache.GetStringStart(cacheKey, 10);

                Assert.NotEqual(node1Val1, node2Val2);

                Assert.Equal(node2Val2, node1Val2);
                Assert.Equal(node2Subval2, node1Subval2);
                Assert.NotEqual(node1Val2, node1Subval2);
                Assert.StartsWith(node1Subval2, node1Val2);

                Assert.Equal(node1Subval1, node1Subval2);
                Assert.Equal(node1Subval1, node2Subval1);
                Assert.Equal(node1Subval1, node1Subval2);
                Assert.Equal(node1Subval1, node2Subval2);

                Assert.True(node2Val1Expires1.TotalSeconds > 1);
                Assert.True(node1Val1Expires1.TotalSeconds > 1);
                Assert.True(node2Val2Expires1.TotalSeconds > 1);
                Assert.True(node1Val2Expires1.TotalSeconds > 1);

                var missingVal = basicRedis2.Cache.StringGetWithExpiry(cacheKeyMissing, out TimeSpan missingExpiryVal);
                var missingSubVal = basicRedis2.Cache.StringGetWithExpiry(cacheKeyMissing, out TimeSpan missingExpirySubVal);

                Assert.Null(missingVal);
                Assert.Null(missingSubVal);

                Task.Delay(3500).Wait();
                var expiredValNull = basicRedis2.Cache.StringGetWithExpiry(cacheKey, out TimeSpan expiredValExpiry);
                var expiredSubValNull = basicRedis2.Cache.GetStringStart(cacheKey, 10);

                Assert.Null(expiredValNull);
                Assert.Null(expiredSubValNull);
            }
        }

        [SkippableFact]
        public void Messaging()
        {
            var redisConnection = TestMachineHasRedis.GetTestEnvironmentRedis();

            var testId = Guid.NewGuid();
            using (var basicRedis1 = new BasicRedisWrapper(redisConnection, false))
            using (var basicRedis2 = new BasicRedisWrapper(redisConnection, false))
            {
                var topic1 = $"{typeof(TestMachineHasRedis).FullName}:Bus:{Guid.NewGuid():N}";
                var topic2 = $"{typeof(TestMachineHasRedis).FullName}:Bus:{Guid.NewGuid():N}";

                string
                    t1M1B1S1 = $"testmsg-{Guid.NewGuid():N}",
                    t1M2B1S2 = $"testmsg-{Guid.NewGuid():N}",
                    t1M3B1S2 = $"testmsg-{Guid.NewGuid():N}",
                    t1M4B2S1 = $"testmsg-{Guid.NewGuid():N}",
                    t1M5B2S1 = $"testmsg-{Guid.NewGuid():N}",
                    t1M6B2S1 = $"testmsg-{Guid.NewGuid():N}",
                    t2M7B1S1 = $"testmsg-{Guid.NewGuid():N}",
                    t2M8B2S1 = $"testmsg-{Guid.NewGuid():N}",
                    t1M9B1S1 = $"testmsg-{Guid.NewGuid():N}",
                    x = $"testmsg-x-{Guid.NewGuid():N}";

                var waitForMessage = new Action<List<string>, string>((bag, msg) =>
                {
                    var end = DateTime.Now.AddSeconds(1);
                    while (!bag.Contains(msg) && DateTime.Now < end) Task.Delay(100).Wait();
                });

                var msgsFromB1Sub1T1 = new List<string>();
                var msgsFromB1Sub2T1 = new List<string>();

                var msgsFromB2Sub1T1 = new List<string>();
                var msgsFromB2Sub2T1 = new List<string>();

                var msgsFromB1Sub1T2 = new List<string>();
                var msgsFromB2Sub1T2 = new List<string>();

                // bus 1 subscription 1
                basicRedis1.Bus.Subscribe(topic1, s => {lock(msgsFromB1Sub1T1)msgsFromB1Sub1T1.Add(s);});
                basicRedis1.Bus.Publish(topic1, t1M1B1S1);
                waitForMessage(msgsFromB1Sub1T1, t1M1B1S1);

                // bus 1 subscription 2
                basicRedis1.Bus.Subscribe(topic1, s => {lock(msgsFromB1Sub2T1) msgsFromB1Sub2T1.Add(s);});
                basicRedis1.Bus.Publish(topic1, t1M2B1S2);
                basicRedis1.Bus.Publish(topic1, t1M3B1S2);
                waitForMessage(msgsFromB1Sub2T1, t1M3B1S2);

                // bus 2 subscription 1
                basicRedis2.Bus.Subscribe(topic1, s => {lock(msgsFromB2Sub1T1) msgsFromB2Sub1T1.Add(s);});
                basicRedis2.Bus.Publish(topic1, t1M4B2S1);
                waitForMessage(msgsFromB2Sub1T1, t1M4B2S1);

                // bus 2 subscription 2
                basicRedis2.Bus.Subscribe(topic1, s => {lock(msgsFromB2Sub2T1) msgsFromB2Sub2T1.Add(s);});
                basicRedis2.Bus.Publish(topic1, t1M5B2S1);
                basicRedis2.Bus.Publish(topic1, t1M6B2S1);
                waitForMessage(msgsFromB2Sub2T1, t1M6B2S1);

                // topic2 via bus1 and bus2
                basicRedis1.Bus.Subscribe(topic2, s => {lock(msgsFromB1Sub1T2) msgsFromB1Sub1T2.Add(s);});
                basicRedis2.Bus.Subscribe(topic2, s => {lock(msgsFromB2Sub1T2) msgsFromB2Sub1T2.Add(s);});
                basicRedis1.Bus.Publish(topic2, t2M7B1S1);
                basicRedis2.Bus.Publish(topic2, t2M8B2S1);
                waitForMessage(msgsFromB1Sub1T2, t2M8B2S1);

                Assert.Equal(new List<string>(new[]{ t1M1B1S1, t1M2B1S2, t1M3B1S2, t1M4B2S1, t1M5B2S1, t1M6B2S1 }), msgsFromB1Sub1T1);
                Assert.Equal(new List<string>(new[] { t1M2B1S2, t1M3B1S2, t1M4B2S1, t1M5B2S1, t1M6B2S1 }), msgsFromB1Sub2T1);
                Assert.Equal(new List<string>(new[] { t1M4B2S1, t1M5B2S1, t1M6B2S1 }), msgsFromB2Sub1T1);
                Assert.Equal(new List<string>(new[] { t1M5B2S1, t1M6B2S1 }), msgsFromB2Sub2T1);
                Assert.Equal(new List<string>(new[] { t2M7B1S1, t2M8B2S1, }), msgsFromB1Sub1T2);
                Assert.Equal(new List<string>(new[] { t2M7B1S1, t2M8B2S1, }), msgsFromB2Sub1T2);
            }
        }
    }
}
