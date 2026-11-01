using Nir.Compiler.Diagnostics;
using Nir.Compiler.Semantics;
using static Nir.Tests.Support.BinderHarness;

namespace Nir.Tests.Semantics;

public class StructBinderTests
{
    // Line 1 declares Point, so `fn main() {` is line 2 and the first statement of main is on line 3 at column 5.
    private const string PointDeclaration = "type Point = struct { x: int, y: int }\n";

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind(PointDeclaration + "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    // ---- declarations ----

    [Fact]
    public void A_struct_with_fields_binds()
    {
        var (program, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print(p.x + p.y)");

        Assert.Empty(diagnostics);
        var point = Assert.Single(program!.Structs);
        Assert.Equal("Point", point.Name);
        Assert.Equal(["x", "y"], point.Fields.Select(f => f.Name));
        Assert.All(point.Fields, f => Assert.Equal(TypeSymbol.IntType, f.Type));
    }

    [Fact]
    public void A_struct_may_be_declared_after_it_is_used_and_may_use_a_later_struct()
    {
        var (_, _, diagnostics) = Bind(
            "fn main() {\n    let l = Line { from = Point { x = 1 }, to = Point { x = 2 } }\n    print(l.to.x)\n}\n" +
            "type Line = struct { from: Point, to: Point }\n" +
            "type Point = struct { x: int }\n");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_empty_struct_binds()
    {
        var (_, _, diagnostics) = Bind("type Nothing = struct {}\nfn main() {\n    let n = Nothing {}\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_struct_name_used_twice_reports_NIR2013_at_the_second()
    {
        var (_, bag, diagnostics) = Bind("type P = struct { }\ntype P = struct { }\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6"], diagnostics);
        Assert.Equal("type 'P' is already defined", bag.Single().Message);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("double")]
    [InlineData("bool")]
    [InlineData("string")]
    public void A_struct_cannot_be_named_like_a_built_in_type(string name)
    {
        var (_, _, diagnostics) = Bind($"type {name} = struct {{ }}\nfn main() {{\n}}");

        Assert.Equal(["NIR2013 1:6"], diagnostics);
    }

    [Fact]
    public void A_struct_cannot_be_named_Program()
    {
        var (_, _, diagnostics) = Bind("type Program = struct { }\nfn main() {\n}");

        Assert.Equal(["NIR2013 1:6"], diagnostics);
    }

    [Fact]
    public void A_struct_cannot_be_named_like_an_import()
    {
        var (_, _, diagnostics) = Bind("using clr::cs::System::Math\ntype Math = struct { }\nfn main() {\n}");

        Assert.Equal(["NIR2013 2:6"], diagnostics);
    }

    [Fact]
    public void A_field_of_an_unknown_type_reports_NIR2006()
    {
        var (_, _, diagnostics) = Bind("type P = struct { x: nope }\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:22"], diagnostics);
    }

    [Fact]
    public void A_field_declared_twice_reports_NIR2016_at_the_second()
    {
        var (_, _, diagnostics) = Bind("type P = struct { x: int, x: int }\nfn main() {\n}");

        Assert.Equal(["NIR2016 1:27"], diagnostics);
    }

    [Fact]
    public void A_struct_that_contains_itself_reports_NIR2012()
    {
        var (_, bag, diagnostics) = Bind("type A = struct { a: A }\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
        Assert.Equal("struct 'A' contains itself, so it would have no size", bag.Single().Message);
    }

    [Fact]
    public void Two_structs_that_contain_each_other_are_reported_once()
    {
        var (_, _, diagnostics) = Bind("type A = struct { b: B }\ntype B = struct { a: A }\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
    }

    [Fact]
    public void A_longer_cycle_is_reported_once()
    {
        var (_, _, diagnostics) = Bind(
            "type A = struct { b: B }\ntype B = struct { c: C }\ntype C = struct { a: A }\nfn main() {\n}");

        Assert.Equal(["NIR2012 1:19"], diagnostics);
    }

    [Fact]
    public void Nested_structs_that_do_not_loop_are_fine()
    {
        var (_, _, diagnostics) = Bind(
            "type A = struct { x: int }\ntype B = struct { a: A, c: A }\ntype C = struct { b: B, a: A }\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    // ---- literals ----

    [Fact]
    public void A_literal_gives_the_struct_type_and_keeps_the_written_order()
    {
        var (program, _, diagnostics) = BindMain("let p = Point { y = 2, x = 1 }");

        Assert.Empty(diagnostics);
        var declaration = (BoundVariableDeclaration)program!.Functions[0].Body.Statements[0];
        var literal = Assert.IsType<BoundStructLiteral>(declaration.Initializer);
        Assert.Equal(["y", "x"], literal.Initializers.Select(i => i.Field.Name));
        Assert.Same(program.Structs[0], declaration.Variable.Type);
    }

    [Fact]
    public void A_literal_with_a_missing_field_reports_NIR2017_at_the_type_name()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1 }");

        Assert.Equal(["NIR2017 3:13"], diagnostics);
        Assert.Equal("missing field 'y' in 'Point'", bag.Single().Message);
    }

    [Fact]
    public void Several_missing_fields_are_one_diagnostic()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { }");

        Assert.Equal(["NIR2017 3:13"], diagnostics);
        Assert.Equal("missing fields 'x', 'y' in 'Point'", bag.Single().Message);
    }

    [Fact]
    public void A_literal_with_an_unknown_field_reports_NIR2011()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1, y = 2, z = 3 }");

        Assert.Equal(["NIR2011 3:35"], diagnostics);
        Assert.Equal("'z' is not a member of 'Point'", bag.Single().Message);
    }

    [Fact]
    public void A_field_given_twice_reports_NIR2016_at_the_second()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, x = 2, y = 3 }");

        Assert.Equal(["NIR2016 3:28"], diagnostics);
    }

    [Fact]
    public void A_field_value_of_the_wrong_type_reports_NIR3001_at_the_value()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1.5, y = 2 }");

        Assert.Equal(["NIR3001 3:25"], diagnostics);
    }

    [Fact]
    public void An_error_inside_a_field_value_is_reported_once()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = nope, y = 2 }");

        Assert.Equal(["NIR2001 3:25"], diagnostics);
    }

    [Fact]
    public void A_literal_of_an_unknown_name_reports_NIR2006()
    {
        var (_, _, diagnostics) = BindMain("let p = Nope { x = 1 }");

        Assert.Equal(["NIR2006 3:13"], diagnostics);
    }

    [Fact]
    public void A_literal_of_a_built_in_type_reports_NIR2014()
    {
        var (_, bag, diagnostics) = BindMain("let p = int { x = 1 }");

        Assert.Equal(["NIR2014 3:13"], diagnostics);
        Assert.Equal("'int' is not a struct", bag.Single().Message);
    }

    // ---- fields ----

    [Fact]
    public void Field_reads_have_the_field_type_and_chain()
    {
        var (program, _, diagnostics) = Bind(
            "type Line = struct { from: Point, to: Point, label: string }\n" + PointDeclaration +
            "fn main() {\n    let l = Line { from = Point { x = 1, y = 2 }, to = Point { x = 3, y = 4 }, label = \"l\" }\n" +
            "    let a: int = l.to.x\n    let s: string = l.label\n}");

        Assert.Empty(diagnostics);
        var declaration = (BoundVariableDeclaration)program!.Functions[0].Body.Statements[1];
        var read = Assert.IsType<BoundFieldAccess>(declaration.Initializer);
        Assert.Equal("x", read.Field.Name);
        Assert.Equal("to", Assert.IsType<BoundFieldAccess>(read.Receiver).Field.Name);
    }

    [Fact]
    public void An_unknown_field_reports_NIR2011_at_the_field()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print(p.z)");

        Assert.Equal(["NIR2011 4:13"], diagnostics);
        Assert.Equal("'z' is not a member of 'Point'", bag.Single().Message);
    }

    [Fact]
    public void A_field_of_a_number_reports_NIR2011()
    {
        var (_, bag, diagnostics) = BindMain("let n = 1", "print(n.x)");

        Assert.Equal(["NIR2011 4:13"], diagnostics);
        Assert.Equal("'x' is not a member of 'int'", bag.Single().Message);
    }

    [Fact]
    public void A_field_of_an_error_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("print(nope.x)");

        Assert.Equal(["NIR2001 3:11"], diagnostics);
    }

    // ---- assignment ----

    [Fact]
    public void A_field_of_a_mutable_struct_can_be_assigned()
    {
        var (program, _, diagnostics) = BindMain("let mut p = Point { x = 1, y = 2 }", "p.x = 5", "p.y = p.x + 1");

        Assert.Empty(diagnostics);
        Assert.IsType<BoundFieldAssignment>(program!.Functions[0].Body.Statements[1]);
    }

    [Fact]
    public void A_nested_field_of_a_mutable_struct_can_be_assigned()
    {
        var (_, _, diagnostics) = Bind(
            PointDeclaration + "type Line = struct { to: Point }\n" +
            "fn main() {\n    let mut l = Line { to = Point { x = 1, y = 2 } }\n    l.to.x = 9\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_field_of_an_immutable_struct_cannot_be_assigned()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "p.x = 5");

        Assert.Equal(["NIR3002 4:5"], diagnostics);
        Assert.Equal("cannot assign to 'p.x' because 'p' is not mutable", bag.Single().Message);
    }

    [Fact]
    public void A_nested_field_of_an_immutable_struct_cannot_be_assigned()
    {
        var (_, bag, diagnostics) = Bind(
            PointDeclaration + "type Line = struct { to: Point }\n" +
            "fn main() {\n    let l = Line { to = Point { x = 1, y = 2 } }\n    l.to.x = 9\n}");

        Assert.Equal(["NIR3002 5:5"], diagnostics);
        Assert.Equal("cannot assign to 'l.to.x' because 'l' is not mutable", bag.Single().Message);
    }

    [Fact]
    public void A_field_of_a_parameter_cannot_be_assigned()
    {
        var (_, _, diagnostics) = Bind(PointDeclaration + "fn f(p: Point) {\n    p.x = 1\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 3:5"], diagnostics);
    }

    [Fact]
    public void A_field_of_a_call_result_is_not_an_assignment_target_at_all()
    {
        // The grammar only accepts a name (or `self`) followed by fields on the left of `=`, so this is a syntax error.
        var (_, _, diagnostics) = Bind(
            PointDeclaration + "fn make() -> Point {\n    return Point { x = 1, y = 2 }\n}\nfn main() {\n    make().x = 5\n}");

        Assert.StartsWith("NIR1001", Assert.Single(diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_assignment_checks_the_type()
    {
        var (_, _, diagnostics) = BindMain("let mut p = Point { x = 1, y = 2 }", "p.x = 2.5");

        Assert.Equal(["NIR3001 4:11"], diagnostics);
    }

    [Fact]
    public void Assigning_an_unknown_field_reports_NIR2011_once()
    {
        var (_, _, diagnostics) = BindMain("let mut p = Point { x = 1, y = 2 }", "p.z = 1");

        Assert.Equal(["NIR2011 4:7"], diagnostics);
    }

    // ---- values ----

    [Fact]
    public void Structs_work_as_parameters_results_and_annotations()
    {
        var (_, _, diagnostics) = Bind(
            PointDeclaration +
            "fn shift(p: Point, dx: int) -> Point {\n    let mut q = p\n    q.x = q.x + dx\n    return q\n}\n" +
            "fn main() {\n    let a: Point = Point { x = 1, y = 2 }\n    let b = shift(a, 5)\n    print(b.x)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Passing_the_wrong_struct_or_a_number_reports_NIR3001()
    {
        var (_, bag, diagnostics) = Bind(
            PointDeclaration + "type Other = struct { x: int }\nfn f(p: Point) {\n}\n" +
            "fn main() {\n    f(Other { x = 1 })\n    f(5)\n}");

        Assert.Equal(["NIR3001 6:7", "NIR3001 7:7"], diagnostics);
        Assert.Equal("cannot convert 'Other' to 'Point'", bag[0].Message);
    }

    [Fact]
    public void An_annotation_must_match_the_struct()
    {
        var (_, _, diagnostics) = BindMain("let a: Point = 5");

        Assert.Equal(["NIR3001 3:20"], diagnostics);
    }

    [Theory]
    [InlineData("p == q")]
    [InlineData("p != q")]
    [InlineData("p + q")]
    public void Operators_on_structs_report_NIR3003(string expression)
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "let q = p", $"print({expression})");

        Assert.Contains("NIR3003", diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Printing_a_struct_reports_NIR3006()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print(p)");

        Assert.Equal(["NIR3006 4:5"], diagnostics);
    }

    [Fact]
    public void A_struct_in_an_interpolation_hole_reports_NIR3016()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print($\"{p}\")");

        Assert.Equal(["NIR3016 4:14"], diagnostics);
        Assert.Equal("cannot put a value of type 'Point' in an interpolated string; use its fields", bag.Single().Message);
    }

    [Fact]
    public void A_field_in_an_interpolation_hole_is_fine()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print($\"{p.x},{p.y}\")");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_struct_cannot_be_converted_with_as()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "print(p as int)");

        Assert.Equal(["NIR3001 4:11"], diagnostics);
    }
}
