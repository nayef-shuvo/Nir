# Roadmap

Work is divided into **slices**. A slice takes one small group of language features through every compiler phase
(lexer, parser, binder, emitter, CLI) until programs using them run. Slices are done in order. A slice is done only
when all its acceptance programs pass.

How to use this file:
- Take the first unchecked task `[ ]` in the lowest-numbered unfinished slice. Tasks inside a slice are in dependency order.
- Slice 1 is fully broken down into tasks. Slices 2 and later are specified (language scope, decisions needed,
  acceptance programs) but are **broken into tasks only when the slice starts**: write them as `T<n>.<m>` entries here,
  in the same format as slice 1, before coding.
- **Decisions required** are language questions that `design.md` does not settle. Ask the user, record the answer in
  `design.md` and `decisions.md`, then code. Never choose silently.

Status key: `[ ]` not started, `[x]` done.

---

## Slice 1: hello world (walking skeleton)

**Goal.** Prove the whole pipeline: source file in, running .NET program out.

**Language scope (all new, nothing else exists yet).**

```
// comments run to end of line
fn greet() {
    print("hi")
}

fn main() {
    print("hello")
    greet()
}
```

- Top-level function declarations: `fn name() { ... }`. No parameters, no return type (returns `()`).
- Statements: an expression statement, ended by a newline or `;`.
- Expressions: a call `name(args)` and a string literal `"text"`.
- Built-in `print(string)`: writes the string and a newline to stdout.
- A program needs exactly one function named `main`. Functions can be declared in any order.
- Call to a user function with zero arguments is allowed (this checks forward references).
- Escapes are not decoded yet: the raw text between the quotes is kept, backslashes included. (A backslash still
  protects the next character, so `\"` does not end the string.)
- Out of scope: variables, numbers, parameters, `return`, operators, control flow, types, interpolation.

**Grammar for this slice** (the parser implements exactly this):

```
compilationUnit     : terminator? (functionDeclaration (terminator functionDeclaration)*)? terminator? EOF ;
functionDeclaration : FN name=IDENTIFIER LPAREN RPAREN block ;
block               : LBRACE statementList RBRACE ;
statementList       : terminator? (statement (terminator statement)*)? terminator? ;
statement           : expression ;
expression          : callExpression | stringLiteral ;
callExpression      : callee=IDENTIFIER LPAREN (expression (COMMA expression)* COMMA?)? RPAREN ;
stringLiteral       : STRING_START STRING_TEXT? STRING_END ;
terminator          : SEMI+ ;
```

Tokens: `FN` (`fn`), `LPAREN` `RPAREN` `LBRACE` `RBRACE` `COMMA` `SEMI`, `IDENTIFIER`
(`[a-zA-Z_][a-zA-Z0-9_]*`), string tokens in a lexer mode, whitespace and `//` comments skipped.

**Acceptance (all must be true):**

1. `dotnet run --project src/Nir.Cli -- run samples/hello.nir` prints `hello` then `world`, exit code 0.
2. All program tests in `tests/Nir.Tests/Programs/slice1/` pass (list in T1.8).
3. `dotnet build` has 0 warnings, 0 errors. `dotnet test` is fully green.
4. `docs/architecture.md` section 9 contains the verified emit findings.

### Tasks

- [x] **T1.0 Scaffold the solution**
  - Create `Nir.slnx` listing the three projects (`src/Nir.Compiler`, `src/Nir.Cli`, `tests/Nir.Tests`) in folders `/src/`
    and `/tests/` (same format as the old repo's `Nir.slnx`).
  - `src/Nir.Compiler/Nir.Compiler.csproj`: class library; `PackageReference` `Antlr4.Runtime.Standard` 4.13.1 and
    `Antlr4BuildTasks` 12.14.0. Add the two `<Antlr4 Include=...>` items from `architecture.md` section 5, with minimal
    grammar files (`lexer grammar NirLexer;` with token `FN: 'fn';`, and `parser grammar NirParser; options { tokenVocab = NirLexer; } start : FN EOF ;`).
  - `src/Nir.Cli/Nir.Cli.csproj`: `OutputType` Exe, `AssemblyName` `nir`, `ProjectReference` to the compiler.
    `Program.cs` prints `nir` and returns 0.
  - `tests/Nir.Tests/Nir.Tests.csproj`: packages `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3,
    `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4; `<Using Include="Xunit" />`; `IsPackable` false;
    project references to compiler and CLI. One test `Solution_builds` that asserts `true`.
  - Create empty folders by putting real files in them only when a task needs them. Do not add placeholder files.
  - Run `dotnet build` once, alone, with a long timeout (first build downloads the JRE if `~/.jre` is missing; it
    exists on this machine). Then `dotnet test`.
  - **Check the ASSUMPTION** in `architecture.md` section 5 (tokenVocab with both grammars in one project). Record the
    result in `decisions.md`.
  - **Done when:** `dotnet build` → 0 warnings, 0 errors; `dotnet test` → 1 passed; the generated `NirLexer`/`NirParser`
    classes exist under `obj/`; `dotnet run --project src/Nir.Cli` prints `nir`.

- [x] **T1.1 Emit spike (throwaway)**
  - Purpose: verify the emit approach before any compiler code depends on it.
  - Create `spikes/EmitHello/` (console project, **not** in `Nir.slnx`) that uses `PersistedAssemblyBuilder` to
    write `hello.dll` (class `Program`, static method `main` that calls `Console.WriteLine("hello")`, set as PE entry
    point) and `hello.runtimeconfig.json`, exactly as described in `architecture.md` section 8.
  - Run it, then run `dotnet hello.dll`.
  - Answer and record in `architecture.md` section 9 (replace `(empty)`), as short bullet points:
    1. Does `dotnet hello.dll` print `hello` with exit code 0?
    2. Exact `runtimeconfig.json` content that works (and whether it works without it).
    3. Which core assembly argument was used for `PersistedAssemblyBuilder`, and the assembly references in the output.
    4. How the `Console.WriteLine(string)` `MethodInfo` was obtained and that it emits correctly.
    5. Any gotchas (API quirks, warnings, version issues).
  - If the approach does not work, stop and report to the user with the error. Do not switch to another approach silently.
  - Afterwards, delete `spikes/`.
  - **Done when:** all five answers are written in section 9, `spikes/` is gone, and section 8 is edited to match reality.

- [x] **T1.2 Diagnostics infrastructure** (`src/Nir.Compiler/Diagnostics/`)
  - Files: `SourceText.cs`, `TextSpan.cs`, `Diagnostic.cs`, `Severity.cs`, `DiagnosticBag.cs`, `DiagnosticCodes.cs`,
    all as specified in `architecture.md` section 4. `DiagnosticCodes` contains the seven codes of slice 1.
  - Line rule: a line ends at `\n`. A `\r` immediately before `\n` belongs to the terminator (not the line text, not
    the column count). Lines and columns are 1-based. A position equal to `Text.Length` is valid (end of file).
  - Tests (`tests/Nir.Tests/Diagnostics/`), with these exact cases:
    - `Position_zero_is_line_1_column_1`
    - `Position_after_newline_is_next_line_column_1` (`"ab\ncd"`, position 3 → 2:1)
    - `Position_in_middle_of_line` (`"ab\ncd"`, position 4 → 2:2)
    - `Carriage_return_before_newline_is_part_of_terminator` (`"ab\r\ncd"`, position 4 → 2:1; `GetLineText(1)` is `ab`)
    - `Position_at_end_of_file_is_valid` (`"ab"`, position 2 → 1:3)
    - `Last_line_without_newline` (`GetLineText(2)` of `"ab\ncd"` is `cd`)
    - `Bag_reports_errors_but_not_warnings_as_HasErrors`
  - **Done when:** those seven tests pass.

- [x] **T1.3 Lexer** (`Parsing/NirLexer.g4`, `Parsing/NirLexerBase.cs`)
  - Write the slice-1 lexer grammar (tokens listed above) based on `reference/NirLexer.g4`: default mode plus the
    `PLAIN_STRING` mode (`STRING_START`, `STRING_TEXT`, `STRING_END`). Do not port the interpolation modes, `var`/`const`
    or other tokens; they are added in the slices that need them. `superClass = NirLexerBase`.
  - Port `NirLexerBase` from `reference/NirLexerBase.cs`. `EndsStatement` for this slice: `IDENTIFIER`, `STRING_END`,
    `RPAREN`, `RBRACE`.
  - (As built: lexical errors are reported inside `NirLexerBase`, not in a separate listener.) Report lexical errors as `NIR0001` (bad character, using the span of that
    character) and `NIR0002` (a newline or end of file reached inside a string; span starts at the opening quote).
    For a string that is missing its closing quote, report `NIR0002` exactly once.
  - Add a test-only helper `Support/TokenFormatter.cs` that lexes a string and returns the tokens as lines
    `NAME 'text'`, skipping nothing the parser would see.
  - Tests (`tests/Nir.Tests/Lexing/LexerTests.cs`), each asserting the full token list:
    - `Function_header` (`fn main() {}`)
    - `Newline_after_identifier_becomes_semicolon` (`a\nb` → `IDENTIFIER SEMI IDENTIFIER SEMI EOF`: the second `SEMI` is the synthesized end-of-file one)
    - `Newline_after_closing_paren_becomes_semicolon`
    - `Newline_after_open_brace_is_dropped`
    - `Newline_after_comma_is_dropped`
    - `Newline_inside_parentheses_is_dropped`
    - `Blank_lines_collapse_to_one_semicolon`
    - `Explicit_semicolon_is_kept`
    - `Line_comment_is_skipped`
    - `String_literal_tokens` (`"hi"` → `STRING_START STRING_TEXT STRING_END`)
    - `Empty_string_has_no_text_token`
    - `Backslash_is_kept_raw_in_string` (`"a\nb"` as typed characters backslash-n stays in `STRING_TEXT`)
    - `Missing_closing_quote_reports_NIR0002` (position = the opening quote)
    - `Unexpected_character_reports_NIR0001` (`@`)
  - **Done when:** these tests pass.

- [x] **T1.4 Syntax tree and parser** (`Syntax/*.cs`, `Parsing/NirParser.g4`, `AstBuilder.cs`, `SyntaxParser.cs`)
  - AST records exactly as in `architecture.md` section 6.
  - Parser grammar exactly as in the "Grammar for this slice" block above.
  - `ParserErrorListener.cs`: report parser errors as `NIR1001`.
  - `AstBuilder` builds the nodes; `Span` of each node runs from its first token's start to its last token's end.
    A function's `Span` ends at its closing `}`.
  - `SyntaxParser.Parse` wires lexer + parser + listeners; it removes the default console error listeners.
  - Test helper `Support/AstFormatter.cs`: prints the tree as indented text, one node per line, e.g.
    ```
    CompilationUnit
      FunctionDeclaration main
        Block
          ExpressionStatement
            CallExpression print
              StringLiteral "hello"
    ```
  - Tests (`tests/Nir.Tests/Parsing/ParserTests.cs`):
    - `Empty_file_parses_to_empty_unit`
    - `Function_with_empty_body`
    - `Call_with_string_argument` (the tree above)
    - `Two_statements_separated_by_newline`
    - `Two_statements_separated_by_semicolon_on_one_line`
    - `Two_functions`
    - `Call_with_two_arguments_and_trailing_comma` (`f("a", "b",)`)
    - `Call_with_arguments_over_multiple_lines` (arguments on separate lines inside the parentheses, no trailing comma)
    - `Spans_cover_whole_node` (check `Start` and `Length` of the call and the function)
    - `Missing_closing_paren_reports_NIR1001_at_expected_position`
    - `Missing_function_body_reports_NIR1001`
    - `Syntax_error_still_returns_a_tree` (no exception, `Parse` returns a `CompilationUnit`)
  - **Done when:** these tests pass.

- [x] **T1.5 Binder** (`Semantics/*.cs`)
  - Types and nodes exactly as in `architecture.md` section 7 (only the slice-1 members).
  - `BuiltinFunctions` table with `print(string)` → `typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])`.
  - Rules:
    - Pass 1 declares every function. A second function with the same name reports `NIR2003` at the second function's
      name identifier; the first stays the declared one.
    - If there is no function named `main`, report `NIR2002` at line 1, column 1 (span `(0, 0)`).
    - Pass 2 binds bodies. A call to a name that is neither a user function nor a built-in reports `NIR2001` at the callee
      identifier. A user function shadows nothing: a user function named `print` is allowed and wins over the built-in.
    - Argument count differing from the parameter count reports `NIR2004` at the call expression span start.
    - On any error `Bind` returns `null`.
  - Tests (`tests/Nir.Tests/Semantics/BinderTests.cs`), asserting `(code, line, column)`:
    - `Valid_program_binds_without_diagnostics`
    - `Call_before_declaration_is_allowed`
    - `Undefined_function_reports_NIR2001`
    - `Missing_main_reports_NIR2002_at_1_1`
    - `Duplicate_function_reports_NIR2003_at_second_name`
    - `Print_without_arguments_reports_NIR2004`
    - `Print_with_two_arguments_reports_NIR2004`
    - `User_function_named_print_wins_over_builtin`
    - `Bind_returns_null_when_errors`
  - **Done when:** these tests pass.

- [x] **T1.6 Emitter and test host** (`Emit/Emitter.cs`, `Emit/RuntimeConfigWriter.cs`, `tests/.../Support/TestHost.cs`)
  - Implement `Emitter.Emit` following `architecture.md` section 8 **as corrected by section 9**.
  - Emit rules for slice 1: each `BoundFunction` becomes a public static void method on class `Program`; a
    `BoundCallExpression` to a built-in emits the argument(s) then `call <MethodInfo>`; to a user function emits `call`
    on its method builder; a `BoundStringLiteral` emits `ldstr`; an `ExpressionStatement` whose expression type is not
    `Unit` emits `pop` after the expression (none exist yet but the rule is part of the contract); each method ends
    with `ret`.
  - `TestHost.Run(source)`: compile via `NirCompiler`, emit into a fresh temp directory, start `dotnet <dll>` with
    redirected stdout/stderr, wait (timeout 20 seconds, then kill and fail), delete the temp directory, return
    stdout, stderr, exit code.
  - Tests (`tests/Nir.Tests/Emit/EmitterTests.cs`) using `TestHost.Run`:
    - `Hello_prints_text_and_newline` (stdout is `hello` plus the platform newline; compare with `\n` after normalizing `\r\n`)
    - `Statements_run_in_order`
    - `Function_call_runs_callee`
    - `Forward_call_works` (callee declared after caller)
    - `Empty_main_prints_nothing`
    - `Two_semicolons_on_one_line` (`print("a"); print("b")`)
  - **Done when:** these tests pass.

- [x] **T1.7 Facade and CLI** (`NirCompiler.cs`, `src/Nir.Cli/*`)
  - `NirCompiler.Compile` / `Emit` and `CompilationResult` exactly as in `architecture.md` section 10.
  - CLI exactly as in `architecture.md` section 11: `build` and `run`, exit codes 0/1/2/3, `DiagnosticPrinter`.
  - `nir` with no arguments, or an unknown command, prints a usage message to stderr and exits 2.
  - Tests (`tests/Nir.Tests/Cli/CliTests.cs`, run the built `nir` through `Program.Main` or as a process; process is preferred):
    - `Build_writes_dll_and_runtimeconfig`
    - `Run_prints_program_output`
    - `Compile_error_prints_formatted_diagnostic_and_exits_1` (stderr contains `<file>:2:5: error NIR2001: 'prnt' is not defined`)
    - `Missing_file_exits_2`
    - `Unknown_command_exits_2`
  - **Done when:** these tests pass and `dotnet run --project src/Nir.Cli -- run samples/hello.nir` works.

- [x] **T1.8 Program tests and samples**
  - Create `samples/hello.nir` (the two-print program from acceptance 1).
  - Create the golden test harness described in `practices.md` section 3 and these programs under
    `tests/Nir.Tests/Programs/slice1/` (each is `<name>.nir` + `<name>.stdout` or `<name>.diag`):

    | Program | Source | Expected |
    |---|---|---|
    | `hello` | `fn main() {` / `    print("hello")` / `    print("world")` / `}` | stdout `hello\nworld\n` |
    | `empty_main` | `fn main() {` / `}` | stdout empty |
    | `forward_call` | `main` calls `greet` which is declared below it | stdout from `greet` |
    | `comments` | comments on their own line and after statements | stdout unchanged |
    | `undefined_name` | `fn main() {` / `    prnt("x")` / `}` | `2:5 NIR2001` |
    | `no_main` | `fn other() {` / `}` | `1:1 NIR2002` |
    | `duplicate_main` | two `fn main() {` / `}` blocks, the second starting on line 3 | `3:4 NIR2003` |
    | `bad_arity` | `fn main() {` / `    print()` / `}` | `2:5 NIR2004` |
    | `syntax_error` | a minimal program for which ANTLR reports exactly one error (for example `fn main( {` / `}`) | that error, `NIR1001` |

    As built, two more programs were added: `semicolons` (`fn main() { print("a"); print("b") }`) and `unit_as_string`
    (`print(greet())` where `greet` returns nothing, `4:11 NIR3001`).

    For `syntax_error`, adjust the sample if ANTLR reports cascading errors, so the golden file lists exactly one
    diagnostic, and write the reason in a comment line at the top of the `.nir` file.
  - **Done when:** every row passes as a program test.

- [x] **T1.9 Architecture guard and wrap-up**
  - Test `Architecture_tests`: using reflection over the `Nir.Compiler` assembly, assert that no public type outside
    namespace `Nir.Compiler.Parsing` has a public member (parameter, return, property, field, base type) whose type is
    in an `Antlr4` namespace or in the generated `Nir.Compiler.Parsing` lexer/parser classes. Also assert that no type in
    `Nir.Compiler.Semantics` or `Nir.Compiler.Emit` references `Nir.Compiler.Syntax` or `Nir.Compiler.Parsing`
    types in a public signature, except `Semantics` using `Syntax` (allowed by the dependency rules).
  - Update docs: tick tasks, set the status block in `CLAUDE.md` to slice 2, T2.0 (once written), update section 8/9 of
    `architecture.md` if reality differs from the text, append decisions made during the slice to `decisions.md`.
  - Delete `reference/` only if nothing in slices 2 to 4 needs it (default: keep it until slice 5).
  - **Done when:** the architecture test passes and all four acceptance criteria of slice 1 are true.

---

## Slice 2: bindings and arithmetic

**Goal.** Variables, numbers and the first type errors.

```
fn main() {
    let x = 10
    let mut y = 2.5
    y = y * 2.0
    print(x)
    print(y)
    print(x + 1)
}
```

**Scope.**
- Primitive types `int` (CLR `Int32`), `double` (`Double`), `bool` (`Boolean`), `string`. `()` as the unit type.
- Literals: `10`, `2.5`, `true`, `false`. (Lexer rule from the old repo: `.` is a decimal point only if a digit follows,
  so `0..10` lexes as int, range, int.)
- `let name = expr`, `let mut name = expr`, `let name: type = expr`. Assignment statement `name = expr` (only to `let mut`).
- Binary `+ - * / %` on two operands of the same numeric type; comparison `< <= > >= == !=` giving `bool`; unary `-`;
  parentheses. Precedence as in `reference/NirParser.g4`: multiplicative, additive, relational, equality.
- `print` overloads for `int`, `double`, `bool`, `string` (bound to the matching `Console.WriteLine`).
- Locals are emitted with `ILGenerator.DeclareLocal`. Integer arithmetic is checked (decision 2): `add.ovf sub.ovf mul.ovf`,
  and `0 - x` with `sub.ovf` for unary minus; `div` and `rem` already throw on division by zero. `double` arithmetic
  uses plain `add sub mul div rem neg`. Comparisons use `clt cgt ceq` (with `not` combos for `<=`, `>=`, `!=`).

**New diagnostics (assign numbers in order after NIR3001, add to the table in architecture.md).**
`NIR3001` type mismatch (`cannot convert '{from}' to '{to}'`) already exists from slice 1 and is reused; add: assignment to immutable (`cannot assign to '{name}' because it is not mutable`);
operator not defined for types (`operator '{op}' cannot be applied to '{left}' and '{right}'`); missing initializer or
unknown type name. Undefined variable reuses `NIR2001`.

**Decisions (answered 2026-11-01, recorded in design.md section 1 and decisions.md D-030 to D-033):**
1. Redeclaring a name in the same block is an error. Still open: how this interacts with `let mut x = x` shadowing a
   parameter (see design.md section 1, "Scopes"). Settle it in slice 3, when parameters arrive; slice 2 has no parameters.
2. Integer overflow throws (checked arithmetic). A build flag to switch to wrapping may come later.
3. Only `double` for now; there is no `float` type.
4. No implicit conversions between numeric types. Conversion syntax is decided in slice 5.

**Acceptance programs** (`Programs/slice2/`): arithmetic on ints and doubles with printed results; comparison printing
`true`/`false`; `let mut` reassignment; errors for assign-to-immutable, type mismatch (`let x: int = 2.5`), undefined
variable, `int + double`, redeclaring a name in the same block, and a division check (`10 / 3` prints `3`,
`10.0 / 4.0` prints `2.5`). Overflow: `2147483647 + 1` ends with an unhandled `OverflowException` (exit code not 0,
stderr contains `OverflowException`); this one is a normal xUnit test in `Emit/`, not a golden program, because the exit
code differs per OS.

---

## Slice 3: functions with parameters and results

**Goal.** Real functions.

```
fn add(a: int, b: int) -> int {
    return a + b
}

fn main() {
    print(add(2, 3))
}
```

**Scope.** Parameters (never assignable; `let mut x = x` makes a copy), `-> type` return types, `return expr`, bare
`return` in unit functions, calls with arguments, recursion, functions calling each other in any order. `main` must have
no parameters and the unit return type.

**New diagnostics.** wrong argument type; missing return on some path; return value in a unit function; return type
mismatch; `main` with parameters or a return type.

**Decisions required:** whether a "not all code paths return" check uses a simple rule (last statement of the block is
`return`, or both branches of an `if` return) or full flow analysis. Recommendation: simple rule now, revisit with `match`.
Implicit last-expression results are **not** supported (design section 1 uses explicit `return`).

**Acceptance programs** (`Programs/slice3/`): recursion (factorial, fibonacci), mutual calls, shadowed parameter,
assigning a parameter is an error, wrong argument type, missing return, return in unit function.

---

## Slice 4: first C# interop (the riskiest piece, deliberately early)

**Goal.** Call the .NET BCL by name using the `clr::cs` import rules from design section 9.

```
using clr::cs::System::IO
using clr::cs::System::Math

fn main() {
    print(Math::Sqrt(16.0))
    IO::File::WriteAllText(IO::Path::Combine(IO::Path::GetTempPath(), "nir.txt"), "hi")
}
```

**Scope.** `using <path>` imports exactly one named thing (namespace or type), local name = last segment, `{A, B}`
groups. `::` path expressions. Calls to **static** methods of BCL types. Type mapping: `int`↔`Int32`, `double`↔`Double`,
`bool`↔`Boolean`, `string`↔`String`, unit↔`void`. Overload resolution: exact parameter-type match only; if none, report
an error (no implicit conversions). Instance method calls on values (`"abc".ToUpper()`), via `.`, are included.
Not included: constructors, generics, properties, events, extension methods, `ref`/`out` parameters.

**How types are found (design this task first, then record it in `architecture.md`):** resolve `clr::cs::A::B::T` by
loading the runtime's reference/implementation assemblies (start with `System.Runtime`, `System.Console`,
`System.Private.CoreLib` and the assemblies of the shared framework) and looking up the type `A.B.T`. Prefer
`System.Reflection.MetadataLoadContext` so the compiler does not execute BCL code and is not coupled to its own
runtime; fall back to plain reflection only if `MetadataLoadContext` is not viable, and say so in `decisions.md`.

**New diagnostics.** namespace or type not found under `clr::cs`; type has no member `{name}`; no matching overload.

**Decisions required:** none beyond the design (all locked in design sections 8 and 9). The overload and loading
strategy is a technical decision to record in `decisions.md`.

**Acceptance programs** (`Programs/slice4/`): `Math::Sqrt`, `Math::Max` on ints, string instance methods, file write
then read through a temp path, `Environment`-free output; errors for unknown namespace, unknown member, wrong argument type.

---

## Later slices (outline; detailed when each starts)

**Slice 5: control flow and strings.** `if` / `else if` / `else`; `for i in a..b`; `while`; `break` / `continue`; boolean
operators; string interpolation (`$"..."`, port the lexer modes from `reference/NirLexer.g4`) and escape decoding;
string `+`; explicit numeric conversion. *Decisions required:* is `a..b` end-exclusive (recommendation: yes); does `while`
exist (recommendation: yes); boolean operator spelling (`&&` `||` `!` or `and` `or` `not`); conversion syntax.

**Slice 6: structs, methods, mutability by path.** `type P = struct {...}`, literals `P { x = 1 }`, field read/write,
value semantics, `impl` blocks, `&self` / `&mut self` (compiled to CLR byref `this`), associated functions
`P::new()`, `Self`. Design sections 2 to 5.

**Slice 7: tuples, unions, `match`.** Tuples as `ValueTuple`; `union { A, B }`; exhaustive `match`. Design section 11.

**Slice 8: errors.** `type E = error {...}`, `E!T`, `anyerror`, `error` keyword, `try`, `catch`, `fn main() -> anyerror!`,
C# exceptions wrapped as `anyerror` under `try`. Design section 10.

**Slice 9: heap references.** `&T`, `&mut T`, `&T {...}`, `&` of a variable (hoisted to a `Ref<T>`), `?T`, null. Design
sections 3, 4, 6, 7.

**Slice 10: modules.** `namespace`, multi-file compilation, `using` for Nir code, `impl` placement rule, `extend`
blocks and method imports, C# extension methods. Design section 9.

**Slice 11: enum, interface, arrays, generic C# types.** Needed for real library use (`List<T>`, `byte[]`).

**Milestone M1.** An encrypt/decrypt CLI (read a file, transform its bytes with a key, write the result, handle errors
with `try`) written in Nir and compiled with `nir`. The real program is written when slice 8 is done; it fixes the exact
set of missing BCL-interop features.

## Backlog (not scheduled; do not start without being asked)

- `nir dump tokens|ast|bound <file>` debug commands (the old repo had token and AST printers to copy ideas from).
- Better syntax error messages.
- `nir fmt`, language server, REPL.
- A Nir-written `std` library (`std::fs` etc.) on top of `clr::cs`.
- Removing ANTLR in favour of a hand-written parser (must only touch `Parsing/`).
- Optional `A | B` union syntax.
- `frozen` types, structural types, Rust-style borrow checking.
