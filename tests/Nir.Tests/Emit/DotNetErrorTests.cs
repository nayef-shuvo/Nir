using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class DotNetErrorTests
{
    private const string Imports = "using clr::cs::System::{Int32, IO}";

    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void A_call_that_works_gives_its_value_under_catch()
    {
        Assert.Equal("12\n", Run(
            Imports,
            "fn main() {",
            "    let n = Int32::Parse(\"12\") catch -1",
            "    print(n)",
            "}"));
    }

    [Fact]
    public void A_call_that_throws_gives_the_fallback()
    {
        Assert.Equal("-1\n", Run(
            Imports,
            "fn main() {",
            "    let n = Int32::Parse(\"zz\") catch -1",
            "    print(n)",
            "}"));
    }

    [Fact]
    public void The_handler_gets_the_exceptions_message()
    {
        Assert.Equal("True\n", Run(
            Imports,
            "fn main() {",
            "    Int32::Parse(\"zz\") catch e => {",
            "        print(e.message.Contains(\"zz\"))",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void Try_passes_a_dotnet_failure_up_as_an_anyerror()
    {
        Assert.Equal("42\nTrue\n", Run(
            Imports,
            "fn parse(s: string) -> anyerror!int {",
            "    let n = try Int32::Parse(s)",
            "    return n * 2",
            "}",
            "fn main() {",
            "    print(parse(\"21\") catch 0)",
            "    parse(\"abc\") catch e => {",
            "        print(e.message.Contains(\"abc\"))",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void A_missing_file_is_an_error_you_can_handle()
    {
        Assert.Equal("no such file\n", Run(
            Imports,
            "fn read(path: string) -> anyerror!string {",
            "    return try IO::File::ReadAllText(path)",
            "}",
            "fn main() {",
            "    let text = read(\"/definitely/not/here.txt\") catch e => {",
            "        print(\"no such file\")",
            "        return",
            "    }",
            "    print(text)",
            "}"));
    }

    [Fact]
    public void A_method_with_no_value_can_fail_too()
    {
        Assert.Equal("failed\n", Run(
            Imports,
            "fn main() {",
            "    IO::Directory::Delete(\"/definitely/not/here\") catch e => print(\"failed\")",
            "}"));
    }

    [Fact]
    public void A_method_with_no_value_that_works_succeeds()
    {
        Assert.Equal("ok\n", Run(
            "using clr::cs::System::Console",
            "fn run() -> anyerror! {",
            "    try Console::Write(\"\")",
            "}",
            "fn main() {",
            "    run() catch e => print(e.message)",
            "    print(\"ok\")",
            "}"));
    }

    [Fact]
    public void An_instance_method_on_a_value_can_be_protected()
    {
        Assert.Equal("HELLO\n", Run(
            "fn main() {",
            "    let s = \"hello\".ToUpper() catch \"\"",
            "    print(s)",
            "}"));
    }

    [Fact]
    public void Only_the_call_is_protected_not_the_working_out_of_its_arguments()
    {
        var (_, stderr, exitCode) = TestHost.Run(string.Join("\n",
            Imports,
            "fn main() {",
            "    let n = Int32::Parse(\"1\".Substring(5)) catch -1",
            "    print(n)",
            "}") + "\n");

        Assert.Contains("ArgumentOutOfRangeException", stderr);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void A_protected_call_can_sit_inside_a_larger_expression()
    {
        Assert.Equal("6\n", Run(
            Imports,
            "fn main() {",
            "    print(1 + (Int32::Parse(\"x\") catch 2) + (Int32::Parse(\"3\") catch e => 0))",
            "}"));
    }

    [Fact]
    public void Without_try_or_catch_a_failing_call_still_crashes()
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n",
            Imports,
            "fn main() {",
            "    print(\"before\")",
            "    print(Int32::Parse(\"zz\"))",
            "}") + "\n");

        Assert.Equal("before\n", stdout.ReplaceLineEndings("\n"));
        Assert.Contains("FormatException", stderr);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void An_error_that_escapes_main_from_a_dotnet_call_is_reported()
    {
        var (_, stderr, exitCode) = TestHost.Run(string.Join("\n",
            Imports,
            "fn main() -> anyerror! {",
            "    let n = try Int32::Parse(\"zz\")",
            "    print(n)",
            "}") + "\n");

        Assert.StartsWith("error: ", stderr);
        Assert.Contains("zz", stderr);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Protected_calls_work_in_loops_and_repeatedly()
    {
        Assert.Equal("1\n-1\n3\n0\n", Run(
            Imports,
            "fn main() {",
            "    print(Int32::Parse(\"1\") catch 0)",
            "    print(Int32::Parse(\"x\") catch -1)",
            "    print(Int32::Parse(\"3\") catch -1)",
            "    let mut total = 0",
            "    for i in 0..3 {",
            "        let n = Int32::Parse(\"q\") catch 0",
            "        total = total + n",
            "    }",
            "    print(total)",
            "}"));
    }
}
