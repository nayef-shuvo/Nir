namespace Nir.Tests.Programs;

public class Slice6ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice6");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice6", name);
}
