using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class EmitterTests
{
    [Fact]
    public void Hello_prints_text_and_newline()
    {
        var (stdout, stderr, exitCode) = TestHost.Run("fn main() {\n    print(\"hello\")\n}");

        Assert.Equal("hello\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Statements_run_in_order()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(\"a\")\n    print(\"b\")\n    print(\"c\")\n}");

        Assert.Equal("a\nb\nc\n", stdout);
    }

    [Fact]
    public void Function_call_runs_callee()
    {
        var (stdout, _, _) = TestHost.Run("fn greet() {\n    print(\"hi\")\n}\nfn main() {\n    greet()\n    greet()\n}");

        Assert.Equal("hi\nhi\n", stdout);
    }

    [Fact]
    public void Forward_call_works()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    later()\n}\nfn later() {\n    print(\"late\")\n}");

        Assert.Equal("late\n", stdout);
    }

    [Fact]
    public void Empty_main_prints_nothing()
    {
        var (stdout, _, exitCode) = TestHost.Run("fn main() {\n}");

        Assert.Equal(string.Empty, stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Two_semicolons_on_one_line()
    {
        var (stdout, _, _) = TestHost.Run("fn main() { print(\"a\"); print(\"b\") }");

        Assert.Equal("a\nb\n", stdout);
    }

    [Fact]
    public void Backslashes_in_strings_are_printed_as_written()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(\"a\\nb\")\n}");

        Assert.Equal("a\\nb\n", stdout);
    }
}
