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
        /// <summary>
        /// Cancel an awaiter ahead of disposing it. This will set the task completion
        /// result to cancelled if it isn't already complete/errored.
        /// </summary>
        void Cancel();

        /// <summary>
        /// Task which can be waited on (e.g. via await, .Wait() or .Result).
        /// Task result is of TMessage which was send to NotifyAwaiters.
        /// </summary>
        Task<TMessage> Task { get; }
    }
}