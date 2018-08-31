using System;
using RegenerativeDistributedCache.Interfaces;
using StackExchange.Redis;

namespace RegenerativeDistributedCache.Redis
{
    public class RedisFanOutBus : IFanOutBus
    {
        private readonly ISubscriber _redisSubscriber;

        public RedisFanOutBus(ISubscriber redisSubscriber)
        {
            _redisSubscriber = redisSubscriber;
        }

        public void Subscribe(string topicKey, Action<string> messageReceive)
        {
            _redisSubscriber.Subscribe(topicKey, (ch, value) => messageReceive(value));
        }

        public void Publish(string topicKey, string value)
        {
            _redisSubscriber.Publish(topicKey, value);
        }
    }
}
