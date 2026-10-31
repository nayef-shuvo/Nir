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
Nir2/
├── Nir.slnx
├── global.json, Directory.Build.props, .editorconfig, .gitignore
├── CLAUDE.md, README.md
├── docs/                       design.md, architecture.md, practices.md, roadmap.md, decisions.md
├── samples/                    example .nir programs (hello.nir, ...)
├── reference/                  old ANTLR files, read-only reference. Not compiled. Delete when no longer needed.
├── src/
│   ├── Nir.Compiler/           the whole compiler, one project
│   │   ├── Diagnostics/
│   │   ├── Parsing/            *.g4, NirLexerBase.cs, AstBuilder.cs, ParserErrorListener.cs, SyntaxParser.cs
│   │   ├── Syntax/             AST records
│   │   ├── Semantics/          Binder, symbols, types, bound tree
│   │   ├── Emit/               Emitter, RuntimeConfigWriter
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
`Nir.Compiler.Semantics`, `Nir.Compiler.Emit`. The facade `NirCompiler` is in `Nir.Compiler`.
The CLI is `Nir.Cli`. Tests are `Nir.Tests.<Folder>`.

## 3. Dependency rules

- `Diagnostics` depends on nothing.
- `Syntax` depends on `Diagnostics` (for `TextSpan`) only.
- `Parsing` depends on `Syntax`, `Diagnostics`, and ANTLR.
- `Semantics` depends on `Syntax`, `Diagnostics`. Never on `Parsing`.
- `Emit` depends on `Semantics`, `Diagnostics`. Never on `Syntax` or `Parsing`.
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

Printed form (the CLI prints exactly this, one line per diagnostic, to stderr):

```
samples/bad.nir:3:5: error NIR2001: 'prnt' is not defined
```

That is `<source name>:<line>:<column>: <severity> <code>: <message>`. Severity is lowercase `error` or `warning`.

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

Codes defined so far (slices 1 and 2):

| Code | Meaning | Message template |
|---|---|---|
| NIR0001 | unexpected character | `unexpected character '{c}'` |
| NIR0002 | unterminated string literal | `unterminated string literal` |
| NIR1001 | syntax error (any parser error) | the text from the parser, see section 5 |
| NIR2001 | name not defined | `'{name}' is not defined` |
| NIR2002 | missing entry point | `program has no 'main' function` |
| NIR2003 | duplicate function | `function '{name}' is already defined` |
| NIR2004 | wrong argument count | `'{name}' expects {n} argument(s) but got {m}` |
| NIR2005 | name declared twice in one block | `'{name}' is already declared in this block` |
| NIR2006 | unknown type in an annotation | `unknown type '{name}'` |
| NIR3001 | type mismatch | `cannot convert '{from}' to '{to}'` |
| NIR3002 | assignment to a non-`mut` variable | `cannot assign to '{name}' because it is not mutable` |
| NIR3003 | operator not defined for these types | `operator '{op}' cannot be applied to '{left}' and '{right}'` (unary: `... to '{type}'`) |
| NIR3004 | literal out of range | `'{text}' is out of range for '{type}'` |
| NIR3005 | variable would have type `()` | `cannot declare '{name}' with type '()'` |
| NIR3006 | no overload accepts the argument types | `no overload of '{name}' accepts ({types})` |

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
bad input.

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
`IDENTIFIER` in the grammar (ANTLR resolves same-length ties by rule order). Keywords so far: `fn`, `let`, `mut`, `true`, `false`.

**Strings.** Plain strings use lexer modes: `STRING_START`, `STRING_TEXT`, `STRING_END`. The lexer keeps raw text;
escape sequences are decoded by `AstBuilder` (from slice 5). In slice 1 a `StringLiteralExpression.Value` is the raw text
between the quotes. A backslash and the next character are consumed together (so `\"` does not end the string) but are
kept undecoded.

A newline or the end of the file inside a string reports `NIR0002` once, at the opening quote. The lexer then repairs
the token stream (turns the newline, or a synthesized token at end of file, into a `STRING_END`) so the parser sees a
complete string and does not add errors about it. Note the newline is consumed by the string, so a later syntax error
about a missing `)` can still follow.

**Error messages.** `ParserErrorListener` reports every ANTLR syntax error as `NIR1001` with ANTLR's own message text,
lowercased first letter, and the span of the offending token. Lexical errors use `NIR0001`/`NIR0002`. Improving
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

**Error recovery.** `BindExpression` never returns null. A failed expression becomes `BoundErrorExpression` with
`TypeSymbol.ErrorType`. Anything that receives an error-typed operand reports nothing more and yields an error type, so
one mistake gives one diagnostic. A variable whose initializer failed is still declared, with `ErrorType`.

**Scope.** One flat dictionary of variables per function body, reset for each function. A variable is visible only after
its declaration; the initializer is bound before the variable is declared, so `let x = x` does not see the new `x`.
Redeclaring a name reports `NIR2005` and keeps the first declaration. Types are looked up by name in `LookupType`
(`int`, `double`, `bool`, `string`).

**Operator typing.** Both operands must have the same type (no implicit conversions). `+ - * / %` need `int` or `double`
and give that type; `< <= > >=` need `int` or `double` and give `bool`; `== !=` need `int`, `double` or `bool` and give
`bool`; unary `-` needs `int` or `double`. Anything else is `NIR3003`, reported at the start of the expression.

**Overloads.** `BuiltinFunctions` holds a list of overloads per name (`print` has `int`, `double`, `bool`, `string`,
each bound to the matching `Console.WriteLine`). A user function with the same name wins. For a built-in: no overload with
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
  same name. This naming is temporary and changes when modules arrive (slice 9).
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
- Integer `+ - *` use `add.ovf` / `sub.ovf` / `mul.ovf` (overflow throws, design.md "Numbers"); integer negation is `0 - x`
  with `sub.ovf`; `div` and `rem` throw on division by zero. `double` arithmetic uses plain `add sub mul div rem neg`.
- Comparisons use `clt cgt ceq`. `<=` and `>=` are "not greater" / "not less", using `cgt.un` / `clt.un` for `double` so a
  NaN operand makes them false. `!=` is `ceq` then not.
- The generated runtimeconfig also sets `configProperties: {"System.Globalization.Invariant": true}` so numbers print
  the same on every machine (`2.5`, never `2,5`). `Console.WriteLine(true)` prints `True` (accepted output).

Emit never reports user errors. If something it cannot emit reaches it, that is a compiler bug: throw
`InvalidOperationException` with a clear message.

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
- Diagnostics go to stderr, in the printed form from section 4. Program output is never mixed with them.
- Exit codes: `0` success; `1` compile errors; `2` usage error (bad arguments, file not found); `3` internal compiler
  error (unhandled exception; print `internal compiler error: <message>` and the stack trace to stderr).
- Argument parsing is hand-written in `CommandLineArguments.cs`; no third-party package.
- Files: `Program.cs` (dispatch + top-level exception handler), `CommandLineArguments.cs`, `Commands.cs` (`Build` and
  `Run`), `DiagnosticPrinter.cs`, `ExitCodes.cs`.
