using System;

namespace RegenerativeDistributedCache.Interfaces
{
    public interface ITraceWriter
    {
        void Write(string message, ConsoleColor? fgColor = null, ConsoleColor? bgColor = null);
    }
}
