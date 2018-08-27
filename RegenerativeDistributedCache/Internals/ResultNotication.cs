#region *   License     *
/*
    RegenerativeDistributedCache

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
using Newtonsoft.Json;

namespace RegenerativeDistributedCache.Internals
{
    internal class ResultNotication
    {
        public bool Success { get; set; }
        public string Key { get; set; }
        public string Exception { get; set; }
        public string Sender { get; set; }


        public ResultNotication()
        { }

        public ResultNotication(string key, string senderId)
        {
            Success = true;
            Key = key;
            Exception = null;
            Sender = senderId;
        }

        public ResultNotication(string key, string exception, string senderId)
        {
            Success = false;
            Key = key;
            Exception = exception;
            Sender = senderId;
        }

        public bool IsLocalSender(string localSenderId)
        {
            return Sender == localSenderId;
        }

        public static ResultNotication FromString(string val)
        {
            // object is small and more complex, json serialization sufficient
            return JsonConvert.DeserializeObject<ResultNotication>(val);
        }

        public override string ToString()
        {
            // object is small and more complex, json serialization sufficient
            return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }
    }
}
