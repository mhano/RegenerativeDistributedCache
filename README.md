# ![RegenerativeDistributedCache Icon](https://raw.githubusercontent.com/mhano/RegenerativeDistributedCache/master/docs/Icon.png) RegenerativeDistributedCache

A cache that supports scheduling the regeneration of cache items in the background ahead of their
expiry and manages this across a farm of web/service nodes minimising duplicated cache value 
generation work.

Requires an external (network) cache, a fan out pub/sub message bus, and a distributed locking
mechanism (all three of these can be provided by Redis or you might use alternatives for one or
more of these such as RabbitMq for messaging). Basic Redis implementations of these are provided
in RegenerativeCacheManager.Redis.

* Packages:
  * [NuGet.org -> RegenerativeDistributedCache](https://www.nuget.org/packages/RegenerativeDistributedCache/)
  * [NuGet.org -> RegenerativeDistributedCache.Redis](https://www.nuget.org/packages/RegenerativeDistributedCache.Redis/)
* Website: [GitHub -> mhano/RegenerativeDistributedCache](https://github.com/mhano/RegenerativeDistributedCache)
* Builds: [Appveyor -> mhano/regenerativedistributedcache](https://ci.appveyor.com/project/mhano/regenerativedistributedcache)
* License: ["MIT" License Here](https://raw.githubusercontent.com/mhano/RegenerativeDistributedCache/master/LICENSE), reference: [[OpenSource.org -> MIT](https://opensource.org/licenses/mit)]

## RegenerativeDistributedCache.Redis

Basic Redis backed implementations of the interfaces in RegenerativeDistributedCache.Interfaces for 
an external (network) cache, a fan out pub/sub message bus, and a distributed locking mechanism for
use with RegenerativeDistributedCache.RegenerativeCacheManager.

Use a combination of RedisExternalCache, RedisDistributedLockFactory and RedisFanoutBus connected
to your existing wiring / configuration Redis/RedLock or replace components as needed (such as 
implementing a RabbitMq or MassTransit based IFanOutBus instead of RedisFanOutBus).

CAUTION - The BasicRedisWrapper wraps up the creation of all three (either based on a single Redis 
connection, or a Redis connection per concern [caching/locking/messaging]) but performs some pretty
basic implemention in regards to connecting to Redis, you will want to review the approach carefully
prior to using.

## RegenerativeCacheManager

Is the cache that supports scheduling the regeneration of cache items in the background ahead
of their expiry (and manages this across a farm of web/service nodes).

Each node takes responsibility for regenerating the cache value if it hasn't been sufficiently 
recently generated, an external global lock is used to ensure only a single node actually calls
the generation callback. All nodes are informed when there is a new value in the cache 
(causing removal of old value from app memory / allowing lazy fetch of updated value from
network [Redis] cache store). Nodes which compete for but don't win the global lock and are
waiting on an updated value, await a notification message from the lock winning node and 
then return the value from the network cache (which also populates the local memory cache
for faster future retrievals).

### Setup:

As a static/shared instance or singleton from your IOC container:

```C#
regenerativeCacheManagerSingleton = new RegenerativeCacheManager(
	"mykeyspace", externalCache, distributedLockFactory, fanoutBus)
{
    // Default values:
    CacheExpiryToleranceSeconds = 30, 
    FarmClockToleranceSeconds = 15,
    MinimumForwardSchedulingSeconds = 5,
    TriggerDelaySeconds = 1,
};
```

#### Practical Guidance on Time-spans / Timing Settings:
* All up-front (above) settings should have the same values across a farm.
* Regeneration Interval and Inactive Retention (below) should be consistent across a farm
   for a given key value.
* Clock differences between farm nodes should be minimised.
* CacheExpiryToleranceSeconds (typically 30 seconds to minutes) should be greater than 
   FarmClockToleranceSeconds (maximum amount of time clocks might differ amongst nodes).
* TriggerDelaySeconds (delay in causing expired MemoryCache items to be removed, 1 second
   works with current .net Framework - do not set below 1).
* RegenerationInterval (below) should be comfortably larger than the time it takes to 
   generate a value.
* InactiveRetention (below) is the period of time for which scheduled background 
   re-generation of value continues to be scheduled.
* Generation occurs once per regenerationInterval (regardless of how long generation takes).

### Use:

```C#
var result = regenerativeCacheManagerSingleton.GetOrAdd(
    key: $"{nameof(Item)}:{itemId}", 

	// will not be called if value exists
    generateFunc: () => GetItem(itemId).AsString(),

	// total time in cache and regenerating after last GetOrAdd call
    inactiveRetention: TimeSpan.FromMinutes(30),

	// how frequently to update cache from generateFunc()
    regenerationInterval : TimeSpan.FromMinutes(2)
);
```
## CorrelatedAwaitManager

Allows user to await the receipt of a message based on a key. Allows multiple threads to receive a
single copy of a message (often originating remotely).

Typical use is to support multiple local threads receiving a notification from some remote source
(such as a fan out message).

CorrelatedAwaitManager receives a copy of all messages delivered to it then delivers to any threads 
that have setup an awaiter for the specified key value.

Basically a very short lived hyper efficient subscribe mechanism to support coordination within in
distributed system, much cheaper than setting up a typical subscriber.

*used in RegenerativeCacheManager*

### Setup:

```C#
_remoteBus.Subscribe<TMessage>(m => _singletonCorrelatedAwaitManager.NotifyAwaiters(m));
```

### Use:

```C#
TMessage message; // the awaited notification message type.
TSomething result;

if((result = GetSomething(key)) != null) return result;

// CAUTION awaiter must be disposed or cancelled (awaiter.Cancel())
using(var awaiter = _correlatedAwaitManager.CreateAwaiter(key))
{
	// Note you must be certain that the message is being sent after the
	// awaiter has been created (or you could end up waiting forever / timing out).

	// Often this involves a second test as to whether some result became
	// available just now (some time before/after awaiter creation), therefore
	// we should double check it's not available as the notification about it
	// may have been sent before we setup the awaiter.

	if((result = GetSomething(key)) != null) return result;

	// The below or: message = awaiter.Task.Result;
	// or awaiter.Task.Wait(timeOut);
	message = await awaiter.Task.ConfigureAwait(false);
}

// as we have awaited the awaiter we now know the result is available.
return GetSomething(key);
```

## MemoryFrontedExternalCache

Provides a memory front to a network cache so that multiple retrieves on a node only results in a
single retrieve from the network cache store (such as Redis).

*used in RegenerativeCacheManager*

## License (MIT)

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

### Links
* Packages:
  * [NuGet.org -> RegenerativeDistributedCache](https://www.nuget.org/packages/RegenerativeDistributedCache/)
  * [NuGet.org -> RegenerativeDistributedCache.Redis](https://www.nuget.org/packages/RegenerativeDistributedCache.Redis/)
* Website: [GitHub -> mhano/RegenerativeDistributedCache](https://github.com/mhano/RegenerativeDistributedCache)
* Builds: [Appveyor -> mhano/regenerativedistributedcache](https://ci.appveyor.com/project/mhano/regenerativedistributedcache)
* License: ["MIT" License Here](https://raw.githubusercontent.com/mhano/RegenerativeDistributedCache/master/LICENSE), reference: [[OpenSource.org -> MIT](https://opensource.org/licenses/mit)]
