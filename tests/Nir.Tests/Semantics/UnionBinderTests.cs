using Nir.Compiler.Diagnostics;
using Nir.Compiler.Semantics;
using static Nir.Tests.Support.BinderHarness;

namespace Nir.Tests.Semantics;

public class UnionBinderTests
{
    // Lines 1 to 3 declare the types, so `fn main() {` is line 4 and the first statement is line 5 at column 5.
    private const string Types =
        "type Circle = struct { r: double }\n" +
        "type Square = struct { side: double }\n" +
        "type Shape = union { Circle, Square }\n";

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind(Types + "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    // ---- declarations ----

    [Fact]
    public void A_union_has_its_members_in_order()
    {
        var (program, _, diagnostics) = BindMain();

        Assert.Empty(diagnostics);
        var union = Assert.Single(program!.Unions);
        Assert.Equal("Shape", union.Name);
        Assert.Equal(["Circle", "Square"], union.Members.Select(m => m.Name));
        Assert.Equal(1, union.IndexOf(union.Members[1]));
    }

    [Fact]
    public void Members_can_be_primitives_and_tuples()
    {
        var (program, _, diagnostics) = Bind("type U = union { int, string, (int, int), double, bool }\nfn main() {\n}");

        Assert.Empty(diagnostics);
        Assert.Equal(5, program!.Unions.Single().Members.Length);
    }

    [Fact]
    public void Declarations_may_come_in_any_order()
    {
        var (_, _, diagnostics) = Bind("type U = union { A, B }\ntype B = struct { n: int }\ntype A = struct { n: int }\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_union_may_contain_an_alias_and_an_alias_may_contain_a_union()
    {
        var (_, _, diagnostics) = Bind("type P = (u: U, n: int)\ntype U = union { P2, int }\ntype P2 = (a: int, b: int)\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_repeated_member_is_reported()
    {
        var (_, bag, diagnostics) = Bind("type U = union { int, string, int }\nfn main() {\n}");

        Assert.Equal(["NIR2021 1:31"], diagnostics);
        Assert.Equal("'int' is listed twice in union 'U'", bag.Single().Message);
    }

    [Fact]
    public void A_tuple_member_is_the_same_as_another_with_the_same_elements()
    {
        var (_, _, diagnostics) = Bind("type U = union { (int, int), (a: int, b: int) }\nfn main() {\n}");

        Assert.Equal(["NIR2021 1:30"], diagnostics);
    }

    [Fact]
    public void A_union_cannot_contain_a_union()
    {
        var (_, bag, diagnostics) = Bind("type A = union { int, string }\ntype B = union { A, double }\nfn main() {\n}");

        Assert.Equal(["NIR2022 2:18"], diagnostics);
        Assert.Equal("a union cannot contain another union ('A') yet; list its members instead", bag.Single().Message);
    }

    [Fact]
    public void A_union_that_contains_itself_is_reported_once()
    {
        var (_, _, diagnostics) = Bind("type U = union { U, int }\nfn main() {\n}");

        Assert.Equal(["NIR2020 1:6"], diagnostics);
    }

    [Fact]
    public void An_unknown_member_type_is_reported_once()
    {
        var (_, _, diagnostics) = Bind("type U = union { Nope, int }\nfn main() {\n    let u: U = 1\n}");

        Assert.Equal(["NIR2006 1:18"], diagnostics);
    }

    [Fact]
    public void A_duplicate_union_name_is_NIR2013()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\ntype U = union { int, bool }\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6"], diagnostics);
    }

    [Fact]
    public void A_union_named_like_a_struct_or_a_primitive_is_NIR2013()
    {
        var (_, _, diagnostics) = Bind("type S = struct { n: int }\ntype S = union { int, bool }\ntype int = union { bool, string }\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6", "NIR2013 3:6"], diagnostics);
    }

    [Fact]
    public void A_struct_cannot_contain_itself_through_a_union()
    {
        var (_, _, diagnostics) = Bind("type S = struct { u: U }\ntype U = union { S, int }\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
    }

    [Fact]
    public void A_union_cannot_have_methods_or_be_a_struct_literal()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nU impl {\n}\nfn main() {\n    let u = U { n = 1 }\n}");

        Assert.Equal(["NIR2014 2:1", "NIR2014 5:13"], diagnostics);
    }

    // ---- converting a member into the union ----

    [Fact]
    public void A_member_converts_to_the_union_where_the_union_is_expected()
    {
        var (_, _, diagnostics) = Bind(
            Types + "type Holder = struct { s: Shape }\n" +
            "fn take(s: Shape) {\n}\n" +
            "fn make() -> Shape {\n    return Circle { r = 1.0 }\n}\n" +
            "fn main() {\n" +
            "    let a: Shape = Circle { r = 1.0 }\n" +
            "    let mut b: Shape = Square { side = 1.0 }\n" +
            "    b = Circle { r = 2.0 }\n" +
            "    take(Circle { r = 3.0 })\n" +
            "    let mut h = Holder { s = Square { side = 4.0 } }\n" +
            "    h.s = Circle { r = 5.0 }\n" +
            "}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_conversion_is_a_wrap_with_the_members_tag()
    {
        var (program, _, _) = BindMain("let s: Shape = Square { side = 1.0 }");

        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var wrap = Assert.IsType<BoundUnionWrap>(((BoundVariableDeclaration)main.Body.Statements[0]).Initializer);
        Assert.Equal(1, wrap.Index);
        Assert.Equal("Shape", wrap.Type.Name);
    }

    [Fact]
    public void Without_an_annotation_the_variable_keeps_the_members_type()
    {
        var (_, bag, diagnostics) = BindMain("let c = Circle { r = 1.0 }", "let s: Shape = c", "let again: Circle = s");

        Assert.Equal(["NIR3001 7:25"], diagnostics);
        Assert.Equal("cannot convert 'Shape' to 'Circle'", bag.Single().Message);
    }

    [Fact]
    public void A_type_that_is_not_a_member_does_not_convert()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = 5");

        Assert.Equal(["NIR3001 5:20"], diagnostics);
        Assert.Equal("cannot convert 'int' to 'Shape'", bag.Single().Message);
    }

    [Fact]
    public void A_union_does_not_convert_to_another_union()
    {
        var (_, _, diagnostics) = Bind("type A = union { int, string }\ntype B = union { int, string }\nfn main() {\n    let a: A = 1\n    let b: B = a\n}");

        Assert.Equal(["NIR3001 5:16"], diagnostics);
    }

    [Fact]
    public void A_wrong_argument_for_a_union_parameter_is_reported()
    {
        var (_, bag, diagnostics) = Bind(Types + "fn take(s: Shape) {\n}\nfn main() {\n    take(\"x\")\n}");

        Assert.Equal(["NIR3001 7:10"], diagnostics);
        Assert.Equal("cannot convert 'string' to 'Shape'", bag.Single().Message);
    }

    [Fact]
    public void A_member_in_a_tuple_literal_is_not_converted()
    {
        var (_, _, diagnostics) = BindMain("let t: (Shape, int) = (Circle { r = 1.0 }, 1)");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR3001", diagnostics[0]);
    }

    [Fact]
    public void A_function_can_return_a_union_member_from_different_branches()
    {
        var (_, _, diagnostics) = Bind(
            Types + "fn make(flag: bool) -> Shape {\n    if flag {\n        return Circle { r = 1.0 }\n    }\n    return Square { side = 1.0 }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    // ---- match ----

    [Fact]
    public void A_value_match_binds_each_arm_variable_with_the_members_type()
    {
        var (program, _, diagnostics) = BindMain(
            "let s: Shape = Circle { r = 1.0 }",
            "let area: double = match s {",
            "    c: Circle => c.r * 2.0,",
            "    q: Square => q.side,",
            "}");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var match = Assert.IsType<BoundMatch>(((BoundVariableDeclaration)main.Body.Statements[1]).Initializer);
        Assert.Equal(TypeSymbol.DoubleType, match.Type);
        Assert.Equal([0, 1], match.Arms.Select(a => a.Index));
        Assert.Equal(["Circle", "Square"], match.Arms.Select(a => a.Variable!.Type.Name));
        Assert.All(match.Arms, a => Assert.NotNull(a.Value));
    }

    [Fact]
    public void Arms_may_come_in_any_order()
    {
        var (_, _, diagnostics) = BindMain(
            "let s: Shape = Circle { r = 1.0 }",
            "let n = match s { q: Square => 1, c: Circle => 2 }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_statement_match_may_have_blocks_and_arms_of_different_types()
    {
        var (program, _, diagnostics) = BindMain(
            "let s: Shape = Circle { r = 1.0 }",
            "match s {",
            "    c: Circle => { print(\"c\") }",
            "    _: Square => 5",
            "}");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var match = Assert.IsType<BoundMatch>(((BoundExpressionStatement)main.Body.Statements[1]).Expression);
        Assert.Equal(TypeSymbol.UnitType, match.Type);
        Assert.All(match.Arms, a => Assert.NotNull(a.Body));
        Assert.Null(match.Arms[1].Variable);
    }

    [Fact]
    public void A_value_match_with_a_block_arm_is_reported()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = Circle { r = 1.0 }", "let n = match s { c: Circle => { 1 }, q: Square => 2 }");

        Assert.Equal(["NIR3022 6:23"], diagnostics);
        Assert.StartsWith("a match whose value is used needs an expression in every arm", bag.Single().Message);
    }

    [Fact]
    public void Arms_of_a_value_match_must_have_one_type()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = Circle { r = 1.0 }", "let n = match s { c: Circle => 1, q: Square => 2.5 }");

        Assert.Equal(["NIR3001 6:52"], diagnostics);
        Assert.Equal("the arms of a match must all have the same type: this one is 'double' but an earlier one is 'int'", bag.Single().Message);
    }

    [Fact]
    public void Matching_a_non_union_is_reported()
    {
        var (_, bag, diagnostics) = BindMain("let n = 5", "let x = match n { a: int => 1 }");

        Assert.Equal(["NIR3018 6:19"], diagnostics);
        Assert.Equal("cannot match on a value of type 'int'; only a union can be matched", bag.Single().Message);
    }

    [Fact]
    public void An_arm_for_a_type_that_is_not_a_member_is_reported()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = Circle { r = 1.0 }", "let n = match s { c: Circle => 1, q: Square => 2, i: int => 3 }");

        Assert.Equal(["NIR3019 6:58"], diagnostics);
        Assert.Equal("'int' is not one of the cases of 'Shape'", bag.Single().Message);
    }

    [Fact]
    public void Two_arms_for_one_member_are_reported()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = Circle { r = 1.0 }", "let n = match s { c: Circle => 1, d: Circle => 2, q: Square => 3 }");

        Assert.Equal(["NIR3020 6:42"], diagnostics);
        Assert.Equal("case 'Circle' is already handled by an earlier arm", bag.Single().Message);
    }

    [Fact]
    public void A_match_that_leaves_out_a_member_is_reported()
    {
        var (_, bag, diagnostics) = BindMain("let s: Shape = Circle { r = 1.0 }", "let n = match s { c: Circle => 1 }");

        Assert.Equal(["NIR3021 6:19"], diagnostics);
        Assert.Equal("match on 'Shape' does not cover 'Square'", bag.Single().Message);
    }

    [Fact]
    public void A_match_that_leaves_out_several_members_names_them_all()
    {
        var (_, bag, diagnostics) = Bind("type U = union { int, string, bool }\nfn main() {\n    let u: U = 1\n    match u { n: int => print(n) }\n}");

        Assert.Equal(["NIR3021 4:11"], diagnostics);
        Assert.Equal("match on 'U' does not cover 'string', 'bool'", bag.Single().Message);
    }

    [Fact]
    public void The_arm_variable_is_immutable()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    match u {\n        n: int => { n = 2 }\n        _: string => print(\"s\")\n    }\n}");

        Assert.Equal(["NIR3002 5:21"], diagnostics);
    }

    [Fact]
    public void The_arm_variable_follows_the_scope_rules()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let n = 1\n    let u: U = 1\n    match u {\n        n: int => print(n)\n        _: string => print(\"s\")\n    }\n}");

        Assert.Equal(["NIR2005 6:9"], diagnostics);
    }

    [Fact]
    public void Two_arms_may_use_the_same_variable_name()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    match u {\n        v: int => print(v)\n        v: string => print(v)\n    }\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_arm_variable_is_not_visible_after_the_match()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    match u {\n        v: int => print(v)\n        _: string => print(\"s\")\n    }\n    print(v)\n}");

        Assert.Equal(["NIR2001 8:11"], diagnostics);
    }

    [Fact]
    public void A_mistake_inside_an_arm_is_reported_once()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    match u {\n        n: int => print(nope)\n        _: string => print(\"s\")\n    }\n}");

        Assert.Equal(["NIR2001 5:25"], diagnostics);
    }

    [Fact]
    public void An_unknown_arm_type_is_reported_once()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    match u {\n        n: Nope => print(n)\n        _: string => print(\"s\")\n    }\n}");

        Assert.Equal(["NIR2006 5:12"], diagnostics);
    }

    [Fact]
    public void Arms_can_be_tuples()
    {
        var (_, _, diagnostics) = Bind("type U = union { (int, int), string }\nfn main() {\n    let u: U = (1, 2)\n    let n = match u { p: (int, int) => p.0 + p.1, s: string => 0 }\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_function_ending_in_a_match_whose_arms_all_return_needs_no_final_return()
    {
        var (_, _, diagnostics) = Bind(
            "type U = union { int, string }\nfn f(u: U) -> int {\n    match u {\n        n: int => { return n }\n        _: string => { return 0 }\n    }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_match_where_one_arm_does_not_return_still_needs_a_return()
    {
        var (_, _, diagnostics) = Bind(
            "type U = union { int, string }\nfn f(u: U) -> int {\n    match u {\n        n: int => { return n }\n        _: string => print(\"s\")\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR3007 2:4"], diagnostics);
    }

    // ---- what a union cannot do ----

    [Theory]
    [InlineData("let x = u == u", "NIR3003")]
    [InlineData("print(u)", "NIR3006")]
    [InlineData("print($\"{u}\")", "NIR3016")]
    [InlineData("u.ToString()", "NIR2011")]
    [InlineData("let n = u.x", "NIR2011")]
    [InlineData("let n = u as int", "NIR3001")]
    [InlineData("let (a, b) = u", "NIR3001")]
    public void A_union_can_only_be_taken_apart_with_match(string statement, string code)
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\nfn main() {\n    let u: U = 1\n    " + statement + "\n}");

        Assert.Single(diagnostics);
        Assert.StartsWith(code, diagnostics[0]);
    }

    [Fact]
    public void A_union_cannot_be_borrowed()
    {
        var (_, _, diagnostics) = Bind("type U = union { int, string }\ntype S = struct { n: int }\nfn f(s: &S) {\n}\nfn main() {\n    let u: U = 1\n    f(&u)\n}");

        Assert.Equal(["NIR3017 7:7"], diagnostics);
    }
}
