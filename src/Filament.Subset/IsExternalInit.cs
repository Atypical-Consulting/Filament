namespace System.Runtime.CompilerServices
{
    // netstandard2.0 lacks IsExternalInit, which the C# compiler requires for `init` setters
    // and positional records. This internal shim supplies it. Standard netstandard2.0 polyfill.
    internal static class IsExternalInit { }
}
