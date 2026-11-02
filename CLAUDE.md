# Nir

Nir is a modern, minimal language on the .NET CLR: "Kotlin for C#". It compiles to IL and uses C# code and the BCL
directly. It is also a learning project. Repo: `/mnt/Codes/Project/Nir`. The earlier attempts were removed; the last one is kept only as a backup archive
(`/mnt/Codes/Project/Nir-old-backup.tar.gz`, reference only).

## Read in this order

0. `docs/handoff.md`: where the last session stopped, how the user likes to work, and the questions to ask next.
1. `docs/design.md`: the language. Every item is tagged locked / proposed / open.
2. `docs/architecture.md`: compiler structure, projects, types, pipeline. Follow it exactly.
3. `docs/practices.md`: coding rules, testing rules, git rules, ANTLR gotchas. Follow it exactly.
4. `docs/roadmap.md`: slices and tasks. This is where to find the next piece of work.
5. `docs/decisions.md`: append-only log of why things are the way they are.
6. `docs/code-map.md`: what every file does and how a feature flows through them.

## Current status

Update this block whenever a task is completed.

- Slice 1 (hello world): **done** (T1.0 to T1.9, 83 tests green, committed on `main`).
- Slice 2 (bindings and arithmetic): **done** (T2.0 to T2.4, 203 tests green).
- Slice 3 (functions with parameters and results): **done** (T3.0 to T3.3, 281 tests green).
- Slice 4 (first C# interop): **done** (T4.0 to T4.4, 430 tests green). .NET types are found through
  `Clr/ClrUniverse` (a `MetadataLoadContext`); never use `typeof(...)` for a type that reaches the emitter.
- Slice 5 (control flow): **done** (T5.0 to T5.3, 587 tests green).
- Slice 6 (strings and conversions): **done** (T6.0 to T6.3, 717 tests green).
- Slice 7 (structs, methods, borrows): **done** (T7.0 to T7.4, 933 tests green).
- Slice 8a (tuples): **done** (1065 tests green).
- Slice 8b (unions and `match`): **done** (1162 tests green).
- Slice 9a (errors in Nir): **done** (1277 tests green).
- Slice 9b (`try` / `catch` on .NET calls): **done** (1304 tests green).
- After 9b: emitter split into partial files, names moved into constants (`Language/`, `Clr/ClrNames`), VS Code highlighting
  (`editors/vscode-nir/`), the bank demo (`samples/apps/bank.nir`), `SamplesTests`. 1374 tests green.
- Slice 9c (the rest of the primitive types): **done** (1830 tests green). `byte sbyte short ushort uint long ulong float decimal char`
  next to `int double bool string`; literals take their type from context or a suffix (`200_byte`); `as` is checked between all number
  types and `char`; `catch` works on `as`; ranges run over any integer type and `char`. `samples/test.nir` is the user's scratch file:
  never commit it, never depend on it.
- Slice 9d (`typeof`): **done** (1916 tests green). `typeof(x)` (a variable, an expression or a type name) is worked out while compiling and
  returns the built-in struct `std::types::TypeInfo { name }` (import it with `using std::types::TypeInfo` to write its name); the operand never runs. The run-time half, `std::types::runtime_type(x)`, waits for slice 11.
- Next: **slice 10 (heap references)**. Ask the user its open design decisions first (design.md sections 3, 4, 6, 7, 12), then
  write the `T10.x` tasks in `docs/roadmap.md`.

## How to pick up work

1. Open `docs/roadmap.md`. Take the first unchecked task in the lowest-numbered unfinished slice. Do not skip ahead.
2. Read the task's "Done when" before writing code. Write the failing test first, then the code.
3. If the task needs a language decision that is not tagged **locked** in `docs/design.md`, stop and ask the user.
   Record the answer in `docs/design.md` and `docs/decisions.md` before coding it. Never invent language behavior.
4. When the task is done: tick it in `docs/roadmap.md`, update the status block above, and run the full checks below.

## Commands

```bash
dotnet build                                   # must finish with 0 warnings, 0 errors
dotnet test                                    # must be fully green
dotnet run --project src/Nir.Cli -- run samples/hello.nir
dotnet run --project src/Nir.Cli -- build samples/hello.nir -o out
```

Never run two `dotnet build` commands at the same time. The first build downloads a Java runtime for ANTLR into
`~/.jre`, and overlapping builds corrupt it. See `docs/practices.md` section 7.

## Hard rules

- Compile errors are `Diagnostic` values, never exceptions. Exceptions mean a compiler bug.
- ANTLR types never appear outside `src/Nir.Compiler/Parsing/`. Everything after parsing sees only our own AST.
- The build is strict: warnings are errors. Fix them; do not suppress them.
- A language-visible change starts with an edit to `docs/design.md`, not with code.
- Commit only when the user asks. Never push.
- Do not add features, files or abstractions that the current task does not call for.
