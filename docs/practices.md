# Practices

Rules for working on this repo. They apply to people and to coding agents alike.

## 1. Code style

The compiler enforces most of this (`.editorconfig` + `Directory.Build.props`, warnings are errors). Do not fight it.

- File-scoped namespaces. `using` directives outside the namespace, `System` first.
- Allman braces. 4-space indent in `.cs`, 2 in project files and JSON. LF line endings, final newline.
- Names: types, methods, properties `PascalCase`. Parameters and locals `camelCase`. Private/internal fields
  `_camelCase`. Interfaces `IName`.
- `Nullable` is on. Do not use `!` (null-forgiving) unless a comment on the same line says why it is safe.
- Use `readonly` fields and `init`/primary-constructor records. Prefer immutable data.
- AST and bound-tree nodes: `sealed record`, collections as `ImmutableArray<T>`. No mutable public collections.
- Prefer a `switch` expression over type tests on node hierarchies. The default arm is
  `_ => throw new UnreachableException()` with no message. Reaching it is a compiler bug, not a user error.
- One public type per file, file named after the type (exception: small related records may share a file named after
  the group, e.g. `Expressions.cs`).
- Comments explain **why**, never what. No commented-out code. No TODO without a task id: `// TODO(T2.4): ...`.
- XML doc comments on public types of the facade (`NirCompiler`, `Diagnostics`). Elsewhere, only where the intent is
  not obvious from the name.

## 2. Compiler rules

- **User errors are diagnostics, never exceptions.** A phase reports a `Diagnostic` and continues if it can.
- **Exceptions mean compiler bugs.** Use `InvalidOperationException` or `UnreachableException`. The CLI turns them
  into exit code 3.
- A phase never reads another phase's input. Emit never looks at the AST; Binder never looks at ANTLR contexts.
- Error recovery: after reporting an error, produce a placeholder (an "error" type/node) so later checks do not pile up
  secondary errors on the same mistake. One mistake should produce one diagnostic where practical.
- No global mutable state. Pass `DiagnosticBag` and `SourceText` explicitly.
- No culture-sensitive APIs (`InvariantGlobalization` is on). Use ordinal comparison for strings.
- Do not add an abstraction (interface, base class, generic helper) until a second concrete use exists.

## 3. Testing

Framework: xUnit 2.9.3. One test project: `tests/Nir.Tests`.

**Test first.** For every task: write the failing test, see it fail for the right reason, then write the code.
Every bug fix starts with a test that reproduces the bug.

**Levels:**

1. **Phase unit tests** (`Lexing/`, `Parsing/`, `Semantics/`, `Emit/`): feed one phase, check its output.
   - Lexer: assert the list of `(token name, text)` pairs for a source string.
   - Parser: assert the AST shape through `AstFormatter` (a test-only helper in `Support/` that prints the AST as
     indented text); compare to an expected multi-line string.
   - Binder: assert reported diagnostics (`code`, `line`, `column`) for an input; for valid input assert no diagnostics.
2. **Program tests** (`Programs/`): compile and run whole `.nir` programs, compare output. See below.
3. **CLI tests** (`Cli/`): a few tests that run the real `nir` and check exit codes and stderr format.

**Program tests (golden files).** Layout: `tests/Nir.Tests/Programs/<slice>/<name>.nir` plus exactly one of:
- `<name>.stdout`: expected stdout of a program that compiles and runs. Compared exactly (LF newlines). Exit code must be 0.
- `<name>.diag`: expected compile errors, one per line, format `line:col CODE`. Messages are not compared here (unit
  tests cover messages). The program must fail to compile with exactly these diagnostics in this order.

One `[Theory]` per slice folder discovers every `.nir` file in it via `MemberData`. The `.nir` files are copied to the
test output directory (`<None Include="Programs/**" CopyToOutputDirectory="PreserveNewest" />`). Adding a program test
means adding two files; no C# change.

**Test helpers (`Support/`):**

```csharp
public static class TestHost
{
    public static CompilationResult Compile(string source, string name = "test.nir");
    public static (string Stdout, string Stderr, int ExitCode) Run(string source);   // compile, emit to temp dir, run via `dotnet`
}
```

**Naming.** Test classes `<Subject>Tests`. Test methods `Pascal_Snake_Case` describing behavior, for example
`Newline_after_identifier_becomes_semicolon`. (`.editorconfig` exempts `tests/**.cs` from the normal naming rule.)

**Rules.**
- Tests must not depend on each other, on order, or on the current directory.
- No sleeping, no network, no files outside a temp directory.
- Do not test ANTLR itself. Test what our lexer/parser produce.
- Keep each test small: one behavior, 1 to 10 lines of source.
- Never weaken or delete a test to make a build pass. Fix the code, or ask.

## 4. Diagnostics

Defined in `docs/architecture.md` section 4. Short version: stable `NIR####` codes in `DiagnosticCodes`, never reused;
lowercase message with no trailing period; position is 1-based line and column; printed as
`file:line:col: error NIRxxxx: message`. Adding a diagnostic means: add the constant, add a unit test asserting code and
position, and add a row to the table in `architecture.md` if it is new in the slice.

## 5. Git

- Default branch `main`. Work for a slice on a branch `slice-<n>-<short-name>` (for example `slice-1-hello`). Merge it
  into `main` fast-forward, only when the slice's acceptance criteria all pass.
- Commits are small and each one builds and passes tests. Subject: imperative, at most 72 characters, no trailing
  period (`Add newline-to-semicolon rule to lexer`). Body (optional) explains why, not what.
- Never commit generated files: `bin/`, `obj/`, `out/`. (`.gitignore` already covers them.)
- Coding agents commit only when the user asks, and never push.

## 6. Changing the design

The language is defined by `docs/design.md`. To change it:

1. Discuss and decide with the user. Do not guess.
2. Edit `docs/design.md` (update the tag: locked / proposed / open).
3. Append an entry to `docs/decisions.md` with date, decision, and reason.
4. Update `docs/architecture.md` and `docs/roadmap.md` if structure or tasks change.
5. Only then write code.

If code and design disagree, the design is right and the code is the bug, unless step 1 changed the design.
A slice may only use language features tagged **locked** in `design.md`. If a task needs an **open** or **proposed** item,
that is a "decision required" and the user must decide before coding (each slice in `roadmap.md` lists these).

## 7. ANTLR gotchas

- `Antlr4BuildTasks` runs the Java ANTLR tool at build time. There is no `java` on PATH on this machine; the task
  downloads a JRE into `~/.jre` on the first build and uses `~/.m2/antlr4-4.13.1-complete.jar`. Both already exist
  from the previous repo.
- **Never run two `dotnet build`/`dotnet test` commands at once.** The old repo had a corrupted JRE download because two
  builds overlapped. Also be careful with the 2-minute default timeout on the first build: run it with a longer
  timeout, or in the background, and wait for it to finish.
- Symptoms of a corrupt `~/.jre` or jar: "Cannot find Java executable", "Could not find or load main class
  org.antlr.v4.Tool". Check for truncated files in `~/.jre` and `~/.m2`, delete the bad one, and rebuild once.
- Lexer rule order matters: for equal-length matches the earlier rule wins. Keywords go before `IDENTIFIER`.
- `NirLexerBase.EndsStatement` must list every token that can end a statement. Forgetting a new token produces silent
  "missing semicolon" bugs. Each new such token needs a lexer test.
- Generated code lands in `obj/`. Never edit it, never commit it. After changing `<Visitor>`/`<Listener>` in
  `Nir.Compiler.csproj`, delete `src/Nir.Compiler/obj` and `bin` once; stale generated files otherwise break the build.
- Do not name a class `Parser` (or `Lexer`) in `Nir.Compiler.Parsing`: the generated `NirParser : Parser` would bind to it.
- ANTLR types stay in `Parsing/`. See `architecture.md` section 3.

## 8. Definition of done (for any task)

- The "Done when" list in `roadmap.md` is fully true.
- `dotnet build` finishes with 0 warnings and 0 errors, and `dotnet test` is fully green (run them one after the other).
- New behavior has tests; new diagnostics have tests.
- `docs/` is updated if a decision, structure or diagnostic changed.
- The task is ticked in `roadmap.md` and the status block in `CLAUDE.md` is updated.

## 9. What not to do

- Do not build ahead of the roadmap. No "while I am here" features.
- Do not copy code from the old repo (`/mnt/Codes/Project/Nir`) without reading and understanding it; the only planned
  reuse is `reference/NirLexerBase.cs` logic and the lexer-mode design in `reference/NirLexer.g4`.
- Do not suppress warnings, use `#pragma warning disable`, or relax `Directory.Build.props`. The single allowed
  exception is the CLI's top-level `catch (Exception)` in `Program.cs` (CA1031), which turns a compiler bug into exit
  code 3. Any other suppression needs a user decision first.
- Do not invent syntax or semantics that `design.md` does not define.
- Do not leave a failing or skipped test.
