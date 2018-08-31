using System;
using System.Threading.Tasks;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Interface of an "awaiter" that CorrelatedAwaitManger returns on which code using it relies
    /// to await a requested result / message (based on a message id / key).
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public interface ICorrelatedAwaiter<TMessage> : IDisposable
    {
        void Cancel();
        Task<TMessage> Task { get; }
    }
}