using System;
using Xunit;

namespace RegenDistCache.Tests.DynamicSkippableTests
{
    public class SkippableIfNoRedisFactAttribute : FactAttribute
    {
        public SkippableIfNoRedisFactAttribute()
        {
            SkipIfNoLocalRedis(this);
        }

        public static void SkipIfNoLocalRedis(FactAttribute fact)
        {
            try
            {
                TestRedisConfig.SkipLiveRedisBasedTests();
            }
            catch (Exception ex)
            {
                fact.Skip = $"Skipped: {ex.Message}";
            }
        }
    }
}
