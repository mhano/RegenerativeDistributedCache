using Newtonsoft.Json;

namespace RegenerativeDistributedCache.Internals
{
    internal class ResultNotication
    {
        public bool Success { get; set; }
        public string Key { get; set; }
        public string Exception { get; set; }

        #region Ctor and Serialization

        public ResultNotication()
        { }

        public ResultNotication(string key)
        {
            Success = true;
            Key = key;
            Exception = null;
        }

        public ResultNotication(string key, string exception)
        {
            Success = false;
            Key = key;
            Exception = exception;
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
        #endregion
    }
}
