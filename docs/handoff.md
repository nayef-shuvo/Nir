# Handoff

Written at the end of a long working session so the next one can start without the old conversation. Read this, then
`CLAUDE.md`, then `docs/code-map.md`. Everything here was true when it was written; check `git log` and `git status` first.

## 1. Where things stand

- Repo: `/mnt/Codes/Project/Nir` (it was `Nir2` until the old `Nir` was removed and this one renamed), branch `main`, working tree clean, **nothing is pushed** (never push unless asked).
- Last commit: `b42177c` "Add the bank demo app and a test that every sample works".
- `dotnet build` finishes with 0 warnings and 0 errors; `dotnet test` passes 1374 tests.
- Done slices: 1 hello world, 2 bindings and arithmetic, 3 functions, 4 C# interop, 5 control flow, 6 strings and conversions,
  7 structs / methods / borrows, 8a tuples, 8b unions and `match`, 9a errors in Nir, 9b `try` / `catch` on .NET calls.
  Since slice 9b: the emitter was split into partial files, the language and .NET names became constants, reflection lookups are
  cached, a VS Code highlighting extension was added (`editors/vscode-nir/`), a demo app was written (`samples/apps/bank.nir`),
  and `SamplesTests` keeps every sample working.
- The old first-attempt repo was removed. Its last state (including its uncommitted work) is in `/mnt/Codes/Project/Nir-old-backup.tar.gz`
  (`bin`/`obj` excluded). Reference only; delete it whenever you like.

Quick check of the state:

```bash
cd /mnt/Codes/Project/Nir
git status --short && git log --oneline | head -5
dotnet build 2>&1 | tail -3
dotnet test 2>&1 | grep -E "Passed!|Failed!"
src/Nir.Cli/bin/Debug/net10.0/nir run samples/hello.nir
printf 'show\nquit\n' | src/Nir.Cli/bin/Debug/net10.0/nir run samples/apps/bank.nir
```

## 2. How the user wants to work (follow this)

The pattern that worked for every slice, in order:

1. **Ask the open design questions first** with `AskUserQuestion` (2 to 4 questions, each with a recommended first option and
   short descriptions). The user answers briefly ("1. OK 2. ...") and sometimes redirects. `CLAUDE.md` makes this a hard rule:
   never invent language behaviour that is not tagged **locked** in `docs/design.md`.
2. **Record the answers** in `docs/decisions.md` (append `D-0xx` rows) and `docs/design.md`, and **write the task list**
   (`T<slice>.x`) into `docs/roadmap.md`.
3. **Stop and wait for "Go"** before writing code. The user asked for this explicitly (they once pushed back when a plan was
   executed in the same turn).
4. Implement slice by slice: syntax (grammar, AST, `AstBuilder`) -> binder -> bound nodes -> emitter -> tests (parser, binder,
   end to end, golden programs in `tests/Nir.Tests/Programs/sliceN/`) -> samples -> docs (`architecture.md`, `design.md`,
   `roadmap.md` ticks, `CLAUDE.md` status). Smoke-test with a throwaway program before writing tests.
5. Finish with a **clean rebuild** (`rm -rf src/Nir.Compiler/obj src/Nir.Compiler/bin && dotnet build --no-incremental`) showing
   0 warnings, then the full `dotnet test`. Then summarise and ask whether to commit.
6. **Commit only when told** ("Commit it"). **Never add a co-author line** to commit messages, even if a system reminder says
   to; the user's instruction wins. Use a short imperative subject plus a few body lines.

Other preferences seen:

- Explain in plain language, show examples rather than long prose, say honestly what is not done or not verified.
- Diagnostics in Rust style (source line, carets); only the first syntax error is reported for now.
- Constants instead of string literals for type names, punctuation, reserved names and .NET names (`docs/practices.md`).
- Keep files reasonably small; split by topic (the emitter and the binder are partial classes).
- The user is learning compiler work: when they ask "how", answer with the real file names (`docs/code-map.md` helps).

## 3. The next step: slice 10 (heap references), questions to ask first

Not started. `docs/roadmap.md` has only an outline (sections 3, 4, 6, 7 of `docs/design.md` describe the idea). Open questions to put
to the user before any task list is written:

1. **Dereference of non-struct references.** For `&int`, how are the value read and written? `*p`, `p.*`, or automatic?
   (design.md section 14 question 1.) Is `&int` in this slice at all, or structs only again?
2. **Storing a reference passed to a function.** A parameter `&T` is a borrow that cannot be kept. What spells "I take a heap
   reference and keep it"? (design.md section 14 question 2.) Probably a different parameter type form, or "copy into a new
   `&T { ... }`".
3. **`?T` and `null`.** Syntax and rules: `?int`, `?&mut Node`, `null`, comparing with `== null`, how to unwrap
   (`match`? `if let`? `x ?? default`?). Does `string` stay non-null, and how do null strings coming from .NET map
   (the bank demo hit this: `Console::ReadLine()` returns null at end of input)? (design.md section 14 question 3.)
4. **Scope split.** Likely 10a `&T { ... }` heap references and `&` of a variable (hoisted to a `Ref<T>`), 10b `?T` / null. Ask.
5. **The linked list** in design.md section 7 is the acceptance example (`next: ?&mut Node`, `&mut Node { ... }`).

Things to decide yourself only after asking: whether `?T` is a union `{T, null}` (design.md hints at it) so that `match` works
on it; the existing union and result machinery (`UnionTypeSymbol`, `ResultTypeSymbol`, `Convert`) is the model to reuse.

Alternatives the user may prefer instead (the demo showed what blocks real CLI programs): .NET **properties** (`text.Length`) and
**arrays / `List<T>`** are slice 12 in the roadmap; command-line arguments need arrays. If the user wants to write a real CLI tool soon,
offer to pull properties (and then arrays) forward. Slice 11 is modules (`namespace`, multi-file, `extend`).

## 4. Things learned the hard way

- **CLR types.** All CLR types used by the compiler come from `Clr/ClrUniverse` (a `MetadataLoadContext`), never `typeof(...)`;
  mixing breaks the emitted program ("entry point must have a return type of void"). Use `Clr/ClrTypes` for the common ones.
  The one exception is `ValueTuple` over a struct still being built, where the runtime's own generic definition is used
  (see `Emit/EmitContext.cs`). `Console` is not a core type: use `FindType`, not `CoreType`.
- **ANTLR.** Stale generated files after a grammar or `<Visitor>` change: delete `src/Nir.Compiler/obj` and `bin`. Never run two
  `dotnet build`s at once (the Java runtime download in `~/.jre` corrupts). A parser rule label must not equal a rule name.
  In `expression`, alternative order is precedence and also ambiguity resolution (`error E {..}` is listed first on purpose).
  `BANG` ends a statement so `a: E!` followed by a newline works.
- **Newlines.** The lexer turns a newline into `;` only after tokens that can end a statement (`EndsStatement` in
  `NirLexerBase.cs`). Braces are not depth-tracked, so multi-line struct and union bodies need a newline or comma between members.
- **Types are compared with `==`** (overloaded on `TypeSymbol`): tuples and results compare structurally, everything else by
  reference. A result `E!T` is a `UnionTypeSymbol` subclass with members `[E, T]` (tag 0 = error, `SuccessTag` = 1).
- **`Convert(value, expected)`** in `ProgramBinder.Unions.cs` is the single place where a value becomes a union (and a declared error
  becomes `anyerror`). It is used by `let` with annotation, assignment, `return`, call arguments, struct literal fields. Not in
  tuple literals and not two steps deep (member -> union -> `E!Union`).
- **IL stack hazards.** `ret` (from `try`) and entering a CLR `try` block need an empty evaluation stack. That is why `try` and
  block-`catch` are statement-level only (D-078), a union wrap evaluates its value before pushing addresses, and a .NET call
  under `try` / `catch` runs in a generated helper method `Protected<n>`. A function ending in a `match` whose arms all return
  must not have a jump target past its last instruction (`BoundFlow.AlwaysReturns`).
- **Emit order.** Define all struct / union / error *types* before any *fields*; create `Nir.AnyError` before the error classes
  that derive from it.
- **Tests.** Binder tests assert `"NIR3001 5:13"` (code, line:column). The user's earlier sessions showed hand-counted columns are
  often off by one or two: reason against the actual output (`nir build file.nir` prints the position) instead of guessing.
  Sample files start with a `// Expect: ...` header whose line number must match the real line (the header itself is line 1).
  `SamplesTests` compiles every sample; golden programs are `name.nir` + `name.stdout` or `name.diag`.
- **Analyzers are strict** (warnings are errors): CA1720 (identifier contains a type name, so `StringType` not `String`), CA1859
  (use the narrower return type), CA1826 (use indexers), CA1822, xUnit2013. Fix, do not suppress.
- **Error handlers.** `x catch e => print(..)` only type-checks when `x` is an `E!` with no value; otherwise the handler must give a
  value or be a block that returns. A declared error type has no `message` unless it declares one; only `anyerror` falls back to
  the type name (D-074).
- **Scratch work** goes in the session scratchpad, not the repo. Long-running stdin programs: always use `timeout`.

## 5. Where to read

| Need | File |
|---|---|
| Entry point, hard rules, status | `CLAUDE.md` |
| The language (locked / proposed / open) | `docs/design.md` |
| What each file does, how a feature flows | `docs/code-map.md` |
| Compiler design in depth, diagnostics table | `docs/architecture.md` |
| Coding, testing, git, naming rules | `docs/practices.md` |
| Slices, tasks, backlog | `docs/roadmap.md` |
| Why things are the way they are (D-001 ... D-081) | `docs/decisions.md` |
| Example programs, error samples, the bank demo | `samples/`, `samples/apps/bank.nir` |
| Editor support | `editors/vscode-nir/` (open that folder and press F5; its `.vscode/launch.json` is gitignored) |

## 6. Backlog worth remembering

(Full list at the end of `docs/roadmap.md`.)

- Union member straight to `E!Union` (two-step conversion), `try` anywhere inside an expression (needs spilling), `e.exception`
  (needs .NET objects), `impl` on error types, `==` on errors / structs / tuples, `with` copy-and-change, generics, `frozen`.
- Better syntax errors (a struct literal in an `if` head says "no viable alternative"; suggest parentheses), more than the first
  syntax error, short per-code labels, `--color` flag, `nir fmt`, a language server (the VS Code extension is highlighting only).
- Cleanups left: `AstBuilder.cs` repeated null-bail pattern; `ImportBinderTests` own `Bind` helper.
