//HintName: EnumExtensions.g.cs

namespace SG.EnumGenerators
{
    public static partial class EnumExtensions
    {
                public static string ToStringFast(this Color value)
                    => value switch
                    {
                Color.Red => nameof(Color.Red),
                Color.Blue => nameof(Color.Blue),
                    _ => value.ToString(),
                };

    }
}