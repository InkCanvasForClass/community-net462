// 为 net462 提供 System.Runtime.CompilerServices.IsExternalInit 占位类型，
// 以便 C# 9+ 的 record / init-only 属性可在此目标框架下编译。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
