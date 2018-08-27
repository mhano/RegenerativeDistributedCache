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
using System.Globalization;

namespace RegenerativeDistributedCache.Internals
{
    internal class TimestampedCacheValue
    {
        // ReSharper disable FieldCanBeMadeReadOnly.Local - Access required to serialize/deserialize
        public DateTime CreateCommenced;
        public string Value;
        // ReSharper restore FieldCanBeMadeReadOnly.Local

        #region Ctor and Serialization

        public TimestampedCacheValue()
        { }

        public TimestampedCacheValue(DateTime createCommenced, string value)
        {
            CreateCommenced = createCommenced;
            Value = value;
        }

        public static TimestampedCacheValue FromString(string val)
        {
            // about 10x to 100x faster than: return JsonConvert.DeserializeObject<TimestampedCacheValue>(val);
            var split = val.IndexOf(';');
            if (split < 20 || split > 50)
            {
                throw new ArgumentException("StringToCreateTsValue requires iso formatted date/time value in format of {DateTime.UtcNow:0};string");
            }

            return new TimestampedCacheValue(
                DateTime.ParseExact(val.Substring(0, split), "O", DateTimeFormatInfo.InvariantInfo).ToUniversalTime(),
                val.Substring(split + 1, val.Length - split - 1));
        }

        public override string ToString()
        {
            // about 10x faster than: return JsonConvert.SerializeObject(this);
            return $"{CreateCommenced:O};{Value}";
        }
        #endregion
    }
}
