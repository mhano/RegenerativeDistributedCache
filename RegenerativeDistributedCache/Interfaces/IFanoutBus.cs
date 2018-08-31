using System;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Provide access to a non-durable fan out pub/sub mechanism (such as redis or rabbitmq)
    /// as required by RegenerativeCacheManager. Implement this interface to use an alternative
    /// message bus (such as RabbitMQ).
    /// A Redis implementation is provided in RegenerativeDistributedCache.Redis.
    /// </summary>
    public interface IFanOutBus
    {
        /// <summary>
        /// Create a non durable subscription to a topic based on a key. The method must only return once the subscription is created.
        /// </summary>
        /// <param name="topicKey">A unique key representing a topic (shared amongst a concern / group of subscribers across multiple nodes)</param>
        /// <param name="messageReceive">The action to call (synchronously or asynchronously upon receiving a relevant message [based on topic key]).</param>
        void Subscribe(string topicKey, Action<string> messageReceive);

        /// <summary>
        /// Publish a message to all listeners that have subscribed to the topic key.
        /// This method may be implemented synchronously or asynchronously under the covers.
        /// </summary>
        /// <param name="topicKey">A unique key representing a topic (shared amongst a concern / group of subscribers across multiple nodes)</param>
        /// <param name="value">A string message to send to all subscribers</param>
        void Publish(string topicKey, string value);
    }
}
