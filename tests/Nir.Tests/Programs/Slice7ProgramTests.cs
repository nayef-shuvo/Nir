namespace Nir.Tests.Programs;

public class Slice7ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice7");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice7", name);
}
