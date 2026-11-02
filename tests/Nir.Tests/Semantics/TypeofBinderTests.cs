using Nir.Compiler.Semantics;
using Nir.Tests.Support;

namespace Nir.Tests.Semantics;

/// <summary>Slice 9d: <c>typeof</c> and the built-in struct <c>TypeInfo</c> (design.md "Knowing a type").</summary>
public class TypeofBinderTests
{
    // Binds the lines as a program: `setup` before main, then the statements as the body of main.
    private static (BoundProgram? Program, IReadOnlyList<string> Diagnostics) Bind(string setup, params string[] lines)
    {
        var source = (setup.Length == 0 ? string.Empty : setup + "\n") + "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}";
        var (program, _, diagnostics) = BinderHarness.Bind(source);
        return (program, diagnostics);
    }

    // The name that `typeof(...)` was worked out to: the last of `lines` is the typeof, the ones before it are ordinary statements.
    private static string NameOf(string setup, params string[] lines) =>
        TypeName(BindOk(setup, [.. lines[..^1], "let t = " + lines[^1]]));

    private static string TypeName(BoundProgram program)
    {
        var statements = program.Functions.Single(f => f.Symbol.Name == "main").Body.Statements;
        var literal = Assert.IsType<BoundStructLiteral>(((BoundVariableDeclaration)statements[^1]).Initializer);
        Assert.Equal("TypeInfo", literal.Struct.Name);
        return Assert.IsType<BoundStringLiteral>(Assert.Single(literal.Initializers).Value).Value;
    }

    private static BoundProgram BindOk(string setup, params string[] lines)
    {
        var (program, diagnostics) = Bind(setup, lines);
        Assert.Empty(diagnostics);
        return program!;
    }

    [Theory]
    [InlineData("let x = 1", "typeof(x)", "int")]
    [InlineData("let x = 1.5", "typeof(x)", "double")]
    [InlineData("let x = 5_long", "typeof(x)", "long")]
    [InlineData("let x = 200_byte", "typeof(x)", "byte")]
    [InlineData("let x = 'c'", "typeof(x)", "char")]
    [InlineData("let x = true", "typeof(x)", "bool")]
    [InlineData("let x = \"s\"", "typeof(x)", "string")]
    [InlineData("let x = 2.5_decimal", "typeof(x)", "decimal")]
    [InlineData("let x = (1, \"a\")", "typeof(x)", "(int, string)")]
    [InlineData("let x = 1", "typeof(x + 1)", "int")]
    [InlineData("let x = 1", "typeof(x == 1)", "bool")]
    [InlineData("let x = 1", "typeof(x as long)", "long")]
    [InlineData("let x = 1", "typeof($\"{x}\")", "string")]
    public void It_names_the_type_of_a_variable_or_expression(string declaration, string typeofText, string expected)
    {
        var program = BindOk(string.Empty, declaration, "let t = " + typeofText);

        Assert.Equal(expected, TypeName(program));
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("anyerror", "anyerror")]
    [InlineData("(int, string)", "(int, string)")]
    [InlineData("(x: int, y: int)", "(x: int, y: int)")]
    [InlineData("Point", "Point")]
    [InlineData("Shape", "Shape")]
    [InlineData("Failure!int", "Failure!int")]
    public void It_names_a_type_written_out(string written, string expected)
    {
        const string Setup = "type Point = struct { x: int }\ntype Failure = error { message: string }\ntype Shape = union { int, string }";

        Assert.Equal(expected, NameOf(Setup, $"typeof({written})"));
    }

    [Fact]
    public void A_union_variable_gives_the_declared_union_and_a_result_gives_its_result_type()
    {
        const string Setup = "type Shape = union { int, string }\ntype Failure = error { message: string }";

        Assert.Equal("Shape", NameOf(Setup, "let s: Shape = 5", "typeof(s)"));
        Assert.Equal("Failure!int", NameOf(Setup, "let r: Failure!int = 5", "typeof(r)"));
        Assert.Equal("anyerror", NameOf(Setup, "let a: anyerror = error \"x\"", "typeof(a)"));
    }

    [Fact]
    public void A_field_and_a_tuple_element_have_the_type_of_the_field()
    {
        const string Setup = "type Point = struct { x: long, y: int }";

        Assert.Equal("long", NameOf(Setup, "let p = Point { x = 1, y = 2 }", "typeof(p.x)"));
        Assert.Equal("string", NameOf(string.Empty, "let pair = (1, \"a\")", "typeof(pair.1)"));
    }

    [Fact]
    public void A_tuple_of_variables_gives_the_tuple_of_their_types()
    {
        Assert.Equal("(int, string)", NameOf(string.Empty, "let a = 1", "let b = \"s\"", "typeof((a, b))"));
        Assert.Equal("(int, (long, char))", NameOf(string.Empty, "let a = 1", "let b = 2_long", "let c = 'c'", "typeof((a, (b, c)))"));
    }

    [Fact]
    public void A_borrow_keeps_its_borrow_in_the_name()
    {
        const string Setup = "type Point = struct { x: int }\nfn show(p: &Point) { let t = typeof(p) }";
        var (program, diagnostics) = Bind(Setup, string.Empty);
        Assert.Empty(diagnostics);

        var body = program!.Functions.Single(f => f.Symbol.Name == "show").Body.Statements;
        var literal = (BoundStructLiteral)((BoundVariableDeclaration)body[0]).Initializer;
        Assert.Equal("&Point", ((BoundStringLiteral)literal.Initializers[0].Value).Value);
    }

    [Fact]
    public void The_operand_is_not_part_of_the_bound_tree_so_it_cannot_run()
    {
        var program = BindOk("fn side() -> int { return 1 }", "let t = typeof(side())");

        var statements = program.Functions.Single(f => f.Symbol.Name == "main").Body.Statements;
        var literal = (BoundStructLiteral)((BoundVariableDeclaration)statements[0]).Initializer;
        Assert.IsType<BoundStringLiteral>(Assert.Single(literal.Initializers).Value);
        Assert.Equal("int", ((BoundStringLiteral)literal.Initializers[0].Value).Value);
    }

    [Fact]
    public void TypeInfo_is_in_the_program_only_when_it_is_used()
    {
        var without = BindOk(string.Empty, "let x = 1");
        var with = BindOk(string.Empty, "let t = typeof(1)");

        Assert.DoesNotContain(without.Structs, s => s.Name == "TypeInfo");
        Assert.Single(with.Structs, s => s.Name == "TypeInfo");
    }

    [Fact]
    public void The_result_has_one_field_called_name_of_type_string()
    {
        var program = BindOk(string.Empty, "let t = typeof(1)");

        var info = Assert.Single(program.Structs);
        var field = Assert.Single(info.Fields);
        Assert.Equal("name", field.Name);
        Assert.Equal(TypeSymbol.StringType, field.Type);
        Assert.False(info.IsError);
    }

    [Theory]
    [InlineData("using std::types::TypeInfo")]
    [InlineData("using std::types::{TypeInfo}")]
    public void TypeInfo_can_be_named_as_a_type_and_built_by_hand_once_imported(string import)
    {
        var (_, diagnostics) = Bind(import + "\nfn show(t: TypeInfo) { print(t.name) }", "show(typeof(1))", "show(TypeInfo { name = \"mine\" })");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void typeof_works_without_the_import()
    {
        var (program, diagnostics) = Bind(string.Empty, "print(typeof(1).name)");

        Assert.Empty(diagnostics);
        Assert.Single(program!.Structs, s => s.Name == "TypeInfo");
    }

    [Fact]
    public void Without_the_import_the_name_is_unknown_and_the_message_says_which_import_to_add()
    {
        var (_, bag, diagnostics) = BinderHarness.Bind("fn show(t: TypeInfo) { }\nfn main() { }");

        Assert.Equal(["NIR2006 1:12"], diagnostics);
        Assert.Contains("using std::types::TypeInfo", bag.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let t = TypeInfo { name = \"x\" }")]
    [InlineData("let t: TypeInfo = typeof(1)")]
    public void Without_the_import_the_name_cannot_be_used_in_a_statement_either(string statement)
    {
        var (_, diagnostics) = Bind(string.Empty, statement);

        Assert.Contains(diagnostics, d => d.StartsWith("NIR2006", StringComparison.Ordinal));
    }

    [Fact]
    public void A_program_may_declare_its_own_TypeInfo_when_it_does_not_import_the_built_in_one()
    {
        var (program, diagnostics) = Bind("type TypeInfo = struct { a: int }", "let mine = TypeInfo { a = 1 }", "let theirs = typeof(1)");

        Assert.Empty(diagnostics);
        Assert.Equal(2, program!.Structs.Count(s => s.Name == "TypeInfo"));
        var statements = program.Functions.Single(f => f.Symbol.Name == "main").Body.Statements;
        Assert.NotEqual(((BoundVariableDeclaration)statements[0]).Variable.Type, ((BoundVariableDeclaration)statements[1]).Variable.Type);
    }

    [Fact]
    public void The_type_of_a_statement_that_is_a_typeof_is_the_struct()
    {
        var program = BindOk(string.Empty, "let t = typeof(1)");

        var statements = program.Functions.Single(f => f.Symbol.Name == "main").Body.Statements;
        Assert.Equal("TypeInfo", ((BoundVariableDeclaration)statements[0]).Variable.Type.Name);
    }

    // ---- mistakes ----

    [Fact]
    public void An_unknown_name_is_reported_as_not_defined()
    {
        var (_, diagnostics) = Bind(string.Empty, "let t = typeof(nope)");

        Assert.Equal(["NIR2001 2:20"], diagnostics);
    }

    [Fact]
    public void An_unknown_type_inside_a_type_is_reported_as_unknown_type()
    {
        var (_, diagnostics) = Bind(string.Empty, "let t = typeof(Nope!int)");

        Assert.Equal(["NIR2006 2:20"], diagnostics);
    }

    [Fact]
    public void TypeInfo_has_only_a_name()
    {
        var (_, diagnostics) = Bind(string.Empty, "let t = typeof(1)", "let s = t.size");

        Assert.Equal(["NIR2011 3:15"], diagnostics);
    }

    [Fact]
    public void A_program_cannot_declare_a_TypeInfo_when_it_imports_the_built_in_one()
    {
        var (_, diagnostics) = Bind("using std::types::TypeInfo\ntype TypeInfo = struct { a: int }");

        Assert.Equal(["NIR2013 2:6"], diagnostics);
    }

    [Fact]
    public void A_program_cannot_add_methods_to_the_built_in_TypeInfo()
    {
        var (_, diagnostics) = Bind("using std::types::TypeInfo\nTypeInfo impl { fn f(&self) { } }");

        Assert.Equal(["NIR2014 2:1"], diagnostics);
    }

    // ---- imports from std ----

    [Theory]
    [InlineData("using std", "NIR2008 1:7")]
    [InlineData("using std::types", "NIR2008 1:12")]
    [InlineData("using std::types::Nope", "NIR2009 1:19")]
    [InlineData("using std::other::TypeInfo", "NIR2009 1:19")]
    [InlineData("using std::types::TypeInfo::More", "NIR2009 1:29")]
    [InlineData("using std::types::{TypeInfo, Nope}", "NIR2009 1:30")]
    public void Only_std_types_TypeInfo_can_be_imported_from_std(string import, string expected)
    {
        var (_, diagnostics) = Bind(import);

        Assert.Equal(expected, diagnostics.First(d => d.StartsWith("NIR200", StringComparison.Ordinal)));
    }

    [Fact]
    public void Importing_it_twice_is_reported()
    {
        var (_, diagnostics) = Bind("using std::types::TypeInfo\nusing std::types::TypeInfo");

        Assert.Equal(["NIR2010 2:19"], diagnostics.Where(d => !d.StartsWith("NIR2002", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public void The_import_message_for_a_bad_start_names_both_roots()
    {
        var (_, bag, _) = BinderHarness.Bind("using other::x\nfn main() { }");

        Assert.Contains("clr::cs", bag.Single().Message, StringComparison.Ordinal);
        Assert.Contains("std", bag.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mistake_inside_the_operand_is_still_reported_once()
    {
        var (_, diagnostics) = Bind(string.Empty, "let t = typeof(1 + \"a\")");

        Assert.Equal(["NIR3003 2:20"], diagnostics);
    }
}
