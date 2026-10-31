using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class ImportBinderTests
{
    private const string Math = "using clr::cs::System::Math\n";
    private const string Io = "using clr::cs::System::IO\n";

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) Bind(string text)
    {
        var source = new SourceText("test.nir", text);
        var bag = new DiagnosticBag();
        var unit = SyntaxParser.Parse(source, bag);
        var program = Binder.Bind(unit, source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }

    // `imports` is a block of `using` lines (each ending in a newline); the statements follow on the lines after
    // `fn main() {`, indented by 4 spaces. With one import line, the first statement is on line 3, column 5.
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(string imports, params string[] statements) =>
        Bind(imports + "fn main() {\n" + string.Concat(statements.Select(s => "    " + s + "\n")) + "}");

    private static BoundCallExpression FirstCall(BoundProgram program)
    {
        var statement = (BoundExpressionStatement)program.Functions[0].Body.Statements[0];
        return (BoundCallExpression)statement.Expression;
    }

    // ---- imports ----

    [Fact]
    public void Namespace_and_type_imports_are_accepted()
    {
        var (_, _, diagnostics) = BindMain(Io + Math);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("using System::IO\n")]
    [InlineData("using clr\n")]
    [InlineData("using clr::cs\n")]
    [InlineData("using clr::fsharp::Foo\n")]
    public void Imports_must_start_with_clr_cs_and_name_something(string import)
    {
        var (_, _, diagnostics) = BindMain(import);

        Assert.Equal(["NIR2008 1:7"], diagnostics);
    }

    [Fact]
    public void Unknown_type_reports_NIR2009_at_the_last_segment()
    {
        var (_, bag, diagnostics) = BindMain("using clr::cs::System::Nope\n");

        Assert.Equal(["NIR2009 1:24"], diagnostics);
        Assert.Equal("'System.Nope' was not found", bag.Single().Message);
    }

    [Fact]
    public void Unknown_namespace_reports_NIR2009()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::Nope\n");

        Assert.Equal(["NIR2009 1:16"], diagnostics);
    }

    [Fact]
    public void Group_import_reports_each_missing_member_and_keeps_the_others()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::System::{Math, Nope}\n", "print(Math::Sqrt(4.0))");

        Assert.Equal(["NIR2009 1:31"], diagnostics);
    }

    [Fact]
    public void Group_directly_under_clr_cs_looks_for_global_types()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::{Math}\n");

        Assert.Equal(["NIR2009 1:17"], diagnostics);
    }

    [Fact]
    public void Importing_the_same_name_twice_reports_NIR2010_and_keeps_the_first()
    {
        var (_, _, diagnostics) = BindMain(Io + Io);

        Assert.Equal(["NIR2010 2:24"], diagnostics);
    }

    [Fact]
    public void Two_different_types_with_the_same_local_name_clash()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::System::Timers::Timer\nusing clr::cs::System::Threading::Timer\n");

        Assert.Equal(["NIR2010 2:35"], diagnostics);
    }

    // ---- static calls ----

    [Fact]
    public void Static_call_has_the_method_types()
    {
        var (program, _, diagnostics) = BindMain(Math, "print(Math::Sqrt(16.0))");

        Assert.Empty(diagnostics);
        var print = FirstCall(program!);
        var sqrt = Assert.IsType<BoundCallExpression>(print.Arguments[0]);
        var symbol = Assert.IsType<ClrMethodSymbol>(sqrt.Target);
        Assert.Equal("Sqrt", symbol.Method.Name);
        Assert.Equal(TypeSymbol.DoubleType, symbol.ReturnType);
        Assert.Equal([TypeSymbol.DoubleType], symbol.ParameterTypes.ToArray());
    }

    [Theory]
    [InlineData("Math::Max(1, 2)", "int")]
    [InlineData("Math::Max(1.5, 2.5)", "double")]
    [InlineData("Math::Abs(-3)", "int")]
    [InlineData("Math::Abs(-3.5)", "double")]
    public void Overload_is_chosen_by_exact_argument_types(string call, string expectedType)
    {
        var (program, _, diagnostics) = BindMain(Math, $"print({call})");

        Assert.Empty(diagnostics);
        var inner = Assert.IsType<BoundCallExpression>(FirstCall(program!).Arguments[0]);
        Assert.Equal(expectedType, inner.Type.Name);
        Assert.All(inner.Target.ParameterTypes, p => Assert.Equal(expectedType, p.Name));
    }

    [Fact]
    public void An_int_is_not_converted_to_a_double()
    {
        var (_, _, diagnostics) = BindMain(Math, "print(Math::Sqrt(16))");

        Assert.Equal(["NIR3006 3:11"], diagnostics);
    }

    [Fact]
    public void Mixed_argument_types_find_no_overload()
    {
        var (_, _, diagnostics) = BindMain(Math, "print(Math::Max(1, 2.5))");

        Assert.Equal(["NIR3006 3:11"], diagnostics);
    }

    [Fact]
    public void Wrong_argument_type_reports_NIR3006_at_the_call_start()
    {
        var (_, bag, diagnostics) = BindMain(Math, "print(Math::Sqrt(\"x\"))");

        Assert.Equal(["NIR3006 3:11"], diagnostics);
        Assert.Equal("no overload of 'Math.Sqrt' accepts (string)", bag.Single().Message);
    }

    [Fact]
    public void Wrong_argument_count_reports_NIR2004()
    {
        var (_, bag, diagnostics) = BindMain(Math, "Math::Sqrt(1.0, 2.0)");

        Assert.Equal(["NIR2004 3:5"], diagnostics);
        Assert.Equal("'Math.Sqrt' expects 1 argument(s) but got 2", bag.Single().Message);
    }

    [Fact]
    public void Unimported_name_reports_NIR2001()
    {
        var (_, _, diagnostics) = Bind("fn main() {\n    Math::Sqrt(4.0)\n}");

        Assert.Equal(["NIR2001 2:5"], diagnostics);
    }

    [Fact]
    public void Unknown_method_reports_NIR2011_at_the_method()
    {
        var (_, bag, diagnostics) = BindMain(Math, "Math::Nope(1)");

        Assert.Equal(["NIR2011 3:11"], diagnostics);
        Assert.Equal("'Nope' is not a member of 'System.Math'", bag.Single().Message);
    }

    [Fact]
    public void Walking_through_namespaces_to_a_type_and_method()
    {
        var (program, _, diagnostics) = BindMain(Io, "print(IO::Path::GetFileName(\"a/b.txt\"))");

        Assert.Empty(diagnostics);
        Assert.Equal("GetFileName", ((ClrMethodSymbol)((BoundCallExpression)FirstCall(program!).Arguments[0]).Target).Method.Name);
    }

    [Fact]
    public void Importing_System_itself_allows_System_Math()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::System\n", "print(System::Math::Sqrt(4.0))");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Unknown_type_inside_an_imported_namespace()
    {
        var (_, bag, diagnostics) = BindMain(Io, "IO::Nope::F(\"x\")");

        Assert.Equal(["NIR2011 3:9"], diagnostics);
        Assert.Equal("'Nope' is not a member of 'System.IO'", bag.Single().Message);
    }

    [Fact]
    public void Unknown_method_on_a_type_reached_through_a_namespace()
    {
        var (_, _, diagnostics) = BindMain(Io, "IO::File::Nope(\"x\")");

        Assert.Equal(["NIR2011 3:15"], diagnostics);
    }

    [Fact]
    public void A_namespace_cannot_be_called()
    {
        var (_, _, diagnostics) = BindMain(Io, "IO::File(\"x\")");

        Assert.Equal(["NIR2011 3:9"], diagnostics);
    }

    [Fact]
    public void A_method_cannot_be_descended_into()
    {
        var (_, _, diagnostics) = BindMain(Math, "Math::Sqrt::X(1)");

        Assert.Equal(["NIR2011 3:11"], diagnostics);
    }

    [Fact]
    public void An_instance_method_is_not_a_static_member()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::System::String\n", "String::ToUpper(\"x\")");

        Assert.Equal(["NIR2011 3:13"], diagnostics);
    }

    [Fact]
    public void Unsupported_return_type_reports_NIR3011_at_the_call_start()
    {
        var (_, bag, diagnostics) = BindMain(Io, "IO::File::ReadAllBytes(\"x\")");

        Assert.Equal(["NIR3011 3:5"], diagnostics);
        Assert.Equal("'File.ReadAllBytes' returns 'System.Byte[]', which Nir cannot use yet", bag.Single().Message);
    }

    [Fact]
    public void Overloads_with_unsupported_parameters_are_skipped()
    {
        // Path.Combine has overloads taking `params string[]` and string arrays besides the two-string one.
        var (program, _, diagnostics) = BindMain(Io, "print(IO::Path::Combine(\"a\", \"b\"))");

        Assert.Empty(diagnostics);
        var combine = (BoundCallExpression)FirstCall(program!).Arguments[0];
        Assert.Equal(TypeSymbol.StringType, combine.Type);
        Assert.Equal(2, combine.Target.ParameterTypes.Length);
    }

    [Fact]
    public void Error_argument_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain(Math, "print(Math::Sqrt(nope()))");

        Assert.Equal(["NIR2001 3:22"], diagnostics);
    }

    [Fact]
    public void Void_method_cannot_initialize_a_variable()
    {
        var (_, _, diagnostics) = BindMain(Io, "let x = IO::File::WriteAllText(\"a\", \"b\")");

        Assert.Equal(["NIR3005 3:9"], diagnostics);
    }

    [Fact]
    public void Group_import_gives_every_member_a_local_name()
    {
        var (_, _, diagnostics) = BindMain("using clr::cs::System::{Math, Console}\n", "Console::WriteLine(\"hi\")", "print(Math::Sqrt(4.0))");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Imports_below_the_functions_still_apply()
    {
        var (_, _, diagnostics) = Bind("fn main() {\n    print(Math::Sqrt(4.0))\n}\nusing clr::cs::System::Math\n");

        Assert.Empty(diagnostics);
    }
}
