// netstandard2.0 predates C# 9. The compiler only needs this type to exist to compile records and
// init accessors; it is internal, so it never clashes with the real one on newer frameworks.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
