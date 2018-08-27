﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace RegenerativeDistributedCache.Tests
{
    public class RegenerativeCacheManagerTests
    {
        private readonly ITestOutputHelper _output;
        public RegenerativeCacheManagerTests(ITestOutputHelper output)
        {
            this._output = output;
        }

        private const bool UseMockedRedis = true;
        private const string RedisConnection = UseMockedRedis ? "mock" : "localhost:6379";

        private string MockGenDelay(string val)
        {
            Task.Delay(200).Wait();
            return val;
        }

        [Fact]
        public void SingleNodeGets()
        {
            var testRunKeyspace = $"{Guid.NewGuid():N}";

            var tw = new TraceWriter();
            using (var ext = new RedisIntercept(RedisConnection))
            using (var cache = new RegenerativeCacheManager(testRunKeyspace, ext.Cache, ext.Lock, ext.Bus, tw)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            {
                var inactiveRetention = TimeSpan.FromSeconds(6);
                var regenerationInterval = TimeSpan.FromSeconds(2);

                var result1 = cache.GetOrAdd("test1", () => $"t1_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);
                var result2 = cache.GetOrAdd("test1", () => $"t1_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);

                Assert.StartsWith("t1_", result1);
                Assert.Equal(result1, result2);

                Assert.Single(ext.CacheSets);
                Assert.Single(ext.Subscribes);
                Assert.Single(ext.Publishes);
                Assert.Single(ext.ReceivedMessages);
                Assert.Single(ext.LockAttempts);
                Assert.Single(ext.LockAttempts.Where(l => l.Value));

                Thread.Sleep(3500);

                // confirm cache has been regenerated in the background
                Assert.Equal(2, ext.CacheSets.Count);

                Assert.Single(ext.Subscribes);
                Assert.Equal(2, ext.Publishes.Count);
                Assert.Equal(2, ext.ReceivedMessages.Count);
                Assert.Equal(2, ext.LockAttempts.Count);
                Assert.Equal(2, ext.LockAttempts.Count(l => l.Value));
                
                var result3 = cache.GetOrAdd("test1", () => $"t2_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);
                var result4 = cache.GetOrAdd("test1", () => $"t2_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);

                // result still starts with t1_ as we have not passed the 6s mark
                // but has been regenerated so is not equal to previous value
                Assert.NotEqual(result1, result3);
                Assert.StartsWith("t1_", result3);
                Assert.Equal(result3, result4);

                Thread.Sleep(10000); // 6s (regens) + 2 (validity) + 1.5 (tolerance)

                var result5 = cache.GetOrAdd("test1", () => $"t3_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);
                var result6 = cache.GetOrAdd("test1", () => $"t3_{Guid.NewGuid():N}", inactiveRetention, regenerationInterval);

                // result still starts with t1_ as we have not passed the 3s mark
                Assert.StartsWith("t3_", result5);
                Assert.Equal(result5, result6);

                // File.WriteAllLines("C:\\temp\\temp3.txt", tw.GetOutput());
                // tw.GetOutput().ToList().ForEach(t => output.WriteLine(t));
            }
        }

        [Fact]
        public void MultiNodeGets()
        {
            var testRunKeyspace = $"{Guid.NewGuid():N}";

            var dtw = new DualTraceWriter();
            using (var node1Ext = new RedisIntercept(RedisConnection))
            using (var node1Cache = new RegenerativeCacheManager(testRunKeyspace, node1Ext.Cache, node1Ext.Lock, node1Ext.Bus, dtw.T1)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            using (var node2Ext = new RedisIntercept(RedisConnection))
            using (var node2Cache = new RegenerativeCacheManager(testRunKeyspace, node2Ext.Cache, node2Ext.Lock, node2Ext.Bus, dtw.T2)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            {
                var inactiveRetention = TimeSpan.FromSeconds(6);
                var regenerationInterval = TimeSpan.FromSeconds(2);

                var node1Result1 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t1n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node1Result2 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t1n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);

                Assert.StartsWith("t1n1_", node1Result1);
                Assert.Equal(node1Result1, node1Result2);

                Assert.Single(node1Ext.CacheSets);
                Assert.Single(node1Ext.Subscribes);
                Assert.Single(node1Ext.Publishes);
                Assert.Single(node1Ext.ReceivedMessages);
                Assert.Single(node1Ext.LockAttempts);
                Assert.Single(node1Ext.LockAttempts.Where(l => l.Value));

                Assert.Empty(node2Ext.CacheSets);
                Assert.Single(node2Ext.Subscribes);
                Assert.Empty(node2Ext.Publishes);
                Assert.Single(node2Ext.ReceivedMessages);
                Assert.Empty(node2Ext.LockAttempts);

                var node2Result1 = node2Cache.GetOrAdd("test1", () => MockGenDelay($"t1n2_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node2Result2 = node2Cache.GetOrAdd("test1", () => MockGenDelay($"t1n2_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);

                Assert.Single(node1Ext.CacheSets);
                Assert.Single(node1Ext.Subscribes);
                Assert.Single(node1Ext.Publishes);
                Assert.Single(node1Ext.ReceivedMessages);
                Assert.Single(node1Ext.LockAttempts);
                Assert.Single(node1Ext.LockAttempts.Where(l => l.Value));

                Assert.Single(node2Ext.CacheGets);
                Assert.Empty(node2Ext.CacheSets);
                Assert.Single(node2Ext.Subscribes);
                Assert.Empty(node2Ext.Publishes);
                Assert.Single(node2Ext.ReceivedMessages);
                Assert.Empty(node2Ext.LockAttempts);

                // confirm node2 gets result generated on node1 from external cache
                Assert.StartsWith("t1n1_", node2Result1);
                Assert.Equal(node2Result1, node2Result2);

                Thread.Sleep(3500);

                // confirm cache has been regenerated in the background (on both nodes)

                // one set from initial and one from regeneration (across farm)
                Assert.Equal(2, node1Ext.CacheSets.Count + node2Ext.CacheSets.Count);

                Assert.Single(node1Ext.Subscribes);

                var totalPublished = node1Ext.Publishes.Count + node2Ext.Publishes.Count;
                Assert.InRange(totalPublished, 2,4);
                Assert.Equal(totalPublished, node1Ext.ReceivedMessages.Count);
                Assert.Equal(totalPublished, node2Ext.ReceivedMessages.Count);

                // two to four total lock attempt (depending on thread race conditions)
                Assert.InRange(node1Ext.LockAttempts.Count + node2Ext.LockAttempts.Count, 2, 4);

                // two successful locks across the farm (initial generation + 1 regeneration)
                Assert.InRange(node1Ext.LockAttempts.Count(l => l.Value) + node2Ext.LockAttempts.Count(l => l.Value),
                    2,4);

                var locksAcquired = node1Ext.LockAttempts.Count(l => l.Value) +
                                      node2Ext.LockAttempts.Count(l => l.Value);

                // confirm first hit to node2 returns val from node1
                Assert.Single(node2Ext.Subscribes);

                // initial generation + 1 re-generation per node
                Assert.Equal(locksAcquired, node1Ext.ReceivedMessages.Count);
                Assert.Equal(locksAcquired, node2Ext.ReceivedMessages.Count);

                var node1Result3 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t2n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node1Result4 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t2n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);

                // result still starts with t1n1_ or t1n2 
                Assert.NotEqual(node1Result1, node1Result3);
                Assert.StartsWith("t1", node1Result3);
                Assert.Equal(node1Result3, node1Result4);

                Thread.Sleep(10000);

                var node1Result5 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t3n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node1Result6 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t3n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);

                // result still start with t3n1
                Assert.StartsWith("t3n1_", node1Result5);
                Assert.Equal(node1Result5, node1Result6);

                Thread.Sleep(10000);

                var node2Result3 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t4n2_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node2Result4 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t4n2_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node1Result7 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t4n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);
                var node1Result8 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t4n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval);

                // check results flow from node2 back to node1 as well
                Assert.StartsWith("t4n2_", node2Result3);
                Assert.Equal(node2Result3, node2Result4);
                Assert.Equal(node2Result3, node1Result7);
                Assert.Equal(node2Result3, node1Result8);

                // tw.GetOutput().ToList().ForEach(t => output.WriteLine(t));
                // File.WriteAllLines("C:\\temp\\temp3.txt", dtw.GetOutput());
            }
        }
    }
}