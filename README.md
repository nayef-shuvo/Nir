# Nir

A modern, minimal language for the .NET CLR. It compiles to IL and talks to C# code directly, the way Kotlin does with Java.

```
fn main() {
    print("hello")
}
```

Status: early development. Works today: functions with parameters and results, `let` / `let mut`, `int`, `double`,
`bool` and `string`, arithmetic and comparisons, `if` / `while` / `for` / `break` / `continue`, `&&` `||` `!`, strings with `+`, `==` and `$"...{x}..."`, `as` between `int` and `double`, structs with methods (`type P = struct {..}`, `P impl {..}`) passed by copy or by `&` / `&mut` borrow, tuples (`(1, 2)`, `t.0`, `let (a, b) = t`), unions with `match` (`type Shape = union { Circle, Square }`), `print`, and calls into the .NET libraries
(`using clr::cs::System::Math` then `Math::Sqrt(16.0)`, or `"abc".ToUpper()`). The language design is in
`docs/design.md`, and the plan is in `docs/roadmap.md`. Coding agents and contributors: read `CLAUDE.md` first.

## Requirements

- .NET SDK 10 (see `global.json`).
- Network access on the very first build: it downloads a Java runtime for the ANTLR code generator into `~/.jre`.

## Build

```bash
dotnet build
```

Run one build at a time. Overlapping builds can corrupt the Java download (see `docs/practices.md` section 7).

## Run a Nir program

Compile and run in one step:

```bash
dotnet run --project src/Nir.Cli -- run samples/hello.nir
```

Or use the built executable directly (after `dotnet build`):

```bash
src/Nir.Cli/bin/Debug/net10.0/nir run samples/hello.nir
```

Compile to a folder without running it, then run the result with `dotnet`:

```bash
src/Nir.Cli/bin/Debug/net10.0/nir build samples/hello.nir -o out
dotnet out/hello.dll
```

Compile errors print on stderr in the style of Rust's compiler, with the source line and carets under the exact place,
in colour on a terminal (set `NO_COLOR=1` to turn colour off), and the exit code is 1:

```
error[NIR3002]: cannot assign to 'x' because it is not mutable
 --> samples/errors/assign_immutable.nir:4:5
  |
4 |     x = 2
  |     ^ cannot assign to 'x' because it is not mutable

error: aborting due to 1 previous error
```

Exit codes: `0` success, `1` compile errors, `2` bad usage, `3` internal compiler error.

## Run the tests

```bash
dotnet test
```

Run a subset by name:

```bash
dotnet test --filter "FullyQualifiedName~LexerTests"
dotnet test --filter "FullyQualifiedName~Slice1ProgramTests"
```

The test folders are `Lexing`, `Parsing`, `Semantics`, `Emit`, `Cli`, `Diagnostics` and `Programs` under `tests/Nir.Tests`.
To add a whole-program test, put `name.nir` and either `name.stdout` (expected output) or `name.diag` (expected errors,
one `line:col CODE` per line) in `tests/Nir.Tests/Programs/<slice>/`. No C# change is needed.
