namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Implement this interface and provide to RegenerativeCacheManager to reveive detailed trace messages.
    /// CAUTION: handling the messages should be fast and non blocking.
    /// </summary>
    public interface ITraceWriter
    {
        /// <summary>
        /// Receive a trace message.
        /// </summary>
        /// <param name="message"></param>
        void Write(string message);
    }
}
