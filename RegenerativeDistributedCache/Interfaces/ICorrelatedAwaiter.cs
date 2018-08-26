using System;
using System.Threading.Tasks;

namespace RegenerativeDistributedCache.Interfaces
{
    public interface ICorrelatedAwaiter<TMessage> : IDisposable
    {
        void Cancel();
        Task<TMessage> Task { get; }
    }
}