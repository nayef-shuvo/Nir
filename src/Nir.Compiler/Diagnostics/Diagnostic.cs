namespace Nir.Compiler.Diagnostics;

/// <summary>One problem found in the user's source code. See <see cref="DiagnosticCodes"/> for the codes.</summary>
public sealed record Diagnostic(string Code, Severity Severity, string Message, SourceText Source, TextSpan Span);
