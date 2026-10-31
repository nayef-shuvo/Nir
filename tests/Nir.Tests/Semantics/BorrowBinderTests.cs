using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class BorrowBinderTests
{
    // Lines 1 to 9 declare Point and three functions, so `fn main() {` is line 10 and its first statement is line 11 at column 5.
    private const string Declarations =
        "type Point = struct { x: int, y: int }\n" +
        "fn bump(p: &mut Point) {\n    p.y = p.y + 1\n}\n" +
        "fn show(p: &Point) {\n    print(p.x)\n}\n" +
        "fn take(p: Point) {\n}\n";

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

    private const string Mutable = "let mut p = Point { x = 1, y = 2 }";

    // ---- the call site ----

    [Fact]
    public void Borrows_match_borrow_parameters()
    {
        var (program, _, diagnostics) = BindMain(Mutable, "bump(&mut p)", "show(&p)", "show(&mut p)");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var call = (BoundCallExpression)((BoundExpressionStatement)main.Body.Statements[1]).Expression;
        var borrow = Assert.IsType<BoundBorrowExpression>(call.Arguments[0]);
        Assert.Equal("&mut Point", borrow.Type.Name);
    }

    [Fact]
    public void A_shared_borrow_does_not_fit_a_mutable_parameter()
    {
        var (_, bag, diagnostics) = BindMain(Mutable, "bump(&p)");

        Assert.Equal(["NIR3001 12:10"], diagnostics);
        Assert.Equal("cannot convert '&Point' to '&mut Point'; borrow it with '&mut' instead", bag.Single().Message);
    }

    [Fact]
    public void A_plain_value_does_not_fit_a_borrow_parameter_and_the_message_says_how_to_fix_it()
    {
        var (_, bag, diagnostics) = BindMain(Mutable, "bump(p)", "show(p)");

        Assert.Equal(["NIR3001 12:10", "NIR3001 13:10"], diagnostics);
        Assert.Equal("cannot convert 'Point' to '&mut Point'; write '&mut' before the variable to borrow it", bag[0].Message);
        Assert.Equal("cannot convert 'Point' to '&Point'; write '&' before the variable to borrow it", bag[1].Message);
    }

    [Fact]
    public void A_borrow_does_not_fit_a_by_value_parameter()
    {
        var (_, bag, diagnostics) = BindMain(Mutable, "take(&p)");

        Assert.Equal(["NIR3001 12:10"], diagnostics);
        Assert.Equal(
            "cannot convert '&Point' to 'Point'; a borrow cannot be passed where a value is expected, pass the variable itself to copy it",
            bag.Single().Message);
    }

    [Fact]
    public void A_mutable_borrow_of_an_immutable_variable_reports_NIR3002()
    {
        var (_, bag, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "bump(&mut p)");

        Assert.Equal(["NIR3002 12:10"], diagnostics);
        Assert.Equal("cannot borrow 'p' with '&mut' because 'p' is not mutable", bag.Single().Message);
    }

    [Fact]
    public void A_shared_borrow_of_an_immutable_variable_is_fine()
    {
        var (_, _, diagnostics) = BindMain("let p = Point { x = 1, y = 2 }", "show(&p)");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_by_value_parameter_cannot_be_borrowed_mutably()
    {
        var (_, _, diagnostics) = Bind(Declarations + "fn f(p: Point) {\n    bump(&mut p)\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 11:10"], diagnostics);
    }

    [Fact]
    public void A_shared_borrow_cannot_be_borrowed_mutably()
    {
        var (_, bag, diagnostics) = Bind(Declarations + "fn f(p: &Point) {\n    bump(&mut p)\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 11:10"], diagnostics);
        Assert.Equal("cannot borrow 'p' with '&mut' because 'p' is a read-only borrow ('&Point'); take '&mut' to change it", bag.Single().Message);
    }

    [Fact]
    public void A_borrow_can_be_passed_on_and_a_mutable_one_can_be_downgraded()
    {
        var (_, _, diagnostics) = Bind(Declarations + "fn f(p: &mut Point) {\n    bump(&mut p)\n    show(&p)\n    show(&mut p)\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Self_can_be_borrowed_in_the_right_kind_of_method()
    {
        var (_, _, ok) = Bind(
            Declarations + "Point impl {\n    fn a(&mut self) {\n        bump(&mut self)\n        show(&self)\n    }\n}\nfn main() {\n}");
        var (_, bag, bad) = Bind(
            Declarations + "Point impl {\n    fn a(&self) {\n        bump(&mut self)\n    }\n}\nfn main() {\n}");

        Assert.Empty(ok);
        Assert.Equal(["NIR3002 12:14"], bad);
        Assert.Equal("cannot borrow 'self' with '&mut' because 'self' is a read-only borrow ('&Point'); take '&mut self' to change it", bag.Single().Message);
    }

    [Fact]
    public void Methods_can_take_borrow_parameters()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn absorb(&mut self, other: &P) {\n        self.n = self.n + other.n\n    }\n}\n" +
            "fn main() {\n    let mut a = P { n = 1 }\n    let b = P { n = 2 }\n    a.absorb(&b)\n}");

        Assert.Empty(diagnostics);
    }

    // ---- inside the function ----

    [Fact]
    public void A_mutable_borrow_allows_field_writes_and_a_shared_one_does_not()
    {
        var (_, bag, diagnostics) = Bind(Declarations + "fn a(p: &mut Point) {\n    p.x = 1\n}\nfn b(p: &Point) {\n    p.x = 1\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 14:5"], diagnostics);
        Assert.Equal("cannot assign to 'p.x' because 'p' is a read-only borrow ('&Point'); take '&mut' to change it", bag.Single().Message);
    }

    [Fact]
    public void A_borrow_parameter_cannot_be_assigned()
    {
        var (_, _, diagnostics) = Bind(Declarations + "fn f(p: &mut Point) {\n    p = Point { x = 1, y = 2 }\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 11:5"], diagnostics);
    }

    [Fact]
    public void A_borrow_used_as_a_value_is_a_copy_of_the_struct()
    {
        var (program, _, diagnostics) = Bind(Declarations + "fn f(p: &mut Point) -> Point {\n    let q = p\n    return q\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        var function = program!.Functions.Single(f => f.Symbol.Name == "f");
        var declaration = (BoundVariableDeclaration)function.Body.Statements[0];
        Assert.IsType<BoundDereference>(declaration.Initializer);
        Assert.Equal("Point", declaration.Variable.Type.Name);
    }

    [Fact]
    public void A_borrow_parameter_allows_field_reads_and_method_calls()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn get(&self) -> int {\n        return self.n\n    }\n    fn bump(&mut self) {\n        self.n = self.n + 1\n    }\n}\n" +
            "fn f(p: &mut P) -> int {\n    p.bump()\n    return p.get() + p.n\n}\nfn g(p: &P) -> int {\n    return p.get()\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_mutating_method_through_a_shared_borrow_reports_NIR3002()
    {
        var (_, _, diagnostics) = Bind(
            "type P = struct { n: int }\nP impl {\n    fn bump(&mut self) {\n    }\n}\nfn f(p: &P) {\n    p.bump()\n}\nfn main() {\n}");

        Assert.Equal(["NIR3002 7:5"], diagnostics);
    }

    // ---- where a borrow is not allowed ----

    [Fact]
    public void A_borrow_cannot_be_stored_in_a_variable()
    {
        var (_, bag, diagnostics) = BindMain(Mutable, "let r = &p");

        Assert.Equal(["NIR3014 12:13"], diagnostics);
        Assert.Equal("a borrow can only be passed directly as an argument to a function or method written in Nir", bag.Single().Message);
    }

    [Theory]
    [InlineData("print(&p)", "NIR3014 13:11")]
    [InlineData("print(Math::Sqrt(&p))", "NIR3014 13:22")]
    [InlineData("print(\"abc\".Contains(&p))", "NIR3014 13:26")]
    public void A_borrow_cannot_be_passed_to_print_or_a_dotnet_method(string statement, string expected)
    {
        var (_, _, diagnostics) = Bind("using clr::cs::System::Math\n" + Declarations + "fn main() {\n    " + Mutable + "\n    " + statement + "\n}");

        Assert.Equal([expected], diagnostics);
    }

    [Theory]
    [InlineData("show(&p.x)")]
    [InlineData("show(&(p))")]
    [InlineData("show(&5)")]
    public void Only_a_whole_variable_can_be_borrowed(string statement)
    {
        var (_, bag, diagnostics) = BindMain(Mutable, statement);

        Assert.Equal(["NIR3015 12:10"], diagnostics);
        Assert.Equal("only a whole variable can be borrowed, not a field or another expression", bag.Single().Message);
    }

    [Fact]
    public void Only_structs_can_be_borrowed()
    {
        var (_, bag, diagnostics) = BindMain("let n = 5", "show(&n)");

        Assert.Equal(["NIR3017 12:10"], diagnostics);
        Assert.Equal("cannot borrow a value of type 'int': only structs can be borrowed for now", bag.Single().Message);
    }

    [Fact]
    public void A_borrow_parameter_of_a_non_struct_type_is_rejected()
    {
        var (_, bag, diagnostics) = Bind("fn f(n: &mut int) {\n}\nfn main() {\n}");

        Assert.Equal(["NIR3017 1:9"], diagnostics);
        Assert.Equal("cannot borrow 'int': only structs can be borrowed for now, so '&mut int' is not supported yet", bag.Single().Message);
    }

    [Fact]
    public void A_borrow_parameter_of_an_unknown_type_reports_NIR2006()
    {
        var (_, _, diagnostics) = Bind("fn f(n: &Nope) {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:10"], diagnostics);
    }

    [Fact]
    public void An_unknown_variable_is_reported_once()
    {
        var (_, _, diagnostics) = BindMain("bump(&mut nope)");

        Assert.Equal(["NIR2001 11:15"], diagnostics);
    }

    [Fact]
    public void Wrong_argument_count_is_still_reported()
    {
        var (_, _, diagnostics) = BindMain(Mutable, "bump(&mut p, 1)");

        Assert.Equal(["NIR2004 12:5"], diagnostics);
    }

    [Fact]
    public void The_same_variable_may_be_borrowed_twice_in_one_call()
    {
        var (_, _, diagnostics) = Bind(
            Declarations + "fn swap(a: &mut Point, b: &mut Point) {\n}\nfn main() {\n    " + Mutable + "\n    swap(&mut p, &mut p)\n}");

        Assert.Empty(diagnostics);
    }
}
