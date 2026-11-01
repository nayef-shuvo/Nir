# Compiler architecture

This file says how the compiler is built. Follow it exactly. If a task needs a different structure, change this file
first and add a line to `docs/decisions.md`.

Items marked **ASSUMPTION** are believed true but not yet checked. (The slice 1 assumptions about ANTLR and about emit
were checked by T1.0 and T1.1; results are in sections 5 and 9.) Do not build on an assumption without checking it.

## 1. Pipeline

```
SourceText
   │  Parsing   (ANTLR lexer + parser, then AstBuilder)
   ▼
SyntaxTree / AST          our own immutable records, no ANTLR types
   │  Semantics (Binder: name resolution, type checking)
   ▼
BoundProgram              typed, resolved tree; every name points at a symbol
   │  Emit      (System.Reflection.Emit PersistedAssemblyBuilder)
   ▼
<name>.dll + <name>.runtimeconfig.json     run with: dotnet <name>.dll
```

Every phase takes the previous phase's output plus a `DiagnosticBag` and returns its own output. A phase never throws
for bad user code; it reports a `Diagnostic` and carries on as far as it can. After a phase, if the bag has errors, the
driver stops and does not run later phases.

## 2. Solution layout

```
Nir/
├── Nir.slnx
├── global.json, Directory.Build.props, .editorconfig, .gitignore
├── CLAUDE.md, README.md
├── docs/                       design.md, architecture.md, practices.md, roadmap.md, decisions.md
├── samples/                    example .nir programs (hello.nir, ...)
├── reference/                  old ANTLR files, read-only reference. Not compiled. Delete when no longer needed.
├── src/
│   ├── Nir.Compiler/           the whole compiler, one project
│   │   ├── Diagnostics/
│   │   ├── Clr/                ClrUniverse (the .NET types the compiler can see), ClrTypes (the ones used constantly, looked up once),
│   │   │                       ClrNames, GeneratedNames (names of what the compiler generates)
│   │   ├── Language/           TypeNames, Punctuation, ReservedNames, ImportRoot: the words of Nir as constants
│   │   ├── Parsing/            *.g4, NirLexerBase.cs, AstBuilder.cs, ParserErrorListener.cs, SyntaxParser.cs
│   │   ├── Syntax/             AST records
│   │   ├── Semantics/          Binder, symbols, types, bound tree
│   │   ├── Emit/               Emitter(.Definitions), BodyEmitter.* (one partial class split by topic), EmitContext, RuntimeConfigWriter
│   │   └── NirCompiler.cs      the public facade
│   └── Nir.Cli/                the `nir` executable (assembly name `nir`)
└── tests/
    └── Nir.Tests/
        ├── Diagnostics/ Lexing/ Parsing/ Semantics/ Emit/ Cli/
        ├── Programs/           golden program tests, see practices.md section 3
        └── Support/            TestHost and helpers
```

Why one compiler project: this is a one-person compiler, and per-phase projects make every cross-phase change touch
several projects. Split a phase into its own project only when a concrete reason appears (for example a second tool
that needs just the parser). Do not split preemptively.

Namespaces match folders: `Nir.Compiler.Diagnostics`, `Nir.Compiler.Parsing`, `Nir.Compiler.Syntax`,
`Nir.Compiler.Semantics`, `Nir.Compiler.Emit`, plus `Nir.Compiler.Language` (constants for the words of the language, usable
everywhere through a global using) and `Nir.Compiler.Clr`. The facade `NirCompiler` is in `Nir.Compiler`.
The CLI is `Nir.Cli`. Tests are `Nir.Tests.<Folder>`.

## 3. Dependency rules

- `Diagnostics` and `Clr` depend on nothing in Nir.
- `Syntax` depends on `Diagnostics` (for `TextSpan`) only.
- `Parsing` depends on `Syntax`, `Diagnostics`, and ANTLR.
- `Semantics` depends on `Syntax`, `Diagnostics`, `Clr`. Never on `Parsing`.
- `Emit` depends on `Semantics`, `Diagnostics`, `Clr`. Never on `Syntax` or `Parsing`.
- `Nir.Cli` depends on `Nir.Compiler` through `NirCompiler` and `Diagnostics` types only.

**ANTLR boundary.** ANTLR generated classes (`NirLexer`, `NirParser`, contexts) are public because the generator makes
them public. They must be used only inside `Nir.Compiler/Parsing/`. No public member of any type outside `Parsing` may
mention a type from the `Antlr4` namespace or from the generated classes. A test enforces this (task T1.9).
Removing ANTLR later must only touch `Parsing/`.

## 4. Diagnostics (`Nir.Compiler.Diagnostics`)

```csharp
public sealed record SourceText(string Name, string Text)
{
    public (int Line, int Column) GetLineAndColumn(int position);   // both 1-based; column counts UTF-16 units
    public string GetLineText(int line);                            // without the line terminator
}

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public enum Severity { Error, Warning }

public sealed record Diagnostic(string Code, Severity Severity, string Message, SourceText Source, TextSpan Span);

public sealed class DiagnosticBag : IReadOnlyList<Diagnostic>
{
    public bool HasErrors { get; }
    public void Report(string code, Severity severity, string message, SourceText source, TextSpan span);
}
```

Printed form. The CLI prints diagnostics to stderr in rustc's style (`Nir.Cli/DiagnosticPrinter.cs`): a header with the
severity, code and message; a ` --> file:line:column` pointer; the source line with a gutter holding its line number; and
carets under the exact span with the message beside them. A blank line follows each diagnostic, and a last line counts the
errors.

```
error[NIR2001]: 'prnt' is not defined
 --> samples/bad.nir:3:5
  |
3 |     prnt("hello")
  |     ^^^^ 'prnt' is not defined

error: aborting due to 1 previous error
```

Rules: the gutter is as wide as the line number (so `10 |` shifts the rest by one column); tabs are shown as 4 spaces and
the carets follow; a span with no length gets one caret; a span that runs past the end of its first line is underlined to
the end of that line; CRLF line endings never show. The message appears twice (header and beside the carets) because
diagnostics have one message and no separate short label.

Colour (ANSI) is used when stderr is a terminal: bold red for `error[CODE]` and the carets with their message, bold blue
for `-->`, `|` and line numbers, bold for the header message. Redirected output is plain. `NO_COLOR` turns colour off
and `CLICOLOR_FORCE=1` forces it on. `TerminalColor.ForError()` decides. Severity is lowercase `error` or `warning`.

**Codes.** `NIR` plus four digits. Ranges:

| Range | Phase |
|---|---|
| NIR0xxx | lexical (bad character, unterminated string) |
| NIR1xxx | syntax (parser) |
| NIR2xxx | name resolution and declarations |
| NIR3xxx | type checking and mutability |
| NIR4xxx | emit |

Codes are never reused or renumbered. Each code is a `public const string` in a static class
`DiagnosticCodes` (one file), with a one-line comment saying what it means. Message wording: lowercase start, no trailing
period, names in single quotes, say what is wrong, not what to do. Example: `'x' is not defined`,
`cannot assign to 'x' because it is not mutable`.

Codes defined so far (slices 1 to 7; the message text in `DiagnosticCodes` call sites is authoritative):

| Code | Meaning | Message template |
|---|---|---|
| NIR0001 | unexpected character | `unexpected character '{c}'` |
| NIR0002 | unterminated string literal | `unterminated string literal` |
| NIR0003 | backslash followed by something that is not an escape | `unknown escape sequence '\q'` |
| NIR1001 | syntax error (any parser error) | the text from the parser, see section 5 |
| NIR2001 | name not defined | `'{name}' is not defined` |
| NIR2002 | missing entry point | `program has no 'main' function` |
| NIR2003 | duplicate function | `function '{name}' is already defined` |
| NIR2004 | wrong argument count | `'{name}' expects {n} argument(s) but got {m}` |
| NIR2005 | name already declared in this block or in a block around it | `'{name}' is already declared in this block` / `... in an enclosing block` |
| NIR2006 | unknown type in an annotation, parameter or return type | `unknown type '{name}'` |
| NIR2007 | `main` with parameters or a return type | `'main' must have no parameters and no return type` |
| NIR2008 | import not starting with `clr::cs`, or naming nothing below it | `imports must start with 'clr::cs'` / `'clr::cs' alone names nothing; add a namespace or type after it` |
| NIR2009 | imported namespace or type does not exist | `'{name}' was not found` |
| NIR2010 | same local name imported twice | `'{name}' is already imported` |
| NIR2011 | namespace, type or value has no such member | `'{member}' is not a member of '{container}'` |
| NIR3001 | type mismatch | `cannot convert '{from}' to '{to}'` |
| NIR3002 | assignment to a non-`mut` variable | `cannot assign to '{name}' because it is not mutable` |
| NIR3003 | operator not defined for these types | `operator '{op}' cannot be applied to '{left}' and '{right}'` (unary: `... to '{type}'`) |
| NIR3004 | literal out of range | `'{text}' is out of range for '{type}'` |
| NIR3029 | literal suffix its literal cannot have | `'{digits}' has a fraction, so it cannot be a '{type}'` |
| NIR3030 | character literal that is not one character | `a character literal cannot be empty` / `a character literal must hold exactly one character (one UTF-16 code unit)` |
| NIR3005 | variable would have type `()` | `cannot declare '{name}' with type '()'` |
| NIR3006 | no overload accepts the argument types | `no overload of '{name}' accepts ({types})` |
| NIR3007 | function does not end with a return | `'{name}' does not return a value on every path` |
| NIR3008 | `return value` in a `()` function | `cannot return a value from '{name}' because it returns '()'` |
| NIR3009 | bare `return` in a value function | `'{name}' must return a value of type '{type}'` |
| NIR3010 | `break` or `continue` outside a loop | `'{keyword}' can only be used inside a loop` |
| NIR3012 | value of type `()` inside `{ }` of an interpolated string | `cannot use a value of type '()' in an interpolated string` |
| NIR3011 | .NET method returns a type Nir cannot use yet | `'{method}' returns '{type}', which Nir cannot use yet` |
| NIR2012 | struct contains itself | `struct '{name}' contains itself, so it would have no size` |
| NIR2013 | duplicate or reserved type name | `type '{name}' is already defined` / `'{name}' is a reserved type name` |
| NIR2014 | `impl` target is not a struct of this file | `cannot add methods to '{name}': only structs declared in this file can have an 'impl'` |
| NIR2016 | field given twice (in a declaration or a literal) | `field '{name}' is given more than once` |
| NIR2017 | field missing from a struct literal | `missing field '{name}' in '{struct}'` |
| NIR2018 | `self` outside a method | `'self' can only be used inside a method` |
| NIR2019 | tuple type with some elements named and some not | `either every element of a tuple type has a name or none does` |
| NIR2020 | tuple alias or union defined in terms of itself | `type '{name}' is defined in terms of itself` |
| NIR2021 | union lists the same type twice | `'{type}' is listed twice in union '{name}'` |
| NIR2022 | union member is a union | `a union cannot contain another union ('{name}') yet; list its members instead` |
| NIR2023 | type before `!` is not an error type | `the type before '!' must be an error type or 'anyerror', but '{type}' is not one` |
| NIR2024 | type after `!` is an error or a result | `the value after '!' cannot be an error or a result, but '{type}' is one` |
| NIR3013 | `&mut self` method called on a temporary | `cannot call '{method}' on a temporary because it changes its receiver` |
| NIR3014 | borrow anywhere but a direct argument to a Nir function or method | `a borrow can only be passed directly as an argument to a function or method written in Nir` |
| NIR3015 | borrow of something other than a whole variable | `only a whole variable can be borrowed, not a field or another expression` |
| NIR3016 | struct or tuple inside `{ }` of an interpolated string | `cannot put a value of type '{type}' in an interpolated string; use its fields` (tuples: `elements`) |
| NIR3018 | `match` on a value that is not a union | `cannot match on a value of type '{type}'; only a union can be matched` |
| NIR3019 | match arm type is not a member | `'{type}' is not one of the cases of '{union}'` |
| NIR3020 | two arms for one member | `case '{type}' is already handled by an earlier arm` |
| NIR3021 | match leaves out members | `match on '{union}' does not cover '{a}', '{b}'` |
| NIR3022 | block arm in a match whose value is used | `a match whose value is used needs an expression in every arm; use it as a statement to have blocks` |
| NIR3023 | `try` in a function that does not return an `E!T` | `'try' returns the error to the caller, but '{f}' returns '{type}'; make it return an 'E!T' or handle the error with 'catch'` |
| NIR3024 | `try` of an error the function cannot hold | `'try' would return a '{E}' from '{f}', which returns '{type}'; handle it with 'catch' and make a '{E2}' yourself` |
| NIR3025 | `try` or `catch` of something that is not an `E!T` | `'try' needs a value of an 'E!T' type, but this is '{type}'` |
| NIR3026 | `try` or a block `catch` not the whole value of a statement | `'try' can only be the whole value of a 'let', an assignment to a variable, a 'return' or a statement` |
| NIR3027 | `catch` block can end without returning | `the block of a 'catch' must end with a 'return' on every path, because there is no value to carry on with` |
| NIR3028 | error type made without `error`, or `error` on a struct | `'{E}' is an error type; make one with 'error {E} { ... }'` / `'{S}' is a struct, not an error type; write it without 'error'` |
| NIR3017 | borrow of a non-struct | `cannot borrow a value of type '{type}': only structs can be borrowed for now` |

## 5. Parsing (`Nir.Compiler.Parsing`)

Files:
- `NirLexer.g4`: lexer grammar. Superclass `NirLexerBase`.
- `NirLexerBase.cs`: newline-to-`SEMI` rule and all lexical error reporting (`NIR0001`, `NIR0002`); see below.
- `NirParser.g4`: parser grammar, `options { tokenVocab = NirLexer; }`.
- `AstBuilder.cs`: ANTLR parse tree to our AST. A plain class that reads the parse-tree contexts; the ANTLR visitor
  classes are not generated (`<Visitor>false</Visitor>`). It skips parts that error recovery left missing.
- `ParserErrorListener.cs`: turns every ANTLR parser error into an `NIR1001` diagnostic.
- `SyntaxParser.cs`: the only entry point other code uses. It is not called `Parser` because the generated
  `NirParser : Parser` would then resolve `Parser` to our class instead of ANTLR's.

```csharp
public static class SyntaxParser
{
    public static CompilationUnit Parse(SourceText source, DiagnosticBag diagnostics);
}
```

`Parse` always returns a `CompilationUnit`, even for broken input (possibly with fewer members). It never throws for
bad input. It reports **only the first** lexical or syntax problem of the file (the one with the smallest position): the
parser's recovery guesses what the user meant, and every guess after the first mistake is usually wrong. A hand-written
parser could later report more.

**ANTLR setup.** One project holds both grammars, so no copy tricks are needed. In `Nir.Compiler.csproj`:
- `PackageReference Antlr4.Runtime.Standard 4.13.1` and `Antlr4BuildTasks 12.14.0`.
- `<Antlr4 Include="Parsing/NirLexer.g4"><Package>Nir.Compiler.Parsing</Package><Listener>false</Listener><Visitor>false</Visitor></Antlr4>`
- `<Antlr4 Include="Parsing/NirParser.g4"><Package>Nir.Compiler.Parsing</Package><Listener>false</Listener><Visitor>false</Visitor></Antlr4>`

`Antlr4BuildTasks` resolves `tokenVocab = NirLexer` with both grammars in the same project (verified in T1.0; the
generated `NirLexer.tokens`, `NirLexer.cs`, `NirParser.cs` appear under `obj/`). If you change a `<Visitor>`/`<Listener>`
setting, delete `src/Nir.Compiler/obj` and `bin` once, or stale generated files stay in the build.

**Newline rule (`NirLexerBase`).** Semicolons are optional. The grammar emits a `NEWLINE` token for runs of newlines
and `SEMI` for an explicit `;`. `NirLexerBase.NextToken()` turns each `NEWLINE` into a `SEMI` or drops it:
- It becomes `SEMI` only if bracket depth is 0 (`(...)` and `[...]` are tracked; `{...}` is not) **and** the previous
  token can end a statement.
- The set of tokens that can end a statement is the method `EndsStatement`. **Every time a token that can end an
  expression or statement is added to the lexer grammar, add it to `EndsStatement` and add a lexer test for it.**
- At end of file, if the previous token could end a statement, a `SEMI` is synthesized before `EOF`.
- Consequence to keep in mind: a multi-line struct body or array literal in `{}` needs a trailing comma before `}`.

The newline logic was ported from `reference/NirLexerBase.cs`; do not redesign it. The queue of pending tokens at end of
file replaces the old single stashed `EOF`.

**Keywords.** A keyword is added to the lexer grammar in the slice that first uses it. Keywords are listed before
`IDENTIFIER` in the grammar (ANTLR resolves same-length ties by rule order). Keywords so far: `fn`, `let`, `mut`, `return`, `using`, `if`, `else`, `while`, `for`, `in`, `break`, `continue`, `as`, `true`, `false`. `->` is `ARROW`, `::` is `COLONCOLON`, `.` is `DOT`.
A `using` line is `USING importPath`, and the compilation unit is a list of `topLevelItem`s (a `using` or a function), so
imports may appear anywhere at the top level. A call target is a `qualifiedName`; a method call on a value is the
highest-precedence expression alternative (`receiver.Method(args)`). A line cannot start with `.` to continue the previous
line: the newline already ended the statement.

**Strings.** Plain strings use lexer modes: `STRING_START`, `STRING_TEXT`, `STRING_END`. The lexer keeps the raw text; a
backslash and the next character are consumed together (so `\"` does not end the string) and `AstBuilder` decodes them
with `StringEscapes.Decode`: `\n \t \r \\ \" \{`, and an unknown one is `NIR0003` at the backslash. So
`StringLiteralExpression.Value` and `InterpolatedText.Text` hold the decoded text.

**Interpolated strings** (`$"text {expression} text"`) use a second mode, `INTERP_STRING`, for the text between holes:
`INTERP_STRING_START`, `INTERP_TEXT` (which may contain `{{` and `}}`), `HOLE_START`, `HOLE_END`, `INTERP_STRING_END`. The
hole itself is lexed in the default mode, so it can contain any expression, strings and nested interpolated strings. The
`{` in `INTERP_STRING` is `HOLE_START` and pushes the default mode. `NirLexerBase` finds the `}` that closes the hole by
counting: a stack of brace counters, one per open hole, goes up for each `{` and down for each `}` inside the hole, and a
`}` at zero is retyped `HOLE_END` and calls `PopMode()`. A hole also counts as brackets for the newline rule. The parser
rule is `interpolatedString : INTERP_STRING_START interpolatedPart* INTERP_STRING_END` with parts `INTERP_TEXT` or
`HOLE_START expression HOLE_END`.

A newline or the end of the file inside a string (plain or interpolated) reports `NIR0002` once, at the opening quote
(at the outermost open string when the file ends). The lexer keeps a stack of open strings and repairs the token stream
(turns the newline, or a synthesized token at end of file, into the matching end token) so the parser sees a complete
string and does not add errors about it. The parser still reports only the earliest problem of the file.

**Error messages.** `ParserErrorListener` reports every ANTLR syntax error as `NIR1001` with ANTLR's message text rewritten in
plain words (`Friendly`: token names such as `INT_LITERAL` become `a number`, repeats in the expected set are removed,
`'<EOF>'` becomes `the end of the file`), a lowercase first letter, and the span of the offending token. Lexical errors use `NIR0001`/`NIR0002`. Improving
messages is a later task; correctness of position matters more than wording.

## 6. Syntax tree (`Nir.Compiler.Syntax`)

All nodes are `sealed record`s deriving from `abstract record SyntaxNode(TextSpan Span)`. Lists are
`ImmutableArray<T>`. Nodes hold no references to ANTLR objects and no parent pointers.

Slice 1 nodes:

```csharp
public abstract record SyntaxNode(TextSpan Span);
public sealed record Identifier(string Text, TextSpan Span) : SyntaxNode(Span);

public sealed record CompilationUnit(ImmutableArray<FunctionDeclaration> Functions, TextSpan Span) : SyntaxNode(Span);
public sealed record FunctionDeclaration(Identifier Name, Block Body, TextSpan Span) : SyntaxNode(Span);

public sealed record Block(ImmutableArray<Statement> Statements, TextSpan Span) : SyntaxNode(Span);

public abstract record Statement(TextSpan Span) : SyntaxNode(Span);
public sealed record ExpressionStatement(Expression Expression, TextSpan Span) : Statement(Span);

public abstract record Expression(TextSpan Span) : SyntaxNode(Span);
public sealed record CallExpression(Identifier Callee, ImmutableArray<Expression> Arguments, TextSpan Span) : Expression(Span);
public sealed record StringLiteralExpression(string Value, TextSpan Span) : Expression(Span);
```

Slice 2 adds: `VariableDeclaration(Name, IsMutable, DeclaredType?, Initializer)` and `AssignmentStatement(Target, Value)`
(statements); `NameExpression`, `IntLiteralExpression(Text)`, `DoubleLiteralExpression(Text)`, `BoolLiteralExpression(Value)`,
`UnaryExpression(Operator, Operand)`, `BinaryExpression(Left, Operator, Right)` and `ParenthesizedExpression(Inner)`
(expressions); the `BinaryOperator` / `UnaryOperator` enums and `OperatorText` (operator spelling for messages).
Literal text stays a string so the binder can report out-of-range values. The expression grammar is a precedence ladder:
unary `-`, then `* / %`, `+ -`, `< <= > >=`, `== !=`, all left-associative.

Slice 3 adds: `Parameter(Name, Type)` and `ReturnStatement(Value?)`, and `FunctionDeclaration` now has
`Parameters` and an optional `ReturnType` (null means the function returns `()`). Types are plain identifiers;
the binder decides which names are types.

Slice 6 adds `InterpolatedStringExpression(Parts)` with `InterpolatedText(Text)` and `InterpolatedHole(Expression)` parts, and
`CastExpression(Operand, TargetType)` for `x as T`. The cast sits in the expression ladder just below unary `-` / `!` and above
`* / %`, so `-x as double` is `(-x) as double` and `a + b as double` is `a + (b as double)`.

Slice 5 adds the statements `IfStatement(Condition, Then, ElseBlock?, ElseIf?)`, `WhileStatement`, `ForStatement(Variable, Start,
End, IsInclusive, Body)`, `BreakStatement` and `ContinueStatement`, and the operators `UnaryOperator.Not`, `BinaryOperator.And` and
`Or`. Grammar: `else` may follow a newline (`terminator? ELSE`); the range of a `for` is part of the `for` rule, not an
expression; `&&` binds tighter than `||` and both are looser than `==`. `0..10` lexes as `INT_LITERAL DOTDOT INT_LITERAL`
because a `.` starts a fraction only when a digit follows. `BREAK` and `CONTINUE` are in `EndsStatement`.

Slice 4 adds: `UsingDirective(Path, Members)` (`Members` is empty unless a `{A, B}` group is used), `QualifiedName(Parts)`
with a `Text` property joining the parts with `::`, and `MemberCallExpression(Receiver, Method, Arguments)`.
`CallExpression.Callee` is now a `QualifiedName` and `CompilationUnit` has `Usings`.

Later slices add nodes; they never change the meaning of existing ones. Each node type is created only by
`AstBuilder`. `Span` covers the node's full source text, from its first token to its last.

## 7. Semantics (`Nir.Compiler.Semantics`)

The binder turns the AST into a **bound tree**: a separate, typed tree where every name has been resolved to a symbol.
Emit reads only the bound tree. This is why AST and bound tree are separate types.

```csharp
public sealed class TypeSymbol { string Name; Type ClrType; }     // static members: UnitType, StringType (+ IntType, DoubleType, BoolType
                                                                  // from slice 2). Suffixed because the CA1720 analyzer rejects `String`.
public abstract class FunctionSymbol { string Name; TypeSymbol ReturnType; ImmutableArray<TypeSymbol> ParameterTypes; }
public sealed class BuiltinFunctionSymbol : FunctionSymbol { MethodInfo Method; }   // e.g. print -> Console.WriteLine(string)
public sealed class UserFunctionSymbol : FunctionSymbol { FunctionDeclaration Declaration; }

public abstract record BoundNode;
public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, UserFunctionSymbol EntryPoint) : BoundNode;
public sealed record BoundFunction(UserFunctionSymbol Symbol, BoundBlock Body) : BoundNode;
public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements) : BoundNode;
public abstract record BoundStatement : BoundNode;
public sealed record BoundExpressionStatement(BoundExpression Expression) : BoundStatement;
public abstract record BoundExpression(TypeSymbol Type) : BoundNode;
public sealed record BoundCallExpression(FunctionSymbol Target, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Target.ReturnType);
public sealed record BoundStringLiteral(string Value) : BoundExpression(TypeSymbol.StringType);
```

Slice 2 adds to the bound tree: `BoundVariableDeclaration`, `BoundAssignment` (statements); `BoundVariableExpression`,
`BoundIntLiteral`, `BoundDoubleLiteral`, `BoundBoolLiteral`, `BoundUnaryExpression`, `BoundBinaryExpression` and
`BoundErrorExpression` (expressions); the `BoundBinaryOperator` / `BoundUnaryOperator` enums (separate from the syntax
ones, because Emit may not reference Syntax); and `VariableSymbol(Name, Type, IsMutable)`.
`TypeSymbol` gains `IntType`, `DoubleType`, `BoolType` and `ErrorType`.

Slice 3 adds `BoundReturnStatement(Value?)`. `UserFunctionSymbol` now carries its resolved return type and its
`Parameters`, which are immutable `VariableSymbol`s. Signatures are resolved in pass 1, before any body is bound.
Parameters are put in the function's single scope first, then the body's variables, so a body cannot redeclare a
parameter (`NIR2005`) or assign one (`NIR3002`). `BindReturn` checks the value against the function's return type. The
"every path returns" rule is the simple one (D-038): a function whose return type is not `()` must have a
`BoundReturnStatement` as the last statement of its body (`NIR3007`, reported at the function name).

Slice 4 adds `ClrMethodSymbol` (replacing the old built-in symbol: a `print` overload is just one), `BoundMemberCallExpression
(Receiver, Target, Arguments)`, and two internal helpers: `ClrCandidate` and `ClrMethods`, which list the public methods of
a .NET type that Nir can call.

**Imports.** `BindImports` runs before functions are declared. A path must start `clr::cs` and name something below it
(`NIR2008`); the rest, joined with `.`, must be a public top-level type or a namespace of the framework (`NIR2009`). The
local name is the last segment; a second import of the same name is `NIR2010` and the first wins. A group
`using clr::cs::System::{A, B}` is the same as one `using` per member. Imports apply to the whole file, wherever they are.

**Qualified calls** (`A::B::Method(args)`). The first part must be an imported name (else `NIR2001`). Middle parts walk
namespaces down to a type. The last part is a public static method of that type. A missing part, a namespace where a type
is needed, or a method that is not a public static member is `NIR2011` at that part. Nested types are not supported.

**Calling .NET methods** (static and instance). Only methods whose parameters are all primitive types (the numbers, `char`, `bool`, `string`)
count as overloads (generic methods, `ref`/`out`, arrays and every other type are skipped). The overload is chosen by exact
argument types (when none matches, the overloads that match once the number literals without a suffix take their parameter's type are
tried, and exactly one must remain: `Math::Max(count, 1)` with a `long` count calls `Max(long, long)`); no overload with that argument count is `NIR2004`, the right count but no exact match is `NIR3006`. A chosen
method whose return type is not `void` or a primitive type is `NIR3011`. `receiver.Method(args)` uses the CLR
type the receiver's Nir type stands for (`string` is `System.String`, and so on) and the public instance methods of it.
`print` goes through the same routine, so it follows the same rules.

**Error recovery.** `BindExpression` never returns null. A failed expression becomes `BoundErrorExpression` with
`TypeSymbol.ErrorType`. Anything that receives an error-typed operand reports nothing more and yields an error type, so
one mistake gives one diagnostic. A variable whose initializer failed is still declared, with `ErrorType`.

**Scope.** `ScopeStack` holds one dictionary per block, innermost last, and is rebuilt for each function. The parameters and
the function body share the first scope; every nested block (`if`, `else`, `while` and `for` bodies) and every `for` loop
variable gets its own, which ends with it. A variable is visible only after its declaration; the initializer is bound before
the variable is declared, so `let x = x` does not see the new `x`. A name may not be declared if any scope of the function
already has it: `NIR2005`, worded "in this block" or "in an enclosing block", and the first declaration is kept. Blocks side by
side can reuse names. Types are looked up by name in `LookupType` (the primitive names are in `LookupPrimitive`).

**Strings and conversions.** `+` on two strings is a string; `==` / `!=` on two strings are `bool`; every other operator on
strings is `NIR3003`. An interpolated string becomes `BoundInterpolatedString(Parts)` in which every part is a string: a
hole that is not a string is wrapped in a `BoundMemberCallExpression` of its type's zero-argument `ToString()` (found with
the same `ClrMethods` lookup as other instance calls), a hole of type `()` is `NIR3012`, and a hole that already failed makes
the whole string an error with no second diagnostic. `x as T` needs `T` to be a known type (`NIR2006`), and the pair to be number to number or
`char` to or from an integer type (`CanConvert`; anything else is `NIR3001` at the cast); converting to the same type returns `x` unchanged,
otherwise the result is `BoundConversion(Operand, Target)`. Every conversion is checked (see "Slice 9c" below).

**Control flow.** `BindCondition` requires `bool` (`NIR3001` otherwise, and nothing more when the condition already failed).
`for` bounds are bound before the loop variable exists and must have the same type, an integer type or `char` (`NIR3001`; a
literal bound takes the other bound's type); the variable is an immutable variable of that type in its own scope, and the body is a nested block inside that scope, so the body cannot redeclare it. `_loopDepth` counts the loops
being bound; `break` / `continue` at depth 0 is `NIR3010`, and the depth resets for each function. `else if` is bound as an
else block holding one `if`. `!`, `&&` and `||` accept `bool` only (`NIR3003`). `BoundFlow.AlwaysReturns(block)` decides
"every path returns": the last statement is a return, or an `if` with an `else` whose two blocks both always return.

**Operator typing.** Both operands must have the same type (no implicit conversions; a number literal without a suffix first
takes the type of the other operand, `MatchLiteral`). `+ - * / %` need a number type (`NumberTypes.IsNumber`: the integers,
`float`, `double`, `decimal`) and give that type; `< <= > >=` need a number type or `char` and give `bool`; `== !=` need a number
type, `char`, `string` or `bool` and give `bool`; unary `-` needs a signed integer, `float`, `double` or `decimal`. Anything else
is `NIR3003`, reported at the start of the expression.

**Overloads.** `BuiltinFunctions` holds a list of overloads per name (`print` has one for each number type, `char`, `bool` and `string`,
each bound to the matching `Console.WriteLine`; `byte`, `sbyte`, `short` and `ushort` have none in .NET, so they use the `int` one,
which prints the same text). A user function with the same name wins. For a built-in: no overload with
the argument count gives `NIR2004`; the right count but no exact type match gives `NIR3006` at the call start.

Entry point:

```csharp
public static class Binder
{
    // Returns null if errors were reported.
    public static BoundProgram? Bind(CompilationUnit unit, SourceText source, DiagnosticBag diagnostics);
}
```

Binding is two passes: (1) declare every function so order in the file does not matter, (2) bind bodies.

**Built-in functions.** Slice 1 has exactly one: `print(string)`, bound to `Console.WriteLine(string)`.
They live in a static table `BuiltinFunctions` in `Semantics/`. From slice 2, `print` has one overload per primitive
type, each bound to the matching `Console.WriteLine` overload. Built-ins are a temporary stand-in for the `std` prelude
(design.md section 9) and move into Nir source when `std` exists.

**Unit type.** The "nothing" type is `()` (design.md section 10). In code it is `TypeSymbol.UnitType`, CLR type `void`.

### Slice 7 additions: structs, methods, borrows

`ProgramBinder` is a partial class: `ProgramBinder.cs` (state, `Bind`, declarations, function bodies), `.Statements.cs`,
`.Expressions.cs`, `.Calls.cs` (Nir and .NET calls, argument binding), `.Imports.cs`, `.Structs.cs` (declaring structs
before bodies, field types, cycle detection, literals, field reads and writes), `.Methods.cs` (`impl` blocks, `self`,
places, method and associated calls) and `.Borrows.cs` (borrow parameter types, `&x` / `&mut x`, argument fitting).

- Types. `StructTypeSymbol` (fields in declaration order, methods; it has no CLR type, Emit creates one) and
  `BorrowTypeSymbol(Element, IsMutable)`, used only for borrow parameters, receivers and borrow expressions.
- A **place** is a variable, `self`, or a chain of fields. `BoundPlaces.IsMutable(root)` decides writes: the root is
  `let mut`, `self` of a `&mut self` method, or a `&mut` borrow parameter. A by-value parameter and a `&` borrow are not
  mutable, so mutability follows the access path and there is no per-field `mut`.
- New bound nodes: `BoundStructLiteral`, `BoundFieldAccess`, `BoundFieldAssignment`, `BoundMethodCall`,
  `BoundBorrowExpression(Place, Borrow)`, `BoundDereference` (a bare use of a borrow variable, an implicit copy).
- Borrow argument rules (`ArgumentFits`): `&mut Point` accepts only `&mut p`; `&Point` accepts `&p` or `&mut p`; a plain
  `Point` parameter rejects both and the message says how to fix it. `&x` is only legal as a direct argument to a Nir
  function or method (`NIR3014` otherwise); the operand must be a whole variable (`NIR3015`) of struct type (`NIR3017`).
  Assigning to a borrow variable itself is `NIR3002` only (no second mismatch error).

### Slice 8a additions: tuples

- Syntax. `TypeSyntax` is the base of every type as written: `TypeReference` (a name, with a borrow marker only for parameters) and
  `TupleTypeSyntax`. `TupleAliasDeclaration` (`type Pair = (...)`), `TupleLiteralExpression`, `DestructuringDeclaration`. An element
  access `t.0` is a `FieldAccessExpression` whose field "name" is the digits; the lexer reads `0.1` in `t.0.1` as one decimal
  literal, so `AstBuilder.AddFieldAccesses` splits it into two accesses with exact spans.
- `TupleTypeSymbol` has its elements as `FieldSymbol`s (named by their name or their index), so reading, writing and
  places work exactly as for struct fields (`BoundFieldAccess`, `BoundFieldAssignment`). `TypeSymbol` now overloads `==`:
  types are equal when they are the same symbol, except tuples, which are equal when their element types are. Use `==`, never
  `ReferenceEquals`, to compare types.
- `ProgramBinder.Tuples.cs`: `ResolveTypeSyntax` (the one place that turns a written type into a symbol), alias declaration with
  on-demand resolution (so aliases and structs may refer to each other in any order, with a cycle check `NIR2020`),
  `BindTupleLiteral`, `BindDestructuring`. A struct cycle through a tuple is found by `FindCycle` via `StructsInside`.
- Bound nodes: `BoundTupleLiteral`, `BoundDestructuring(Variables, Initializer, Tuple)` (a `null` variable is a `_`).

### Slice 8b additions: unions and match

- Syntax. Keywords `union` and `match`, token `=>`. `UnionDeclaration(Name, Members)`, `MatchExpression(Scrutinee, Arms)`,
  `MatchArm(Name, Type, Block | Value)`. `match` is an ordinary expression in the grammar (its scrutinee is parsed with struct
  literals forbidden, its arms with them allowed again); the binder decides from the position whether it is a statement
  (`ExpressionStatement` whose expression is a `MatchExpression`) or a value.
- `UnionTypeSymbol` is nominal (one symbol per declaration) with its `Members`; the tag of a member is its position.
  `ProgramBinder.Unions.cs`: names are registered first (`DeclareUnions`), members are resolved on first use (`ResolveUnion`, with the
  same self-reference check as aliases), then `Convert(value, expected)` is the single place where a member becomes a union
  (`BoundUnionWrap`). It is used by `let` with an annotation, assignment, `return`, call arguments
  (`CheckUserCallArguments` now returns the converted arguments), struct literal fields and field assignment.
- `BindMatch`: scrutinee must be a union; each arm resolves its type and finds the member index; unknown or repeated cases are
  reported; the arm variable lives in a scope of its own; a value match checks one result type; missing members are reported
  once. `BoundMatch` has a `Body` per arm for a statement and a `Value` per arm for a value. `BoundFlow.AlwaysReturns`
  looks through a statement match whose arms all return.

### Slice 9a additions: errors

- Syntax. Keywords `error`, `try`, `catch`. `StructDeclaration.IsError` (`type E = error {..}`), `ResultTypeSyntax` (`E!T`, `E!`),
  `ErrorLiteralExpression`, `ErrorMessageExpression`, `TryExpression`, `CatchExpression` (fallback, handler expression or handler
  block). `catch` is the lowest-precedence operator and `try` / `error text` are prefix operators at the level of unary minus. The
  error-literal alternative is listed first in the `expression` rule so `error E { .. }` is not read as `error` applied to a struct
  literal. `BANG` ends a statement (`EndsStatement`) so `a: E!` followed by a newline does not swallow the next line.
- Types. An error type is a `StructTypeSymbol` with `IsError`; `AnyErrorTypeSymbol.Instance` is `anyerror` (one field, `message`);
  `ResultTypeSymbol` derives from `UnionTypeSymbol` (members `[E, T]` or `[E]`, structural `Equals`, interned by the binder, tag 0 is
  the error and `SuccessTag` is 1). Because a result is a union, `BindMatch`, `Convert` and the emitter's union code serve it
  unchanged; `Convert` adds the two widenings (declared error to `anyerror`, and into an `anyerror!T`).
- `ProgramBinder.Errors.cs`: `ResolveResultType`, `BindErrorLiteral` (shares `BindInitializers` with struct literals),
  `BindErrorMessage`, `BindTry`, `BindCatch` and `BindStatementValue`, which is how the statement forms that allow `try` (`let`, variable
  assignment, `return`, expression statement) are told apart from every other place. Error values are immutable
  (`BindFieldAssignment` refuses any path through one); errors are not borrowable and have no `impl`.
- Bound nodes: `BoundErrorLiteral`, `BoundErrorMessage`, `BoundErrorToAny`, `BoundUnitSuccess`, `BoundTry`, `BoundCatch`.

## 8. Emit (`Nir.Compiler.Emit`)

```csharp
public static class Emitter
{
    public static void Emit(BoundProgram program, string assemblyName, string outputDirectory);
}
```

Writes `<outputDirectory>/<assemblyName>.dll` and `<outputDirectory>/<assemblyName>.runtimeconfig.json`.
Creates the directory if needed. Overwrites existing files.

Approach (verified by T1.1, see section 9):
- Use `System.Reflection.Emit.PersistedAssemblyBuilder` (available since .NET 9) with core assembly
  `typeof(object).Assembly`.
- Define one public static class named `Program` in the global namespace with one public static method per Nir function,
  same name. This naming is temporary and changes when modules arrive (slice 11).
- Mark the Nir `main` function as the PE entry point:
  `PersistedAssemblyBuilder.GenerateMetadata(out ilStream, out fieldData)`, then `ManagedPEBuilder` with
  `entryPoint: MetadataTokens.MethodDefinitionHandle(mainMethod.MetadataToken)` and a
  `PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage)`, then `Serialize` to a `BlobBuilder`, then
  write the bytes to the `.dll`.
- Calls to built-ins emit `call` on the stored `MethodInfo`.
- The `.runtimeconfig.json` is needed for `dotnet x.dll` to start:
  `{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"<installed 10.x>"}}}`.
  `RuntimeConfigWriter` writes it. The version string comes from `Environment.Version` major, written as `10.0.0`.

Slice 2 additions to emit (`BodyEmitter`, a private class holding the per-function state, which is the local variables):
- Locals: `DeclareLocal` at each declaration, `stloc` / `ldloc`.
- Functions: methods are defined with the declared parameter and return CLR types. A parameter is read with `ldarg`
  (parameters are never assigned). `return value` emits the value then `ret`. The trailing `ret` is emitted only for
  `()` functions; any other function ends with its own `return`.
- Integer `+ - *` use `add.ovf` / `sub.ovf` / `mul.ovf` (overflow throws, design.md "Numbers"; widths other than `int`: see Slice 9c
  below); integer negation is `0 - x` with `sub.ovf`; `div` and `rem` throw on division by zero. `double` arithmetic uses plain `add sub mul div rem neg`.
- Comparisons use `clt cgt ceq`. `<=` and `>=` are "not greater" / "not less", using `cgt.un` / `clt.un` for `double` so a
  NaN operand makes them false. `!=` is `ceq` then not.
- The generated runtimeconfig also sets `configProperties: {"System.Globalization.Invariant": true}` so numbers print
  the same on every machine (`2.5`, never `2,5`). `Console.WriteLine(true)` prints `True` (accepted output).

Slice 6 emit additions: string `+` is `call String.Concat(string, string)`, `==` is `call String.op_Equality` (ordinal), `!=`
that followed by a not (`StringMethods` holds these `MethodInfo`s, taken from the load context). An interpolated string is
`ldstr ""` when it has no parts; for one part, the part then `ldstr ""` then `Concat(string, string)` so a null becomes
empty; for 2, 3 or 4 parts the matching `Concat` overload; above 4, a `string[]` filled with `newarr` / `stelem.ref` and
`Concat(string[])`. `int` to `double` is `conv.r8` and `double` to `int` is `conv.ovf.i4` (truncates toward zero, throws
`OverflowException` for a value that does not fit and for NaN); slice 9c generalised this to every number type.

### Slice 9c: number types, literals, characters (D-083 to D-090)

**Types.** `TypeSymbol` has one static per primitive (`ByteType` ... `CharType`), `FromClr` maps the CLR types back, and `NumberTypes`
(Semantics) is the one place that says which types are integers, signed, narrow, countable (for ranges) or ordered. The emitter asks
it too, so no phase lists types itself.

**Literals.** The lexer takes `DIGIT+` or `DIGIT+ . DIGIT+` followed by an optional `_typename` (`NUMBER_SUFFIX`) and `CHAR_LITERAL`
(anything up to the closing quote; the binder checks it is one character). `BindNumberLiteral` makes a `BoundNumberLiteral(Value, Text,
Type, CanAdapt, Span)`. A literal with a suffix has its type for good. One without is `int` / `double` with `CanAdapt` set, and
keeps its `Text` so it can be read again: `Convert` (the single place a value becomes the expected type) calls `Adapt` first, which
parses the text as the expected type (`NumberTypes.Parse`) and reports `NIR3004` if it does not fit. The other places that adapt are
`MatchLiteral` (binary operators and range bounds), `ChooseClrMethod` (.NET overloads), tuple literals converted to a tuple type, and
a union that has exactly one member the literal fits. A minus in front of a literal is folded into it (`BindUnary`), so `-128`
fits an `sbyte`. A whole number too big for `int` is held in `_pendingLiterals` until the statement is bound, because an enclosing
context may still make it a `long`; what nothing adopted is reported by `BindStatement` as `NIR3004`.

**Emit.** `BodyEmitter.Numbers.cs`: a literal is `ldc.i4` / `ldc.i8` / `ldc.r4` / `ldc.r8` (a `decimal` is built with its five-part
constructor). Integer `+ - *` are `add.ovf` / `sub.ovf` / `mul.ovf`, the `.un` form for `uint` and `ulong`, and `div.un`, `rem.un`,
`clt.un`, `cgt.un` there too. The 8 and 16-bit types are computed in 32 bits and narrowed with `conv.ovf.*` after `+ - * /`, so
`250_byte + 10_byte` throws. `float` and `double` use the plain instructions; `decimal` calls its operators (`NumberMethods`).
`as` is `NumberConversions.Emit`: `conv.ovf.<target>` (the `.un` form when an integer source is unsigned), `conv.r4` / `conv.r8`
(after `conv.r.un` from an unsigned source) for floating targets, and `Decimal`'s own conversion operators to and from `decimal`.
`catch` / `try` on an `as` use the same generated `Protected<n>` helper as .NET calls (`DefineProtectedHelper`), keyed by the pair
of types. `a..=b` runs the body, then stops when the counter equals `b` instead of adding one more, so it works for the largest
value of the type.

Slice 5 emit additions (`BodyEmitter`): a stack of `(Break, Continue)` labels for the loops being emitted.
- `if`: `brfalse` to the else label. The `br` over the else part and the end label are emitted only when the then-block can fall
  through (`BoundFlow.AlwaysReturns` is false), so a function that ends in an `if` / `else` where both branches return has no
  label pointing past its last instruction, which would be invalid IL. An `if` without `else` just marks the label.
- `while`: top label, condition, `brfalse` to the end, body, `br` to the top. `continue` jumps to the top, `break` to the end.
- `for`: the loop variable's local is the counter (safe because it is immutable), the end goes into a hidden local, both
  evaluated once, start first. The test is `bge end` (`bgt` when inclusive); `continue` jumps to the increment, which is a
  checked `+ 1`. So `a..b` with `b` = `int.MaxValue` is safe, and `a..=int.MaxValue` ends with an `OverflowException`.
- `&&` / `||` are branches (`brfalse` / `brtrue` to a label that pushes the decided constant); `!` is `ldc.i4.0; ceq`.

Slice 4 emit additions: the `PersistedAssemblyBuilder` takes its core assembly from `ClrUniverse`, and every CLR type comes
from the same source (`TypeSymbol.ClrType`, the `print` methods, the found methods). A static CLR call is `call`. A call on a
value evaluates the receiver first; a value-type receiver (`int`, `double`, `bool`) is stored in a temporary and passed by
address with `call`, a method declared on a reference type uses `callvirt` (a value-type receiver is boxed first).

Emit never reports user errors. If something it cannot emit reaches it, that is a compiler bug: throw
`InvalidOperationException` with a clear message.

### Slice 7 additions: structs, methods, borrows

- A struct becomes a public sealed CLR value type (base `System.ValueType`, sequential layout, public instance fields)
  created before function bodies; `EmitContext.Structs` maps symbol to `TypeBuilder` and `ClrOf` is the only place that
  turns a Nir type into a CLR type (`BorrowTypeSymbol` becomes `T&`, a `ref` parameter, through `MakeByRefType`).
- Instance methods are CLR instance methods of the struct (`this` is a managed pointer, parameters start at argument 1);
  associated functions are static methods of the struct; free functions stay static methods of `Program`.
- Places are emitted as addresses: `ldloca` for a local, `ldarga` for a by-value parameter, `ldarg` for a borrow (it already
  is an address), `ldflda` for each field step. Read = address + `ldfld`; write = address, value, `stfld`; borrow argument =
  the address; `BoundDereference` = `ldobj`; a struct literal is a temporary local (`initobj`, then `stfld` per field in
  source order, then `ldloc`); a call result used as a receiver goes through a temporary.

### Slice 8a additions: tuples

- A tuple is `System.ValueTuple<...>` over the CLR types of its elements (`EmitContext.ClrOf`). One `ValueTuple` holds seven;
  a longer tuple keeps its first seven and puts the rest in a `ValueTuple` of its own in the eighth type argument (`Rest`), and
  so on, so there is no limit and C# sees the usual type. A literal pushes the elements left to right, building the rest last,
  and calls the constructor (`newobj`). `EmitContext.FieldPath` gives the CLR fields to follow to an element or struct field
  (`Item1`..`Item7`, and `Rest` first for the later ones: element 9 is `Rest.Item2`); loads and stores walk the path with
  `ldflda` and finish with `ldfld` / `stfld`. `BoundDestructuring` stores the tuple in a temporary, then loads each element
  into its variable's local.
- When a tuple holds a struct (at any depth) the struct's `TypeBuilder` is a type argument. The load context's generic
  definition refuses that, so such tuples use the runtime's own `typeof(ValueTuple<,>)` definition, and members come from
  `TypeBuilder.GetField` / `TypeBuilder.GetConstructor`. Both definitions name the same type in System.Private.CoreLib.

### Slice 8b additions: unions and match

- A union is a public sealed value type with `int Tag` and one field `Case<i>` per member (`UnionLayout`). All struct and
  union types are defined before any of their fields, because fields may use types declared later (also through tuples).
  `ContainsStruct` in `EmitContext` counts unions too, so a tuple holding a union uses the runtime `ValueTuple` definition
  like one holding a struct.
- `BoundUnionWrap` builds a temporary: `initobj`, store tag, store the member field, `ldloc`. `BoundMatch` stores the scrutinee in a local;
  each arm but the last tests the tag and jumps to the next arm when it differs (the last needs no test, the binder checked
  that all members are covered); it loads the arm variable from the member field, runs its body or value, and jumps to the end.
  A value match stores each arm's value in a result local so the stack is the same on every path. An arm that always returns gets
  no jump, so a function that ends in such a match has no jump target past its last instruction.

### Slice 9a additions: errors

- `Nir.AnyError` is a generated class with a public `Message` string field; every declared error type is a sealed class
  deriving from it (public fields, a constructor that calls the base one), so any error is an `anyerror` with no conversion code.
  Defined once per program, created before the error classes. A field access on an error loads the object reference
  (`EmitAddress` returns the reference for error types instead of the address of the variable).
- `BoundErrorLiteral`: `newobj`, store the fields in the order written, then set `Message` from the field called `message` or the type
  name. A result type is emitted by the union code (`Tag`, `Case0` the error, `Case1` the value; `Nir.Result<n>` names).
- `BoundTry` stores the operand, tests the tag, and on an error returns (a widened copy when the function's error type differs) with
  `ret`; on a success it loads `Case1`. `BoundCatch` stores the operand and runs the handler on an error, with the result in a local
  so the stack is the same on every path. A function returning `E!` that falls off the end succeeds (`EmitUnitSuccess`).
- **.NET calls under `try` / `catch` (slice 9b).** The binder wraps the bound call in `BoundProtectedCall` typed `anyerror!T` (`T` is the
  method's return type, or no value for `void`), so `BindTry` and `BindCatch` need no other change. Emit does not open a CLR `try` block
  at the call site (it needs an empty stack, and the call can be inside an expression): it pushes the receiver and arguments, then calls a
  generated static helper `Protected<n>` on `Program` (one per .NET method, cached in `EmitContext.ProtectedHelpers`) that makes the
  call inside a `try` block, stores the value and the success tag, and in `catch (System.Exception)` stores a new `Nir.AnyError` whose
  `Message` is `ex.Message`. So only the call is protected, not the evaluation of its arguments.
- A `main` returning `E!` gets a generated entry `Main` returning `int`: it calls the user's `main`, prints `error: <message>` to
  `Console.Error` for an error and returns 1, otherwise 0. Other entry points are unchanged.

## 9. Emit findings

Verified by the T1.1 spike on 2026-11-01 (SDK 10.0.114, runtime 10.0.14, Linux x64). The approach in section 8 works.

1. `dotnet hello.dll` prints `hello` and exits 0.
2. `runtimeconfig.json` is **required**. Without it `dotnet hello.dll` fails with exit code 131 ("libhostpolicy.so
   required to execute the application was not found"; it treats the app as self-contained). This content works:
   `{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}`
   (`tfm` = `net` + runtime major.minor, `version` = major.minor + `.0`, both from `Environment.Version`).
3. Core assembly passed to `PersistedAssemblyBuilder`: `typeof(object).Assembly` (System.Private.CoreLib). The output
   references `System.Private.CoreLib` and `System.Console` (where `Console` is found through `typeof(Console)`).
   This ties the output to the implementation assemblies of the running runtime, which is fine for same-machine runs.
   Revisit if outputs must run on other runtime builds (slice 4 may switch to reference assemblies).
4. `typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])` emitted with `OpCodes.Call` works.
5. Gotchas:
   - `MethodBuilder.MetadataToken` is valid after `type.CreateType()` and `GenerateMetadata(...)`; use it for the entry
     point as in section 8.
   - `PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage)` is enough; no `Dll` flag needed.
   - Writing the runtimeconfig JSON as a C# raw string literal fails to compile because of `}}}`. Build it with
     `System.Text.Json` or string concatenation (`RuntimeConfigWriter`).
   - Directory.Build.props makes warnings errors; a throwaway project can opt out with
     `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`, the real compiler cannot.

Verified by the slice 4 spike (2026-11-01), for `System.Reflection.MetadataLoadContext` 10.0.0:

6. A `MetadataLoadContext` over every `*.dll` in `RuntimeEnvironment.GetRuntimeDirectory()`, with core assembly
   `System.Private.CoreLib`, finds framework types and methods, and `PersistedAssemblyBuilder` accepts its types and methods:
   the program reads a file, calls an instance method and `Math.Sqrt`, and runs.
7. **Every CLR type used while emitting must come from the load context.** Using the compiler's own `typeof(void)` for the
   entry point's return type made the program fail to start ("Entry point must have a return type of void, integer, or
   unsigned integer"). So `ClrUniverse` is the only source of CLR types: `TypeSymbol`, the built-in `print` methods and the
   found methods all use it. Never write `typeof(...)` for a type that reaches the emitter.
8. The types are found without loading every assembly: `ClrUniverse` reads each file's metadata tables with
   `System.Reflection.Metadata` to index public top-level types and namespaces, then loads only the assembly that defines a
   type it is asked for. The emitted program references the implementation assemblies (for example `System.Private.CoreLib`,
   `System.Console`), which is fine for same-machine runs.
9. The load context is not assumed to be thread safe, so `NirCompiler.Compile` and `Emit` hold one process-wide lock
   (`ClrUniverse.Gate`). Tests run in parallel and shared the context without trouble under that lock.

## 10. Public facade (`Nir.Compiler.NirCompiler`)

The CLI and tests use only this and the `Diagnostics` types:

```csharp
public sealed record CompilationResult(BoundProgram? Program, DiagnosticBag Diagnostics)
{
    public bool Succeeded => Program is not null && !Diagnostics.HasErrors;
}

public static class NirCompiler
{
    public static CompilationResult Compile(SourceText source);                                   // parse + bind
    public static void Emit(CompilationResult result, string assemblyName, string outputDirectory); // result.Succeeded must be true
}
```

`Compile` runs Parse; if errors, stops. Then Bind. Then returns.

## 11. CLI (`Nir.Cli`, executable `nir`)

```
nir build <file.nir> [-o <dir>]     compile; write <name>.dll and runtimeconfig to <dir> (default: ./out)
nir run <file.nir> [-- args...]     compile to a temp directory, then run it with `dotnet <name>.dll`
```

- `<name>` is the file name without extension. Allowed characters: letters, digits, `_`, `-`. Otherwise exit code 2.
- `run` starts a child process `dotnet <temp>/<name>.dll` with stdin/stdout/stderr inherited, and returns the child's
  exit code. The temp directory is deleted afterwards. In-process loading is deliberately not used (isolated, simpler to
  test); it can be an optimization later.
- Diagnostics go to stderr, in the printed form from section 4 (Rust style, coloured on a terminal). Program output is
  never mixed with them.
- Exit codes: `0` success; `1` compile errors; `2` usage error (bad arguments, file not found); `3` internal compiler
  error (unhandled exception; print `internal compiler error: <message>` and the stack trace to stderr).
- Argument parsing is hand-written in `CommandLineArguments.cs`; no third-party package.
- Files: `Program.cs` (dispatch + top-level exception handler), `CommandLineArguments.cs`, `Commands.cs` (`Build` and
  `Run`), `DiagnosticPrinter.cs`, `ExitCodes.cs`.
