#region *   License     *
/*
    RegenerativeDistributedCache - Tests

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

    License: https://opensource.org/licenses/mit
    Website: https://github.com/mhano/RegenerativeDistributedCache
 */
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RegenerativeDistributedCache.Interfaces;

namespace RegenerativeDistributedCache.Tests.Helpers
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
