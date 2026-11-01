namespace Nir.Tests.Programs;

public class Slice8ProgramTests
{
    public static TheoryData<string> Programs => ProgramCases.Names("slice8");

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_behaves_as_recorded(string name) => ProgramCases.Check("slice8", name);
}
