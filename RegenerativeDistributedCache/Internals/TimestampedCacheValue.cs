using System;
using System.Globalization;

namespace RegenerativeDistributedCache.Internals
{
    internal class TimestampedCacheValue
    {
        public const int MaxDateLength = 50;
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
            // also allows simple retrieval of just the expiry time component
            var split = val.IndexOf(';');
            if (split < 20 || split > MaxDateLength)
            {
                throw new ArgumentException("StringToCreateTsValue requires iso formatted date/time value in format of {DateTime.UtcNow:0} followed by a semi-colon (;) and the string cache value. i.e. 2018-08-28T02:28:40.1098063Z;cache-value.");
            }

            return new TimestampedCacheValue(
                DateTime.ParseExact(val.Substring(0, split), "O", DateTimeFormatInfo.InvariantInfo).ToUniversalTime(),
                val.Substring(split + 1, val.Length - split - 1));
        }

        public override string ToString()
        {
            // about 10x faster than: return JsonConvert.SerializeObject(this);
            // also allows simple retrieval of just the expiry time component
            return $"{CreateCommenced:O};{Value}";
        }
        #endregion
    }
}
