using System;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;

namespace RegenerativeDistributedCache.Redis
{
    /// <summary>
    /// A redis implementation of the pub/sub fan out message bus required by RegenerativeCacheManager
    /// based on StackExchange.Redis.
    /// </summary>
    public class RedisFanOutBus : IFanOutBus
    {
        private readonly ISubscriber _redisSubscriber;

        /// <summary>
        /// A redis implementation of the pub/sub fan out message bus required by RegenerativeCacheManager
        /// based on StackExchange.Redis.
        /// </summary>
        /// <param name="redisSubscriber">StackExchange.Redis ISubscriber / redis connection to use.</param>
        public RedisFanOutBus(ISubscriber redisSubscriber)
        {
            _redisSubscriber = redisSubscriber;
        }

        /// <summary>
        /// From RegenerativeDistributedCache -> IFanOutBus - Create a non durable subscription to a topic based on a key. The method must only return once the subscription is created.
        /// </summary>
        /// <param name="topicKey">A unique key representing a topic (shared amongst a concern / group of subscribers across multiple nodes)</param>
        /// <param name="messageReceive">The action to call (synchronously or asynchronously upon receiving a relevant message [based on topic key]).</param>
        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            _redisSubscriber.Subscribe(topicKey, (ch, value) => messageReceive(value));
        }

        /// <summary>
        /// From RegenerativeDistributedCache -> IFanOutBus - Publish a message to all listeners that have subscribed to the topic key.
        /// This method may be implemented synchronously or asynchronously under the covers.
        /// </summary>
        /// <param name="topicKey">A unique key representing a topic (shared amongst a concern / group of subscribers across multiple nodes)</param>
        /// <param name="value">A string message to send to all subscribers</param>
        public void Publish(string topicKey, string value)
        {
            _redisSubscriber.Publish(topicKey, value);
        }
    }
}
