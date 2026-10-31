using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Calls: user functions, qualified names and .NET methods.</summary>
internal sealed partial class ProgramBinder
{
    private BoundExpression BindCall(CallExpression call)
    {
        // Arguments are bound before the callee is looked up. A `&x` argument is only valid for a function written in Nir; the
        // paths that call anything else reject it (RejectBorrows).
        var arguments = call.Arguments.Select(BindArgument).ToImmutableArray();

        if (call.Callee.Parts.Length > 1)
        {
            return BindQualifiedCall(call, arguments);
        }

        var name = call.Callee.Text;
        if (_functions.TryGetValue(name, out var user))
        {
            return BindUserCall(call, user, arguments);
        }

        var overloads = BuiltinFunctions.GetOverloads(name);
        if (overloads.IsEmpty)
        {
            Error(DiagnosticCodes.UndefinedName, $"'{name}' is not defined", call.Callee.Span);
            return new BoundErrorExpression();
        }

        return BindClrCall(call.Span, name, name, [.. overloads.Select(ClrCandidate.From)], RejectBorrows(call.Arguments, arguments));
    }

    private BoundExpression BindUserCall(CallExpression call, UserFunctionSymbol target, ImmutableArray<BoundExpression> arguments) =>
        CheckUserCallArguments(call.Span, target.Name, call.Arguments, target, arguments)
            ? new BoundCallExpression(target, arguments)
            : new BoundErrorExpression();

    // `A::B::Method(args)`: the first part must be an imported name; the parts in between walk namespaces down to a
    // type; the last part is a static method of that type.
    private BoundExpression BindQualifiedCall(CallExpression call, ImmutableArray<BoundExpression> arguments)
    {
        var parts = call.Callee.Parts;
        if (StructNamed(parts[0]) is { } owner)
        {
            return BindAssociatedCall(call, owner, arguments);
        }

        if (!_imports.TryGetValue(parts[0].Text, out var current))
        {
            Error(DiagnosticCodes.UndefinedName, $"'{parts[0].Text}' is not defined", parts[0].Span);
            return new BoundErrorExpression();
        }

        for (var i = 1; i < parts.Length - 1; i++)
        {
            var next = Descend(current, parts[i]);
            if (next is null)
            {
                return new BoundErrorExpression();
            }

            current = next;
        }

        var method = parts[^1];
        if (current is not TypeName typeName)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{method.Text}' is not a member of '{current.FullName}'", method.Span);
            return new BoundErrorExpression();
        }

        return BindClrMethod(call.Span, typeName.Type, method, isStatic: true, RejectBorrows(call.Arguments, arguments));
    }

    // One step down a qualified name: a namespace contains namespaces and types; a type contains nothing we can name here.
    private ImportedName? Descend(ImportedName current, Identifier part)
    {
        if (current is NamespaceName ns)
        {
            var fullName = ns.FullName + "." + part.Text;
            if (ClrUniverse.FindType(fullName) is { } type)
            {
                return new TypeName(type);
            }

            if (ClrUniverse.NamespaceExists(fullName))
            {
                return new NamespaceName(fullName);
            }
        }

        Error(DiagnosticCodes.NoSuchMember, $"'{part.Text}' is not a member of '{current.FullName}'", part.Span);
        return null;
    }

    // Finds the method on a .NET type and binds the call, for both `Type::Method(...)` and `value.Method(...)`.
    private BoundExpression BindClrMethod(TextSpan callSpan, Type type, Identifier method, bool isStatic, ImmutableArray<BoundExpression> arguments)
    {
        if (!ClrMethods.HasMethodNamed(type, method.Text, isStatic))
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{method.Text}' is not a member of '{type.FullName}'", method.Span);
            return new BoundErrorExpression();
        }

        var candidates = ClrMethods.FindCandidates(type, method.Text, isStatic);
        return BindClrCall(callSpan, method.Text, $"{type.Name}.{method.Text}", candidates, arguments);
    }

    // Chooses the overload whose parameter types equal the argument types exactly (design.md: no implicit conversions).
    private BoundExpression BindClrCall(
        TextSpan callSpan,
        string name,
        string displayName,
        IReadOnlyList<ClrCandidate> candidates,
        ImmutableArray<BoundExpression> arguments) =>
        ChooseClrMethod(callSpan, name, displayName, candidates, arguments) is { } symbol
            ? new BoundCallExpression(symbol, arguments)
            : new BoundErrorExpression();

    // Reports the error and returns null if there is no usable overload.
    private ClrMethodSymbol? ChooseClrMethod(
        TextSpan callSpan,
        string name,
        string displayName,
        IReadOnlyList<ClrCandidate> candidates,
        ImmutableArray<BoundExpression> arguments)
    {
        var sameArity = candidates.Where(c => c.ParameterTypes.Length == arguments.Length).ToList();
        if (candidates.Count > 0 && sameArity.Count == 0)
        {
            ReportWrongArgumentCount(callSpan, displayName, candidates[0].ParameterTypes.Length, arguments.Length);
            return null;
        }

        var match = sameArity.FirstOrDefault(c => Accepts(c, arguments));
        if (match is null)
        {
            var types = string.Join(", ", arguments.Select(a => a.Type));
            Error(DiagnosticCodes.NoMatchingOverload, $"no overload of '{displayName}' accepts ({types})", callSpan);
            return null;
        }

        if (match.ReturnType is not { } returnType)
        {
            Error(
                DiagnosticCodes.UnsupportedClrType,
                $"'{displayName}' returns '{match.Method.ReturnType.FullName}', which Nir cannot use yet",
                callSpan);
            return null;
        }

        return new ClrMethodSymbol(name, match.Method, returnType, match.ParameterTypes);
    }

    // `receiver.Method(args)`: the method of the .NET type that the receiver's Nir type stands for.
    private BoundExpression BindMemberCall(MemberCallExpression call)
    {
        var receiver = BindPlace(call.Receiver);
        var arguments = call.Arguments.Select(BindArgument).ToImmutableArray();
        if (receiver.Type == TypeSymbol.ErrorType)
        {
            return new BoundErrorExpression();
        }

        if (StructOf(receiver.Type) is { } owner)
        {
            return BindStructMethodCall(call, receiver, owner, arguments);
        }

        var typeName = receiver.Type.Name;

        if (receiver.Type == TypeSymbol.UnitType || !ClrMethods.HasMethodNamed(receiver.Type.ClrType, call.Method.Text, isStatic: false))
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{call.Method.Text}' is not a member of '{typeName}'", call.Method.Span);
            return new BoundErrorExpression();
        }

        var candidates = ClrMethods.FindCandidates(receiver.Type.ClrType, call.Method.Text, isStatic: false);
        var clrArguments = RejectBorrows(call.Arguments, arguments);
        return ChooseClrMethod(call.Span, call.Method.Text, $"{typeName}.{call.Method.Text}", candidates, clrArguments) is { } symbol
            ? new BoundMemberCallExpression(receiver, symbol, clrArguments)
            : new BoundErrorExpression();
    }

    // An error-typed argument fits any parameter: its own error is already reported.
    private static bool Accepts(ClrCandidate candidate, ImmutableArray<BoundExpression> arguments) =>
        arguments.Select((argument, i) => argument.Type == TypeSymbol.ErrorType || argument.Type == candidate.ParameterTypes[i]).All(fits => fits);

    private void ReportWrongArgumentCount(TextSpan callSpan, string name, int expected, int actual) =>
        Error(DiagnosticCodes.WrongArgumentCount, $"'{name}' expects {expected} argument(s) but got {actual}", callSpan);
}
