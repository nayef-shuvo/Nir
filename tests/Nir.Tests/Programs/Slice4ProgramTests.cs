namespace Nir.Tests.Programs;

public class Slice4ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice4");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice4", name);
}
