using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class ControlFlowTests
{
    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void Every_branch_of_an_if_chain_can_be_taken()
    {
        Assert.Equal("negative\nzero\npositive\n", Run(
            "fn classify(n: int) {",
            "    if n < 0 {",
            "        print(\"negative\")",
            "    } else if n == 0 {",
            "        print(\"zero\")",
            "    } else {",
            "        print(\"positive\")",
            "    }",
            "}",
            "fn main() {",
            "    classify(-5)",
            "    classify(0)",
            "    classify(7)",
            "}"));
    }

    [Fact]
    public void An_if_without_else_skips_its_block()
    {
        Assert.Equal("after\n", Run(
            "fn main() {",
            "    if 1 > 2 {",
            "        print(\"never\")",
            "    }",
            "    print(\"after\")",
            "}"));
    }

    [Fact]
    public void Else_may_start_on_the_next_line()
    {
        Assert.Equal("else\n", Run(
            "fn main() {",
            "    if false {",
            "        print(\"then\")",
            "    }",
            "    else {",
            "        print(\"else\")",
            "    }",
            "}"));
    }

    [Fact]
    public void While_loops_until_the_condition_is_false()
    {
        Assert.Equal("55\n", Run(
            "fn main() {",
            "    let mut i = 1",
            "    let mut total = 0",
            "    while i <= 10 {",
            "        total = total + i",
            "        i = i + 1",
            "    }",
            "    print(total)",
            "}"));
    }

    [Fact]
    public void A_while_whose_condition_starts_false_never_runs()
    {
        Assert.Equal("done\n", Run(
            "fn main() {",
            "    while false {",
            "        print(\"body\")",
            "    }",
            "    print(\"done\")",
            "}"));
    }

    [Fact]
    public void For_ranges_exclusive_inclusive_empty_and_single()
    {
        Assert.Equal("0\n1\n2\n--\n1\n2\n3\n--\n--\n--\n4\n", Run(
            "fn main() {",
            "    for i in 0..3 {",
            "        print(i)",
            "    }",
            "    print(\"--\")",
            "    for i in 1..=3 {",
            "        print(i)",
            "    }",
            "    print(\"--\")",
            "    for i in 3..3 {",
            "        print(i)",
            "    }",
            "    print(\"--\")",
            "    for i in 5..2 {",
            "        print(i)",
            "    }",
            "    print(\"--\")",
            "    for i in 4..=4 {",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void For_adds_up_a_range()
    {
        Assert.Equal("5050\n", Run(
            "fn main() {",
            "    let mut sum = 0",
            "    for i in 1..=100 {",
            "        sum = sum + i",
            "    }",
            "    print(sum)",
            "}"));
    }

    [Fact]
    public void Break_and_continue_affect_only_the_innermost_loop()
    {
        Assert.Equal("0\n10\n20\n", Run(
            "fn main() {",
            "    for i in 0..3 {",
            "        for j in 0..3 {",
            "            if j == 1 {",
            "                continue",
            "            }",
            "            if j == 2 {",
            "                break",
            "            }",
            "            print(i * 10 + j)",
            "        }",
            "    }",
            "}"));
    }

    [Fact]
    public void Break_leaves_a_while_true()
    {
        Assert.Equal("3\n", Run(
            "fn main() {",
            "    let mut n = 0",
            "    while true {",
            "        n = n + 1",
            "        if n == 3 {",
            "            break",
            "        }",
            "    }",
            "    print(n)",
            "}"));
    }

    [Fact]
    public void Continue_in_a_while_goes_back_to_the_condition()
    {
        Assert.Equal("1\n3\n5\n", Run(
            "fn main() {",
            "    let mut i = 0",
            "    while i < 6 {",
            "        i = i + 1",
            "        if i % 2 == 0 {",
            "            continue",
            "        }",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void The_for_bounds_are_evaluated_once()
    {
        Assert.Equal("bound\n0\n1\n2\n0\n1\n2\n", Run(
            "fn bound() -> int {",
            "    print(\"bound\")",
            "    return 3",
            "}",
            "fn main() {",
            "    for i in 0..bound() {",
            "        print(i)",
            "    }",
            "    let mut n = 3",
            "    for i in 0..n {",
            "        n = 10",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void A_range_ending_at_the_largest_int_does_not_overflow()
    {
        Assert.Equal("2147483646\n", Run(
            "fn main() {",
            "    for i in 2147483646..2147483647 {",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void Variables_inside_a_loop_body_start_fresh_each_pass()
    {
        Assert.Equal("0\n2\n4\n", Run(
            "fn main() {",
            "    let mut count = 0",
            "    while count < 3 {",
            "        let doubled = count * 2",
            "        print(doubled)",
            "        count = count + 1",
            "    }",
            "}"));
    }

    [Fact]
    public void Sibling_loops_can_use_the_same_names()
    {
        Assert.Equal("0\n1\n0\n1\n", Run(
            "fn main() {",
            "    for i in 0..2 {",
            "        print(i)",
            "    }",
            "    for i in 0..2 {",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void And_and_or_do_not_evaluate_the_right_side_when_the_left_decides()
    {
        Assert.Equal(
            "no called\nFalse\nyes called\nTrue\nyes called\nno called\nFalse\nno called\nyes called\nTrue\n",
            Run(
                "fn yes() -> bool {",
                "    print(\"yes called\")",
                "    return true",
                "}",
                "fn no() -> bool {",
                "    print(\"no called\")",
                "    return false",
                "}",
                "fn main() {",
                "    print(no() && yes())",
                "    print(yes() || no())",
                "    print(yes() && no())",
                "    print(no() || yes())",
                "}"));
    }

    [Fact]
    public void Truth_tables()
    {
        Assert.Equal(
            "False\nFalse\nFalse\nTrue\n" + "False\nTrue\nTrue\nTrue\n" + "False\nTrue\n",
            Run(
                "fn main() {",
                "    print(false && false)",
                "    print(false && true)",
                "    print(true && false)",
                "    print(true && true)",
                "    print(false || false)",
                "    print(false || true)",
                "    print(true || false)",
                "    print(true || true)",
                "    print(!true)",
                "    print(!false)",
                "}"));
    }

    [Fact]
    public void Precedence_of_not_and_or_with_comparisons()
    {
        Assert.Equal("True\nFalse\nTrue\n", Run(
            "fn main() {",
            "    print(1 < 2 && 2 < 3 || false)",
            "    print(!(1 < 2) || 3 == 4)",
            "    print(true || false && false)",
            "}"));
    }

    [Fact]
    public void Conditions_can_be_variables_and_calls()
    {
        Assert.Equal("yes\nno\n", Run(
            "fn positive(n: int) -> bool {",
            "    return n > 0",
            "}",
            "fn main() {",
            "    let flag = positive(3)",
            "    if flag {",
            "        print(\"yes\")",
            "    }",
            "    if !positive(-3) {",
            "        print(\"no\")",
            "    }",
            "}"));
    }

    [Fact]
    public void Recursion_factorial_fibonacci_and_mutual_recursion()
    {
        Assert.Equal("3628800\n610\nTrue\nTrue\nFalse\n", Run(
            "fn factorial(n: int) -> int {",
            "    if n <= 1 {",
            "        return 1",
            "    }",
            "    return n * factorial(n - 1)",
            "}",
            "fn fibonacci(n: int) -> int {",
            "    if n < 2 {",
            "        return n",
            "    }",
            "    return fibonacci(n - 1) + fibonacci(n - 2)",
            "}",
            "fn is_even(n: int) -> bool {",
            "    if n == 0 {",
            "        return true",
            "    }",
            "    return is_odd(n - 1)",
            "}",
            "fn is_odd(n: int) -> bool {",
            "    if n == 0 {",
            "        return false",
            "    }",
            "    return is_even(n - 1)",
            "}",
            "fn main() {",
            "    print(factorial(10))",
            "    print(fibonacci(15))",
            "    print(is_even(10))",
            "    print(is_odd(7))",
            "    print(is_even(7))",
            "}"));
    }

    [Fact]
    public void Collatz_and_gcd()
    {
        Assert.Equal("111\n6\n", Run(
            "fn collatz(start: int) -> int {",
            "    let mut n = start",
            "    let mut steps = 0",
            "    while n != 1 {",
            "        if n % 2 == 0 { n = n / 2 } else { n = 3 * n + 1 }",
            "        steps = steps + 1",
            "    }",
            "    return steps",
            "}",
            "fn gcd(x: int, y: int) -> int {",
            "    let mut a = x",
            "    let mut b = y",
            "    while b != 0 {",
            "        let t = b",
            "        b = a % b",
            "        a = t",
            "    }",
            "    return a",
            "}",
            "fn main() {",
            "    print(collatz(27))",
            "    print(gcd(48, 18))",
            "}"));
    }

    [Fact]
    public void A_function_that_ends_in_a_returning_if_else_is_valid_IL()
    {
        Assert.Equal("-1\n0\n1\n", Run(
            "fn sign(n: int) -> int {",
            "    if n < 0 {",
            "        return -1",
            "    } else if n == 0 {",
            "        return 0",
            "    } else {",
            "        return 1",
            "    }",
            "}",
            "fn main() {",
            "    print(sign(-5))",
            "    print(sign(0))",
            "    print(sign(9))",
            "}"));
    }

    [Fact]
    public void Return_from_inside_a_loop()
    {
        Assert.Equal("7\n13\n", Run(
            "fn first_divisor(n: int) -> int {",
            "    for d in 2..n {",
            "        if n % d == 0 {",
            "            return d",
            "        }",
            "    }",
            "    return n",
            "}",
            "fn main() {",
            "    print(first_divisor(91))",
            "    print(first_divisor(13))",
            "}"));
    }

    [Fact]
    public void Early_return_from_a_unit_function_inside_a_loop()
    {
        Assert.Equal("0\n1\nstopped\n", Run(
            "fn count_to(limit: int) {",
            "    for i in 0..100 {",
            "        if i == limit {",
            "            print(\"stopped\")",
            "            return",
            "        }",
            "        print(i)",
            "    }",
            "}",
            "fn main() {",
            "    count_to(2)",
            "}"));
    }

    [Fact]
    public void Checked_arithmetic_still_applies_inside_loops()
    {
        var (stdout, stderr, exitCode) = TestHost.Run(
            "fn main() {\n    let mut n = 2147483640\n    while true {\n        n = n + 3\n        print(n)\n    }\n}\n");

        Assert.Equal("2147483643\n2147483646\n", stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("OverflowException", stderr, StringComparison.Ordinal);
    }
}
