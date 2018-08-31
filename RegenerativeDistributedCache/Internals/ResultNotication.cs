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
