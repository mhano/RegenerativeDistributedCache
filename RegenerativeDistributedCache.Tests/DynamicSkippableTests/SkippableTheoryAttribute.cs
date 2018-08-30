using System;
using Xunit;

namespace RegenerativeDistributedCache.Tests.DynamicSkippableTests
{
    public class SkippableTheoryAttribute : TheoryAttribute
    {
        public SkippableTheoryAttribute()
        {
            SkippableFactAttribute.SkipIfNoLocalRedis(this);
        }
    }
}
