# RegenerativeDistributedCache

Provides a cache that supports scheduling the regeneration of cache items in the background ahead
of their expiry (and to manage this across a farm of web/service nodes).

Requires an external (network) cache, a fan out pub/sub message bus, and a distributed locking
mechanism (all three of these can be provided by Redis or you might use alternatives for one or
more of these such as RabbitMq for messaging). Basic redis implementations of these are provided
in RegenerativeCacheManager.Redis


## RegenerativeCacheManager

Is the cache that supports scheduling the regeneration of cache items in the background ahead
of their expiry (and to manage this across a farm of web/service nodes).

### Setup:

As a static/shared instance or singleton from your IOC container:

```C#
regenerativeCacheManagerSingleton = new RegenerativeCacheManager(
	"mykeyspace", externalCache, distributedLockFactory, fanoutBus)
{
    CacheExpiryToleranceSeconds = 60,
    MinimumForwardSchedulingSeconds = 5,
};
```

### Use:

```C#
var result = regenerativeCacheManagerSingleton.GetOrAdd(
    key: $"{nameof(Item)}:{itemId}", 
    generateFunc: () => GetItem(itemId).AsString(), // will not be called if value exists
    inactiveRetention: TimeSpan.FromMinutes(30),
    regenerationInterval : TimeSpan.FromMinutes(2)
);
```

## CorrelatedAwaitManager

Allows user to await the receipt of a message based on a key. Allows multiple threads to receive a
single copy of a message (often originating remotely).

Typical use is to support multiple local threads receiving a notification from some remote source
(such as a fan out message).

CorrelatedAwaitManager receives a copy of all messages delivered to it then delivers to any threads that have setup
an awaiter for the specified key value.

Basically a very short lived subscribe mechanism to support coordination within in distributed system,
but much cheaper than setting up a specific subscriber.

* used in RegenerativeCacheManager

### Setup:

```C#
_remoteBus.SubscribeTMessage(m => _singletonCorrelatedAwaitManager.NotifyAwaiters(m));
```

### Use:

```C#
using(var correlatedAwaiter = _correlatedAwaitManager.CreateAwaiter(key))
{
    return await correlatedAwaiter.Task.ConfigureAwait(false);
    // or return correlatedAwaiter.Task.Result;
}
```

### CAUTION awaiter must be disposed or cancelled (awaiter.Cancel) or you will have a memory leak.

## MemoryFrontedExternalCache

Provides a memory front to a network cache so that multiple retrieves on a node only results in a
single retrieve from the network cache store (such as redis).

* used in RegenerativeCacheManager