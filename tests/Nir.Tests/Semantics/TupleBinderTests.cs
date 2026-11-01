using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class TupleBinderTests
{
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) Bind(string text)
    {
        var source = new SourceText("test.nir", text);
        var bag = new DiagnosticBag();
        var program = Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }

    // `fn main() {` is line 1, so the first statement is on line 2 at column 5.
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind("fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    private static TypeSymbol TypeOfFirstVariable(BoundProgram program)
    {
        var main = program.Functions.Single(f => f.Symbol.Name == "main");
        return ((BoundVariableDeclaration)main.Body.Statements[0]).Variable.Type;
    }

    // ---- types and literals ----

    [Fact]
    public void A_tuple_literal_has_a_tuple_type_of_its_elements()
    {
        var (program, _, diagnostics) = BindMain("let t = (1, 2.5, \"x\")");

        Assert.Empty(diagnostics);
        var type = Assert.IsType<TupleTypeSymbol>(TypeOfFirstVariable(program!));
        Assert.Equal("(int, double, string)", type.Name);
        Assert.Equal([TypeSymbol.IntType, TypeSymbol.DoubleType, TypeSymbol.StringType], type.Elements.Select(e => e.Type));
    }

    [Fact]
    public void Elements_are_read_by_index()
    {
        var (_, _, diagnostics) = BindMain("let t = (1, 2.5)", "let a: int = t.0", "let b: double = t.1");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_index_past_the_end_is_not_a_member()
    {
        var (_, bag, diagnostics) = BindMain("let t = (1, 2)", "print(t.2)");

        Assert.Equal(["NIR2011 3:13"], diagnostics);
        Assert.Equal("'2' is not a member of '(int, int)'", bag.Single().Message);
    }

    [Fact]
    public void A_name_on_an_unnamed_tuple_is_not_a_member()
    {
        var (_, _, diagnostics) = BindMain("let t = (1, 2)", "print(t.x)");

        Assert.Equal(["NIR2011 3:13"], diagnostics);
    }

    [Fact]
    public void Nested_tuples_are_read_through()
    {
        var (_, _, diagnostics) = BindMain("let t = ((1, 2), \"s\")", "let a: int = t.0.1", "let s: string = t.1");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_tuple_may_be_a_temporary()
    {
        var (_, _, diagnostics) = BindMain("let a = (1, 2).1", "let b = f().0");

        Assert.Equal(["NIR2001 3:13"], diagnostics);
    }

    [Fact]
    public void A_unit_value_cannot_be_a_tuple_element()
    {
        var (_, bag, diagnostics) = BindMain("let t = (print(1), 2)");

        Assert.Equal(["NIR3005 2:14"], diagnostics);
        Assert.Equal("cannot use a value of type '()' as a tuple element", bag.Single().Message);
    }

    [Fact]
    public void An_error_inside_a_tuple_is_reported_once()
    {
        var (_, _, diagnostics) = BindMain("let t = (nope, 2)", "print(t.0)");

        Assert.Equal(["NIR2001 2:14"], diagnostics);
    }

    [Fact]
    public void There_is_no_limit_on_the_number_of_elements()
    {
        var (program, _, diagnostics) = BindMain("let t = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)", "let x: int = t.19");

        Assert.Empty(diagnostics);
        Assert.Equal(20, ((TupleTypeSymbol)TypeOfFirstVariable(program!)).Elements.Length);
    }

    [Fact]
    public void A_long_tuple_type_can_be_written_and_checked()
    {
        var (_, bag, diagnostics) = Bind(
            "fn f() -> (int, int, int, int, int, int, int, int, int) {\n    return (1, 2, 3, 4, 5, 6, 7, 8, \"9\")\n}\nfn main() {\n}");

        Assert.Equal(["NIR3001 2:12"], diagnostics);
        Assert.Equal("cannot convert '(int, int, int, int, int, int, int, int, string)' to '(int, int, int, int, int, int, int, int, int)'", bag.Single().Message);
    }

    // ---- sameness of types ----

    [Fact]
    public void Tuples_with_the_same_element_types_are_the_same_type()
    {
        var (_, _, diagnostics) = Bind(
            "fn f(p: (int, string)) -> (int, string) {\n    return p\n}\nfn main() {\n    let r: (int, string) = f((1, \"a\"))\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Tuples_with_different_element_types_are_different_types()
    {
        var (_, bag, diagnostics) = BindMain("let t: (int, int) = (1, 2.5)");

        Assert.Equal(["NIR3001 2:25"], diagnostics);
        Assert.Equal("cannot convert '(int, double)' to '(int, int)'", bag.Single().Message);
    }

    [Fact]
    public void A_tuple_of_another_length_is_a_different_type()
    {
        var (_, _, diagnostics) = BindMain("let t: (int, int) = (1, 2, 3)");

        Assert.Equal(["NIR3001 2:25"], diagnostics);
    }

    [Fact]
    public void Names_are_not_part_of_the_type()
    {
        var (_, _, diagnostics) = Bind(
            "type Pair = (x: int, y: int)\n" +
            "fn f(p: (a: int, b: int)) -> Pair {\n    return p\n}\n" +
            "fn main() {\n    let p: Pair = (1, 2)\n    let q: (int, int) = p\n    let r: Pair = f(q)\n}");

        Assert.Empty(diagnostics);
    }

    // ---- named tuples and aliases ----

    [Fact]
    public void A_named_tuple_is_read_by_name_and_by_index()
    {
        var (_, _, diagnostics) = Bind(
            "type Pair = (x: int, y: int)\nfn main() {\n    let p: Pair = (1, 2)\n    let a: int = p.x\n    let b: int = p.1\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_unknown_name_on_a_named_tuple_is_not_a_member()
    {
        var (_, bag, diagnostics) = Bind("type Pair = (x: int, y: int)\nfn main() {\n    let p: Pair = (1, 2)\n    print(p.z)\n}");

        Assert.Equal(["NIR2011 4:13"], diagnostics);
        Assert.Equal("'z' is not a member of '(x: int, y: int)'", bag.Single().Message);
    }

    [Fact]
    public void A_named_tuple_type_can_be_written_inline()
    {
        var (_, _, diagnostics) = Bind(
            "fn divmod(a: int, b: int) -> (quotient: int, remainder: int) {\n    return (a / b, a % b)\n}\nfn main() {\n    let r = divmod(7, 2)\n    print(r.quotient + r.remainder)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Aliases_may_appear_in_any_order_and_refer_to_each_other()
    {
        var (_, _, diagnostics) = Bind(
            "type Outer = (inner: Inner, n: int)\ntype Inner = (a: int, b: int)\nfn main() {\n    let o: Outer = ((1, 2), 3)\n    print(o.inner.b)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_alias_may_use_a_struct_declared_later()
    {
        var (_, _, diagnostics) = Bind(
            "type Pair = (p: P, n: int)\ntype P = struct { v: int }\nfn main() {\n    let x: Pair = (P { v = 1 }, 2)\n    print(x.p.v)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_alias_that_refers_to_itself_is_reported_once()
    {
        var (_, bag, diagnostics) = Bind("type A = (a: A, n: int)\nfn main() {\n}");

        Assert.Equal(["NIR2020 1:6"], diagnostics);
        Assert.Equal("type 'A' is defined in terms of itself", bag.Single().Message);
    }

    [Fact]
    public void Two_aliases_that_refer_to_each_other_are_reported_once()
    {
        var (_, _, diagnostics) = Bind("type A = (b: B, n: int)\ntype B = (a: A, n: int)\nfn main() {\n}");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR2020", diagnostics[0]);
    }

    [Fact]
    public void A_duplicate_alias_name_is_NIR2013()
    {
        var (_, _, diagnostics) = Bind("type P = (int, int)\ntype P = (int, int)\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6"], diagnostics);
    }

    [Fact]
    public void An_alias_named_like_a_struct_or_a_primitive_is_NIR2013()
    {
        var (_, _, diagnostics) = Bind("type P = struct { n: int }\ntype P = (int, int)\ntype int = (int, int)\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6", "NIR2013 3:6"], diagnostics);
    }

    [Fact]
    public void An_alias_cannot_be_used_as_a_struct_literal_or_an_impl_target()
    {
        var (_, _, diagnostics) = Bind("type P = (int, int)\nP impl {\n}\nfn main() {\n    let p = P { n = 1 }\n}");

        Assert.Equal(["NIR2014 2:1", "NIR2014 5:13"], diagnostics);
    }

    [Fact]
    public void Mixed_named_and_unnamed_elements_are_reported()
    {
        var (_, bag, diagnostics) = Bind("type P = (x: int, int)\nfn main() {\n}");

        Assert.Equal(["NIR2019 1:10"], diagnostics);
        Assert.Equal("either every element of a tuple type has a name or none does", bag.Single().Message);
    }

    [Fact]
    public void A_repeated_element_name_is_reported()
    {
        var (_, _, diagnostics) = Bind("type P = (x: int, x: int)\nfn main() {\n}");

        Assert.Equal(["NIR2016 1:19"], diagnostics);
    }

    [Fact]
    public void An_unknown_type_inside_a_tuple_type_is_reported_once()
    {
        var (_, _, diagnostics) = Bind("fn f(p: (int, Nope)) {\n}\nfn main() {\n    f((1, 2))\n}");

        Assert.Equal(["NIR2006 1:15"], diagnostics);
    }

    // ---- structs holding tuples ----

    [Fact]
    public void A_struct_may_have_a_tuple_field()
    {
        var (_, _, diagnostics) = Bind(
            "type S = struct { p: (int, int), name: string }\nfn main() {\n    let mut s = S { p = (1, 2), name = \"n\" }\n    s.p.1 = 5\n    print(s.p.0 + s.p.1)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_struct_cannot_contain_itself_through_a_tuple()
    {
        var (_, _, diagnostics) = Bind("type S = struct { p: (int, S) }\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
    }

    [Fact]
    public void A_struct_cannot_contain_itself_through_an_alias()
    {
        var (_, _, diagnostics) = Bind("type S = struct { p: Pair }\ntype Pair = (a: S, b: int)\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
    }

    // ---- changing elements ----

    [Fact]
    public void An_element_of_a_mutable_tuple_can_be_assigned()
    {
        var (_, _, diagnostics) = BindMain("let mut t = (1, 2)", "t.0 = 5", "t.1 = t.0 + 1");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_element_of_an_immutable_tuple_cannot_be_assigned()
    {
        var (_, bag, diagnostics) = BindMain("let t = (1, 2)", "t.0 = 5");

        Assert.Equal(["NIR3002 3:5"], diagnostics);
        Assert.Equal("cannot assign to 't.0' because 't' is not mutable", bag.Single().Message);
    }

    [Fact]
    public void A_parameter_tuple_cannot_be_changed()
    {
        var (_, _, diagnostics) = Bind("fn f(p: (int, int)) {\n    p.0 = 1\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 2:5"], diagnostics);
    }

    [Fact]
    public void An_element_assignment_checks_the_type()
    {
        var (_, bag, diagnostics) = BindMain("let mut t = (1, 2)", "t.0 = \"x\"");

        Assert.Equal(["NIR3001 3:11"], diagnostics);
        Assert.Equal("cannot convert 'string' to 'int'", bag.Single().Message);
    }

    [Fact]
    public void A_whole_tuple_variable_can_be_assigned_when_mutable()
    {
        var (_, _, diagnostics) = BindMain("let mut t = (1, 2)", "t = (3, 4)");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Assigning_a_tuple_of_another_type_is_a_mismatch()
    {
        var (_, _, diagnostics) = BindMain("let mut t = (1, 2)", "t = (3, \"x\")");

        Assert.Equal(["NIR3001 3:9"], diagnostics);
    }

    // ---- taking apart ----

    [Fact]
    public void Destructuring_declares_one_variable_per_element()
    {
        var (program, _, diagnostics) = BindMain("let (a, b) = (1, \"x\")", "let i: int = a", "let s: string = b");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var destructuring = Assert.IsType<BoundDestructuring>(main.Body.Statements[0]);
        Assert.Equal([TypeSymbol.IntType, TypeSymbol.StringType], destructuring.Variables.Select(v => v!.Type));
    }

    [Fact]
    public void Destructured_variables_are_immutable_unless_marked_mut()
    {
        var (_, _, diagnostics) = BindMain("let (a, mut b) = (1, 2)", "b = 5", "a = 5");

        Assert.Equal(["NIR3002 4:5"], diagnostics);
    }

    [Fact]
    public void An_underscore_takes_an_element_and_declares_nothing()
    {
        var (program, _, diagnostics) = BindMain("let (_, b) = (1, 2)", "let (_, c) = (3, 4)");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        Assert.Null(((BoundDestructuring)main.Body.Statements[0]).Variables[0]);
    }

    [Fact]
    public void Destructuring_a_non_tuple_is_a_type_mismatch()
    {
        var (_, bag, diagnostics) = BindMain("let (a, b) = 5", "print(a)");

        Assert.Equal(["NIR3001 2:18"], diagnostics);
        Assert.Equal("cannot take apart a value of type 'int'; only a tuple can be", bag.Single().Message);
    }

    [Fact]
    public void Destructuring_into_the_wrong_number_of_names_is_a_type_mismatch()
    {
        var (_, bag, diagnostics) = BindMain("let (a, b, c) = (1, 2)", "print(a)");

        Assert.Equal(["NIR3001 2:5"], diagnostics);
        Assert.Equal("cannot take apart '(int, int)' into 3 names; it has 2 elements", bag.Single().Message);
    }

    [Fact]
    public void Destructured_names_follow_the_scope_rules()
    {
        var (_, _, diagnostics) = BindMain("let a = 1", "let (a, b) = (1, 2)");

        Assert.Equal(["NIR2005 3:10"], diagnostics);
    }

    [Fact]
    public void The_right_side_cannot_see_the_names_being_declared()
    {
        var (_, _, diagnostics) = BindMain("let (a, b) = (a, 2)");

        Assert.Equal(["NIR2001 2:19"], diagnostics);
    }

    // ---- things a tuple cannot do (yet) ----

    [Theory]
    [InlineData("let u = t == t")]
    [InlineData("let u = t != t")]
    public void Tuples_cannot_be_compared(string statement)
    {
        var (_, bag, diagnostics) = BindMain("let t = (1, 2)", statement);

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR3003", diagnostics[0]);
        Assert.Contains("cannot be applied to '(int, int)' and '(int, int)'", bag.Single().Message);
    }

    [Fact]
    public void A_tuple_cannot_be_printed()
    {
        var (_, _, diagnostics) = BindMain("let t = (1, 2)", "print(t)");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR3006", diagnostics[0]);
    }

    [Fact]
    public void A_tuple_cannot_go_in_an_interpolation_hole()
    {
        var (_, bag, diagnostics) = BindMain("let t = (1, 2)", "print($\"{t}\")");

        Assert.Equal(["NIR3016 3:14"], diagnostics);
        Assert.Equal("cannot put a value of type '(int, int)' in an interpolated string; use its elements", bag.Single().Message);
    }

    [Fact]
    public void A_tuple_element_can_go_in_an_interpolation_hole()
    {
        var (_, _, diagnostics) = BindMain("let t = (1, \"s\")", "print($\"{t.0} {t.1}\")");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_tuple_has_no_methods()
    {
        var (_, bag, diagnostics) = BindMain("let t = (1, 2)", "t.ToString()");

        Assert.Equal(["NIR2011 3:7"], diagnostics);
        Assert.Equal("'ToString' is not a member of '(int, int)'", bag.Single().Message);
    }

    [Fact]
    public void A_tuple_cannot_be_borrowed()
    {
        var (_, _, diagnostics) = Bind("fn f(p: &(int, int)) {\n}\nfn main() {\n}");

        Assert.StartsWith("NIR1001", diagnostics[0]); // a syntax error; the compiler stops there
    }

    [Fact]
    public void A_borrow_argument_of_a_tuple_is_rejected()
    {
        var (_, _, diagnostics) = Bind("type S = struct { n: int }\nfn f(s: &S) {\n}\nfn main() {\n    let t = (1, 2)\n    f(&t)\n}");

        Assert.Equal(["NIR3017 6:7"], diagnostics);
    }

    [Fact]
    public void A_function_can_return_a_tuple_and_a_tuple_can_be_a_parameter()
    {
        var (_, _, diagnostics) = Bind(
            "fn swap(p: (int, string)) -> (string, int) {\n    return (p.1, p.0)\n}\nfn main() {\n    let (s, n) = swap((1, \"a\"))\n    print(s)\n    print(n)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Returning_the_wrong_tuple_is_a_mismatch()
    {
        var (_, bag, diagnostics) = Bind("fn f() -> (int, int) {\n    return (1, \"a\")\n}\nfn main() {\n}");

        Assert.Equal(["NIR3001 2:12"], diagnostics);
        Assert.Equal("cannot convert '(int, string)' to '(int, int)'", bag.Single().Message);
    }
}
