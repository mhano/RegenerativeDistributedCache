using Xunit;

namespace RegenDistCache.Tests.DynamicSkippableTests
{
    public class SkippableTheoryAttribute : TheoryAttribute
    {
        public SkippableTheoryAttribute()
        {
            SkippableFactAttribute.SkipIfNoLocalRedis(this);
        }
    }
}
