using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RegenerativeDistributedCache.Interfaces;
using RegenerativeDistributedCache.SimpleHelpers;

namespace RegenerativeDistributedCache
{
    /// <summary>
    /// Allows user to await the receipt of a message based on a key. Allows multiple threads to receive a
    /// single copy of a message (often originating remotely).
    /// 
    /// Typical use is to support multiple local threads receiving a notification from some remote source
    /// (such as a fan out message).
    /// 
    /// CorrelatedAwaitManager receives a copy of all messages delivered to it then delivers to any threads that have setup
    /// an awaiter for the specified key value.
    /// 
    /// Basically a very short lived subscribe mechanism to support coordination within in distributed system,
    /// but much cheaper than setting up a specific subscriber.
    /// 
    /// Setup:
    /// _remoteBus.SubscribeTMessage(m => _singletonCorrelatedAwaitManager.NotifyAwaiters(m));
    /// 
    /// Use:
    /// using(var correlatedAwaiter = _correlatedAwaitManager.CreateAwaiter(key))
    /// {
    ///     return correlatedAwaiter.Task.ConfigureAwait(false);
    /// }
    /// 
    /// * awaiter must be disposed or cancelled or you will have a memory leak (awaiter.Cancel())
    /// </summary>
    /// <typeparam name="TMessage">Message to be received which notifies local awaiters that a result is available.</typeparam>
    /// <typeparam name="TKey">The type of key (typically guid/string/int etc.) used to correlate the message to waiters.</typeparam>
    public class CorrelatedAwaitManager<TMessage, TKey>
    {
        private readonly ConcurrentDictionary<TKey, HashSet<CorrelatedAwaiter>> _awaitedNotifications = new ConcurrentDictionary<TKey, HashSet<CorrelatedAwaiter>>();

        private readonly Func<TMessage, TKey> _getKeyFunc;
        private readonly ITraceWriter _traceWriter;

        private readonly string _cacheKeyPrefixNamedLocks;

        /// <summary>
        /// Create an instance of a correlated await manager, typically one is attached to a particular topic
        /// from a fan out message bus subscription.
        /// </summary>
        /// <param name="getKey">
        ///     Function to return TKey from TMessage (when a message is received the key is used to determine who is notified).
        ///     Typically a CASE SENSITIVE string. Internally a ConcurrentDictionary&lt;TKey,...&gt; is used with default comparison.
        /// </param>
        /// <param name="traceWriter">Optional trace writer to receive detailed diagnostic tracing.</param>
        public CorrelatedAwaitManager(Func<TMessage, TKey> getKey, ITraceWriter traceWriter = null)
        {
            _getKeyFunc = getKey;
            _traceWriter = traceWriter;

            _cacheKeyPrefixNamedLocks = $"{nameof(CorrelatedAwaitManager<TMessage, TKey>)}:ManageAwaitersNamedLock:{Guid.NewGuid():N}:";
        }

        /// <summary>
        /// Returns an awaitable task interface to await a requested result / message (based on a message id / key).
        /// </summary>
        public ICorrelatedAwaiter<TMessage> CreateAwaiter(TKey key)
        {
            var awaitableNotification = new CorrelatedAwaiter(key, this);

            var start = DateTime.Now;
            using (NamedLock.CreateAndEnter($"{_cacheKeyPrefixNamedLocks}{key}"))
            {
                _traceWriter?.Write($"{nameof(CorrelatedAwaitManager<TMessage, TKey>)}: {nameof(CreateAwaiter)}: Local_Lock_Acquired in {DateTime.Now.Subtract(start).TotalMilliseconds * 1000:#,##0.0}us");

                if (!_awaitedNotifications.TryGetValue(key, out var hashSet))
                {
                    _awaitedNotifications[key] = hashSet = new HashSet<CorrelatedAwaiter>();
                }

                hashSet.Add(awaitableNotification);
            }

            return awaitableNotification;
        }

        /// <summary>
        /// Notify all awaiters that have been created against this CorrelatedAwaitManager for the particular key
        /// and remove them from receiving future notifications.
        /// </summary>
        /// <param name="msg">Message to extract TKey from and to provide to awaiters.</param>
        public void NotifyAwaiters(TMessage msg)
        {
            // do critical lock synchonisation and manage dictionary of lists of awaiters inline before later
            // notifying waiting request/reply consumers (any new awaiters for same key will start queuing to new entry).
            List<CorrelatedAwaiter> tasksToComplete = null;

            var start = DateTime.Now;
            using (NamedLock.CreateAndEnter($"{_cacheKeyPrefixNamedLocks}{_getKeyFunc(msg)}"))
            {
                _traceWriter?.Write($"{nameof(CorrelatedAwaitManager<TMessage, TKey>)}: {nameof(NotifyAwaiters)}: Local_Lock_Acquired in {DateTime.Now.Subtract(start).TotalMilliseconds * 1000:#,##0.0}us");

                if (_awaitedNotifications.TryGetValue(_getKeyFunc(msg), out var hashSet))
                {
                    tasksToComplete = hashSet.ToList();
                    _awaitedNotifications.TryRemove(_getKeyFunc(msg), out var trash);
                    tasksToComplete.ForEach(tc => tc.Removed = true);
                }
            }

            // Simply sets results on task completion sources allowing scheduling of resumption of work in
            // awaiting threads/tasks.
            tasksToComplete?.ForEach(t => t.SetResult(msg));
        }

        private void RemoveAwaiter(CorrelatedAwaiter awaitableNotification)
        {
            if (!awaitableNotification.Removed)
            {
                var start = DateTime.Now;
                using (NamedLock.CreateAndEnter($"{_cacheKeyPrefixNamedLocks}{awaitableNotification.Key}"))
                {
                    _traceWriter?.Write($"{nameof(CorrelatedAwaitManager<TMessage, TKey>)}: {nameof(RemoveAwaiter)}: Local_Lock_Acquired in {DateTime.Now.Subtract(start).TotalMilliseconds * 1000:#,##0.0}us");

                    if (!awaitableNotification.Removed)
                    {
                        if (_awaitedNotifications.TryGetValue(awaitableNotification.Key, out var hashSet))
                        {
                            hashSet.Remove(awaitableNotification);
                            if (hashSet.Count == 0)
                            {
                                _awaitedNotifications.TryRemove(awaitableNotification.Key, out var trash);
                            }
                        }

                        awaitableNotification.Removed = true;
                    }
                }
            }
        }

        private sealed class CorrelatedAwaiter : IDisposable, ICorrelatedAwaiter<TMessage>
        {
            private TaskCompletionSource<TMessage> TaskCompletionSource { get; }
            private CorrelatedAwaitManager<TMessage, TKey> CorrelatedAwaiterManager { get; }
            internal bool Removed { get; set; }
            internal TKey Key { get; }
            public Task<TMessage> Task => TaskCompletionSource.Task;

            public CorrelatedAwaiter(TKey key, CorrelatedAwaitManager<TMessage, TKey> correlatedAwaiterManager)
            {
                Key = key;
                TaskCompletionSource = new TaskCompletionSource<TMessage>();
                CorrelatedAwaiterManager = correlatedAwaiterManager;
            }

            internal void SetResult(TMessage msg)
            {
                TaskCompletionSource.TrySetResult(msg);
            }

            public void Cancel()
            {
                TaskCompletionSource.TrySetCanceled();
                if (!Removed)
                {
                    CorrelatedAwaiterManager.RemoveAwaiter(this);
                }
            }

            #region IDisposable Support - Critical Cleanup (removal of old items from singleton dictionary)
            private bool _disposed;

            private void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (!Removed)
                    {
                        CorrelatedAwaiterManager.RemoveAwaiter(this);
                    }

                    TaskCompletionSource.TrySetCanceled();

                    _disposed = true;
                }
            }

            ~CorrelatedAwaiter()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            #endregion
        }
    }
}
