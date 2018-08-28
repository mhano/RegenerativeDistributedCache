#region *   License     *
/*
    RegenerativeDistributedCache - ScheduledTriggerManager

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
using System.Runtime.Caching;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache
{
    public class ScheduledTriggerManager : IDisposable
    {
        private readonly ITraceWriter _traceWriter;
        private readonly MemoryCache _memoryCache;

        public ScheduledTriggerManager(string keyspace, ITraceWriter traceWriter = null)
        {
            _traceWriter = traceWriter;
            _memoryCache = new MemoryCache($"{nameof(ScheduledTriggerManager)}_{keyspace}");
        }

        private class TriggerInfo
        {
            public DateTime LastActive;
            public DateTime TargetCallbackTime;
            public Action CallBack;
            public TimeSpan MaxInactiveRetention;
            public TimeSpan CallbackInterval;
            public string TraceId;
        }

        /// <summary>
        /// Minimum amount of time in the future to schedule callback - typically a few seconds.
        /// The goal is to trigger callback once per callback period (thus it excludes the time spent generating).
        /// This guards against an infinite background loop of scheduling re-generation immediately if generation time approachs/exceeds generation interval.
        /// E.g. callbackInterval of 60 seconds on an item that takes 75 seconds to generate, code would otherwise look at the creation time of the current item
        /// and try to schedule the next re-generation for 60 seconds after the previous generation started (15 seconds in the past), this setting allows that need 
        /// to regenerate to be scheduled for near immediately but not quite.
        /// </summary>
        public double MinimumForwardSchedulingSeconds { get; set; } = 5;

        /// <summary>
        /// Delay after expiry of trigger to force trigger item to be expired with a get against the trigger - usually 1 second.
        /// Ensures .net memory cache regards the item as expired and triggers the removal and thus setup of next trigger.
        /// </summary>
        public double TriggerDelaySeconds { get; set; } = 1;

        public void EnsureTriggerScheduled(string key, Action callbackAction, TimeSpan maxInactiveRetention, TimeSpan callbackInterval, DateTime prevCallbackStartTimeUtc, DateTime? lastActive = null, string traceId = null)
        {
            _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(EnsureTriggerScheduled)}: TraceId:{traceId}: Key:{key}");

            if (GetCurrentOrRegenerated(key) == null)
            {
                // always schedule callback from previousStart + callbackInterval unless cache is going awry (generation taking longer than reGenInterval + tollerance)
                // in which case schedule regen for a minimum period of time in the future (will then likely fail to get global lock and defer to next result and schedule
                // further forward).
                var logicalStartTime = prevCallbackStartTimeUtc.Add(callbackInterval);
                if (logicalStartTime < DateTime.UtcNow.AddSeconds(MinimumForwardSchedulingSeconds))
                {
                    logicalStartTime = DateTime.UtcNow.AddSeconds(MinimumForwardSchedulingSeconds);
                }

                var policy = new CacheItemPolicy
                {
                    // if not scheduled but cache item existed, can we schedule regenration to happen 60 (regen interval) seconds after last seen schedule (any where in farm)
                    // Address edge case where callback at the end of maxInactiveRetention might skip a beat by getting something from cache
                    // but not scheduling the callback untill after it expires.
                    AbsoluteExpiration = logicalStartTime,

                    RemovedCallback = (removeArgs) => ScheduleNextAndInvokeCallBack(removeArgs),
                };

                var triggerInfo = new TriggerInfo
                {
                    LastActive = lastActive ?? DateTime.UtcNow,
                    TargetCallbackTime = logicalStartTime,
                    CallBack = callbackAction,
                    MaxInactiveRetention = maxInactiveRetention,
                    CallbackInterval = callbackInterval,
                    TraceId = traceId,
                };

                // only schedule a cleanup task if this thread won the race to add to the dictionary.
                if (Add(key, triggerInfo, policy))
                {
                    // setup a task to reliably cause cache to expire at required time (1 second late to ensure cache invalidation / expiry -
                    // otherwise .net may see the get on the exact second of expiry and regard the item as not expired).
                    // .net will generally do this every 20 seconds for all expired memory cache entries, but this is not documented/gaurenteed
                    // behaviour so it is worth forcing for reliability (a futre .net version could change this behavior such as doing it less
                    // frequently if there is cpu pressure but not memory pressure).
                    var delay = logicalStartTime.Subtract(DateTime.UtcNow).Add(TimeSpan.FromSeconds(TriggerDelaySeconds));

                    _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(EnsureTriggerScheduled)}: TraceId:{traceId}: Schedule Cleanup: Key:{key}, in {delay.TotalSeconds}s");
                    Task.Delay(delay).ContinueWith(t =>
                    {
                        _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(EnsureTriggerScheduled)}: TraceId:{traceId}:  Scheduled Cleanup Executing: Key:{key}");
                        ClearIfExpired(key);
                    }).ConfigureAwait(false);
                }
            }
        }

        public bool UpdateLastActivity(string key, string traceId = null)
        {
            var start = DateTime.Now;
            _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(UpdateLastActivity)}: TraceId:{traceId} start");
            try
            {
                var triggerInfo = GetCurrentOrRegenerated(key);

                if (triggerInfo == null) return false;

                lock (triggerInfo)
                {
                    triggerInfo.LastActive = DateTime.UtcNow;
                }

                return true;
            }
            finally
            {
                _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(UpdateLastActivity)}: TraceId:{traceId}: Completed in {DateTime.Now.Subtract(start).TotalMilliseconds*1000:#,##0.0}us");
            }
        }

        private void ScheduleNextAndInvokeCallBack(CacheEntryRemovedArguments removeArgs)
        {
            // removed due to expiry happens when the callback interval is up and the item is expired
            if (removeArgs.RemovedReason == CacheEntryRemovedReason.Expired)
            {
                var triggerInfo = (TriggerInfo)removeArgs.CacheItem.Value;
                DateTime lastActive, idealStartTime;
                lock (triggerInfo)
                {
                    lastActive = triggerInfo.LastActive;
                    idealStartTime = triggerInfo.TargetCallbackTime; // normally approx now less TriggerDelaySeconds
                }

                var expires = lastActive.Add(triggerInfo.MaxInactiveRetention);

                if (DateTime.UtcNow < expires)
                {
                    _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(ScheduleNextAndInvokeCallBack)}: TraceId:{triggerInfo.TraceId}: Re-scheduling: {removeArgs.CacheItem.Key}");

                    // schedule the next callback at ideal start time of current callback start + callbackInterval
                    // we need to specify last active to avoid it being treated as active due to background activity (which would be infinite re-generation in background)
                    EnsureTriggerScheduled(removeArgs.CacheItem.Key, triggerInfo.CallBack, triggerInfo.MaxInactiveRetention, triggerInfo.CallbackInterval, idealStartTime, lastActive, triggerInfo.TraceId);

                    // run the specified callbackAction asynchronously - generate new content for cache in background
                    // needs to be asynchronous as calls to GetCurrentOrRegenerated would be blocked otherwise
                    Task.Run(() => triggerInfo.CallBack());
                }
                else
                {
                    _traceWriter?.Write($"{nameof(ScheduledTriggerManager)}: {nameof(ScheduleNextAndInvokeCallBack)}: TraceId:{triggerInfo.TraceId}: NOT Re-scheduling: {removeArgs.CacheItem.Key}");
                }
            }
        }

        private TriggerInfo GetCurrentOrRegenerated(string key)
        {
            // in case retrieval triggers removal (due to rexpiry) and immediate re-add (first attempt returns null but causes re-add, second returns newly added value)
            return (TriggerInfo)_memoryCache.Get(key) ?? (TriggerInfo)_memoryCache.Get(key);
        }

        private void ClearIfExpired(string key)
        {
            _memoryCache.Get(key);
        }

        private bool Add(string key, TriggerInfo triggerInfo, CacheItemPolicy policy)
        {
            return _memoryCache.Add(key, triggerInfo, policy);
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
