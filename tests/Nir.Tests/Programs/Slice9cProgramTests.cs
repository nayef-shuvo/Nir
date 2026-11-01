namespace Nir.Tests.Programs;

public class Slice9cProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice9c");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice9c", name);
}
