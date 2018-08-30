using System;
using Xunit;

namespace RegenDistCache.Tests.DynamicSkippableTests
{
    public class SkippableFactAttribute : FactAttribute
    {
        public SkippableFactAttribute()
        {
            SkipIfNoLocalRedis(this);
        }

        public static void SkipIfNoLocalRedis(FactAttribute fact)
        {
            try
            {
                TestMachineHasRedis.SkipLiveRedisBasedTests();
            }
            catch (Exception ex)
            {
                fact.Skip = $"Skipped: {ex.Message}";
            }
        }
    }
}
