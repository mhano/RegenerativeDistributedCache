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

        void IFanOutBus.Subscribe(string topicKey, Action<string> messageReceive)
        {
            _redisSubscriber.Subscribe(topicKey, (ch, value) => messageReceive(value));
        }

        void IFanOutBus.Publish(string topicKey, string value)
        {
            _redisSubscriber.Publish(topicKey, value);
        }
    }
}
