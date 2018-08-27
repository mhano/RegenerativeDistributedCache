#region *   License     *
/*
    RegenerativeDistributedCache - CorrelatedAwaitManager

    Copyright (c) 2018 Mhano Harkness

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

    License: http://www.opensource.org/licenses/mit-license.php
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

using System;
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
    /// _remoteBus.SubscribeTMessage(m => await _singletonCorrelatedAwaitManager.NotifyAwaiters(m));
    /// 
    /// Use:
    /// using(var correlatedAwaiterManager = _correlatedAwaitManager.CreateAwaiter(key))
    /// {
    ///     return correlatedAwaiterManager.Task.ConfigureAwait(false);
    /// }
    /// 
    /// * awaiter must be disposed or cancelled or you will have a memory leak (awaiter.Cancel())
    /// </summary>
    /// <typeparam name="TMessage">Message to be received which notifies local awaiters that a result is available.</typeparam>
    /// <typeparam name="TKey">The type of key (typically guid/string/int etc.) used to correlate the message to waiters.</typeparam>
    public class CorrelatedAwaitManager<TMessage, TKey>
    {
        private readonly Dictionary<TKey, HashSet<CorrelatedAwaiter>> _awaitedNotifications = new Dictionary<TKey, HashSet<CorrelatedAwaiter>>();

        private readonly Func<TMessage, TKey> _getKeyFunc;
        private readonly ITraceWriter _traceWriter;

        private readonly string _cacheKeyPrefixNamedLocks;

        public CorrelatedAwaitManager(Func<TMessage, TKey> getKey, ITraceWriter traceWriter = null)
        {
            _getKeyFunc = getKey;
            _traceWriter = traceWriter;

            _cacheKeyPrefixNamedLocks = $"{nameof(CorrelatedAwaitManager<TMessage, TKey>)}:ManageAwaitersNamedLock:{Guid.NewGuid():N}:";
        }

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
                    _awaitedNotifications.Remove(_getKeyFunc(msg));
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
                                _awaitedNotifications.Remove(awaitableNotification.Key);
                            }
                        }

                        awaitableNotification.Removed = true;
                    }
                }
            }
        }

        private sealed class CorrelatedAwaiter : IDisposable, ICorrelatedAwaiter<TMessage>
        {
            private TaskCompletionSource<TMessage> TaskCompletionSource { get; set; }
            private CorrelatedAwaitManager<TMessage, TKey> CorrelatedAwaiterManager { get; set; }
            internal bool Removed { get; set; }
            internal TKey Key { get; private set; }
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
