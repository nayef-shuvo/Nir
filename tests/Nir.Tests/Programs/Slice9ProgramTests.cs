namespace Nir.Tests.Programs;

public class Slice9ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice9");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice9", name);
}
