# Nir design draft

Status tags: **[locked]** decided in discussion, **[proposed]** suggested, not yet confirmed, **[open]** undecided.
Syntax in examples is a draft. Semantics matter more than spelling.

## 0. Purpose [locked]

Nir is to C# what Kotlin is to Java: a modern, minimal, clean, fast language on the CLR that uses
C# and the BCL directly. It is also a vehicle for learning compilers and the CLR.

Target programs: a file encrypt/decrypt CLI (needs powerful string handling), a simple GUI calculator.

Nir adopts the CLR's object model and improves syntax and defaults on top. Where the CLR already has an answer, use it.

## 1. Bindings and parameters [locked]

```
let x = 10              // cannot be reassigned
let mut count = 0       // can be reassigned
count = count + 1

x = 11                  // error: x is not mutable
```

Parameters are never reassignable. If you need a changeable copy, give it a new name:

```
fn clamp(x: int, max: int) -> int {
    x = 5                   // error: parameters cannot be assigned
    let mut result = x      // ok: a new local with its own name
    if result > max { result = max }
    return result
}
```

### Scopes [locked]

Redeclaring a name in the same block is an error:

```
fn main() {
    let x = 1
    let x = 2               // error: 'x' is already declared in this block
}
```

A function's parameters and its body share one scope, so the body cannot redeclare a parameter either:

```
fn f(x: int) {
    let x = x + 1           // error: 'x' is already declared in this block
}
```

A nested block (`if`, `while`, `for` body) may not declare a name that an enclosing block of the same function already
declares, including the function's parameters and a `for` loop's variable. Blocks side by side may reuse a name, because
each name ends with its block:

```
fn main() {
    let total = 0
    if total == 0 {
        let total = 1           // error: 'total' is already declared in an enclosing block
    }
    if total == 0 {
        let step = 1
    }
    if total == 0 {
        let step = 2            // fine: the first 'step' ended with its block
    }
}
```

### Numbers [locked]

- Primitive types: `int` (32-bit), `double`, `bool`, `string`. There is no `float` for now.
- No implicit conversions between numeric types. `1 + 2.5` is an error. Conversions are written with `as` (see "Strings and conversions").
- Integer arithmetic overflow throws, so `2147483647 + 1` crashes with an `OverflowException`. A build flag to switch to
  wrapping arithmetic may come later. Integer division by zero also crashes (CLR behavior), like any unrecoverable failure.

### Control flow [locked]

```
fn main() {
    let limit = 20
    let mut total = 0

    for i in 0..limit {              // i = 0, 1, ... 19 (the end is not included)
        if i % 2 == 0 {
            continue
        }
        total = total + i
    }

    let mut n = 27
    let mut steps = 0
    while n != 1 {
        if n % 2 == 0 {
            n = n / 2
        } else if n > 1000 && steps > 0 {
            break
        } else {
            n = 3 * n + 1
        }
        steps = steps + 1
    }
}
```

- A condition must be a `bool`. There is no "truthy" `int` or `string`: `if n {` with an `int` is an error. Braces are
  always required; parentheses around a condition are not needed.
- `if` is a statement, not an expression. `else` may be on the next line.
- `while condition { }` and `for i in a..b { }`. The end of `a..b` is not included; `a..=b` includes it. In a `for`,
  `a` and `b` must be `int`, they are evaluated once before the loop, and `i` is a new immutable `int` for each pass.
  Looping over collections comes later. There is no `loop { }`; write `while true { }`.
- `break` leaves the innermost loop and `continue` starts its next pass. There are no labels. Using either outside a
  loop is an error.
- `&&`, `||` and `!` work on `bool` only. `&&` and `||` do not evaluate the right side when the left side already
  decides the result. Precedence, highest first: unary `-` and `!`, then `* / %`, `+ -`, `< <= > >=`, `== !=`, `&&`, `||`.
- "Every path returns": a function that returns a value must end with `return value`, or with an `if` / `else` whose
  branches both end that way (also `else if` chains that end in `else`). A loop never counts, even `while true`.

### Strings and conversions [locked]

```
fn main() {
    let name = "Nir"
    let n = 42
    let ratio = 7.5

    print("Hello, " + name + "!")                        // Hello, Nir!
    print($"{name} has n = {n}, ratio = {ratio}")         // Nir has n = 42, ratio = 7.5
    print($"even: {n % 2 == 0}, shout: {name.ToUpper()}") // even: True, shout: NIR
    print($"{{braces}} and {n}")                          // {braces} and 42
    print("tab:\there, quote: \", backslash: \\")        // tab:<TAB>here, quote: ", backslash: \
    print("a" == "a")                                     // True

    let average = (n as double) / 4.0                     // 10.5
    let whole = ratio as int                              // 7: the fraction is cut off
}
```

- `+` joins two strings. There is no implicit conversion, so `"n = " + 5` is an error; use `$"n = {5}"`.
- `==` and `!=` compare strings by their characters, exactly (ordinal), not by identity. `<` and `>` on strings are errors.
- `$"..."` is an interpolated string. Each `{ expression }` can hold any expression of any type except `()`; its text is
  the value's `ToString()` with invariant culture (`True`, `2.5`, `42`). Write `{{` and `}}` for literal braces. A hole can
  contain strings, calls and nested interpolated strings.
- Escapes, in plain and interpolated strings: `\n` `\t` `\r` `\\` `\"` `\{`. Any other backslash sequence is an error.
- `x as double` and `d as int` convert between `int` and `double` and nothing else. `int` to `double` is exact for every
  `int`. `double` to `int` cuts off the fraction toward zero and throws (crashes) when the value does not fit or is `NaN`,
  like the other checked arithmetic. `as` binds tighter than `*` and looser than unary `-`, so `-x as double` is `(-x) as double`.

## 2. Type declarations [proposed]

One form: `type Name = <shape>`. Struct and tuple use the same form, so `type` vs `const` is gone.

```
type Point = struct {
    x: int,
    y: int,
}

type Pair = (x: int, y: int)        // compiles to ValueTuple
```

Optional values use a `?` prefix:

```
let a: ?int = null
let b: ?&mut Node = null
```

## 3. Value types and heap references [locked]

A struct is a value type. Passing or assigning it copies.

```
let a = Point { x = 1, y = 2 }
let mut b = a                   // copy
b.x = 9
print(a.x)                      // 1
```

`&T` is a heap reference (compiles to a generated `Ref<T>` class). You create one by allocating, or by
taking `&` of a whole variable. Assigning it shares the object, no copy.

```
let n = &mut Node { value = 1, next = null }
let m = n                       // same object
m.value = 5
print(n.value)                  // 5
```

Taking `&` of a local moves that local onto the heap (like Go's escape-to-heap):

```
let mut count = 0
let p = &mut count
// count and p now refer to the same heap variable
```

Only whole variables and `&T { ... }` can be referenced. Pointers into fields or array elements are not allowed:

```
let a = &person.age             // error: cannot reference a field
let b = &nums[1]                // error: cannot reference an array element
```

Reading and writing a `&T` that is not a struct (for example `&int`) needs a dereference syntax. See Open questions.

## 4. Mutability follows the access path [locked]

Fields have no `mut` keyword. A field can be written only if every step to it is mutable.
Value types: `let` vs `let mut`. Heap references: `&` vs `&mut`.

```
let p = Point { x = 1, y = 2 }
p.x = 5                         // error: p is not mutable

let mut q = Point { x = 1, y = 2 }
q.x = 5                         // ok

fn move_right(p: &mut Point) { p.x = p.x + 1 }    // ok
fn show(p: &Point)           { p.x = 0 }          // error: read-only reference
```

`&mut` belongs to heap references; a value gets its mutability from its binding. In `head: ?&mut Node`, the `mut` is part of the field's reference type, not a field modifier.

`&mut T` converts to `&T`. `&T` does not convert to `&mut T`.

`&T` means "I cannot write through this reference", not "nobody can". There are no aliasing rules:

```
let a = &mut Point { x = 1, y = 2 }
let b: &Point = a
a.x = 9                         // b.x is 9 now. Allowed.
```

### Later: `frozen` [proposed, not v1]

```
type Point = frozen struct { x: int, y: int }   // no one can ever write its fields
```

## 5. Methods [locked]

No standalone `self`. A receiver is always `&self` or `&mut self`. There is no ownership or move,
because there is a GC.

```
Point impl {
    fn new(x: int, y: int) -> Point {
        return Point { x = x, y = y }
    }

    fn length_squared(&self) -> int {
        return self.x * self.x + self.y * self.y
    }

    fn move_right(&mut self) {
        self.x = self.x + 1
    }
}

let mut p = Point::new(3, 4)
p.move_right()                  // no allocation: p stays on the stack
```

No inheritance. Composition only.

## 6. Borrowed parameters vs stored references [locked, syntax open]

`&self`, `&T` and `&mut T` as parameters are borrows. A borrow lasts for the call and compiles to a CLR
`ref`, so no allocation. It cannot be stored.

```
fn bump(p: &mut Point) { p.x = p.x + 1 }      // fine

Point impl {
    fn remember(&mut self, other: &Point) {
        self.last = other                      // error: cannot store a borrow
    }
}
```

A stored reference must be a heap reference created by `&T { ... }`.
How a function declares "I take a heap reference and will keep it" is open. See Open questions.

## 7. Example: linked list [locked, apart from open syntax]

```
type Node = struct {
    value: int,
    next: ?&mut Node,           // mutable: a read-only ref could not be used to append
}

type LinkedList = struct {
    head: ?&mut Node,
}

LinkedList impl {
    fn new() -> LinkedList {
        return LinkedList { head = null }
    }

    fn push_front(&mut self, value: int) {
        self.head = &mut Node { value = value, next = self.head }
    }

    fn is_empty(&self) -> bool {
        return self.head == null
    }
}
```

## 8. C# interop [locked, details open]

C# types are used directly, with no `unsafe` block around ordinary calls.
Import rules are in section 9.

```
using clr::cs::System::IO
using clr::cs::System::Collections::Generic::List

let text = IO::File::ReadAllText(path)      // BCL call; error handling comes later
let items = List<int>()                     // C# class: already a reference, no & needed
items.Add(3)
```

- CLR reference types (`List<T>`, arrays, `StringBuilder`) never need `&`. `&List<int>` is an error.
- `string` behaves as a value type in Nir.
- `&mut int` can be passed to a C# `ref int` parameter.
- C# code sees a Nir `&Node` as `Ref<Node>`. Accepted cost of the use-site design.
- C# can mutate anything handed to it. Nir's guarantees apply to Nir code only.
- C# member names are kept as they are (`ReadAllText`, not `read_all_text`).

## 9. Modules and imports [locked, syntax proposed]

Goal: reading a file tells you where every name comes from.

### Namespaces and `using`

A file starts with its namespace. Many files can share one namespace. `using` imports exactly one named thing,
or a brace-group of named things. No wildcards.

```
// geometry/models.nir
namespace geometry::models

type Point = struct { x: double, y: double }

Point impl {
    fn new(x: double, y: double) -> Self { return Self { x = x, y = y } }
}
```

```
// app/main.nir
namespace app
using geometry::models::Point            // imports exactly this item

fn main() {
    let p = Point::new(1.0, 2.0)         // the type and its methods come together
}
```

### `impl` lives with its type

An `impl` block must be in the same namespace as the type, so a type's methods never depend on what a file imports.

```
namespace geometry::utils
using geometry::models::Point

Point impl { fn dist(&self) -> double { ... } }    // error: Point is declared in geometry::models
```

Helpers go in other namespaces as ordinary functions, called with their namespace visible:

```
namespace geometry::utils
using geometry::models::Point

fn lerp(a: Point, b: Point, t: double) -> Point { ... }
```

```
using geometry::utils
let m = utils::lerp(a, b, 0.5)
```

### .NET code under `clr::cs`

.NET namespaces are mirrored exactly, so `System.Timers.Timer` and `System.Threading.Timer` cannot collide.
`clr::fsharp` is reserved for later.

```
using clr::cs::System::IO
let text = IO::File::ReadAllText(path)
```

### Extension methods

Extending a type you do not own uses a named `extend` block. Each extension method is imported by name,
so its origin is visible in the file. A real member of the type always wins over an extension.

```
// app/text.nir
namespace app::text

extend string as StringExt {
    fn shout(&self) -> string { return self.ToUpper() + "!" }
}
```

```
// app/main.nir
using app::text::StringExt::shout        // imports one method

let s = "hi".shout()
```

C# extension methods work the same way, importing from the static class that holds them (LINQ is the main case):

```
using clr::cs::System::Linq::Enumerable::{Sum, Where, ToList}

let total = nums.Sum()
let same = Enumerable::Sum(nums)         // the full path always works, no import needed
```

### `std` is separate from `clr`

`std` is Nir's own library. It may later wrap BCL calls, for example `std::fs::File::read_all_text`
calling `IO.File.ReadAllText` internally. In v1, `std` is only a small prelude [proposed] (`print` and a few basics).
Everything else comes from `clr::cs`.

## 10. Error handling [locked basics, syntax proposed]

Errors are values, not exceptions. `E!T` means "either an error of type `E`, or a `T`".
The error type must always be written. `anyerror` is the catch-all.

```
fn parse_port(s: string) -> ParseError!int { ... }
fn read(path: string) -> anyerror!string { ... }
fn main() -> anyerror! { ... }          // either an error, or nothing
```

There is no `void` keyword. "Nothing" is the empty tuple `()`, and `anyerror!` is shorthand for `anyerror!()` [proposed].

Error types are their own kind. They are declared like a struct with `error` in place of `struct`, and compile to classes:

```
type ParseError = error {
    message: string,
    line: int,
}

ParseError impl {
    fn describe(&self) -> string { return $"line {self.line}: {self.message}" }
}
```

Rules:
- `E` must be an error type or `anyerror`.
- `T` must not be an error type. `ParseError!ConfigError` is not allowed. `!` always means "error or normal value".
- Returning the failure side needs the `error` keyword.

```
fn parse_port(s: string) -> ParseError!int {
    if s == "" { return error ParseError { message = "empty", line = 1 } }
    return 8080
}

fn read(path: string) -> anyerror!string {
    if path == "" { return error "path is empty" }     // ad hoc anyerror made from a message [proposed]
    ...
}
```

Narrowing is explicit. `try` on a C# call gives an `anyerror`, so a function with a narrower `E` converts it itself:

```
let text = IO::File::ReadAllText(path) catch |e| {
    return error ConfigError { path = path, msg = e.message }
}
```

`try` returns the error to the caller. `catch` handles it on the spot:

```
fn read_config(path: string) -> anyerror!string {
    let text = try IO::File::ReadAllText(path)
    return text
}

fn main() -> anyerror! {
    let a = read_config("a.txt") catch "default"           // fallback value
    let b = read_config("b.txt") catch |e| {               // handle it
        print($"failed: {e.message}")
        return
    }
}
```

Errors carry data, not just a tag. Calls into C# are opt-in: a call without `try` that throws crashes the program,
like an unhandled C# exception. `try` turns a thrown exception into an error value that wraps it.

```
let n = items.Count                          // no try: if it throws, the program crashes
let text = try IO::File::ReadAllText(path)   // try: the exception becomes an anyerror
```

`anyerror` has `message` and `exception` (the original .NET exception, or null for errors made in Nir) [proposed names].

Unrecoverable failures (index out of range, null dereference) are not errors. They crash, like a panic.

## 11. The kinds of types [union locked for v1, rest planned]

Every kind uses the same form: `type X = <shape>`.

| Tier | Kinds |
|---|---|
| v1 | struct, tuple, error, union (with `match`) |
| Soon after | interface, enum |
| Later | `any`, generics, structural types |

### Tuple

```
type Point2D = (x: float, y: float)      // compiles to ValueTuple; names are compile-time only
type Pair = (float, float)               // access as p.0 and p.1
```

### Union [v1]

A union is "one of these existing types". Syntax for now is the keyword form; `A | B` may be revisited.

```
type Circle = struct { r: double }
type Square = struct { side: double }
type Shape = union { Circle, Square }

fn area(s: Shape) -> double {
    match s {
        c: Circle => 3.14159 * c.r * c.r,       // match syntax proposed
        q: Square => q.side * q.side,
    }
}
```

`match` must cover every case, or the compiler rejects it.

`E!T` is shorthand for `union { E, T }` where `E` is an error and `T` is not. `?T` could likewise be
`union { T, null }` [proposed]. The compiler emits a small tagged struct for `E!T` so success values are not boxed.

### Interface [soon]

Maps to a CLR interface, so C# can implement and see it. Implemented inside the type's own `impl`:

```
type IPerson = interface {
    fn get_username(&self) -> string,
    fn set_username(&mut self, username: string),
}

Person impl IPerson {
    fn get_username(&self) -> string { return self.username }
    fn set_username(&mut self, username: string) { self.username = username }
}
```

A C# type cannot be made to implement a Nir interface (the CLR cannot add interfaces to an existing class).

### Enum [soon]

One keyword, two shapes. Plain named values map to a real CLR enum. Cases that carry data become a generated class
with one static instance per case.

```
type Level = enum { Low = 1, Mid = 2, High = 3 }                        // CLR enum, usable from C# APIs

type Color = enum { Red = (255, 0, 0), Green = (0, 255, 0), Blue = (0, 0, 255) }   // cases carry data
```

How the data shape of a case is declared (the types of `255, 0, 0`) is open.

### Top types [later]

```
fn a(x: any)      { ... }     // any value, including errors; CLR object, inspect with match
fn b(x: anyerror) { ... }     // any error type, or a union made only of error types
```

`anytype` is dropped for now. It means a compile-time generic in Zig, which belongs with the deferred structural types.

## 12. Passing options, TypeScript-style [proposed]

Named arguments, defaults and record init cover the options-object need in v1:

```
fn connect(host: string, port: int = 80, secure: bool = false) { ... }

connect(host = "example.com")
connect(host = "example.com", secure = true)

let p = Person { name = "shuvo", email = "s@example.com" }
let p2 = p with { name = "other" }
```

Structural types are deferred. See section 13.

## 13. Non-goals and deferred

Non-goals for v1:
- Inheritance
- Unsafe pointers (`*`), pointers into fields/elements, lifetimes, a borrow checker
- LLVM or native targets
- Blanket `unsafe` around C# calls
- Wildcard imports

Deferred (possible later, design must not block them):
- **Structural types.** A shape accepts any struct that has the fields. The compiler emits a specialized copy of the
  function for each concrete type passed, so it is free at runtime but only callable from Nir, not from C#.

```
type Named = { name: string }

fn greet(n: Named) { print($"hi {n.name}") }

greet(Person { name = "a", email = "x" })    // compiler emits greet for Person
greet(struct { name = "b" })                 // compiler invents an anonymous struct
```

- `frozen` types (see section 4)
- Rust-style borrow checking

## 14. Open questions

1. **Dereference of non-struct references.** For `&int`, how do you read and write the value?
   Options: `*p`, `p.*`, or auto-deref where unambiguous.
2. **Storing a reference passed to a function.** A parameter `&T` is a borrow, so what spells "heap reference I keep"?
   Maybe the parameter type is declared differently, or you copy into a new `&T { ... }`.
3. **Null-safety.** `?T` exists in the draft. Does `string` mean non-null? How do values returned from C# map?
4. **Narrow C# marking.** Passing an immutable value to a C# method that takes a mutable `ref`. Should this need a marker?
5. **How does a C# caller see a Nir `E!T` or union?** Probably a generated struct or class. `match` and union are in v1 (section 11).
6. **Generics.** In v1 or not? C# interop makes `List<T>` hard to avoid.
7. **Strings.** What does "powerful and easy" string manipulation mean concretely? (Slicing, `split`, interpolation, raw strings, regex?)
8. **Loops and expressions.** `while`, `loop`, `break`/`continue`, `match`, assignment operators.
