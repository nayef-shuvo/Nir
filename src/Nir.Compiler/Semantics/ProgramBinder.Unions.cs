using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Unions: declarations, the conversion of a member into its union, and <c>match</c>.</summary>
internal sealed partial class ProgramBinder
{
    private readonly Dictionary<string, UnionDeclaration> _unionDeclarations = [];
    private readonly Dictionary<string, TypeSymbol> _unions = [];
    private readonly List<UnionTypeSymbol> _unionOrder = [];
    private readonly HashSet<string> _unionsBeingResolved = [];

    // Pass 0a: the union names, so structs, aliases and other unions can refer to them. Members come later.
    private void DeclareUnions(ImmutableArray<UnionDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (TypeNameProblem(declaration.Name.Text) is { } problem)
            {
                Error(DiagnosticCodes.DuplicateType, problem, declaration.Name.Span);
                continue;
            }

            _unionDeclarations[declaration.Name.Text] = declaration;
        }
    }

    private void ResolveUnions()
    {
        foreach (var name in _unionDeclarations.Keys.ToList())
        {
            ResolveUnion(name);
        }
    }

    // The union's type, with its members, worked out on first use so declarations may come in any order.
    private TypeSymbol ResolveUnion(string name)
    {
        if (_unions.TryGetValue(name, out var resolved))
        {
            return resolved;
        }

        var declaration = _unionDeclarations[name];
        if (!_unionsBeingResolved.Add(name))
        {
            Error(DiagnosticCodes.RecursiveAlias, $"type '{name}' is defined in terms of itself", declaration.Name.Span);
            return TypeSymbol.ErrorType;
        }

        var members = ImmutableArray.CreateBuilder<TypeSymbol>();
        var failed = false;
        foreach (var syntax in declaration.Members)
        {
            var member = ResolveTypeSyntax(syntax);
            if (member == TypeSymbol.ErrorType)
            {
                failed = true;
            }
            else if (member is UnionTypeSymbol)
            {
                Error(DiagnosticCodes.NestedUnion, $"a union cannot contain another union ('{member}') yet; list its members instead", syntax.Span);
                failed = true;
            }
            else if (members.Contains(member))
            {
                Error(DiagnosticCodes.DuplicateUnionMember, $"'{member}' is listed twice in union '{name}'", syntax.Span);
                failed = true;
            }
            else
            {
                members.Add(member);
            }
        }

        _unionsBeingResolved.Remove(name);
        TypeSymbol result = TypeSymbol.ErrorType;
        if (!failed)
        {
            var union = new UnionTypeSymbol(declaration) { Members = members.ToImmutable() };
            _unionOrder.Add(union);
            result = union;
        }

        _unions[name] = result;
        return result;
    }

    // The value as the expected type: itself, or, where a union is expected, wrapped as the union. Null if it does not fit.
    private static BoundExpression? Convert(BoundExpression value, TypeSymbol expected)
    {
        if (value.Type == expected)
        {
            return value;
        }

        // A declared error is an anyerror (and so may be the error of an anyerror!T).
        if (expected is AnyErrorTypeSymbol && IsDeclaredError(value.Type))
        {
            return new BoundErrorToAny(value);
        }

        if (expected is not UnionTypeSymbol union)
        {
            return null;
        }

        if (union.IndexOf(value.Type) is var index and >= 0)
        {
            return new BoundUnionWrap(value, union, index);
        }

        return union is ResultTypeSymbol { Error: AnyErrorTypeSymbol } && IsDeclaredError(value.Type)
            ? new BoundUnionWrap(new BoundErrorToAny(value), union, 0)
            : null;
    }

    // `match value { name: Type => body, ... }`. As a statement the arms may be blocks and have any types; as a value every arm is
    // an expression and they all have one type. Every member of the union is handled exactly once.
    private BoundExpression BindMatch(MatchExpression match, bool isStatement)
    {
        var scrutinee = BindExpression(match.Scrutinee);
        var union = scrutinee.Type as UnionTypeSymbol;
        var failed = scrutinee.Type == TypeSymbol.ErrorType;
        if (union is ResultTypeSymbol { Success: null })
        {
            Error(DiagnosticCodes.MatchOnNonUnion, $"cannot match on '{union}' because success carries no value; use 'catch' instead", match.Scrutinee.Span);
            union = null;
            failed = true;
        }

        if (union is null && !failed)
        {
            Error(DiagnosticCodes.MatchOnNonUnion, $"cannot match on a value of type '{scrutinee.Type}'; only a union can be matched", match.Scrutinee.Span);
            failed = true;
        }

        var arms = ImmutableArray.CreateBuilder<BoundMatchArm>();
        var handled = new HashSet<int>();
        TypeSymbol? resultType = null;
        foreach (var arm in match.Arms)
        {
            var caseType = ResolveTypeSyntax(arm.Type);
            var index = -1;
            if (caseType == TypeSymbol.ErrorType)
            {
                failed = true;
            }
            else if (union is not null && (index = union.IndexOf(caseType)) < 0)
            {
                Error(DiagnosticCodes.UnknownMatchCase, $"'{caseType}' is not one of the cases of '{union}'", arm.Type.Span);
                failed = true;
            }
            else if (union is not null && !handled.Add(index))
            {
                Error(DiagnosticCodes.DuplicateMatchCase, $"case '{caseType}' is already handled by an earlier arm", arm.Type.Span);
                failed = true;
            }

            // The arm is bound even when its case is wrong, with an error-typed variable, so mistakes inside it are reported too.
            _scopes.Push();
            VariableSymbol? variable = null;
            if (arm.Name.Text != ReservedNames.Discard)
            {
                variable = new VariableSymbol(arm.Name.Text, index >= 0 ? caseType : TypeSymbol.ErrorType, isMutable: false);
                Declare(variable, arm.Name);
            }

            BoundBlock? body = null;
            BoundExpression? value = null;
            if (isStatement)
            {
                body = arm.Block is { } block
                    ? BindStatements(block)
                    : new BoundBlock([new BoundExpressionStatement(BindExpression(arm.Value!))]);
            }
            else if (arm.Block is { } blockArm)
            {
                Error(DiagnosticCodes.BlockArmInValueMatch, "a match whose value is used needs an expression in every arm; use it as a statement to have blocks", arm.Span);
                BindStatements(blockArm);
                failed = true;
            }
            else
            {
                value = BindExpression(arm.Value!);
                if (value.Type == TypeSymbol.ErrorType)
                {
                    failed = true;
                }
                else if (resultType is null)
                {
                    resultType = value.Type;
                }
                else if (value.Type != resultType)
                {
                    Error(
                        DiagnosticCodes.TypeMismatch,
                        $"the arms of a match must all have the same type: this one is '{value.Type}' but an earlier one is '{resultType}'",
                        arm.Value!.Span);
                    failed = true;
                }
            }

            _scopes.Pop();
            arms.Add(new BoundMatchArm(index, variable, body, value));
        }

        if (union is not null)
        {
            var missing = Enumerable.Range(0, union.Members.Length).Where(i => !handled.Contains(i)).Select(i => $"'{union.Members[i]}'").ToList();
            if (missing.Count > 0 && !failed)
            {
                Error(DiagnosticCodes.NonExhaustiveMatch, $"match on '{union}' does not cover {string.Join(", ", missing)}", match.Scrutinee.Span);
                failed = true;
            }
        }

        if (failed || union is null)
        {
            return new BoundErrorExpression();
        }

        return new BoundMatch(scrutinee, union, arms.ToImmutable(), isStatement ? TypeSymbol.UnitType : resultType!);
    }
}
