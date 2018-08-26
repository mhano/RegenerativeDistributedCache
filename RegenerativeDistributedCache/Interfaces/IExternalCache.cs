using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RegenerativeDistributedCache.Interfaces
{
    /// <summary>
    /// Represents and external cache (such as redis)
    /// </summary>
    public interface IExternalCache
    {
        void StringSet(string key, string val, TimeSpan absoluteExpiration);

        string StringGetWithExpiry(string key, out TimeSpan expiry);
    }
}
