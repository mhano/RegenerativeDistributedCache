using RegenerativeDistributedCache.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RegenerativeDistributedCache.Demo
{
    public class SynchedColouredConsoleTraceWriter : ITraceWriter
    {
        public bool ShowFullOutputToConsole = true;
        public bool StopAllWriting = false;

        private readonly object _lockSync = new object();

        private int _msgSeq = 0;
        private readonly string _fileName;
        private StreamWriter _outputFile;
        private int _fileSeq = 0;

        public SynchedColouredConsoleTraceWriter(string fileName)
        {
            _fileName = fileName;

            OpenNewOutputFile();
        }

        public void Write(string message)
        {
            WriteLine(message);
        }

        public void OpenNewOutputFile()
        {
            lock (_lockSync)
            {
                StopAllWriting = false;
                _outputFile?.Close();
                _outputFile = null;

                if (!string.IsNullOrWhiteSpace(_fileName))
                {
                    _outputFile = new StreamWriter(File.Open(
                        $"{Path.GetDirectoryName(_fileName)}\\{Path.GetFileNameWithoutExtension(_fileName)}_{_fileSeq++}{Path.GetExtension(_fileName)}"
                        , FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
                }
            }
        }

        public void Resume()
        {
            lock (_lockSync)
            {
                StopAllWriting = false;

                if (_outputFile == null && !string.IsNullOrWhiteSpace(_fileName))
                {
                    _outputFile = new StreamWriter(File.Open(
                        $"{Path.GetDirectoryName(_fileName)}\\{Path.GetFileNameWithoutExtension(_fileName)}_{_fileSeq++}{Path.GetExtension(_fileName)}"
                        , FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
                }
            }
        }

        public void CloseAndStopAllWriting()
        {
            lock (_lockSync)
            {
                StopAllWriting = true;
                _outputFile?.Close();
                _outputFile = null;
            }
        }

        public void WriteLine(string msg, ConsoleColor? fgColor = null, ConsoleColor? bgColor = null, bool overrideShowOutput = false)
        {
            var critOverride = false;
            var lowerInvariantMsg = msg.ToLowerInvariant();

            if (lowerInvariantMsg.Contains("error"))
            {
                critOverride = true;
                fgColor = ConsoleColor.White;
                bgColor = ConsoleColor.Red;
            }
            else if (fgColor == null)
            {
                if (lowerInvariantMsg.Contains("_externalcache"))
                {
                    critOverride = true;
                    fgColor = ConsoleColor.Magenta;
                }
                else if (lowerInvariantMsg.Contains("scheduling"))
                {
                    fgColor = ConsoleColor.Cyan;
                    critOverride = true;
                }
                else if (lowerInvariantMsg.Contains("generated") || lowerInvariantMsg.Contains("generating"))
                {
                    fgColor = ConsoleColor.Yellow;
                    critOverride = true;
                }
                else if (lowerInvariantMsg.Contains("cache miss"))
                {
                    fgColor = ConsoleColor.Red;
                    critOverride = true;
                }
                else if (lowerInvariantMsg.Contains("lock_"))
                {
                    fgColor = ConsoleColor.DarkCyan;
                    critOverride = true;
                }
                else if (lowerInvariantMsg.Contains("cache hit"))
                {
                    fgColor = ConsoleColor.Green;
                }
            }

            var tpl = new Tuple<DateTime, string, ConsoleColor?, ConsoleColor?, int>(DateTime.Now, msg, fgColor, bgColor, Interlocked.Increment(ref _msgSeq));

            lock (_lockSync)
                WriteInternal(tpl, ShowFullOutputToConsole || overrideShowOutput || critOverride);
        }

        private void WriteInternal(Tuple<DateTime, string, ConsoleColor?, ConsoleColor?, int> tpl, bool writeToConsole)
        {
            if (StopAllWriting || (_outputFile == null && !writeToConsole)) return;

            var txt = GetText(tpl);

            if (writeToConsole)
            {
                Console.ForegroundColor = tpl.Item3 ?? ConsoleColor.Gray;
                Console.BackgroundColor = tpl.Item4 ?? ConsoleColor.Black;
                Console.WriteLine(txt);
            }

            _outputFile?.WriteLine(txt);
        }

        private string GetText(Tuple<DateTime, string, ConsoleColor?, ConsoleColor?, int> l)
        {
            return $"{l.Item1:mm:ss.ffffff}:{l.Item5}:  {l.Item2}";
        }

        public void DebugWait(int duration)
        {
            WriteLine($"------- wait {duration}");
            Task.Delay(TimeSpan.FromSeconds(duration)).Wait();
            WriteLine($"------- waited {duration}");
        }
    }
}
