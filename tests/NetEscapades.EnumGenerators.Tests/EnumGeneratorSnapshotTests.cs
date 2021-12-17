using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace SG.EnumGenerators.Tests
{
    [UsesVerify]
    public class EnumGeneratorSnapshotTests
    {
        [Fact]
        public Task GeneratesEnumExtensionsCorrectly()
        {
            var source = @"
using SG.EnumGenerators;

[EnumExtensions]
public enum Color
{
    Red = 0,
    Blue = 1,
}";

            return TestHelper.Verify(source);
        }
    }
}
