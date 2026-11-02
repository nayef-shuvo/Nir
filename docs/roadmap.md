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
1. Redeclaring a name in the same block is an error. Parameters and the function body share one scope, so shadowing a
   parameter is an error too (D-034). Whether a nested block may redeclare an outer name is open until slice 5.
2. Integer overflow throws (checked arithmetic). A build flag to switch to wrapping may come later.
3. Only `double` for now; there is no `float` type.
4. No implicit conversions between numeric types. Conversion syntax is decided in slice 5.

**Acceptance programs** (`Programs/slice2/`): arithmetic on ints and doubles with printed results; comparison printing
`true`/`false`; `let mut` reassignment; errors for assign-to-immutable, type mismatch (`let x: int = 2.5`), undefined
variable, `int + double`, redeclaring a name in the same block, and a division check (`10 / 3` prints `3`,
`10.0 / 4.0` prints `2.5`). Overflow: `2147483647 + 1` ends with an unhandled `OverflowException` (exit code not 0,
stderr contains `OverflowException`); this one is a normal xUnit test in `Emit/`, not a golden program, because the exit
code differs per OS.

**Details fixed for this slice (so nothing is left to guess):**
- Grammar additions: keywords `let`, `mut`, `true`, `false`; tokens `COLON` `EQ` `PLUS` `MINUS` `STAR` `SLASH` `PERCENT`
  `LT` `LE` `GT` `GE` `EQEQ` `NEQ`, `INT_LITERAL` (`[0-9]+`), `DOUBLE_LITERAL` (`[0-9]+ '.' [0-9]+`).
  `EndsStatement` gains `INT_LITERAL`, `DOUBLE_LITERAL`, `TRUE`, `FALSE`. Each new one needs a lexer test.
- Precedence, highest first: unary `-`; `* / %`; `+ -`; `< <= > >=`; `== !=`. All binary operators are left-associative.
- Statements: `let [mut] name [: type] = expression`, `name = expression`, expression statement. A type annotation is a
  plain identifier naming `int`, `double`, `bool` or `string`.
- AST additions: `VariableDeclaration`, `AssignmentStatement`, `BinaryExpression`, `UnaryExpression`,
  `ParenthesizedExpression`, `NameExpression`, `IntLiteralExpression(string Text)`, `DoubleLiteralExpression(string Text)`,
  `BoolLiteralExpression(bool Value)`. Literal text is converted by the binder so out-of-range literals get a diagnostic.
- Operator typing: `+ - * / %` on `int,int` giving `int` or `double,double` giving `double`. `< <= > >=` on two `int` or
  two `double` giving `bool`. `== !=` on two `int`, two `double` or two `bool` giving `bool` (`string` comparison and
  `string` `+` come in slice 5). Unary `-` on `int` or `double`. Anything else is `NIR3003`.
- Integer literals above 2147483647 are an error (`NIR3004`). So `-2147483648` cannot be written yet.
- Error recovery: a failed expression binds to `BoundErrorExpression` of `TypeSymbol.ErrorType`. Any operation that
  receives an error-typed operand yields an error-typed result and reports nothing more. A variable whose initializer
  failed is still declared (with `ErrorType`), so later uses do not report `NIR2001`.
- Scope: one flat scope per function body. A variable is visible only after its declaration, and `let x = x` does not
  see the new `x` (it reports `NIR2001` for the initializer).
- `print` has four overloads (`int`, `double`, `bool`, `string`). Overload choice is an exact match of the argument
  type. No overload with the right argument count gives `NIR2004`; the right count but no matching type gives `NIR3006`.
  (`print(greet())` with a unit-returning `greet` therefore changes from `NIR3001` to `NIR3006`, at the call start.)
- The emitted program's runtimeconfig sets `System.Globalization.Invariant` to `true`, so numbers print the same on every
  machine (`2.5`, never `2,5`).
- New diagnostic codes: `NIR2005` `'{name}' is already declared in this block`; `NIR2006` `unknown type '{name}'`;
  `NIR3002` `cannot assign to '{name}' because it is not mutable`; `NIR3003`
  `operator '{op}' cannot be applied to '{left}' and '{right}'` (unary: `operator '{op}' cannot be applied to '{type}'`);
  `NIR3004` `'{text}' is out of range for '{type}'`; `NIR3005` `cannot declare '{name}' with type '()'`;
  `NIR3006` `no overload of '{name}' accepts ({types})`. Reuse `NIR3001` for initializer and assignment mismatches and
  `NIR2001` for undefined variables.

### Tasks

- [x] **T2.0 Error type, print overloads, invariant runtimeconfig**
  - Add `TypeSymbol.IntType`, `DoubleType`, `BoolType`, `ErrorType`; `BoundErrorExpression`. Change the binder so a
    failed expression returns `BoundErrorExpression` instead of `null`, and calls with error-typed arguments report
    nothing more. `Bind` still returns `null` if this call reported any error.
  - `BuiltinFunctions` holds a list of overloads per name. Four `print` overloads bound to `Console.WriteLine(int | double | bool | string)`.
  - Add `NIR3006` and the binder overload rule above. Update the golden program `unit_as_string.diag` to `4:5 NIR3006`.
  - `RuntimeConfigWriter` adds `"configProperties": {"System.Globalization.Invariant": true}`.
  - Tests: existing tests still pass; binder test `Print_with_unit_argument_reports_NIR3006`.
  - **Done when:** `dotnet test` is green and `architecture.md` section 7 mentions the new types and overload rule.

- [x] **T2.1 Literals**
  - Lexer: `INT_LITERAL`, `DOUBLE_LITERAL`, `TRUE`, `FALSE` (keywords before `IDENTIFIER`), `EndsStatement` update.
    Tests: `Int_literal`, `Double_literal`, `Digits_then_dot_without_digit_is_not_a_double` (`1.` lexes `INT_LITERAL`,
    then reports `NIR0001` for the `.`), `Newline_after_literal_becomes_semicolon` (one per new token),
    `True_and_false_are_keywords`.
  - Parser/AST/AstBuilder: the three literal nodes and a `NameExpression`-free first cut (names come in T2.3).
  - Binder: `BoundIntLiteral`, `BoundDoubleLiteral`, `BoundBoolLiteral`; `NIR3004` for an int above 2147483647 or a
    double that parses to infinity.
  - Emitter: `ldc.i4`, `ldc.r8`, `ldc.i4.0/1`.
  - Tests: parser tree tests for each literal; binder `Int_literal_out_of_range_reports_NIR3004`;
    emit tests `Prints_int`, `Prints_double` (`2.5`), `Prints_bool` (`true`/`false` as .NET prints them: `True`/`False`).
  - **Done when:** these pass. (Note: `Console.WriteLine(true)` prints `True`; that is the accepted output.)

- [x] **T2.2 Operators**
  - Lexer tokens for the operators; grammar `expression` rule as a precedence ladder with labeled alternatives;
    `BinaryExpression`, `UnaryExpression`, `ParenthesizedExpression`, operator enums.
  - Binder: the typing rules above; `BoundBinaryExpression`, `BoundUnaryExpression`; `NIR3003`.
  - Emitter: checked integer arithmetic (`add.ovf`, `sub.ovf`, `mul.ovf`, unary minus as `0 - x` with `sub.ovf`),
    `div`, `rem`; plain `add sub mul div rem neg` for doubles; comparisons as described under "Locals" above, using
    `cgt.un` / `clt.un` for the "or equal" forms on doubles so NaN comparisons are false.
  - Tests: parser precedence and associativity (`1 + 2 * 3`, `1 - 2 - 3`, `-1 * 2`, `(1 + 2) * 3`, `1 < 2 == true`);
    binder `Int_plus_double_reports_NIR3003`, `Minus_on_bool_reports_NIR3003`, `Error_operand_reports_nothing_more`;
    emit tests for every operator on `int` and `double`, including `10 / 3` is `3`, `10.0 / 4.0` is `2.5`, `-7 % 3` is `-1`,
    `5 > 3` is `True`, NaN comparisons (`0.0 / 0.0 == 0.0 / 0.0` is `False`), division by zero crashes,
    `2147483647 + 1` crashes with `OverflowException`.
  - **Done when:** these pass.

- [x] **T2.3 Variables and scopes**
  - Lexer: `LET`, `MUT`, `COLON`, `EQ`; parser: the three statement forms; AST nodes; `NameExpression`.
  - Binder: a `VariableSymbol`; flat per-function scope; declaration after initializer; `NIR2005`, `NIR2006`, `NIR3002`,
    `NIR3005`, `NIR3001` for annotation/assignment mismatches, `NIR2001` for unknown variables; annotated declarations
    check the initializer type; unannotated take it. Error-typed initializer still declares the variable.
  - Emitter: `DeclareLocal` per variable, `stloc`, `ldloc`.
  - Tests: parser trees for each statement form; binder tests for every new code with positions;
    emit tests: declare and print, `let mut` reassignment, use in an expression, a program using several variables.
  - **Done when:** these pass.

- [x] **T2.4 Program tests, overflow test, wrap-up**
  - Add the `Programs/slice2/` golden programs (arithmetic ints and doubles, comparisons, `let mut` reassignment,
    annotation, each error listed in the acceptance programs) and `Slice2ProgramTests`.
  - The overflow xUnit test in `Emit/`.
  - Update `architecture.md` (sections 4 table, 5, 6, 7, 8) and `design.md` if anything changed, tick the tasks, update
    the status block in `CLAUDE.md`, append decisions to `decisions.md`, and add `semicolons`-style notes if needed.
  - **Done when:** all acceptance programs for slice 2 pass, `dotnet build` has 0 warnings, `dotnet test` is green.

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

**Scope.** Parameters (never assignable; redeclaring a parameter name in the body is an error; a changeable copy needs
a new name), `-> type` return types, `return expr`, bare
`return` in unit functions, calls with arguments, recursion, functions calling each other in any order. `main` must have
no parameters and the unit return type.

**Decision (answered 2026-11-01, D-038):** the "every path returns" check is the simple rule. A function whose return
type is not `()` must have a `return value` as the last statement of its body. (When `if` arrives in slice 5, a final
`if`/`else` whose branches both end in `return` will count too.) Functions that return `()` need no `return`. There are no
implicit last-expression results; `return` is always explicit.

**Details fixed for this slice:**
- Grammar: `ARROW` (`->`), keyword `RETURN` (`return`; added to `EndsStatement` so a bare `return` followed by a newline
  ends the statement). `functionDeclaration : FN name=IDENTIFIER LPAREN parameterList? RPAREN (ARROW returnType=IDENTIFIER)? body=block`,
  `parameterList : parameter (COMMA parameter)* COMMA?`, `parameter : name=IDENTIFIER COLON type=IDENTIFIER`,
  `returnStatement : RETURN value=expression?`. The type after `->` is a plain identifier (`int`, `double`, `bool`,
  `string`); there is no way to write `()`, you simply omit `-> type`.
- AST: `FunctionDeclaration(Name, Parameters, ReturnType?, Body)`, `Parameter(Name, Type)`, `ReturnStatement(Value?)`.
- Parameters are variables that cannot be assigned (assigning one is `NIR3002`). Parameters and the function body share
  one scope (D-034), so redeclaring a parameter in the body is `NIR2005`, and two parameters with the same name is
  `NIR2005` at the second one.
- Unknown parameter or return type is `NIR2006` (reuse). Wrong argument count is `NIR2004` and a wrong argument type is
  `NIR3001` at the argument (both exist).
- `return` checks: in a `()` function, `return value` is `NIR3008` at the value. In another function, a bare `return` is
  `NIR3009` at the `return` statement, and a value of the wrong type is `NIR3001` at the value. A function that needs a
  return but does not end with one is `NIR3007` at the function name. Code after a `return` is allowed (no warnings yet).
- `main` must have no parameters and no return type, otherwise `NIR2007` at its name.
- A call as a statement may discard a result (`add(1, 2)` on its own line is allowed).
- Emit: methods get the declared parameter and return CLR types; parameters are read with `ldarg`; `return value` is
  the value then `ret`; the trailing `ret` is emitted only for `()` functions (others end with their own `return`).
- New diagnostic codes: `NIR2007` `'main' must have no parameters and no return type`; `NIR3007`
  `'{name}' does not return a value on every path`; `NIR3008` `cannot return a value from '{name}' because it returns '()'`;
  `NIR3009` `'{name}' must return a value of type '{type}'`.

**Acceptance programs** (`Programs/slice3/`): calls between functions, a function with several
parameters of different types, a function returning each type, early bare `return` in a `()` function, a call result
used in an expression and discarded as a statement, and errors for: redeclaring a parameter in the body, assigning a
parameter, duplicate parameter names, wrong argument type, wrong argument count, missing return, return value in a `()`
function, bare return in a value function, return type mismatch, unknown parameter type, and a bad `main` signature.

### Tasks

- [x] **T3.0 Syntax**
  - Lexer: `ARROW`, `RETURN`; `EndsStatement` gains `RETURN`. Parser rules and AST nodes above; `AstBuilder`.
  - Tests: lexer `Arrow_is_one_token` (`->`, and `-` then `>` separated by a space stays two tokens),
    `Return_is_a_keyword`, `Newline_after_return_ends_the_statement`; parser trees for a function with parameters and a
    return type, a trailing comma in the parameter list, `return`, `return expr`, spans of `Parameter` and
    `ReturnStatement`, and syntax errors (`fn f(a) {}`, `fn f(a: ) {}`, `fn f() -> {}`) that report `NIR1001` without throwing.
  - **Done when:** these pass.

- [x] **T3.1 Binder**
  - Symbols: `UserFunctionSymbol` gets its resolved return type and `Parameters` (`VariableSymbol`s). Resolve signatures
    in pass 1. Bind parameters into the body scope. `BoundReturnStatement(Value?)`.
  - All checks and codes listed above.
  - Tests with positions for every code; also: recursion binds, mutual recursion binds, `print(add(1, 2))` picks the
    `int` overload, an error-typed argument adds no second diagnostic.
  - **Done when:** these pass.

- [x] **T3.2 Emitter** (note: recursion that terminates needs `if`, so factorial, fibonacci and `is_even` / `is_odd`
  are *run* in slice 5; slice 3 only checks that recursive and mutually recursive functions bind, which `BinderTests` does)
  - Parameters and return types in `DefineMethod`, `ldarg`, `BoundReturnStatement`, the conditional trailing `ret`.
  - Tests (via `TestHost.Run`): `add`, functions calling other functions, each return type, a `()` function with early `return` (code after it does not run), result discarded as a statement,
    arguments evaluated left to right, a parameter used in a nested expression.
  - **Done when:** these pass.

- [x] **T3.3 Program tests and docs**
  - `Programs/slice3/` golden programs for every acceptance item and `Slice3ProgramTests`.
  - Update `architecture.md` (sections 4, 6, 7, 8), tick tasks, update the status block in `CLAUDE.md`, append decisions.
  - **Done when:** all acceptance items pass, `dotnet build` has 0 warnings, `dotnet test` is green.

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

**Decision (spike done 2026-11-01, D-042):** types are found with `System.Reflection.MetadataLoadContext` (NuGet
`System.Reflection.MetadataLoadContext` 10.0.0), not plain reflection. The spike showed that lookup and emit both work,
and that **every CLR type used while emitting must come from the load context**. Mixing in the compiler's own runtime
types (`typeof(void)`, `typeof(int)`) makes the program fail to start ("Entry point must have a return type of void").
So `TypeSymbol.ClrType`, the built-in `print` methods and the emitter's core assembly all come from one process-wide
`ClrUniverse`.

**Details fixed for this slice:**
- Syntax. Keyword `using`; tokens `COLONCOLON` (`::`) and `DOT` (`.`). `using clr::cs::System::IO` or a group
  `using clr::cs::System::{Math, Console}` (a multi-line group needs a trailing comma, because `{}` is not depth-tracked).
  `using` lines may appear anywhere at the top level and apply to the whole file. A call target is a qualified name
  `IO::File::ReadAllText(p)`; a method call on a value is `receiver.Method(args)`, which binds tighter than unary `-`.
  A line cannot start with `.` to continue the previous line (the newline already ended the statement).
- AST: `UsingDirective(Path, Members)` (`Members` is empty unless a group is used), `QualifiedName(Parts)` (with a `Text`
  property such as `IO::File::ReadAllText`), `CallExpression` now has a `QualifiedName` callee, `MemberCallExpression
  (Receiver, Method, Arguments)`. `CompilationUnit` gains `Usings`.
- Imports. The path must start `clr::cs` followed by at least one segment: `NIR2008` otherwise (at the first segment).
  The rest, joined with `.`, must name a public top-level type or a namespace in the runtime's framework assemblies:
  `NIR2009` `'{name}' was not found` (at the last segment). The local name is the last segment; importing the same local
  name twice is `NIR2010` `'{name}' is already imported` (the first import wins).
- Qualified calls. The first segment must be an imported name (otherwise `NIR2001`). Middle segments walk namespaces to
  a type; the last segment is a static method of that type. A segment that does not exist, or a method that is not a
  public static method (or a namespace where a type is needed), is `NIR2011` `'{member}' is not a member of '{container}'`
  at that segment. Nested types are not supported.
- Overloads. Only overloads whose parameter types are all Nir types count (`int`, `double`, `bool`, `string`;
  generic methods, `ref`/`out`, arrays and everything else are skipped). Choice is by exact argument types. No overload
  with that argument count is `NIR2004`; the right count but no exact match is `NIR3006` (at the call start). If the
  chosen method's return type is not `void`, `int`, `double`, `bool` or `string`, the call is `NIR3011`
  `'{method}' returns '{type}', which Nir cannot use yet`.
- Instance calls. The receiver's Nir type maps to `System.String`, `Int32`, `Double` or `Boolean`; the method is a
  public instance method of that type with the same overload rules. A method that is not there is `NIR2011`. A receiver
  of a value type (`42.ToString()`) is stored in a temporary and called by address; a `string` receiver uses `callvirt`.
- Emit: `ClrMethodSymbol` replaces `BuiltinFunctionSymbol` (a `print` overload is just a `ClrMethodSymbol`). A static
  method is `call`; an instance method evaluates the receiver first, then the arguments.
- Thread safety. The load context is shared by the whole process, so `NirCompiler.Compile` and `Emit` take one global
  lock. (Tests run in parallel.)
- Out of scope: constructors, generics, properties and fields, events, extension methods, `ref`/`out`, nested types, the
  `System.Linq` extension-method import form.

**New diagnostics.** `NIR2008` `imports must start with 'clr::cs'` (or, for `using clr::cs` alone, `'clr::cs' alone names nothing; add a namespace or type after it`); `NIR2009` `'{name}' was not found`; `NIR2010`
`'{name}' is already imported`; `NIR2011` `'{member}' is not a member of '{container}'`; `NIR3011`
`'{method}' returns '{type}', which Nir cannot use yet`. `NIR2001`, `NIR2004`, `NIR3006` are reused.

**Acceptance programs** (`Programs/slice4/`): `Math::Sqrt`, `Math::Max` on ints and on doubles, `Math::Pow`; string
instance methods and a chain (`"  hi ".Trim().ToUpper()`); `42.ToString()`; `Int32::Parse("42")`; `String::Concat`;
`Console::WriteLine` through a group import; a file written, read back and deleted through a temp path; errors for a bad
import root, an unknown namespace/type, a duplicate import, an unimported name, an unknown member, a wrong argument type,
a wrong argument count, an unknown instance method, and a method with an unsupported return type.

### Tasks

- [x] **T4.0 The .NET type universe (refactor, no new language feature)**
  - Add the `System.Reflection.MetadataLoadContext` 10.0.0 package. New folder `Clr/` with `ClrUniverse` (internal,
    static): one `MetadataLoadContext` over every `*.dll` in `RuntimeEnvironment.GetRuntimeDirectory()`, core assembly
    `System.Private.CoreLib`; `CoreAssembly`; `FindType(fullName)`; `NamespaceExists(name)`; and `ToNirType(Type)` /
    mapping by full name. The type and namespace index is built once, lazily, by reading each file's metadata tables
    with `System.Reflection.Metadata` (public top-level type definitions only; native files with no metadata are
    skipped), so no assembly is loaded just to be indexed.
  - `TypeSymbol.ClrType` values come from the universe. `Emitter` uses `ClrUniverse.CoreAssembly` for the
    `PersistedAssemblyBuilder`. Introduce `ClrMethodSymbol` and rebuild the `print` overloads from the universe.
    `NirCompiler.Compile` and `Emit` take the global lock. Add `Clr` to the dependency rules in `architecture.md` and
    `ArchitectureTests` (Clr depends on nothing in Nir; Semantics and Emit may use it).
  - Tests: `ClrUniverseTests` (finds `System.Math`, `System.IO.File`, `System.Console`; does not find `System.Nope`;
    `System` and `System.IO` are namespaces; `System.IO.File` is not; mapping of the five CLR types; an unmappable type
    gives null). **All existing tests must still pass.**
  - **Done when:** the whole suite is green and `architecture.md` section 8/9 describes the universe.

- [x] **T4.1 Syntax**
  - Tokens, grammar rules (`topLevelItem`, `usingDirective`, `importPath`, `importGroup`, `qualifiedName`, the member-call
    alternative), AST nodes, `AstBuilder`; update the lexer test that expected `1.` to report `NIR0001` (it now lexes
    `INT_LITERAL` then `DOT`).
  - Tests: lexer (`::` vs `:`, `.` after identifiers/strings/literals, keyword `using`); parser trees for a plain
    import, a group import with and without a trailing comma, a qualified call, a member call, a chain
    (`"a".Trim().ToUpper()`), precedence of `-"a".Length`-style unary vs member call (`-x.F()` is `-(x.F())`), a member
    call on a literal (`42.ToString()`), spans; syntax errors (`using`, `using a::`, `using a::{}`, `x.`, `x.F`, `a::()`)
    report `NIR1001` without throwing.
  - **Done when:** these pass.

- [x] **T4.2 Imports and static calls**
  - Binder: import table with `NIR2008` to `NIR2010`; qualified-call resolution; shared overload selection for
    `print` and CLR methods; `NIR2011`, `NIR3011`; `ClrMethodSymbol` for found methods.
  - Emitter: static CLR calls.
  - Tests: binder positions for every code; overloads (`Math.Max` int vs double, `Math.Abs`); a mappable overload is chosen
    when unmappable ones exist (`Path.Combine`); end-to-end runs (`Math::Sqrt`, `Int32::Parse`, `String::Concat`,
    `IO::Path::GetFileName`, file round trip, group import with `Console::WriteLine`).
  - **Done when:** these pass.

- [x] **T4.3 Instance method calls**
  - Binder and emitter for `MemberCallExpression`: string, int, double and bool receivers, value-type receivers by
    address, chained calls.
  - Tests: `"abc".ToUpper()`, `"  hi ".Trim().ToUpper()`, `"abc".Contains("b")`, `"abc".Substring(1)`, `42.ToString()`,
    `2.5.ToString()`, `true.ToString()`, a receiver that is a variable, a call result as a receiver, errors (unknown
    method `NIR2011`, wrong argument type `NIR3006`, error-typed receiver adds nothing).
  - **Done when:** these pass.

- [x] **T4.4 Program tests and docs**
  - `Programs/slice4/` golden programs for the acceptance list and `Slice4ProgramTests`; update `architecture.md`
    (sections 3, 4, 5, 6, 7, 8, 9), tick tasks, update `CLAUDE.md`, append decisions.
  - **Done when:** all acceptance items pass, `dotnet build` has 0 warnings, `dotnet test` is green.

---

## Slice 5: control flow

**Goal.** Branches, loops and boolean logic, and the recursion programs that slice 3 could only compile.

```
fn factorial(n: int) -> int {
    if n <= 1 {
        return 1
    }
    return n * factorial(n - 1)
}

fn main() {
    let mut total = 0
    for i in 1..=5 {
        total = total + factorial(i)
    }
    print(total)                // 153
}
```

**Decisions** are recorded in design.md "Control flow" and "Scopes" and decisions.md D-049 to D-053.

**Details fixed for this slice:**
- Lexer: keywords `if`, `else`, `while`, `for`, `in`, `break`, `continue`; tokens `AMPAMP` (`&&`), `PIPEPIPE` (`||`),
  `BANG` (`!`), `DOTDOT` (`..`), `DOTDOTEQ` (`..=`). `0..10` lexes as `INT_LITERAL DOTDOT INT_LITERAL` because a `.`
  starts a fraction only when a digit follows. `EndsStatement` gains `BREAK` and `CONTINUE`.
- Grammar: statements `ifStatement`, `whileStatement`, `forStatement`, `breakStatement`, `continueStatement`.
  `ifStatement : IF condition=expression then=block (terminator? ELSE (elseIf=ifStatement | elseBlock=block))?` (the
  optional terminator is what lets `else` start on the next line).
  `forStatement : FOR variable=IDENTIFIER IN start=expression (DOTDOT | DOTDOTEQ) end=expression body=block`. The range is
  part of `for`, not a general expression. Expression ladder: unary `-` / `!`, `* / %`, `+ -`, comparisons, `== !=`,
  then new `&&`, then `||`.
- AST: `IfStatement(Condition, Then, ElseBlock?, ElseIf?)`, `WhileStatement(Condition, Body)`,
  `ForStatement(Variable, Start, End, IsInclusive, Body)`, `BreakStatement`, `ContinueStatement`; `UnaryOperator.Not`;
  `BinaryOperator.And` and `Or`.
- Binder. Scopes become a stack of dictionaries, one per block. Declaring a name that any enclosing scope of the function
  already has is `NIR2005`, with the message `'x' is already declared in this block` for the current scope and
  `'x' is already declared in an enclosing block` for an outer one. A condition that is not `bool` is `NIR3001`
  (`cannot convert 'int' to 'bool'`) at the condition. A `for` bound that is not `int` is `NIR3001` at that bound.
  `break` / `continue` outside a loop is `NIR3010` (`'break' can only be used inside a loop`). `!`, `&&` and `||` on
  anything but `bool` are `NIR3003`. An `else if` is bound as an `else` block holding one `if`. The "must return" check uses
  `AlwaysReturns(block)`: the last statement is a `return`, or an `if` with an else where both blocks always return.
- Bound nodes: `BoundIfStatement(Condition, Then, ElseBlock?)`, `BoundWhileStatement`, `BoundForStatement`,
  `BoundBreakStatement`, `BoundContinueStatement`; `BoundUnaryOperator.Not`; `BoundBinaryOperator.And` / `Or`.
- Emit. `if`: `brfalse` to the else label; the `br` over the else block and the end label are emitted only when a
  branch can fall through (a block that always returns needs neither, which also keeps a function that ends in a returning
  `if` / `else` valid IL). `while`: top label, `brfalse` to the end, body, `br` top; `continue` jumps to the top and `break`
  to the end. `for`: evaluate start into the variable's local and the end into a hidden local, then the same shape with the
  test `i < end` (`i <= end` when inclusive) and `i = i + 1` (checked) at the `continue` label. `&&` / `||` are branches,
  not `and` / `or` instructions. `!` is `ldc.i4.0; ceq`.
- Out of scope: `if` as an expression, labels, `loop`, looping over collections, `match`.

**New diagnostics.** `NIR3010` `'{keyword}' can only be used inside a loop`. `NIR2005`'s message gains the "enclosing
block" form. `NIR3001` and `NIR3003` are reused.

**Acceptance programs** (`Programs/slice5/`): `if` / `else if` / `else` (all three branches taken in turn), `else` on
its own line, nested loops with `break` and `continue`, `for` with `..` and `..=` and an empty range, a loop variable
used after the loop is an error, short-circuit proof (a function with a visible `print` on the right side that is not
called), `!`, scope reuse between sibling blocks, factorial / fibonacci / `is_even` + `is_odd` (mutual recursion) /
Collatz, and errors for: non-`bool` condition, non-`int` range bound, redeclaring in a nested block (a local, a parameter
and a loop variable), `break` and `continue` outside a loop, `!` and `&&` on non-booleans, a missing return when the final
`if` has no `else`, and an accepted return when both branches return.

### Tasks

- [x] **T5.0 Syntax**
  - Lexer tokens and keywords above; grammar; AST nodes and operators; `AstBuilder`; `AstFormatter`.
  - Tests: lexer (`..` vs `.` vs `..=` vs a double, `0..10`, `&&` / `||` / `!` vs `!=`, new keywords, newline after
    `break` / `continue` ends the statement, newline before `else`); parser trees for `if` (alone, with else, with
    `else if` chains, with `else` on the next line), `while`, `for` with `..` and `..=`, `break`, `continue`, precedence
    of `!`, `&&`, `||` against comparison and equality, spans; syntax errors (`if {`, `if x`, `for i 0..3 {}`,
    `for i in 0.. {}`, `while`, `else {}` alone) report `NIR1001` without throwing.
  - **Done when:** these pass.

- [x] **T5.1 Binder**
  - Scope stack and the new `NIR2005` wording; conditions; `for` and loop depth; `break` / `continue`; logical operators;
    `AlwaysReturns`; new bound nodes.
  - Tests with positions for every rule and code above, including: a name reused in sibling blocks is fine; a name reused
    inside a nested block, as a parameter, as a loop variable is `NIR2005`; the loop variable is not visible after the
    loop; the loop variable cannot be assigned (`NIR3002`); an error-typed condition adds nothing more;
    `AlwaysReturns` cases (if/else both return, else-if chain with final else, if without else, while).
  - **Done when:** these pass.

- [x] **T5.2 Emitter**
  - Branches, loops, short circuit, `!`, labels for `break` / `continue`.
  - Tests (via `TestHost.Run`): every branch of an `if` chain; `else` on the next line; `while` counting; `for` with `..`,
    `..=`, empty and single-pass ranges; `break` and `continue` in nested loops (each affects only the innermost);
    short circuit (the right side's visible effect does not happen); `!`; truth tables for `&&` and `||`;
    factorial(10) = 3628800, fibonacci(15) = 610, `is_even(10)` / `is_odd(7)`, Collatz steps for 27 = 111; a function whose
    only `return`s are inside an `if` / `else`; an end bound of `int.MaxValue` exclusive does not overflow.
  - **Done when:** these pass.

- [x] **T5.3 Program tests and docs**
  - `Programs/slice5/` golden programs for every acceptance item and `Slice5ProgramTests`; update `architecture.md`
    (sections 4 to 8), the samples (add `control_flow.nir` and error samples), tick tasks, update `CLAUDE.md`.
  - **Done when:** all acceptance items pass, `dotnet build` has 0 warnings, `dotnet test` is green.

---

## Slice 6: strings and conversions

**Goal.** Build and compare text, and convert between `int` and `double`.

```
fn main() {
    let name = "Nir"
    let n = 42
    print("Hello, " + name + "!")
    print($"{name} has n = {n}")
    print("a" == "a")
    print((n as double) / 4.0)       // 10.5
}
```

**Decisions** are recorded in design.md "Strings and conversions" and decisions.md D-055 to D-057.

**Details fixed for this slice:**
- Lexer. New tokens: `INTERP_STRING_START` (`$"`), `INTERP_TEXT`, `HOLE_START` (the `{` that opens a hole),
  `HOLE_END` (the `}` that closes it), `INTERP_STRING_END`, keyword `AS`. New modes: `INTERP_STRING` (text between holes).
  The hole itself is lexed in the default mode: `{` in `INTERP_STRING` is `HOLE_START` and pushes the default mode.
  `NirLexerBase` keeps a stack of brace counters, one per open hole: a `{` inside a hole counts up, a `}` counts down, and a
  `}` at zero is retyped `HOLE_END` and pops back to `INTERP_STRING`. A hole counts as a bracket for the newline rule (a
  newline inside a hole is whitespace). Open strings are tracked in a stack, so an unterminated plain or interpolated string
  reports `NIR0002` at its opening quote once, as before. `INTERP_STRING_END` is in `EndsStatement`.
- Text tokens keep the raw text (backslashes included). Decoding happens in `AstBuilder`, which now receives the diagnostic
  bag: an unknown escape is `NIR0003` `unknown escape sequence '\q'`, spanning the backslash and the next character.
  `StringLiteralExpression.Value` and `InterpolatedText.Text` are the decoded text.
- Grammar. `interpolatedString : INTERP_STRING_START interpolatedPart* INTERP_STRING_END`,
  `interpolatedPart : INTERP_TEXT | HOLE_START expression HOLE_END`, a new expression alternative for it, and
  `operand=expression AS targetType=IDENTIFIER` (a cast) placed just below unary `-` / `!` and above `* / %`.
- AST: `InterpolatedStringExpression(Parts)`, `InterpolatedText(Text)`, `InterpolatedHole(Expression)`,
  `CastExpression(Operand, TargetType)`.
- Binder. `+` with two `string` operands gives `string`; `==` and `!=` with two `string` operands give `bool`; all other
  operators on strings stay `NIR3003`. An interpolated string becomes `BoundInterpolatedString(Parts)` where every part is
  a `string`: text parts are literals and a hole that is not a `string` is wrapped in a call of its type's `ToString()`
  (found with the existing instance-call lookup). A hole of type `()` is `NIR3012`
  (`cannot use a value of type '()' in an interpolated string`); a hole that already had an error makes the whole string an
  error and adds nothing. `x as T`: `T` must name `int` or `double` (`NIR2006` otherwise) and `x` must be `int` or
  `double` (`NIR3001` `cannot convert 'string' to 'int'` otherwise, at the cast); the result is `BoundConversion`.
- Emit. String `+` is `call String.Concat(string, string)`; `==` is `call String.op_Equality`, `!=` that plus a not. An
  interpolated string is `ldstr ""` when empty, `String.Concat` with 2, 3 or 4 strings when it has that many parts, one
  part followed by `ldstr ""` and `Concat(string, string)` (so a null becomes empty), and a `string[]` with
  `String.Concat(string[])` above 4. `int` to `double` is `conv.r8`; `double` to `int` is `conv.ovf.i4`; the same type is
  no instruction. All methods and types come from `ClrUniverse`.
- Out of scope: `<` / `>` on strings, format specifiers (`{x:F2}`), raw strings, Unicode escapes (`\u0041`), `as` for
  `bool` / `string`, string indexing and `.Length` (properties, slice 12).

**New diagnostics.** `NIR0003` `unknown escape sequence '\{text}'`; `NIR3012` `cannot use a value of type '()' in an
interpolated string`. `NIR2006`, `NIR3001`, `NIR3003` are reused.

**Acceptance programs** (`Programs/slice6/`): `+` chains; `==` / `!=` including two separately built equal strings;
every escape; interpolation with every type, expressions, calls, method calls, a string in a hole, a nested interpolated
string, `{{` `}}`, an empty hole-less `$""`, a hole at the start and the end; `as` both ways, truncation of negatives
(`-2.7 as int` is `-2`), precedence (`-x as double`, `a + b as double`), chained `as`; errors for: unknown escape,
unterminated plain and interpolated strings (the first one in the file), `"n" + 5`, `<` on strings, `"x" as int`,
`true as double`, `5 as string`, `5 as nope`, a `()` hole, an undefined name in a hole, `{` without `}`; crashes for
`1e300 as int`-style out-of-range conversions (as xUnit tests, not golden programs, because the exit code differs per OS).

### Tasks

- [x] **T6.0 Lexer, parser and AST**
  - Modes, tokens, `NirLexerBase` hole and open-string tracking, grammar, AST nodes, escape decoding (`StringEscapes`),
    `AstBuilder` taking the bag, `AstFormatter`.
  - Tests: lexer token streams for plain and interpolated strings (hole, nested braces in a hole, nested string in a hole,
    nested interpolated string, `{{` / `}}`, a newline inside a hole, unterminated cases, escaped quote), `as` keyword;
    escape decoding unit tests (each escape, unknown escape position, `{{` handling); parser trees for
    interpolation and casts, precedence of `as` (`-x as double`, `a + b as double`, `x as double as int`,
    `1 as double * 2.0`), spans; syntax errors report `NIR1001` or `NIR0002` without throwing.
  - **Done when:** these pass and every older test still passes.

- [x] **T6.1 Binder**
  - All binder rules above, `BoundInterpolatedString`, `BoundConversion`, `NIR3012`.
  - Tests with positions for every rule and code, including that errors in holes and operands add nothing more.
  - **Done when:** these pass.

- [x] **T6.2 Emitter**
  - Concat, equality, interpolation, conversions.
  - Tests (via `TestHost.Run`): every behaviour in the acceptance list, with printed values checked by hand; the
    out-of-range and `NaN` conversions crash with `OverflowException`.
  - **Done when:** these pass.

- [x] **T6.3 Program tests, samples and docs**
  - `Programs/slice6/` golden programs and `Slice6ProgramTests`; samples `strings.nir` and `conversions.nir` plus error
    samples; update `architecture.md` (sections 4 to 8), `CLAUDE.md`, tick tasks.
  - **Done when:** all acceptance items pass, `dotnet build` has 0 warnings, `dotnet test` is green.

---

## Slice 7: structs, methods, borrows

**Goal.** Declare value types with fields and methods, and pass them by copy or by borrow.

```
type Point = struct {
    x: int,
    y: int,
}

Point impl {
    fn new(x: int, y: int) -> Self {
        return Self { x = x, y = y }
    }

    fn length_squared(&self) -> int {
        return self.x * self.x + self.y * self.y
    }

    fn move_right(&mut self) {
        self.x = self.x + 1
    }
}

fn bump(p: &mut Point) {
    p.y = p.y + 1
}

fn main() {
    let mut p = Point::new(3, 4)
    p.move_right()
    bump(&mut p)
    print(p.length_squared())      // 4*4 + 5*5 = 41
    let q = p                      // a copy
    p.x = 10
    print(q.x)                     // 4
}
```

**Decisions** are recorded in design.md sections 2, 3, 4, 5 and 6 and decisions.md D-058 to D-061.

**Details fixed for this slice:**
- Lexer. Keywords `type`, `struct`, `impl`, `self`; token `AMP` (`&`; `&&` stays `AMPAMP`). `SELF` is in `EndsStatement`.
  `Self` is an ordinary identifier that the binder treats specially inside an `impl`.
- Grammar. Top-level items: `usingDirective | functionDeclaration | structDeclaration | implDeclaration`.
  `structDeclaration : TYPE name=IDENTIFIER EQ STRUCT LBRACE fieldList? RBRACE` with fields separated by `COMMA` or a
  terminator. `implDeclaration : typeName=IDENTIFIER IMPL LBRACE (functionDeclaration separated by terminators)? RBRACE`.
  A parameter list may start with a receiver `&self` or `&mut self`. A parameter type is `Name`, `&Name` or `&mut Name`.
  Return types, field types and `let` annotations are plain names. New expressions: `Name { field = value, ... }` (struct
  literal, guarded by a parser flag so it is not recognised in `if` / `while` / `for` heads outside parentheses),
  `receiver.field` (field access, binds like a method call), `self`, and `&x` / `&mut x` (a prefix alternative at the level of
  unary `-`). Assignment targets become `(name | self)(.field)*`.
- AST. `StructDeclaration(Name, Fields)`, `FieldDeclaration(Name, Type)`, `ImplDeclaration(TypeName, Methods)`;
  `FunctionDeclaration` gains `Receiver` (none, `&self`, `&mut self`); `Parameter.Type` becomes a `TypeReference(Name,
  Borrow)`; `StructLiteralExpression(TypeName, Fields)` with `FieldInitializer(Name, Value)`,
  `FieldAccessExpression(Receiver, Field)`, `SelfExpression`, `ReferenceExpression(Operand, IsMutable)`;
  `AssignmentStatement.Target` becomes an expression (a name, `self`, or a chain of field accesses).
- Types. `TypeSymbol` is no longer sealed. `StructTypeSymbol` has its fields (name, type, in declaration order) and its
  methods; `BorrowTypeSymbol(Element, IsMutable)` is only the type of a borrow parameter, a receiver, or a borrow
  expression. A struct is a value type whose CLR type is created by Emit; Emit keeps the map from struct symbol to CLR
  type and never asks the symbol for it.
- Binder. Struct and `impl` declarations are processed before functions and bodies, so order in the file does not matter.
  Duplicate struct names, a struct named like a primitive type or `Program`, a field of unknown type, a repeated field and a
  struct that contains itself are errors. `impl` for an unknown name is `NIR2006`; for a primitive, or a name that is not a
  struct of this file, `NIR2014`. Several `impl` blocks for one struct merge; two methods with one name are `NIR2003`.
  A method with `&self` / `&mut self` is an instance method, otherwise an associated function (`Point::new`). In an instance
  method `self` is a borrow variable of the struct's type, and `Self` means the struct.
  A struct literal needs every field once: unknown field is `NIR2011`, a repeated one `NIR2016`, a missing one `NIR2017`, a wrong
  type `NIR3001`. A field read or write needs a struct (or a borrow of one) as receiver: `NIR2011` otherwise. A write needs a mutable
  path: the root is `let mut`, `self` in `&mut self`, or a `&mut` borrow parameter; otherwise `NIR3002`.
  A method call checks arity (`NIR2004`) and exact argument types (`NIR3001`) like a function. A `&mut self` method on an
  immutable receiver is `NIR3002`; on a temporary (a call result) `NIR3013`. A call to an instance method as `Type::method` and
  to an associated function as `value.method` are `NIR2011` with a message saying how to call it.
  `&x` / `&mut x`: only as a direct argument (`NIR3014` elsewhere), only of a whole variable (`NIR3015`), `&mut` only of a
  mutable one (`NIR3002`). A borrow parameter must be a struct (`NIR3017`). A parameter `&mut Point` accepts only an
  argument written `&mut p`; `&Point` accepts `&p` or `&mut p`; a plain `Point` parameter rejects a borrow (`NIR3001`, with
  a message that names the fix). A bare use of a borrow variable is an implicit copy of the struct (`BoundDereference`).
  `self` outside a method with a receiver is `NIR2018`. A struct in an interpolation hole is `NIR3016`.
- Emit. A struct is a public sealed CLR value type (base `System.ValueType`, sequential layout) in the global namespace with
  public instance fields. Instance methods are instance methods of that type (CLR `this` is a managed pointer, so nothing is
  boxed or copied); associated functions are static methods of the type. A free function stays a static method of `Program`.
  A struct literal is a temporary local: `initobj`, then `ldloca; value; stfld` for each initializer in source order, then
  `ldloc`. A place (a variable, `self`, a chain of fields) is emitted as an address: `ldloca` for a local, `ldarga` for a
  by-value parameter, `ldarg` for a borrow (it already is an address), `ldflda` for each field step. A field read is the
  address followed by `ldfld`; a write is the address, the value, `stfld`; an instance call is the address, the arguments,
  `call`; a borrow argument is the address; a `BoundDereference` is `ldobj`. A call result used as a receiver goes through a
  temporary. Parameters of an instance method start at argument 1.
- Out of scope: `&` of anything but a whole struct variable, stored references, `==` / printing of structs, `with`, struct
  fields that are borrows, generics, tuples, and calling Nir methods from C# beyond what the CLR gives for free.

**New diagnostics.** `NIR2012` struct contains itself; `NIR2013` duplicate or reserved type name; `NIR2014` `impl` target is
not a struct of this file; `NIR2016` field given twice (also a repeated field declaration); `NIR2017` missing field;
`NIR2018` `self` outside a method; `NIR3013` `&mut self` method on a temporary; `NIR3014` borrow outside an argument;
`NIR3015` only a whole variable can be borrowed; `NIR3016` struct in an interpolation hole; `NIR3017` borrow of a non-struct.
Reused: `NIR2003`, `NIR2004`, `NIR2006`, `NIR2011`, `NIR3001`, `NIR3002`.

**Acceptance programs** (`Programs/slice7/`): the example above; nested structs and nested field writes; copies on
assignment, on passing and on returning; a `&mut` borrow changing the caller's value and a by-value parameter not; `&`
borrows read-only; methods calling methods; a borrow passed on (`&mut p` inside a function that took `&mut Point`); a method
chain; a struct in a loop; struct fields of every type; several `impl` blocks; and errors for every code above plus
the struct-literal-in-condition parse and its parenthesised fix.

### Tasks

- [x] **T7.0 Syntax**
  - Tokens, grammar (with the struct-literal flag and predicate), AST, `AstBuilder`, `AstFormatter`; the existing
    `Parameter` / `AssignmentStatement` / `FunctionDeclaration` changes ripple into the binder only far enough to compile
    (struct features themselves are bound in later tasks).
  - Tests: lexer (`&` vs `&&`, new keywords, `self` ends a statement); parser trees for struct declarations (comma and
    newline separators, empty struct, trailing separator), `impl` blocks, receivers, borrow parameters, struct literals
    (one line, many lines, nested, in `return` and `let`), field access chains, `self`, `&x` / `&mut x`, assignment
    targets (`p.x`, `a.b.c`, `self.x`); the condition rule (`while n != limit { }` parses as a loop,
    `if (P { x = 1 }) == q { }`-style parenthesised literals parse, a bare literal in a head is a syntax error); spans;
    broken input reports one `NIR1001` without throwing.
  - **Done when:** these pass and every older test still passes.

- [x] **T7.1 Structs end to end**
  - Split `ProgramBinder` out of `Binder.cs` into a partial class spread over several files before adding struct code.
  - Struct declarations, fields, literals, field read and write, nested structs, copies, by-value parameters and results,
    mutability by path, `NIR2012` to `NIR2017`; Emit of structs, fields, literals and places.
  - Tests with positions for every rule and code; end-to-end programs.
  - **Done when:** these pass.

- [x] **T7.2 Methods**
  - `impl`, instance methods with `&self` / `&mut self`, associated functions, `Self`, `self`, method calls on places and
    temporaries, mutability checks, `NIR2014`, `NIR2018`, `NIR3013`; Emit of instance and static methods on the struct.
  - Tests and end-to-end programs.
  - **Done when:** these pass.

- [x] **T7.3 Borrows**
  - Borrow parameter types, `&x` / `&mut x` arguments, implicit copy of a borrow variable, passing a borrow on,
    `NIR3014`, `NIR3015`, `NIR3017`; Emit with CLR `ref` parameters.
  - Tests and end-to-end programs.
  - **Done when:** these pass.

- [x] **T7.4 Program tests, samples and docs**
  - `Programs/slice7/` golden programs and `Slice7ProgramTests`; samples `structs.nir` and error samples; update
    `architecture.md`, `CLAUDE.md`, tick tasks.
  - **Done when:** all acceptance items pass, `dotnet build` has 0 warnings, `dotnet test` is green.

---

## Later slices (outline; detailed when each starts)

**Slice 8a: tuples** (done). Decisions D-062, D-063, D-066; design.md section 2. Anonymous and named tuple types, literals,
`t.0` / `t.name` access and assignment, `let (a, mut b, _) = t`, `type Pair = (x: int, y: int)`, `ValueTuple` in Emit.
Tasks: `T8a.0` syntax (types, literals, index, destructuring, alias), `T8a.1` binder (type resolution, equality of types, places),
`T8a.2` emit (`ValueTuple`, structs inside tuples), `T8a.3` tests (parser, binder, end to end, `Programs/slice8/`), samples, docs.
All ticked. Any number of elements (nested `ValueTuple.Rest`). Left for later: `==` and printing of tuples, borrows of tuples.

**Slice 8b: unions and `match`** (done). Decisions D-064, D-065, D-067 to D-071; design.md sections 2 and 11.

```
type Circle = struct { r: double }
type Square = struct { side: double }
type Shape = union { Circle, Square }

fn area(s: Shape) -> double {
    return match s {
        c: Circle => 3.0 * c.r * c.r,
        q: Square => q.side * q.side,
    }
}

fn main() {
    let s: Shape = Circle { r = 2.0 }      // a member converts to the union where a union is expected
    print(area(s))
    match s {
        c: Circle => print("circle")
        _: Square => { print("square") }
    }
}
```

Tasks:

- [x] **T8b.0 Syntax.** Tokens `union`, `match`, `=>`; `type X = union { A, B }` (two or more members, each a type);
  `match scrutinee { name: Type => expression-or-block, ... }` as an expression (arms separated by comma or newline; the
  scrutinee is parsed with struct literals forbidden like an `if` head). AST `UnionDeclaration`, `MatchExpression`, `MatchArm`.
  Parser tests; `AstFormatter`.
- [x] **T8b.1 Binder.** `UnionTypeSymbol` (nominal, members in order); union names resolved lazily like aliases, with the same cycle
  check; members: no duplicates, no unions, no `()`. Implicit conversion of a member value to the union at `let` with annotation,
  assignment, `return`, call arguments, struct literal fields (`BoundUnionWrap`). `match`: scrutinee must be a union; each arm names
  a member type; every member exactly once; arms of a value-match are expressions of one type; statement-match arms may be blocks;
  arm variable immutable, `_` drops it; `return` analysis through a match whose arms all return. Unions cannot be compared,
  printed, interpolated, borrowed or have members.
- [x] **T8b.2 Emit.** A union is a public sealed CLR value type with an `int Tag` and one field per member (a tagged struct, so
  nothing is boxed); wrapping sets tag and field; `match` stores the scrutinee, tests the tag per arm, loads the arm variable from the
  member field. Unions inside tuples work like structs inside tuples.
- [x] **T8b.3 Tests and docs.** Parser, binder and end-to-end tests; `Programs/slice8/`; `samples/unions.nir` and error samples;
  `architecture.md`, `CLAUDE.md`, memory.

**Slice 9a: errors in Nir** (done). Decisions D-072 to D-079; design.md section 10.

```
type ParseError = error { message: string, line: int }

fn parse_port(s: string) -> ParseError!int {
    if s == "" { return error ParseError { message = "empty", line = 1 } }
    return 8080
}

fn load(s: string) -> anyerror!int {
    let port = try parse_port(s)            // returns the error to the caller (widened to anyerror)
    return port + 1
}

fn main() -> anyerror! {
    let a = parse_port("") catch 80                       // fallback value
    let b = load("x") catch e => {                         // handle it; the block must return
        print($"failed: {e.message}")
        return
    }
    match parse_port("1") { n: int => print(n), e: ParseError => print(e.line) }
}
```

Tasks:

- [x] **T9a.0 Syntax.** Keywords `error`, `try`, `catch`. `type E = error { fields }` (a `StructDeclaration` with `IsError`); result type
  `E!T` and `E!` in `typeSyntax` (`ResultTypeSyntax`); `error E { field = v }` and `error message` expressions; `try expr` (prefix, unary
  precedence); `a catch b` and `a catch name => expr-or-block` (lowest precedence). Parser tests; `AstFormatter`.
- [x] **T9a.1 Types.** Error types are `StructTypeSymbol`s with `IsError` (classes, immutable, no `impl`, no borrows, may not contain themselves
  by value); `AnyErrorTypeSymbol` (`anyerror`, with `message`); `ResultTypeSymbol : UnionTypeSymbol` (members `[E, T]`, structural equality,
  interned, `E!` has no success member). `UnionTypeSymbol` loses `sealed` and gets a `ClrName`. Conversions: `E` -> `anyerror`, `E`/`T` ->
  `E!T`, a declared error -> `anyerror!T`. Checks: `E` must be an error type, `T` must not be an error or a result. Binder tests.
- [x] **T9a.2 Expressions.** `error E {..}` literal (all fields once; plain `E {..}` on an error type is an error), `error "text"`; field
  reads on errors and `anyerror.message`; `try` (statement-level positions only; function must return a compatible `E2!U`),
  `catch` (fallback and handler forms; block handlers only at statement level and must return unless the success is `()`); `match` on `E!T`
  (arms `v: T`, `e: E`); `return` bare and falling off the end succeed in `E!` functions; `main` may be `-> E!`. Binder tests.
- [x] **T9a.3 Emit.** Error types are classes deriving from a generated `Nir.AnyError` (field `Message`, set from the declared `message` field or
  the type name); result types reuse the union layout (`Tag`, `Case0` error, `Case1` value; error is tag 0); `try`/`catch` branches on the tag
  (`try` returns a rebuilt result when the type widens); an `E!` entry point gets a generated `Main` that prints `error: <message>` to stderr
  and exits with 1. End-to-end tests.
- [x] **T9a.4 Tests and docs.** Golden programs in `Programs/slice9/`, `samples/errors_handling.nir` and error samples, design.md section 10,
  architecture.md, CLAUDE.md, memory.

Left for 9b and later: `try` on .NET calls (exceptions become `anyerror`, `e.exception`), `impl` on error types, `try` anywhere inside an
expression (needs stack spilling), `==` on errors, converting `E!T` to `anyerror!T` without `try`.

**Slice 9b: C# interop for errors** (done). Decisions D-080, D-081. `try` / `catch` on calls into .NET: a thrown exception becomes an `anyerror`
with the exception's `message`; a call without `try` that throws still crashes. `e.exception` waits for slice 12. Tasks: the binder wraps the
call in `BoundProtectedCall` (`T9b.0`), Emit makes the call in a generated helper with a CLR try block (`T9b.1`), tests, golden programs
and `samples/dotnet_errors.nir` (`T9b.2`). All done.

**Slice 9c: the rest of the primitive types** (decisions D-083 to D-090; design.md "Numbers", "Number literals", "Strings and
conversions", "Characters"). Adds `byte sbyte short ushort uint long ulong float decimal char` next to `int double bool string`.
Done in the order below; every task ended with the full `dotnet test` green. **Done** (1,9xx tests green; see `CLAUDE.md`).

Acceptance: a program that declares a value of each of the 12 number and char types, prints it (also inside `$"..."`), does arithmetic and
comparisons on each, converts between them with `as` (including a failing conversion handled with `catch`), uses literals by
context and with suffixes, loops over a `long` range and over `'a'..='z'`, and passes them through functions, struct fields and tuples.

- [x] **T9c.1 The types exist**
  - `TypeNames`, `ClrNames`, `ClrTypes`, the new `TypeSymbol` statics, `LookupPrimitive` and `FromClr` (so .NET methods with these types become
    candidates); one place that answers "is integer / signed / floating / number / char" instead of today's `IsNumber(int || double)`.
  - `print` and interpolation for every type. There is no `Console.WriteLine(byte | sbyte | short | ushort)`: widen those to `int` first.
  - Tests: `let x: long = ...` is not yet writable, so test with parameters, struct fields and `.NET` calls (`Math::Abs(long)`); an unknown type
    name still reports `NIR2006`. Check which older tests used a `long` / `char` result as their "unsupported .NET type" example and update them.
  - **Done when:** these pass.

- [x] **T9c.2 Conversions with `as`**
  - The full matrix of D-086: IL for every pair (`conv.ovf.*`, `.un` variants for unsigned sources, `decimal` through its conversion operators
    or constructors, `char` as an unsigned 16-bit integer); errors for `bool`, `string`, `char` with floating types.
  - Tests: one binder test per rejected pair, one end-to-end test per source kind showing the value and the `OverflowException` case
    (`300 as byte`, `-1 as ulong`, `3e9 as int`, `NaN as int`, `1e30 as decimal`).
  - **Done when:** these pass.

- [x] **T9c.3 Operators on every type**
  - `BinaryResultType`, `BindUnary` and the emitter for all widths: checked `+ - *` followed by the narrowing conversion for 8/16-bit types, unsigned
    division and remainder (`div.un`, `rem.un`) and comparisons (`clt.un`, `cgt.un`), IEEE `float`, `decimal` through `op_Addition` etc.,
    unary `-` rules. Mixed types stay an error with the existing wording.
  - Tests: an arithmetic table per type (including the overflow edge of each width), unsigned comparison of large values, `float` vs `double`
    rounding, `decimal` exactness (`0.1m + 0.2m == 0.3m` style), `-x` on `uint` rejected.
  - **Done when:** these pass.

- [x] **T9c.4 Number literals**
  - Lexer: a whole or fractional literal may end in `_<typename>`; the binder validates the suffix. Binder: pass an *expected type* into
    literal binding (annotation, assignment, `return`, argument, field, other operand, range bound, unary minus); a negative literal is checked as a
    whole; new diagnostics for out of range, suffix not allowed, whole-number literal where a fraction type is expected.
  - Tests: lexer tokens, one binder test per context in design.md, every out-of-range edge (`255`/`256` byte, `-128`/`-129` sbyte, `u64` max),
    `decimal` and `float` rounding of literals.
  - **Done when:** these pass.

- [x] **T9c.5 char**
  - Lexer: `CHAR_LITERAL` with the escapes of D-088 (reuse `StringEscapes`), errors for `''`, `'ab'` and a non-BMP character; binder and emitter;
    comparisons; `as` to and from integers (done in T9c.2, tested again here with literals).
  - **Done when:** tests for every rule in design.md "Characters" pass.

- [x] **T9c.6 Ranges over other types**
  - `BindFor` / `BindRangeBound` take any integer type or `char` (same type both sides); the emitter's loop variable and limit locals use that
    type; `a..=b` must not overflow when `b` is the maximum (`0..=255_byte` runs 256 times).
  - Tests: `long`, `byte` (inclusive max), `uint`, `char` ranges; mixed bounds rejected; a `double` bound rejected.
  - **Done when:** these pass.

- [x] **T9c.7 `catch` and `try` on `as`**
  - The conversion runs in a generated `Protected<n>` helper as .NET calls do (D-081); `big as byte catch 0`, `catch e => expr`, block handler, `try`.
  - Tests: success path, failing path, the message text, `try` inside a function returning `anyerror!T`.
  - **Done when:** these pass.

- [x] **T9c.8 Programs, samples, docs**
  - `Programs/slice9c/` golden programs, `samples/numbers.nir` (and an error sample per new diagnostic), leave `samples/test.nir`
    (the user's scratch file, skipped by `SamplesTests`) alone; update `architecture.md` (types, conversions, literal typing, emit), `code-map.md`,
    `CLAUDE.md` status, `docs/handoff.md`, `VS Code` grammar (char literals, literal suffixes, new type keywords).
  - **Done when:** all acceptance items pass, a clean rebuild has 0 warnings, `dotnet test` is green.

Additions made while building it (decision D-092): a tuple literal converted to a tuple type, and a union with exactly one fitting member,
also type their literals; a .NET overload may be chosen through its literals; `try` on a conversion needs parentheses
(`try (x as byte)`) because `try` binds tighter than `as`; a whole-number literal that wants a fraction type gets a hint in the error.

**Slice 9d: knowing a type** (decisions D-093, D-094, D-096; design.md "Knowing a type: `typeof`"). **Done.**
The run-time half, `std::types::runtime_type(x)` returning `RuntimeTypeInfo`, is a task of slice 11 (it needs `std`): union tag switch for
unions and results, class name for `anyerror`, tests for each member, both states of `E!T`, two different errors, and a non-union.

- [x] **T9d.1 Syntax**
  - Keyword `typeof` in the lexer and parser; `TypeofExpression(operand)` in the AST where the operand is an expression
    or a type (try the type first, then the expression); `AstBuilder`; friendly names in `ParserErrorListener`; `typeof`
    must also be in the VS Code grammar (`VsCodeGrammarTests` checks every keyword).
  - Tests: parser tests for a variable, an expression, a type name, a tuple type, a missing `)`.
  - **Done when:** these pass.

- [x] **T9d.2 `TypeInfo` and `typeof`**
  - Built-in struct symbol `TypeInfo { name: string }` known to the binder (first reserved everywhere, then moved into `std::types`, T9d.4); the operand
    is bound but not evaluated, the name text is the type's display text. As built there is no `BoundTypeof` node: `typeof` becomes a
    `BoundStructLiteral` of a constant, so the emitter needed no change and `TypeInfo` is emitted (only when used) like any struct.
  - Tests: every primitive, struct, tuple, union, result, `anyerror`, a type name, an expression with a side effect (must not run).
  - **Done when:** these pass, and the operand is shown not to run.

- [x] **T9d.3 Errors, programs, samples, docs**
  - Diagnostics for: `typeof()` with no operand, `TypeInfo` declared by the user, a field that does not exist (`typeof(x).size`).
    Golden programs in `Programs/slice9d/`, a sample `samples/typeof.nir`, `architecture.md`, `code-map.md`, `CLAUDE.md` status, `handoff.md`.
  - **Done when:** all of the above pass, a clean rebuild has 0 warnings, `dotnet test` is green.

- [x] **T9d.4 `TypeInfo` lives in `std::types` (D-097)**
  - `using std::types::TypeInfo` / `std::types::{TypeInfo}` (a hard-coded table until slice 11); `typeof(x).name` needs no import; the name is
    otherwise unknown with a hint; the built-in struct is emitted as `Nir.TypeInfo`; a file may declare its own `TypeInfo`.
  - Tests: both import forms, no import, bad `std` paths, duplicate import, clash with a declared `TypeInfo`, own `TypeInfo` next to the built-in one.
  - **Done when:** these pass.

**Slice 10: heap references.** `&T`, `&mut T`, `&T {...}`, `&` of a variable (hoisted to a `Ref<T>`), `?T`, null. Design
sections 3, 4, 6, 7.

**Slice 11: modules.** `namespace`, multi-file compilation, `using` for Nir code, `impl` placement rule, `extend`
blocks and method imports, C# extension methods. Design section 9.

**Slice 12: enum, interface, arrays, generic C# types.** Needed for real library use (`List<T>`, `byte[]`).

**Milestone M1.** An encrypt/decrypt CLI (read a file, transform its bytes with a key, write the result, handle errors
with `try`) written in Nir and compiled with `nir`. The real program is written when slice 9 is done; it fixes the exact
set of missing BCL-interop features.

## Backlog (not scheduled; do not start without being asked)

- `nir dump tokens|ast|bound <file>` debug commands (the old repo had token and AST printers to copy ideas from).
- Better syntax errors: once the parser recovers well (for example a hand-written one), report more than the first.
- Short per-code labels for the text beside the carets (rustc says "not found in this scope" there and the full message
  in the header); today the label repeats the message.
- `--color=always|never|auto` flag (today: `NO_COLOR` and `CLICOLOR_FORCE` environment variables).
- `nir fmt`, language server, REPL.
- A Nir-written `std` library (`std::fs` etc.) on top of `clr::cs`.
- Removing ANTLR in favour of a hand-written parser (must only touch `Parsing/`).
- Optional `A | B` union syntax.
- `frozen` types, structural types, Rust-style borrow checking.
- Syntax error hint for a struct literal in an `if` / `while` head (today: `no viable alternative at input 'P'`; suggest parentheses, as Rust does).
- Friction found while writing `samples/apps/bank.nir` (candidates for the next slices):
  - A member of a union returned from a function whose result is `E!Union` needs an intermediate `let c: Union = Member {..}`; converting a member straight to `E!Union` (two steps) is not done.
  - No `Length`/other .NET properties and no arrays: parsing a line means `IndexOf` and `Substring`; no `Split`, no command-line arguments.
  - A null .NET string (end of input) is not `== ""`; the demo tells them apart with `String::IsNullOrEmpty` (`?string` is slice 10).
  - A `catch e => print(..)` handler only fits a result that carries no value; for others use a block that returns or give a value.
