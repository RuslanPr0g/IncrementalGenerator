using System.Runtime.CompilerServices;
using VerifyTests;

namespace SG.EnumGenerators.Tests
{
    public static class ModuleInitializer
    {
        [ModuleInitializer]
        public static void Init()
        {
            VerifySourceGenerators.Enable();
        }
    }
}
