using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RegenerativeDistributedCache.Interfaces;

namespace RegenDistCache.Tests.Helpers
{
    internal class TraceWriter : ITraceWriter
    {
        public readonly ConcurrentBag<Tuple<int, DateTime, string>> CollectedOutput = new ConcurrentBag<Tuple<int, DateTime, string>>();
        private int _sequenceSource;
        private bool _stopped;

        public void Write(string message)
        {
            if (_stopped) return;

            CollectedOutput.Add(new Tuple<int, DateTime, string>(
                Interlocked.Increment(ref _sequenceSource), DateTime.Now,
                message
            ));
        }

        public void StopAndClear()
        {
            _stopped = true;
            while (CollectedOutput.TryTake(out var tr));
        }

        public IEnumerable<string> GetOutput()
        {
            return CollectedOutput.OrderBy(i => i.Item1).Select(GetText);
        }

        private static string GetText(Tuple<int, DateTime, string> l)
        {
            return $"{l.Item1}:{l.Item2:mm:ss.ffffff}: {l.Item3}";
        }
    }
}
