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
        private readonly string _traceFileName;
        private readonly string _htmlFileName;
        private StreamWriter _htmlOutputFile;
        private StreamWriter _traceOutputFile;
        private int _fileSeq = 0;

        public SynchedColouredConsoleTraceWriter(string traceFileName = null, string htmlFileName = null)
        {
            _traceFileName = traceFileName;
            _htmlFileName = htmlFileName;

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

                CloseFiles();

                OpenFiles();
            }
        }

        private void OpenFiles()
        {
            if (!string.IsNullOrWhiteSpace(_traceFileName))
            {
                _traceOutputFile = new StreamWriter(File.Open(
                    $"{Path.GetDirectoryName(_traceFileName)}\\{Path.GetFileNameWithoutExtension(_traceFileName)}_{_fileSeq++}{Path.GetExtension(_traceFileName)}"
                    , FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
            }

            if (!string.IsNullOrWhiteSpace(_htmlFileName))
            {
                _htmlOutputFile = new StreamWriter(File.Open(
                    $"{Path.GetDirectoryName(_htmlFileName)}\\{Path.GetFileNameWithoutExtension(_htmlFileName)}_{_fileSeq++}{Path.GetExtension(_htmlFileName)}"
                    , FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
                _htmlOutputFile.WriteLine("<html><body style=\"background-color: black; font-size: 10; font-family: consolas,courier-new,fixed-width;\">");
                _htmlOutputFile.WriteLine($"<h1 style=\"color: white;\">{DateTime.Now}</h1>");
            }
        }

        public void CloseAndStopAllWriting()
        {
            lock (_lockSync)
            {
                StopAllWriting = true;
                CloseFiles();
            }
        }

        private void CloseFiles()
        {
            _traceOutputFile?.Close();
            _traceOutputFile = null;
            _htmlOutputFile?.WriteAsync("</body></html>");
            _htmlOutputFile?.Close();
            _htmlOutputFile = null;
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
            if (StopAllWriting || (_traceOutputFile == null && !writeToConsole)) return;

            var txt = GetText(tpl);

            if (writeToConsole)
            {
                var fg = tpl.Item3 ?? ConsoleColor.Gray;
                var bg = tpl.Item4 ?? ConsoleColor.Black;
                Console.ForegroundColor = fg;
                Console.BackgroundColor = bg;
                Console.WriteLine(txt);

                _htmlOutputFile?.WriteLine($"<span style=\"color: {fg.ToString().ToLowerInvariant()}; background-color: {bg.ToString().ToLowerInvariant()}\">{txt.Replace("\r", "").Replace("\n", "<br/>\r\n")}</span><br/>");
            }

            _traceOutputFile?.WriteLine(txt);
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
