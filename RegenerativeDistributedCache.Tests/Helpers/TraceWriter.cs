using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Tests.Helpers
{
    internal class TraceWriter : ITraceWriter
    {
        public readonly ConcurrentBag<Tuple<int, DateTime, string, ConsoleColor?, ConsoleColor?>> CollectedOutput = new ConcurrentBag<Tuple<int, DateTime, string, ConsoleColor?, ConsoleColor?>>();
        private int _sequenceSource;

        public void Write(string message, ConsoleColor? fgColor = null, ConsoleColor? bgColor = null)
        {
            CollectedOutput.Add(new Tuple<int, DateTime, string, ConsoleColor?, ConsoleColor?>(
                Interlocked.Increment(ref _sequenceSource), DateTime.Now,
                message, fgColor, bgColor
            ));
        }

        public IEnumerable<string> GetOutput()
        {
            return CollectedOutput.OrderBy(i => i.Item1).Select(GetText);
        }

        private static string GetText(Tuple<int, DateTime, string, ConsoleColor?, ConsoleColor?> l)
        {
            return $"{l.Item1}:{l.Item2:mm:ss.ffffff}: {l.Item3}";
        }
    }
}
