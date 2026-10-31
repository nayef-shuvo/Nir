namespace Nir.Tests.Programs;

public class Slice1ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice1");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice1", name);
}
