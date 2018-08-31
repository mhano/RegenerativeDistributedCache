using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RegenerativeDistributedCache.Interfaces;

namespace RegenDistCache.Tests.Helpers
{
    internal class DualTraceWriter
    {
        public ITraceWriter T1;
        public ITraceWriter T2;

        private class T : ITraceWriter
        {
            private readonly string _name;
            private readonly DualTraceWriter _dtw;

            public T(string name, DualTraceWriter dtw)
            {
                _name = name;
                _dtw = dtw;
            }

            public void Write(string message)
            {
                _dtw.CollectedOutput.Add(new Tuple<int, DateTime, string>(
                    Interlocked.Increment(ref _dtw._sequenceSource), DateTime.Now,
                    $"{_name}: {message}"
                ));
            }
        }

        public DualTraceWriter()
        {
            T1 = new T("T1", this);
            T2 = new T("T2", this);
        }

        public readonly ConcurrentBag<Tuple<int, DateTime, string>> CollectedOutput = new ConcurrentBag<Tuple<int, DateTime, string>>();
        private int _sequenceSource;

        public IEnumerable<string> GetOutput()
        {
            var format = string.Join("0", Enumerable.Range(0, _sequenceSource.ToString().Length + 1).Select(i => ""));
            return CollectedOutput.OrderBy(i => i.Item1).Select(t => GetText(t, format));
        }

        private static string GetText(Tuple<int, DateTime, string> l, string seqFormat = "0")
        {
            return $"{l.Item1.ToString(seqFormat)}:{l.Item2:mm:ss.ffffff}: {l.Item3}";
        }
    }
}
