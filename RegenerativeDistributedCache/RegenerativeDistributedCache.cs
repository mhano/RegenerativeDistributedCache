using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.Internals;
using RegenerativeDistributedCache.SimpleHelpers;

namespace RegenerativeDistributedCache
{
    /// <summary>
    /// Note this class should be used as a singleton (create a single static / shared instance) within your purpose (i.e.
    /// a singleton within a IOC container or a static).
    /// You may cache multiple types of things in one of these caches, but you must prefix your cache keys to distinguish.
    /// Selecting an appropriate keyspace is still critical as a unique keyspace shared across the web/svc farm is required,
    /// the keyspace is used to prefix keys in redis (locks, caches, messages)
    /// </summary>
    public class RegenerativeCacheManager : IDisposable
    {
        private readonly ITraceWriter _traceWriter;

        private readonly string _keyspace;
        private readonly IDistributedLockFactory _distributedLockFactory;
        private readonly IFanoutBus _fanoutBus;

        private readonly string _lockKeyPrefixGlobalRegenerate;
        private readonly string _pubSubTopicGenerationCompletedEvent;
        private readonly string _lockKeyPrefixLocalRegenerate;

        private readonly CreationTimestampedCache _underlyingCache;
        private readonly ScheduledTriggerManager _regenTriggers;
        private readonly CorrelatedAwaitManager<ResultNotication, string> _correlatedAwaitManager;

        /// <summary>
        /// Amount of time after regeneration interval to tollerate old cached values - depends on variability of time required to re-generate content.
        /// For example a cache might be regenerated every minute but values kept in cache (memory or redis) will be used for up to two minutes
        /// </summary>
        public int CacheExpiryToleranceSeconds { get; set; } = 5;

        /// <summary>
        /// Minimum amount of time in the future to schedule regeneration - typically a few seconds.
        /// The goal is to trigger regeneration once per regeneration period (thus it excludes the time spent generating).
        /// This guards against an infinite background loop of scheduling re-generation immediately if generation time approachs/exceeds generation interval.
        /// E.g. regenerationInterval of 60 seconds on an item that takes 75 seconds to generate, code would otherwise look at the creation time of the current item
        /// and try to schedule the next re-generation for 60 seconds after the previous generation started (15 seconds in the past), this setting allows that need 
        /// to regenerate to be scheduled for near immediately but not quite.
        /// </summary>
        public int MinimumForwardSchedulingSeconds { get => _regenTriggers.MinimumForwardSchedulingSeconds; set => _regenTriggers.MinimumForwardSchedulingSeconds = value; }

        /// <summary>
        /// Delay after expiry of trigger to force trigger item to be expired with a get against the trigger - usually 1 second.
        /// Ensures .net memory cache regards the item as expired and triggers the removal and thus setup of next trigger.
        /// </summary>
        public int TriggerDelaySeconds { get => _regenTriggers.TriggerDelaySeconds; set => _regenTriggers.TriggerDelaySeconds = value; }

        /// <summary>
        /// WARNING, Choosing a keyspace is important.
        /// </summary>
        /// <param name="keyspace">
        ///     Key space should be unique within app domain and redis database but consistent across nodes in a farm.
        ///     Keyspace is used for both prefixing keys in:
        ///         Redis (for caches, global locks and message topics).
        ///         Setup of two MemoryCache objects named MemoryFrontedExternalCache_{keyspace} and ScheduledTriggerManager_{keyspace}
        /// </param>
        /// <param name="externalCache">External cache (such as redis)</param>
        /// <param name="distributedLockFactory">External distributed lock mechanism (such as RedLock on top of redis)</param>
        /// <param name="fanoutBus">External pub/sub fan out non-durable messaging mechanism (such as Redis or RabbitMq)</param>
        /// <param name="traceWriter">Supply to capture detailed tracing/diagnostic information (cache hit/miss get/puts, scheduling, locking and messaging)</param>
        public RegenerativeCacheManager(string keyspace, IExternalCache externalCache, IDistributedLockFactory distributedLockFactory, IFanoutBus fanoutBus, ITraceWriter traceWriter = null)
        {
            _keyspace = keyspace;
            _distributedLockFactory = distributedLockFactory;
            _fanoutBus = fanoutBus;
            _traceWriter = traceWriter;

            _underlyingCache = new CreationTimestampedCache(_keyspace, externalCache, traceWriter);
            _regenTriggers = new ScheduledTriggerManager(_keyspace, traceWriter);

            // pub-sub topic / channel name - unique within an app domain but common across a farm of servers/services (thus includes keyspace but no guid)
            _pubSubTopicGenerationCompletedEvent = $"{nameof(RegenerativeCacheManager)}:{nameof(ResultNotication)}:{_keyspace}";

            // global locks must be unique within an app domain but common across a farm of servers/services (thus includes keyspace but no guid)
            _lockKeyPrefixGlobalRegenerate = $"{nameof(RegenerativeCacheManager)}:{nameof(RegenerateIfNotUnderway)}:{_keyspace}:";

            // local lock (app domain specific locks) need guid to allow two seperate concerns to act as if they were seperate
            // machines on a rare occasion (usually during testing) - this key is used with SimpleHelpers.NamedLock which
            // uses a single static dictionary to manage locks.
            _lockKeyPrefixLocalRegenerate = $"{nameof(RegenerativeCacheManager)}:{nameof(RegenerateIfNotUnderway)}:{_keyspace}:{Guid.NewGuid():N}:";

            _correlatedAwaitManager = new CorrelatedAwaitManager<ResultNotication, string>(v => v.Key, traceWriter);

            _fanoutBus.Subscribe(_pubSubTopicGenerationCompletedEvent,
                (value) =>
                {
                    _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(_fanoutBus)}.{nameof(_fanoutBus.Subscribe)}.Action: Notify Awaiters from Remote message: {value}", ConsoleColor.Yellow);

                    // Exception caught to protect subscriber
                    try
                    {
                        // Processing NOT deferred as NotifyAwaiters seperates the important task of getting current
                        // awaiters out of the way and then setting TaskCompletionSource results (none of this actually
                        // continues on to processing the subsoquent work of the awaiter).
                        _correlatedAwaitManager.NotifyAwaiters(ResultNotication.FromString(value));
                    }
                    catch (Exception ex)
                    {
                        // TODO: log error/exception details
                        _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(_fanoutBus)}.{nameof(_fanoutBus.Subscribe)}.Action: ERROR receiving message, could not deserialize message as ResultNotication, message: {value}, exception: {ex}", ConsoleColor.White, ConsoleColor.Red);
                    }
                }
            );
        }

        public string GetOrAdd(string key, Func<string> generateFunc, TimeSpan maxInactiveRetention, TimeSpan regenerationInterval)
        {
            TimestampedCacheValue cacheResult = null;

            var traceId = _traceWriter == null ? (Guid?) null : Guid.NewGuid();

            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache check for schedule: {key}", ConsoleColor.Green);
            var triggerExists = _regenTriggers.UpdateLastActivity(key, traceId);
            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache CHECKED for schedule: {key}", ConsoleColor.Green);

            // Cache hit if we have a scheduled regeneration happening AND the value is in local or redis cache
            if (triggerExists && (cacheResult = _underlyingCache.Get($"{key}")) != null)
            {
                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache HIT: {key}", ConsoleColor.Green);
                return cacheResult.Value;
            }
            else if (!triggerExists && (cacheResult = _underlyingCache.Get($"{key}")) != null)
            {
                // got value from cache and local trigger doesn't exist, schedule the trigger and return the result
                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: remote cache HIT: {key}, regneration scheduled.", ConsoleColor.Green);

                _regenTriggers.EnsureTriggerScheduled(key, () => RegenerateIfNotUnderway(key, generateFunc, regenerationInterval, traceId), maxInactiveRetention, regenerationInterval, cacheResult.CreateCommenced, traceId: traceId);

                return cacheResult.Value;
            }
            else
            {
                // cache generation not scheduled and value not in cache
                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache MISS: {key}", ConsoleColor.Red);

                using (var correlatedAwaiter = _correlatedAwaitManager.CreateAwaiter(key))
                {
                    // must be called before await and after setup of correlated awaiter - however it is a double tested cache miss so we must trigger
                    // regeneration if not triggered locally or remotely already (only one thread per machine will attempt to get a farm wide lock, and
                    // only one machine in the farm will obtain the lock and do the generation), all other threads/machines wait for the result which is
                    // shared.
                    // NOTE: was previously async i.e. Task.Run(() => RegenerateIfNotUnderway) - as this is optimistic lock or give up this doesn't need to be async
                    RegenerateIfNotUnderway(key, generateFunc, regenerationInterval, traceId);

                    _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache AWAITING: {key}", ConsoleColor.Cyan);

                    // await notifications that are either fired locally or received from a remote machine the content has been generated
                    var notificationMsg = correlatedAwaiter.Task.Result;

                    _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(GetOrAdd)}: TraceId:{traceId:N}: cache AWAITED: {key}", ConsoleColor.Cyan);

                    if (!notificationMsg.Success)
                    {
                        // Only happens when the generation we were waiting on failed
                        throw new ApplicationException($"TraceId:{traceId:N}: Error generating value for: {key}, reGenInterval: {regenerationInterval}, exception: {notificationMsg.Exception}");
                    }

                    var result = _underlyingCache.Get(key);

                    // result could retrieved from cache so long after it was put there that it is missing (in case of extremely short cache expiry)
                    if (!notificationMsg.Success || result == null)
                    {
                        // Only happens when the generation we were waiting on failed
                        throw new ApplicationException($"TraceId:{traceId:N}: Error generating value for: {key}, reGenInterval: {regenerationInterval}, notification appeared successful (no exception details available).");
                    }

                    // schedule based on generation time of current cache item + regenerationInterval
                    // if this was first request (schedule not already setup then we don't schedule (maxInactiveRetention/regenerationInterval) generations
                    // as they are likely (or could be all errors). Allow next successful request to setup schedule (and pay a cache miss penalty).
                    _regenTriggers.EnsureTriggerScheduled(key, () => RegenerateIfNotUnderway(key, generateFunc, regenerationInterval, traceId), maxInactiveRetention, regenerationInterval, result.CreateCommenced, traceId: traceId);

                    return result.Value;
                }
            }
        }

        private void RegenerateIfNotUnderway(string key, Func<string> generateFunc, TimeSpan regenerationInterval, Guid? traceId)
        {
            // generate value in the background then notify any thread/awaiters that got a cache miss looking for the item
            // this means the heavy call is only made with 1 DOP and all waiting threads get the result when ready
            // note regenerate async will simply exit if it can't get a lock because another machine is in the process
            // of regenerating the content.

            // Local lock and farm wide locks are required, the local lock prevents attempting to get farm-wide locks
            // when another local thread is already generating content with a farm wide lock (the local lock is just
            // an optimisation for some race conditions - like a scheduled cache regeneration is firing off generation
            // at the same time a cache miss is triggering immediate generation.

            var localLockStart = DateTime.Now;
            using (var localLock = NamedLock.CreateAndEnter($"{_lockKeyPrefixLocalRegenerate}{key}", 0))
            {
                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Key: {key}, Local_{(localLock.IsLocked ? "Lock_Acquired" : "Lock_NOT_Acquired")} in {DateTime.Now.Subtract(localLockStart).TotalMilliseconds * 1000:#,##0.0}us", localLock.IsLocked ? ConsoleColor.DarkGreen : ConsoleColor.Red, ConsoleColor.Cyan);

                if (localLock.IsLocked)
                {
                    var remoteLockStart = DateTime.Now;
                    // Only acquire lock for regenration interval (allow a parallel regeneration to commence during the expiry tollerance period)
                    using (var distributedLock = _distributedLockFactory.CreateLock($"{_lockKeyPrefixGlobalRegenerate}{key}", regenerationInterval))
                    {
                        _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}:  Key: {key}, Global_{(distributedLock != null ? "Lock_Acquired" : "Lock_NOT_Acquired")} in {DateTime.Now.Subtract(remoteLockStart).TotalMilliseconds * 1000:#,##0.0}us", distributedLock != null ? ConsoleColor.DarkGreen : ConsoleColor.Red, ConsoleColor.Cyan);
                         
                        if (distributedLock != null)
                        {
                            var start = DateTime.UtcNow;

                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Generating: {key}", ConsoleColor.White);
                            string result;
                            ResultNotication notificationMsg;

                            try
                            {
                                result = generateFunc();
                                notificationMsg = new ResultNotication(key);
                            }
                            catch (Exception ex)
                            {
                                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Error: {ex}", ConsoleColor.White, ConsoleColor.Red);

                                result = null;
                                notificationMsg = new ResultNotication(key, ex.ToString());
                            }

                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: GENERATED: {key}, Succes: {notificationMsg.Success}, Size: {result?.Length ?? -1}", ConsoleColor.White);

                            if (result != null)
                            {
                                // Store in redis and local memory (only if successful), after regenerationInterval+CacheExpiryToleranceSeconds the cache will be empty
                                // and requestors will get errors.
                                _underlyingCache.Set($"{key}", new TimestampedCacheValue(start, result), regenerationInterval.Add(TimeSpan.FromSeconds(CacheExpiryToleranceSeconds)));
                            }

                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Notify Awaiters Local: {key}", ConsoleColor.White);
                            // tell local awaiters content is ready
                            _correlatedAwaitManager.NotifyAwaiters(notificationMsg);

                            // trigger publish of redis fan out message to notify awaiters (also something needs to setup _correlatedAwaitManager to consume)
                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Publish to Remote Awaiters: {key}", ConsoleColor.White);
                            _fanoutBus.Publish(_pubSubTopicGenerationCompletedEvent, notificationMsg.ToString());
                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Published to Remote Awaiters: {key}", ConsoleColor.White);

                            if (DateTime.UtcNow.Subtract(start) > regenerationInterval)
                            {
                                // TODO: log warning
                                _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}:  ******************************* WARNING  **********************************************\r\n" +
                                                         "  * Cache item generation took longer than regenerationInterval, this willresult in cache      *\r\n" +
                                                         "  * misses, unnecessary network traffic/regeneration and application PERFORMANCE PROBLEMS!     *\r\n" +
                                                         $"  * Details: Started: {start:O}, Duration: {DateTime.UtcNow.Subtract(start)}, Key: {key}\r\n" +
                                                         "  **********************************************************************************************",
                                    ConsoleColor.DarkRed, ConsoleColor.Yellow);
                            }

                            _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Global_Lock_Releasing", ConsoleColor.DarkYellow, ConsoleColor.Cyan);
                        }
                    }

                    _traceWriter?.Write($"{nameof(RegenerativeCacheManager)}: {nameof(RegenerateIfNotUnderway)}: TraceId:{traceId:N}: Local_Lock_Releasing", ConsoleColor.DarkYellow, ConsoleColor.Cyan);
                }
            }

        }

        public void Dispose()
        {
            _underlyingCache?.Dispose();
            _regenTriggers?.Dispose();
        }
    }
}
