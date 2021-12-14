using SourceGeneratorIncremental.Enums;
namespace SourceGeneratorIncremental
{
    public static class EnumExtensions
    {
        public static string ToStringFast(this Color color)
            => color switch
            {
                Color.Red => nameof(Color.Red),
                Color.Blue => nameof(Color.Blue),
                _ => color.ToString(),
            };
    }
}
