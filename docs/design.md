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

- Primitive types (slice 9c, D-083), named like C#'s keywords:

  | Kind | Types |
  |---|---|
  | Signed integers | `sbyte` (8), `short` (16), `int` (32), `long` (64) |
  | Unsigned integers | `byte` (8), `ushort` (16), `uint` (32), `ulong` (64) |
  | Floating point | `float` (32-bit), `double` (64-bit), `decimal` (128-bit, base 10) |
  | Other | `char` (one UTF-16 code unit), `bool`, `string` |

- No implicit conversions between numeric types. `1 + 2.5` is an error, and so is `a + b` with `a: int` and `b: long`: both sides of an
  operator must have the same type, and so must the two sides of `==`, `<`, ... Conversions are written with `as` (see "Strings and conversions").
- Integer arithmetic overflow throws, so `2147483647 + 1` and `250_byte + 10_byte` crash with an `OverflowException`; this holds for every
  integer type. A build flag to switch to wrapping arithmetic may come later. Integer division by zero also crashes (CLR behavior), like
  any unrecoverable failure. `float` and `double` follow IEEE (`1.0 / 0.0` is infinity); `decimal` throws on overflow and division by zero.
- Operators on numbers: `+ - * / %` and the comparisons work on every integer, `float`, `double` and `decimal`; the result of an
  arithmetic operator has the type of its operands. Unary `-` works on signed integers, `float`, `double` and `decimal`, and is an error on
  unsigned integers (`0_uint - x` is allowed and throws when `x` is not 0, like any other overflow).

### Number literals [locked, slice 9c: D-084]

A literal without a suffix gets its type from the place where it is used, when it fits; otherwise it is `int` (a whole number) or
`double` (a number with a fraction). A suffix `_type` always wins.

```
let a: byte = 200            // the annotation types the literal
let b = 5_long               // a suffix: b is a long
let c: float = 2.5           // a fraction fits float, double and decimal
let d = b + 1                // 1 takes the type of the other operand: long
let e = a + 100              // 100 is a byte here; 200 + 100 would throw at run time
let f: byte = 300            // error: '300' is out of range for 'byte'
let g: double = 1            // error: a whole-number literal fits integer types only (write 1.0, or 1_double)
```

- Where a literal is typed by context: a `let` with an annotation, an assignment, a `return`, a call argument, a struct field, the other
  operand of a binary operator (also in `a..b`), an element of a tuple literal that is converted to a tuple type
  (`let p: (char, long) = ('Z', 5)`), and the one member of a union (such as `anyerror!long`) that the literal fits. For a .NET method the
  overload is chosen by exact types first; if none matches, the one overload that fits once the literals take their parameter's type is
  used (`Math::Max(count, 1)` with a `long` count). Anywhere else, and in the operand of `as`, it is `int` / `double`.
- Only the literal itself adapts, not an expression made of literals: with a `long` `n`, `n + 1 + 2` works but `1 + 2 + n` does not
  (`1 + 2` is an `int`); write `n + 3` or `3_long + n`.
- A whole-number literal can adapt to the integer types only; a literal with a fraction to `float`, `double` and `decimal` only.
  A suffix may turn a whole-number literal into `float`, `double` or `decimal` (`5_decimal`).
- Suffixes are the type names after an underscore: `_sbyte _byte _short _ushort _int _uint _long _ulong _float _double _decimal`.
  A suffix the literal cannot have is an error (`2.5_int`).
- A negative literal is checked as a whole, so `-128` fits `sbyte`. There are no hex or binary literals and no digit separators yet.
- A literal that does not fit its type is an error at compile time, never a run-time crash.

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
  `a` and `b` must have the same type, any integer type or `char` (slice 9c, D-089), they are evaluated once before the loop, and `i` is a
  new immutable variable of that type for each pass (`0..n` with `n: long` makes `i` a `long`). `a..=b` also works when `b` is the largest
  value of its type (`0..=255_byte`).
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
- `x as T` converts between the number types (slice 9c, D-086), and between `char` and the integer types. Nothing else converts: not
  `bool`, not `string`, not `char` to or from `float` / `double` / `decimal`. Every `as` is checked: a value that does not fit
  the target (`300 as byte`, `-1 as ulong`, `3000000000_long as int`) throws an `OverflowException`, and so does a `NaN` or infinity going to an
  integer. A fraction going to an integer is cut off toward zero. Whole numbers to `double` / `float` round to the nearest
  value that fits; `decimal` to `float` / `double` likewise; a `float` / `double` to `decimal` throws when it is too large.
  `as` binds tighter than `*` and looser than unary `-`, so `-x as double` is `(-x) as double`. Converting to the same type does nothing.
- The conversion that throws can be handled with `catch` (and passed on with `try`), like a call into .NET (D-087):

  ```
  let small = big as byte catch 0                       // 0 when big does not fit
  let n = ratio as int catch e => -1                    // a handler that sees the error, same rules as for any catch
  ```

  The exception becomes an `anyerror` whose `message` is the exception's message. `try` binds tighter than `as`, so a
  conversion that is passed on is written with parentheses: `let b = try (x as byte)`.

### Characters [locked, slice 9c: D-088]

A `char` is one UTF-16 code unit. A literal is written between single quotes, with the escapes of strings plus `\'` and `\0`:

```
let c = 'a'
let nl = '\n'
let quote = '\''
let next = ((c as int) + 1) as char       // 'b': there is no arithmetic on char
print($"{c} {next} {c < next}")           // a b True
```

- Exactly one character: `''` and `'ab'` are errors, and so is a character outside the Basic Multilingual Plane (it needs two UTF-16 units).
- On two chars, `==`, `!=`, `<`, `<=`, `>`, `>=` work (by code unit). `+ - * / %` do not; convert with `as`.
- A `char` can be printed, put in `{ }` of an interpolated string (its text is the character), be a parameter, result, struct field, tuple
  element or union member, and be the bounds of a range (`for c in 'a'..='z'`).

## 2. Type declarations [struct locked, tuple locked]

One form: `type Name = <shape>`. Struct and tuple use the same form, so `type` vs `const` is gone.

```
type Point = struct {
    x: int,
    y: int,
}

type Pair = (x: int, y: int)        // a name for a tuple type; compiles to ValueTuple
```

A struct's fields are separated by a comma or a newline (the last separator is optional), so these are the same:

```
type Point = struct { x: int, y: int }

type Point = struct {
    x: int
    y: int
}
```

A **struct literal** names the type and gives every field exactly once, in any order, with `=` (the same style as named
arguments), separated by commas or newlines: `Point { x = 1, y = 2 }`. There are no default values. Inside its own `impl`
a struct is also called `Self`. A struct cannot contain itself by value (`type A = struct { a: A }` is an error), and its
fields can be any primitive type (`int`, `long`, `double`, `char`, `bool`, `string`, ...) or another struct.

A struct literal cannot appear directly in the head of an `if`, `while` or `for`, because `limit {` would look like the
start of a struct literal. Put it in parentheses: `if p == (Point { x = 1, y = 2 }) { }` (and `==` on structs is not
available yet; the rule is for later).

Struct values: `==` on structs, `print(p)`, and putting a struct in `{ }` of an interpolated string are errors for now;
compare or print the fields. The `p with { x = 5 }` copy-and-change form comes later.

### Tuples [locked]

A tuple is two or more values that travel together. It is a value type (`System.ValueTuple`), copied on assignment. There is no
limit on the number of elements; past seven they are nested through `Rest`, exactly as C# does, so C# sees the same type.

```
let t = (1, 2.5, "x")               // type (int, double, string)
print(t.0)                           // elements are reached by index
let (a, mut b) = (1, 2)              // taking a tuple apart; `_` takes an element and drops it
fn divmod(a: int, b: int) -> (quotient: int, remainder: int) { return (a / b, a % b) }
let r = divmod(7, 2)
print(r.quotient + r.1)              // a named element is reached by name or by index
```

- A tuple type is written `(T1, T2, ...)`, optionally with a name on every element (`(x: int, y: int)`). All elements named or none.
  `type Pair = (x: int, y: int)` only gives such a type a name; it is not a new type.
- Two tuple types are the same type when their element types are the same; names are not part of the type. So `Pair`,
  `(a: int, b: int)` and `(int, int)` convert to each other freely.
- Tuples can be parameters, results, `let` annotations and struct fields, and may nest. Only `mut` bindings and `&mut` paths
  let you assign `t.0 = 5`, like struct fields. A tuple cannot be borrowed yet.
- A one-element "tuple" does not exist: `(x)` is just `x` in parentheses.
- `==`, `print(t)` and `{t}` in an interpolated string are errors for now, like for structs; use the elements.
- A tuple has no methods, and `t.0.1` means element 1 of element 0.

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

## 6. Borrowed parameters vs stored references [borrows locked, stored references open]

`&self`, `&T` and `&mut T` as parameters are borrows. A borrow lasts for the call and compiles to a CLR
`ref`, so no allocation. It cannot be stored. The caller writes the borrow at the call, so a change is visible where it
happens:

```
fn bump(p: &mut Point) { p.x = p.x + 1 }
fn show(p: &Point)     { print(p.x) }

fn main() {
    let mut p = Point { x = 1, y = 2 }
    bump(&mut p)                  // "this call may change p"
    show(&p)                      // a read-only borrow
    show(&mut p)                  // fine: a mutable borrow can be used read-only
}
```

- `&x` and `&mut x` mean "a reference to the variable x". For now they can only be written directly as an argument for a
  borrow parameter (anywhere else is an error); storing a reference arrives with heap references. Passed to a borrow
  parameter they cost nothing.
- Only a whole variable can be borrowed, not a field or an element. `&mut x` needs `x` to be mutable: `let mut`, `self` in
  a `&mut self` method, or a `&mut` borrow parameter. A by-value parameter is immutable, so `&mut` of it is an error.
- For now only structs can be borrowed. `&mut int` would need a way to read and write through the reference (open
  question 1), so a borrow parameter of any other type is an error.
- Inside the function the borrow variable can be used for field reads, field writes (if it is `&mut`), method calls, and
  passed on as `&p` or `&mut p`. Using it bare (`let q = p`) copies the struct. It cannot be assigned.
- A by-value parameter (`p: Point`) is a copy; the function cannot change the caller's value.

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

## 10. Error handling [done: Nir errors (9a) and .NET calls (9b)]

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

### As built in slice 9a (decisions D-072 to D-079)

- `type E = error { fields }` is a class (a reference type) and its values cannot change; the only way to make one is `error E { f = v }`
  (plain `E { .. }` is an error). `error "text"` makes an `anyerror`. Errors cannot be compared, borrowed or given `impl` yet.
- `E!T` is a union of `E` and `T` (members in that order, structural: two `E!T` are the same type), so the conversions of unions
  apply (`T` or `E` converts to it) and `match r { v: T => .., e: E => .. }` works. `E` is an error type or `anyerror`; `T` is neither an error
  nor a result. A declared error also converts to `anyerror`, and so to `anyerror!T`.
- `E!` means success carries nothing. A bare `return`, or reaching the end of the body, succeeds. `fn main() -> E!` is allowed: an error
  that escapes `main` is printed to stderr as `error: <message>` and the exit code is 1.
- `anyerror.message` is the declared `message: string` field of the error if it has one, otherwise the name of the error type. A declared
  error type has no `message` unless it declares one.
- `try expr` yields the value or returns the error from the enclosing function, which must return `E2!U` with `E2` the same error type or
  `anyerror`. For now `try` (and `catch` with a block) must be the whole value of a `let`, an assignment to a variable, a `return` or a
  statement.
- `a catch value` gives a fallback; `a catch e => expr` computes one from the error; `a catch e => { ... }` must return on every path when
  there is a value to produce (for `E!` it need not). `catch` has the lowest precedence.
- **.NET calls (slice 9b).** `try` and `catch` also take a call into .NET: `let text = try IO::File::ReadAllText(path)`,
  `Int32::Parse(s) catch 0`, `IO::Directory::Delete(p) catch e => print("failed")`. A thrown exception becomes an `anyerror` whose `message` is the
  exception's message (nothing else is exposed until .NET objects can be used); a method with no value becomes an `E!`. Only the call itself
  is protected, not the evaluation of its arguments. A call without `try` / `catch` that throws crashes the program as before.
  `try` follows the statement-level rule above; `catch` with a fallback or a handler expression can sit anywhere, because the protected
  call is made by a generated helper method.
- Not done: `e.exception`, `impl` on errors, `==` on errors, `try` anywhere inside an expression.


## 11. The kinds of types [union and match done, rest planned]

Every kind uses the same form: `type X = <shape>`.

| Tier | Kinds |
|---|---|
| v1 | struct, tuple, error, union (with `match`) |
| Soon after | interface, enum |
| Later | `any`, generics, structural types |

### Tuple [done, see section 2]

```
type Point2D = (x: float, y: float)      // compiles to ValueTuple; names are compile-time only
type Pair = (float, float)               // access as p.0 and p.1
```

### Union [done]

A union is "one of these existing types". Syntax for now is the keyword form; `A | B` may be revisited.

```
type Circle = struct { r: double }
type Square = struct { side: double }
type Shape = union { Circle, Square }

fn area(s: Shape) -> double {
    match s {
        c: Circle => 3.14159 * c.r * c.r,
        q: Square => q.side * q.side,
    }
}
```

`match` must cover every case, or the compiler rejects it.

Details (slice 8b, decisions D-067 to D-071):

- A union has two or more members, each any Nir type (primitives, structs, tuples) but not `()` or another union. A repeated member is an error.
  A union is its own type: two unions with the same members are different types.
- A value of a member type converts to the union where a union is expected: `let s: Shape = Circle {..}`, assignment, `return`,
  arguments of Nir functions and methods, struct literal fields. Not inside a tuple literal, not the other way round.
- `match value { name: Type => body, ... }` is an expression. The arm variable is an immutable copy of the member, and `_` drops it.
  Arms are separated by a comma or a newline. In statement position an arm may be a block `{ ... }` and arms may have different
  types. As a value every arm is an expression and all have the same type. Every member exactly once; no wildcard arm.
- `==`, `print`, interpolation, borrowing, methods and field access on a union are errors; use `match`.
- A function whose last statement is a statement-`match` with every arm returning needs no final `return`.
- Emitted as a value type with an `int Tag` and one field per member, so primitives are not boxed.

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
