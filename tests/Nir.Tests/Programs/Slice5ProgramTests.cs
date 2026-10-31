namespace Nir.Tests.Programs;

public class Slice5ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice5");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice5", name);
}
