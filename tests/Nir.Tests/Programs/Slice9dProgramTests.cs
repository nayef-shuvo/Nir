namespace Nir.Tests.Programs;

public class Slice9dProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice9d");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice9d", name);
}
