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
        private static int _seq;

        private readonly ITestOutputHelper _output;
        public RegenerativeCacheManagerTests(ITestOutputHelper output)
        { _output = output; }

        private string MockGenDelay(string val)
        {
            Task.Delay(200).Wait();
            return val;
        }

        [Fact]
        public void MockedRedisSingleNodeGets()
        {
            SingleNodeGetsInternal("mock", false);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LiveRedisOrSkipSingleNodeGets(bool useMultipleRedisConnections)
        {
            if (!TestMachineHasRedis.GetRedisConnection(out string redisConnection)) return;
            SingleNodeGetsInternal(redisConnection, useMultipleRedisConnections);
        }

        [Fact]
        public void MockedRedisMultiNodeGets()
        {
            MultiNodeGetsInternal("mock", false);
        }
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LiveRedisOrSkipMultiNodeGets(bool useMultipleRedisConnections)
        {
            if (!TestMachineHasRedis.GetRedisConnection(out string redisConnection)) return;

            MultiNodeGetsInternal(redisConnection, useMultipleRedisConnections);
        }

        [Fact]
        public void MockedRedisNodesCompete()
        {
            NodesCompeteInternal("mock", false);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LiveRedisNodesCompete(bool useMultipleRedisConnections)
        {
            if (!TestMachineHasRedis.GetRedisConnection(out string redisConnection)) return;

            NodesCompeteInternal(redisConnection, useMultipleRedisConnections);
        }


        private void SingleNodeGetsInternal(string redisConnection, bool useMultipleRedisConnections, string writeTraceFile = null)
        {
            var testRunKeyspace = $"testKs{DateTime.Now:mmssfffffff}s{Interlocked.Increment(ref _seq)}";

            var tw = new TraceWriter();
            using (var ext = new RedisInterceptOrMock(redisConnection, useMultipleRedisConnections))
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

                // tw.GetOutput().ToList().ForEach(t => _output.WriteLine(t));

                //Assert.Empty(ext.CacheSets);
                // Assert.Empty(ext.CacheGets);

                Assert.StartsWith("t1_", result1);
                Assert.Equal(result1, result2);

                Assert.Single(ext.CacheGets);
                Assert.Single(ext.CacheGetStringStarts);
                Assert.Single(ext.CacheSets);
                Assert.Single(ext.Subscribes);
                Assert.Single(ext.Publishes);
                Assert.Single(ext.LockAttempts);
                Assert.Single(ext.LockAttempts.Where(l => l.Value));

                Thread.Sleep(250);
                Assert.Single(ext.ReceivedMessages);

                // Confirm external cache keys are as expected
                Assert.NotNull(ext.StringGetWithExpiry($"{nameof(MemoryFrontedExternalCache)}:{testRunKeyspace}:Item:test1", out TimeSpan exp));
                var first10 = ext.GetStringStart($"{nameof(MemoryFrontedExternalCache)}:{testRunKeyspace}:Item:test1", 10);
                var first100 = ext.GetStringStart($"{nameof(MemoryFrontedExternalCache)}:{testRunKeyspace}:Item:test1", 100);
                Assert.NotNull(first10);
                Assert.NotNull(first100);
                Assert.StartsWith(first10, first100);

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
                if (writeTraceFile != null) File.WriteAllLines(writeTraceFile, tw.GetOutput());
            }
        }

        private void MultiNodeGetsInternal(string redisConnection, bool useMultipleRedisConnections, string writeTraceFile = null)
        {
            var testRunKeyspace = $"testKs{DateTime.Now:mmssfffffff}s{Interlocked.Increment(ref _seq)}";

            var dtw = new DualTraceWriter();
            using (var node1Ext = new RedisInterceptOrMock(redisConnection, useMultipleRedisConnections))
            using (var node1Cache = new RegenerativeCacheManager(testRunKeyspace, node1Ext.Cache, node1Ext.Lock, node1Ext.Bus, dtw.T1)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            using (var node2Ext = new RedisInterceptOrMock(redisConnection, useMultipleRedisConnections))
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

                Assert.Single(node1Ext.CacheGets);
                Assert.Single(node1Ext.CacheGetStringStarts);
                Assert.Single(node1Ext.CacheSets);
                Assert.Single(node1Ext.Subscribes);
                Assert.Single(node1Ext.Publishes);
                Assert.Single(node1Ext.LockAttempts);
                Assert.Single(node1Ext.LockAttempts.Where(l => l.Value));

                Assert.Empty(node2Ext.CacheGets);
                Assert.Empty(node2Ext.CacheGetStringStarts);
                Assert.Empty(node2Ext.CacheSets);
                Assert.Single(node2Ext.Subscribes);
                Assert.Empty(node2Ext.Publishes);
                Assert.Empty(node2Ext.LockAttempts);

                Thread.Sleep(250);

                Assert.Single(node1Ext.ReceivedMessages);
                Assert.Single(node2Ext.ReceivedMessages);

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
                if(writeTraceFile != null) File.WriteAllLines(writeTraceFile, dtw.GetOutput());
            }
        }

        private void NodesCompeteInternal(string redisConnection, bool useMultipleRedisConnections, string writeTraceFile = null)
        {
            var testRunKeyspace = $"testKs{DateTime.Now:mmssfffffff}s{Interlocked.Increment(ref _seq)}";

            var dtw = new DualTraceWriter();
            using (var node1Ext = new RedisInterceptOrMock(redisConnection, useMultipleRedisConnections))
            using (var node1Cache = new RegenerativeCacheManager(testRunKeyspace, node1Ext.Cache, node1Ext.Lock, node1Ext.Bus, dtw.T1)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            using (var node2Ext = new RedisInterceptOrMock(redisConnection, useMultipleRedisConnections))
            using (var node2Cache = new RegenerativeCacheManager(testRunKeyspace, node2Ext.Cache, node2Ext.Lock, node2Ext.Bus, dtw.T2)
            {
                CacheExpiryToleranceSeconds = 1.5,
                MinimumForwardSchedulingSeconds = 1,
                TriggerDelaySeconds = 1,
                FarmClockToleranceSeconds = 0.1,
            })
            {
                var inactiveRetention = TimeSpan.FromSeconds(9);
                var regenerationInterval = TimeSpan.FromSeconds(3);

                bool seenN1 = false, seenN2 = false;
                var start = DateTime.Now;
                var end = start.AddSeconds(120);
                var rnd = new Random();
                bool nodeOrder = false;

                string node1Result1 = "", node2Result1 = "";

                var getFrom1 = new Action(() => node1Result1 = node1Cache.GetOrAdd("test1", () => MockGenDelay($"t1n1_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval));
                var getFrom2 = new Action(() => node2Result1 = node2Cache.GetOrAdd("test1", () => MockGenDelay($"t1n2_{Guid.NewGuid():N}"), inactiveRetention, regenerationInterval));

                var results = new List<Tuple<string, string>>();
                do
                {
                    // ReSharper disable once AssignmentInConditionalExpression
                    if (nodeOrder = !nodeOrder)
                    {
                        getFrom1(); getFrom2();
                    }
                    else
                    {
                        getFrom2(); getFrom1();
                    }

                    results.Add(new Tuple<string, string>(node2Result1, node2Result1));

                    if (node2Result1.StartsWith("t1n1") || node1Result1.StartsWith("t1n1"))
                    {
                        seenN1 = true;
                    }
                    if (node2Result1.StartsWith("t1n2") || node1Result1.StartsWith("t1n2"))
                    {
                        seenN2 = true;
                    }

                    // randomise the delay to randomise the node order switches
                    Task.Delay(rnd.Next(50, 100)).Wait();

                } while (DateTime.Now < end && (!seenN1 || !seenN2));

                var countOfEqual = results.Count(r => r.Item1 == r.Item2) * 1.0;

                var probabilityMsg = $"{end.Subtract(start).TotalSeconds:0} seconds means " +
                                     $"{(int)(end.Subtract(start).TotalSeconds / regenerationInterval.TotalSeconds)} chances to compete, " +
                                     $"chances of not seeing results from both nodes is approximately " +
                                     $"2 in (2^{(int)(end.Subtract(start).TotalSeconds / regenerationInterval.TotalSeconds)}), " +
                                     $"i.e. 1 in {Math.Pow(2, (int)(end.Subtract(start).TotalSeconds / regenerationInterval.TotalSeconds)) / 2}.";


                _output.WriteLine($"90% of results should be identical, {(countOfEqual / results.Count) * 100:0}% were ({countOfEqual} / {results.Count}).");
                _output.WriteLine(probabilityMsg);

                Assert.True(results.Count > 19,
                    $"Shouldn't see any cache regeneration / node competition for 2 to 3 seconds. Saw different results in only {DateTime.Now.Subtract(start).TotalMilliseconds*1000:#,##0.0}us.");

                Assert.True(countOfEqual / results.Count > 0.9,
                    $"90% of results should be identical, only {(countOfEqual / results.Count)*100:0}% were ({countOfEqual} / {results.Count}).");

                var errmsg = $"Did not see generation on {{0}} - {probabilityMsg}";
                Assert.True(seenN1, string.Format(errmsg, "node1"));
                Assert.True(seenN2, string.Format(errmsg, "node2"));
            }
        }
    }
}
