# Code map

Where everything is and what it does. `architecture.md` explains the design in depth; this file is the guided tour: read it
top to bottom once, then use it as an index. File sizes are approximate.

## 1. The whole thing in one picture

```
 source text (.nir)
      |
      |  Parsing/   ANTLR lexer + parser  ->  parse tree          (ANTLR is confined to this folder)
      v
 Syntax tree (AST)         immutable records, our own types        Syntax/
      |
      |  Semantics/ ProgramBinder: names, types, rules, diagnostics
      v
 Bound tree                typed, every name resolved to a symbol  Semantics/BoundNodes.cs
      |
      |  Emit/      Emitter: System.Reflection.Emit -> IL
      v
 program.dll + program.runtimeconfig.json     run with `dotnet program.dll`
```

Diagnostics (errors) are collected in a `DiagnosticBag` by every phase and printed by the CLI. A program with any error is never emitted.

Two projects: `src/Nir.Compiler` (the whole compiler, a library) and `src/Nir.Cli` (the `nir` command). Tests are in
`tests/Nir.Tests`. `NirCompiler` (one file) is the only door into the library.

## 2. `src/Nir.Cli` - the command line

| File | What it does |
|---|---|
| `Program.cs` | `Main`: parses arguments, runs the command, turns exceptions into exit code 3. |
| `CommandLineArguments.cs` | Hand-written argument parser for `nir build|run <file> [-o dir]`. |
| `Commands.cs` | `Build` and `Run`: read the file, call `NirCompiler`, print diagnostics, emit, start `dotnet` for `run`. |
| `DiagnosticPrinter.cs` | The Rust-style error report (source line, carets, colours). |
| `TerminalColor.cs` | Decides whether to use colour (`NO_COLOR`, `CLICOLOR_FORCE`, is stderr a terminal). |
| `ExitCodes.cs` | 0 ok, 1 compile error, 2 usage error, 3 internal error. |

## 3. `src/Nir.Compiler` - the compiler

### Entry point and shared pieces

| File | What it does |
|---|---|
| `NirCompiler.cs` | The public facade: `Compile(SourceText)` -> `CompilationResult` (diagnostics + bound program); `Emit(result, name, dir)`. Holds the global lock around the CLR universe. |
| `Diagnostics/SourceText.cs` | Source name + text; converts a position to line/column; gets a line's text. |
| `Diagnostics/TextSpan.cs`, `Diagnostic.cs`, `Severity.cs` | A range in the source, one reported problem, error/warning. |
| `Diagnostics/DiagnosticBag.cs` | The list every phase reports into. |
| `Diagnostics/DiagnosticCodes.cs` | Every `NIR####` code as a constant with a one-line meaning. Codes are never reused. |
| `Clr/ClrUniverse.cs` | The .NET types the compiler knows, read from the runtime's assemblies through a `MetadataLoadContext` (no BCL code runs). **Every CLR type used while emitting must come from here, never from `typeof(...)`.** |
| `Clr/ClrTypes.cs` | The handful of .NET types and methods used constantly (`Int32`, `String`, `Exception`, `Console.Error`, ...), each looked up once. |
| `Clr/ClrNames.cs`, `Clr/GeneratedNames.cs` | Constants: full names of .NET types, and the names of the types/fields/methods the compiler generates (`Nir.AnyError`, `Tag`, `Case0`, `Protected3`, ...). |
| `Language/TypeNames.cs`, `Punctuation.cs`, `ReservedNames.cs`, `ImportRoot.cs` | Constants for the words of Nir (`"int"`, `"()"`, `"::"`, `"&mut"`, `self`, `main`, `clr::cs`). A global using makes them available in every file; no code spells these as literals. |

### `Parsing/` - text to syntax tree (the only folder that knows ANTLR)

| File | What it does |
|---|---|
| `NirLexer.g4` | Lexer grammar: keywords, operators, literals, string modes (plain and `$"..."`). |
| `NirParser.g4` | Parser grammar: declarations, statements, the `expression` rule (precedence = order of alternatives), types. Has a small C# block for the "struct literal not allowed in `if`/`while` heads" flag. |
| `NirLexerBase.cs` | Base class of the generated lexer. Turns newlines into `;` when a statement can end there (`EndsStatement`), tracks `{ }` holes in interpolated strings, repairs unterminated strings. |
| `SyntaxParser.cs` | `Parse(source, diagnostics)`: runs lexer + parser + `AstBuilder`, keeps only the **first** syntax error. |
| `ParserErrorListener.cs` | Turns ANTLR error callbacks into `NIR1001` diagnostics with friendly wording. |
| `AstBuilder.cs` | Converts the ANTLR parse tree into our own syntax records. The only code that reads ANTLR contexts. Tolerates broken trees after a syntax error. |
| `StringEscapes.cs` | Decodes `\n`, `\"`, `{{` ... in string text and reports unknown escapes. |

### `Syntax/` - the AST (plain immutable records, no logic)

| File | What it has |
|---|---|
| `SyntaxNode.cs` | `SyntaxNode` base (every node has a span), `Identifier`, `QualifiedName`. |
| `Declarations.cs` | `CompilationUnit`, `FunctionDeclaration`, `StructDeclaration` (also error types), `UnionDeclaration`, `TupleAliasDeclaration`, `ImplDeclaration`, `UsingDirective`, `Parameter`, and the **type syntax**: `TypeReference`, `TupleTypeSyntax`, `ResultTypeSyntax` (`E!T`). |
| `Statements.cs` | `let`, destructuring `let (a, b)`, `return`, `if`, `while`, `for`, `break`, `continue`, assignment. |
| `Expressions.cs` | Literals, names, calls, member calls, field access, struct/error/tuple literals, `match`, `try`, `catch`, borrows `&x`, interpolation, casts. |
| `Operators.cs` | Binary/unary operator enums and their source text. |

### `Semantics/` - the binder (meaning, types, rules). The biggest brain.

`ProgramBinder` is one **partial class** split by topic. It walks the AST, resolves names, checks types, reports diagnostics and
builds the bound tree. `Binder.cs` is the tiny static entry (`Binder.Bind(unit, source, diagnostics)`).

| File | What it does |
|---|---|
| `ProgramBinder.cs` | State, the order of passes (`Bind`), declaring functions, binding bodies, scopes of blocks, the statement dispatcher. |
| `.Statements.cs` | `if`/`while`/`for`/`return`/`let`/assignment; **type lookup** (`LookupType`, `LookupPrimitive`). |
| `.Expressions.cs` | The expression dispatcher, literals, operators, casts (`as`), interpolation. |
| `.Calls.cs` | Calls of Nir functions, .NET static and instance methods, overload choice; `CheckUserCallArguments`. |
| `.Imports.cs` | `using clr::cs::...`: resolves namespaces and types in the CLR universe. |
| `.Structs.cs` | Struct and error declarations, fields, cycle check, struct/error literals (`BindInitializers`), field reads and writes. |
| `.Methods.cs` | `impl` blocks, `self`, places, method and associated-function calls, mutability of receivers. |
| `.Borrows.cs` | `&T` / `&mut T` parameters and `&x` arguments. |
| `.Tuples.cs` | Type syntax resolution (`ResolveTypeSyntax`), tuple aliases, tuple literals, destructuring. |
| `.Unions.cs` | Union declarations, **`Convert`** (the one place a value becomes a union), `match`. |
| `.Errors.cs` | `E!T` types, `error` values, `try`, `catch`, protected .NET calls. |
| `TypeSymbol.cs` | Base type: `int`, `double`, `bool`, `string`, `()`, error type. **`==` compares types** (tuples and results structurally). |
| `StructTypeSymbol.cs` (+ `FieldSymbol`) | Struct or error type, its fields and methods. |
| `TupleTypeSymbol.cs`, `UnionTypeSymbol.cs`, `ResultTypeSymbol.cs`, `AnyErrorTypeSymbol.cs`, `BorrowTypeSymbol.cs` | The other kinds of types. A result type *is* a union (`E!T` = union of `E` and `T`). |
| `FunctionSymbol.cs` | `ClrMethodSymbol` (a .NET method or `print`) and `UserFunctionSymbol` (Nir function/method). |
| `VariableSymbol.cs`, `ScopeStack.cs` | Local variables and the stack of block scopes. |
| `BoundNodes.cs` | **The bound tree**: what Emit reads. One record per construct (`BoundIfStatement`, `BoundMatch`, `BoundTry`, ...). |
| `BoundPlaces.cs` | "Is this expression an assignable place, and from which variable?" |
| `BoundFlow.cs` | "Does every path return?" (also looks through `match`). |
| `BuiltinFunctions.cs`, `ClrMethods.cs` | `print` overloads; finding and matching .NET methods by name and argument types. |

### `Emit/` - bound tree to IL

| File | What it does |
|---|---|
| `Emitter.cs` | `Emitter.Emit`: the order of work - define types, declare methods, write bodies, write the PE file. |
| `Emitter.Definitions.cs` | Defining the CLR types: the `Nir.AnyError` class, struct and error classes, union layouts and their fields, and the `Main` wrapper for `-> E!`. |
| `BodyEmitter.cs` | One instance per function: locals, argument slots, `EmitBlock`, `EmitStatement`. The other `BodyEmitter.*.cs` files are the same partial class split by topic: |
| `BodyEmitter.Expressions.cs` | `EmitExpression` (the big switch) and `EmitAddress` (places). |
| `BodyEmitter.ControlFlow.cs` | `if`, `while`, `for`, `&&` / `||`. |
| `BodyEmitter.Operators.cs` | Arithmetic and comparison, string `+` / `==`, interpolation. |
| `BodyEmitter.Calls.cs` | Calls into .NET (`EmitMemberCall`). |
| `BodyEmitter.Aggregates.cs` | Struct and tuple literals, field loads, destructuring. |
| `BodyEmitter.Unions.cs` | Wrapping a value into a union, `match`. |
| `BodyEmitter.Errors.cs` | `error` values, `try`, `catch`, success of an `E!`. |
| `BodyEmitter.Protected.cs` | The generated `Protected<n>` helpers that run a .NET call inside a CLR try block. |
| `EmitContext.cs` | Shared emit state: maps from symbols to `TypeBuilder`/`MethodBuilder`/`FieldBuilder`, `ClrOf(type)` (the one place Nir types become CLR types), `FieldPath`, `ValueTuple` handling (nested `Rest` past 7), caches. |
| `StringMethods.cs` | The `String.Concat` and `op_Equality` methods the emitter calls for string `+`, `==`, interpolation. |
| `RuntimeConfigWriter.cs` | Writes `program.runtimeconfig.json` (invariant globalization). |

## 4. Tests (`tests/Nir.Tests`)

Rule: unit tests per phase + end-to-end tests that compile and run real programs.

| Folder | Tests |
|---|---|
| `Lexing/` | `LexerTests`: token streams (`TokenFormatter` prints them). |
| `Parsing/` | `ParserTests`, `TupleParserTests`, `UnionParserTests`, `ErrorParserTests` (trees via `AstFormatter`), string escapes, syntax-error wording. |
| `Semantics/` | One binder test file per feature: expressions, control flow, strings, imports, member calls, structs, methods, borrows, tuples, unions, errors, .NET errors. Assertions use `"NIR3001 5:13"` = code, line:column. |
| `Emit/` | End-to-end: compile with `TestHost.Run`, run `dotnet`, compare output. One file per feature. |
| `Programs/sliceN/` | Golden programs: `name.nir` + `name.stdout` (must run and print that) or `name.diag` (must fail with these `line:col CODE`s). Add a program = add two files, no C#. |
| `Cli/`, `Clr/`, `Diagnostics/` | Printer, argument parsing, the CLR universe, source text. |
| `ArchitectureTests.cs` | Enforces the dependency rules (ANTLR only in `Parsing`; Emit does not use Syntax; ...). |
| `Support/` | `TestHost`, `ProcessRunner`, `TokenFormatter`, `AstFormatter`, `BinderHarness` (the shared `Bind` helper of the binder tests). |

## 5. Other folders

| Path | What |
|---|---|
| `docs/` | `design.md` (the language), `architecture.md` (the compiler in depth), `practices.md`, `roadmap.md` (slices and tasks), `decisions.md` (every decision, D-001...), this file. |
| `samples/` | Programs to run by hand; `samples/errors/` shows each kind of error message; `samples/crashes/` shows runtime failures. |
| `editors/vscode-nir/` | VS Code syntax highlighting (TextMate grammar). |
| `CLAUDE.md` | Entry point and status for work sessions. |

## 6. Following one feature through the code

`let (a, mut b) = pair` (tuple destructuring):

1. `NirParser.g4`: rule `destructuringDeclaration`.
2. `AstBuilder.BuildDestructuring` makes a `DestructuringDeclaration` (Syntax/Statements.cs).
3. `ProgramBinder.BindStatement` -> `BindDestructuring` (`.Tuples.cs`): binds the right side, checks it is a tuple with that many elements, declares one `VariableSymbol` per name, returns a `BoundDestructuring`.
4. `Emitter.BodyEmitter.EmitDestructuring`: stores the tuple in a local, loads each element through `EmitContext.FieldPath`.
5. Tests: `TupleParserTests`, `TupleBinderTests`, `TupleTests` (end to end), `Programs/slice8/destructuring.nir`.

Adding any feature follows the same path: grammar -> AST record -> `AstBuilder` -> binder method (+ diagnostic code) ->
bound node -> emitter case -> tests -> docs.

## 7. Where to start reading

1. `NirCompiler.cs` (50 lines): the whole pipeline in three calls.
2. `Syntax/Expressions.cs` and `Semantics/BoundNodes.cs`: the two vocabularies.
3. `NirParser.g4`, then `AstBuilder.cs` (skim).
4. `ProgramBinder.cs`, then `.Statements.cs`, `.Expressions.cs`.
5. `Emitter.cs`: start at `Emit(...)`, then `EmitStatement` / `EmitExpression`.

## 8. Cleanup done and what is left

Done: `Emitter.cs` is split into partial files by topic (the largest is now about 230 lines); every type name, punctuation, reserved
name, .NET name and generated name is a constant (see `docs/practices.md`, "Names and literals"); `ClrTypes` looks up the .NET
members once; `ClrMethods` caches the methods of a .NET type and the candidates for a name (reflection over `Console` or `String` was
repeated for every call); tuple CLR types and element paths are cached in `EmitContext`; the shared test `Bind` helper moved to
`Support/BinderHarness.cs`.

Left (not urgent):

- `AstBuilder.cs` repeats the "build a list, bail if any element is null" pattern; a small helper would shorten it.
- `ImportBinderTests` still has its own `Bind` helper (it differs slightly), and `BinderTests` / `MemberCallBinderTests` use older shapes.
- Compile time is dominated by starting the CLR universe (`MetadataLoadContext` index of all runtime assemblies); it is built lazily, once per process.
