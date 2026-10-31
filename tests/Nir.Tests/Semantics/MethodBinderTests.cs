using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class MethodBinderTests
{
    // Lines 1 to 12 declare Point and its methods, so `fn main() {` is line 13 and the first statement of main is line 14.
    private const string Declarations =
        "type Point = struct { x: int, y: int }\n" +
        "Point impl {\n" +
        "    fn new(x: int, y: int) -> Self {\n" +
        "        return Self { x = x, y = y }\n" +
        "    }\n" +
        "    fn get(&self) -> int {\n" +
        "        return self.x\n" +
        "    }\n" +
        "    fn set(&mut self, v: int) {\n" +
        "        self.x = v\n" +
        "    }\n" +
        "}\n";

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

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind(Declarations + "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    // ---- declarations ----

    [Fact]
    public void Methods_and_associated_functions_bind_with_the_right_kinds()
    {
        var (program, _, diagnostics) = BindMain("let p = Point::new(1, 2)", "print(p.get())");

        Assert.Empty(diagnostics);
        var point = Assert.Single(program!.Structs);
        Assert.Equal(["new", "get", "set"], point.Methods.Select(m => m.Name));
        Assert.Equal([ReceiverKind.None, ReceiverKind.Shared, ReceiverKind.Mutable], point.Methods.Select(m => m.Receiver));
        Assert.All(point.Methods, m => Assert.Same(point, m.Owner));
        Assert.Same(point, point.FindMethod("new")!.ReturnType);
    }

    [Fact]
    public void Self_inside_a_method_has_the_borrow_type_of_its_receiver()
    {
        var (_, _, _) = BindMain();
        var (program, _, _) = BindMain();
        var point = program!.Structs[0];

        Assert.Equal("&Point", point.FindMethod("get")!.SelfVariable!.Type.Name);
        Assert.Equal("&mut Point", point.FindMethod("set")!.SelfVariable!.Type.Name);
        Assert.Null(point.FindMethod("new")!.SelfVariable);
    }

    [Fact]
    public void Several_impl_blocks_for_one_struct_merge()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn a(&self) -> int {\n        return self.n\n    }\n}\n" +
            "P impl {\n    fn b(&self) -> int {\n        return self.a()\n    }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_method_defined_twice_reports_NIR2003_at_the_second()
    {
        var (_, bag, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn a(&self) {\n    }\n    fn a(&self) {\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2003 5:8"], diagnostics);
        Assert.Equal("method 'a' is already defined for 'P'", bag.Single().Message);
    }

    [Fact]
    public void A_method_defined_in_two_impl_blocks_is_also_a_duplicate()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn a() {\n    }\n}\nP impl {\n    fn a() {\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2003 7:8"], diagnostics);
    }

    [Fact]
    public void An_impl_for_an_unknown_name_reports_NIR2006()
    {
        var (_, _, diagnostics) = Bind("Nope impl {\n    fn a() {\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:1"], diagnostics);
    }

    [Fact]
    public void An_impl_for_a_built_in_type_reports_NIR2014()
    {
        var (_, _, diagnostics) = Bind("int impl {\n    fn a() {\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2014 1:1"], diagnostics);
    }

    [Fact]
    public void Self_cannot_be_used_as_the_impl_target()
    {
        var (_, _, diagnostics) = Bind("Self impl {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:1"], diagnostics);
    }

    [Fact]
    public void A_receiver_on_a_free_function_reports_NIR2018()
    {
        var (_, _, diagnostics) = Bind("fn f(&self) {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2018 1:6"], diagnostics);
    }

    [Fact]
    public void Self_value_in_a_free_function_reports_NIR2018()
    {
        var (_, bag, diagnostics) = Bind("fn f() {\n    print(self)\n}\nfn main() {\n}");

        Assert.Equal(["NIR2018 2:11"], diagnostics);
        Assert.Equal("'self' can only be used inside a method that takes '&self' or '&mut self'", bag.Single().Message);
    }

    [Fact]
    public void Self_value_in_an_associated_function_reports_NIR2018()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn make() -> int {\n        return self.n\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2018 4:16"], diagnostics);
    }

    [Fact]
    public void The_type_Self_outside_an_impl_is_an_unknown_type()
    {
        var (_, _, diagnostics) = Bind("fn f() -> Self {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:11"], diagnostics);
    }

    [Fact]
    public void Self_can_be_used_as_a_parameter_type_and_in_literals_inside_an_impl()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn same(a: Self, b: Self) -> Self {\n        return Self { n = a.n + b.n }\n    }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    // ---- calls ----

    [Fact]
    public void A_method_call_has_the_method_result_type()
    {
        var (program, _, diagnostics) = BindMain("let p = Point::new(1, 2)", "let n: int = p.get()");

        Assert.Empty(diagnostics);
        var declaration = (BoundVariableDeclaration)program!.Functions.Single(f => f.Symbol.Name == "main").Body.Statements[1];
        Assert.IsType<BoundMethodCall>(declaration.Initializer);
    }

    [Fact]
    public void A_mutating_method_works_on_a_mutable_variable()
    {
        var (_, _, diagnostics) = BindMain("let mut p = Point::new(1, 2)", "p.set(5)");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_mutating_method_on_an_immutable_variable_reports_NIR3002()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point::new(1, 2)", "p.set(5)");

        Assert.Equal(["NIR3002 15:5"], diagnostics);
        Assert.Equal("cannot call 'set' (it takes '&mut self') because 'p' is not mutable", bag.Single().Message);
    }

    [Fact]
    public void A_mutating_method_on_a_parameter_reports_NIR3002()
    {
        var (_, _, diagnostics) = Bind(Declarations + "fn f(p: Point) {\n    p.set(1)\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 14:5"], diagnostics);
    }

    [Fact]
    public void A_mutating_method_on_a_temporary_reports_NIR3013()
    {
        var (_, bag, diagnostics) = BindMain("Point::new(1, 2).set(5)");

        Assert.Equal(["NIR3013 14:5"], diagnostics);
        Assert.Equal("cannot call 'set' on a temporary value because it takes '&mut self'; keep the value in a variable first", bag.Single().Message);
    }

    [Fact]
    public void A_reading_method_works_on_a_temporary()
    {
        var (_, _, diagnostics) = BindMain("print(Point::new(1, 2).get())");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_mutating_method_on_a_field_follows_the_mutability_of_the_root()
    {
        var declarations = "type Line = struct { to: Point }\n";
        var (_, _, ok) = Bind(declarations + Declarations + "fn main() {\n    let mut l = Line { to = Point::new(1, 2) }\n    l.to.set(5)\n}");
        var (_, _, bad) = Bind(declarations + Declarations + "fn main() {\n    let l = Line { to = Point::new(1, 2) }\n    l.to.set(5)\n}");

        Assert.Empty(ok);
        Assert.Equal(["NIR3002 16:5"], bad);
    }

    [Fact]
    public void An_unknown_method_reports_NIR2011_at_the_method()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point::new(1, 2)", "p.nope()");

        Assert.Equal(["NIR2011 15:7"], diagnostics);
        Assert.Equal("'nope' is not a member of 'Point'", bag.Single().Message);
    }

    [Fact]
    public void An_associated_function_cannot_be_called_on_a_value()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point::new(1, 2)", "p.new(1, 2)");

        Assert.Equal(["NIR2011 15:7"], diagnostics);
        Assert.Equal("'new' is an associated function; call it as Point::new(...)", bag.Single().Message);
    }

    [Fact]
    public void A_method_cannot_be_called_through_the_type()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point::new(1, 2)", "print(Point::get(p))");

        Assert.Equal(["NIR2011 15:18"], diagnostics);
        Assert.Equal("'get' is a method; call it on a value, like value.get(...)", bag.Single().Message);
    }

    [Fact]
    public void An_unknown_associated_function_reports_NIR2011()
    {
        var (_, _, diagnostics) = BindMain("let p = Point::nope(1)");

        Assert.Equal(["NIR2011 14:20"], diagnostics);
    }

    [Fact]
    public void Wrong_argument_count_and_type_for_methods()
    {
        var (_, bag, diagnostics) = BindMain("let mut p = Point::new(1, 2)", "p.set()", "p.set(1.5)", "let q = Point::new(1)");

        Assert.Equal(["NIR2004 15:5", "NIR3001 16:11", "NIR2004 17:13"], diagnostics);
        Assert.Equal("'Point.set' expects 1 argument(s) but got 0", bag[0].Message);
        Assert.Equal("'Point::new' expects 2 argument(s) but got 1", bag[2].Message);
    }

    [Fact]
    public void Self_cannot_be_assigned_and_fields_of_a_shared_self_cannot_change()
    {
        var (_, bag, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n" +
            "    fn a(&mut self) {\n        self = P { n = 1 }\n    }\n" +
            "    fn b(&self) {\n        self.n = 1\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 4:9", "NIR3002 7:9"], diagnostics);
        Assert.Equal("cannot assign to 'self' itself; assign to its fields instead", bag[0].Message);
        Assert.Equal("cannot assign to 'self.n' because 'self' is a read-only borrow ('&P'); take '&mut self' to change it", bag[1].Message);
    }

    [Fact]
    public void A_shared_self_cannot_call_a_mutating_method()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn bump(&mut self) {\n    }\n    fn look(&self) {\n        self.bump()\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 6:9"], diagnostics);
    }

    [Fact]
    public void A_mutating_self_can_call_mutating_and_reading_methods()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn bump(&mut self) {\n        self.n = self.n + 1\n    }\n    fn get(&self) -> int {\n        return self.n\n    }\n" +
            "    fn twice(&mut self) -> int {\n        self.bump()\n        self.bump()\n        return self.get()\n    }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Self_used_as_a_value_is_a_copy_of_the_struct()
    {
        var (program, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn copy(&self) -> Self {\n        return self\n    }\n}\nfn show(p: P) {\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        var method = program!.Structs[0].FindMethod("copy")!;
        var body = program.Functions.Single(f => f.Symbol == method).Body;
        var returned = (BoundReturnStatement)body.Statements[0];
        Assert.IsType<BoundDereference>(returned.Value);
    }

    [Fact]
    public void Self_can_be_passed_by_value_to_a_function()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nfn show(p: P) {\n}\nP impl {\n    fn go(&self) {\n        show(self)\n    }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Methods_can_call_free_functions_and_free_functions_can_call_methods()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nfn double(n: int) -> int {\n    return n * 2\n}\n" +
            "P impl {\n    fn doubled(&self) -> int {\n        return double(self.n)\n    }\n}\n" +
            "fn use_it(p: P) -> int {\n    return p.doubled()\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_method_may_share_a_name_with_a_field()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn n(&self) -> int {\n        return self.n\n    }\n}\n" +
            "fn main() {\n    let p = P { n = 1 }\n    print(p.n)\n    print(p.n())\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_error_in_a_receiver_or_argument_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("nope.get()", "let mut p = Point::new(1, 2)", "p.set(missing)");

        Assert.Equal(["NIR2001 14:5", "NIR2001 16:11"], diagnostics);
    }
}
