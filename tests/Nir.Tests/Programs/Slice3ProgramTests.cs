namespace Nir.Tests.Programs;

public class Slice3ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice3");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice3", name);
}
