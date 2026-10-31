# Nir

Nir is a modern, minimal language on the .NET CLR: "Kotlin for C#". It compiles to IL and uses C# code and the BCL
directly. It is also a learning project. Repo: `/mnt/Codes/Project/Nir2`. The previous attempt lives in
`/mnt/Codes/Project/Nir` (reference only; do not edit it, do not copy from it without reading `docs/practices.md` first).

## Read in this order

1. `docs/design.md`: the language. Every item is tagged locked / proposed / open.
2. `docs/architecture.md`: compiler structure, projects, types, pipeline. Follow it exactly.
3. `docs/practices.md`: coding rules, testing rules, git rules, ANTLR gotchas. Follow it exactly.
4. `docs/roadmap.md`: slices and tasks. This is where to find the next piece of work.
5. `docs/decisions.md`: append-only log of why things are the way they are.

## Current status

Update this block whenever a task is completed.

- Slice 1 (hello world): **done** (T1.0 to T1.9, 83 tests green, committed on `main`).
- Slice 2 (bindings and arithmetic): **done** (T2.0 to T2.4, 203 tests green).
- Slice 3 (functions with parameters and results): **done** (T3.0 to T3.3, 281 tests green).
- Next: **slice 4 (first C# interop)**. It has no task list yet: write tasks `T4.0`... in `docs/roadmap.md` in the
  format of the earlier slices, then start. Its technical choice (how .NET types are found, ideally with
  `MetadataLoadContext`) is recorded in `decisions.md` when made. Open for slice 5: whether nested blocks may redeclare
  outer names (design.md "Scopes").

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
