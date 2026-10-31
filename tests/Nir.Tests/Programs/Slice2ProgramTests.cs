namespace Nir.Tests.Programs;

public class Slice2ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice2");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice2", name);
}
