# Samples

Programs to try by hand. From the repository root, build once, then run any of them:

```bash
dotnet build
src/Nir.Cli/bin/Debug/net10.0/nir run samples/arithmetic.nir
```

(or `dotnet run --project src/Nir.Cli -- run samples/arithmetic.nir`).

## Programs that work

| File | Shows |
|---|---|
| `hello.nir` | the smallest program |
| `arithmetic.nir` | `let`, `let mut`, numbers, operators, parentheses |
| `functions.nir` | parameters, results, calls in any order |
| `strings.nir` | `+`, `==`, interpolation `$"{x}"`, escapes |
| `conversions.nir` | `as` between `int` and `double` |
| `control_flow.nir` | `if` / `else if` / `else`, `while`, `for` with `..` and `..=`, `break`, `continue`, `&&` `||` `!` |
| `recursion.nir` | factorial, fibonacci, mutual recursion |
| `dotnet_math.nir` | `using clr::cs::...`, `Math::Sqrt`, `Int32::Parse`, `Console::WriteLine` |
| `dotnet_strings.nir` | methods on values: `"text".Trim().ToUpper()`, `42.ToString()` |
| `structs.nir` | `type P = struct {..}`, `P impl {..}`, `&self` / `&mut self`, `&` / `&mut` arguments, copies, nested structs |
| `tuples.nir` | tuples `(1, 2)`, `t.0`, named tuples, `type Pair = (x: int, y: int)`, `let (a, b) = t` |
| `file_io.nir` | writing, reading and deleting a file through `System.IO` |

## Programs with a compile error (`errors/`)

Each file starts with a comment saying which error to expect and on which line. The compiler prints
a Rust-style report (the source line with carets under the exact place, in colour on a terminal) and exits with code 1.
Run them all:

```bash
for f in samples/errors/*.nir; do echo "== $f"; src/Nir.Cli/bin/Debug/net10.0/nir run $f; done
```

## Programs that compile but crash (`crashes/`)

These show that arithmetic overflow, division by zero and .NET exceptions are not hidden: the program starts, then ends
with an unhandled exception and a non-zero exit code.
