namespace System.Runtime.CompilerServices;

/// <summary>
/// net48 polyfill for C# 9+ <c>init</c> accessors (the type exists in
/// .NET 5+ BCL; the desktop application targets .NET Framework 4.8).
/// The compiler treats this type specially for init-only setters.
/// </summary>
internal sealed class IsExternalInit
{
}
