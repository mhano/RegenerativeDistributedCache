using System;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Provide access to a non-durable fan out pub/sub mechanism (such as redis or rabbitmq)
    /// </summary>
    public interface IFanoutBus
    {
        void Subscribe(string topicKey, Action<string> messageReceive);
        void Publish(string topicKey, string value);
    }
}
