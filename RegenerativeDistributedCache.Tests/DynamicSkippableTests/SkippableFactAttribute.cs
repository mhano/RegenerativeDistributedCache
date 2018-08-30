using System;
using Xunit;

namespace RegenerativeDistributedCache.Tests.DynamicSkippableTests
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
