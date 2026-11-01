using System.Reflection;

namespace Nir.Compiler.Clr;

/// <summary>
/// The .NET types and members the compiler uses all the time, looked up once. Like everything from <see cref="ClrUniverse"/>,
/// they must be used instead of <c>typeof(...)</c>.
/// </summary>
internal static class ClrTypes
{
    public static readonly Type Void = ClrUniverse.CoreType(ClrNames.Void);

    public static readonly Type Byte = ClrUniverse.CoreType(ClrNames.Byte);

    public static readonly Type SByte = ClrUniverse.CoreType(ClrNames.SByte);

    public static readonly Type Int16 = ClrUniverse.CoreType(ClrNames.Int16);

    public static readonly Type UInt16 = ClrUniverse.CoreType(ClrNames.UInt16);

    public static readonly Type Int32 = ClrUniverse.CoreType(ClrNames.Int32);

    public static readonly Type UInt32 = ClrUniverse.CoreType(ClrNames.UInt32);

    public static readonly Type Int64 = ClrUniverse.CoreType(ClrNames.Int64);

    public static readonly Type UInt64 = ClrUniverse.CoreType(ClrNames.UInt64);

    public static readonly Type Single = ClrUniverse.CoreType(ClrNames.Single);

    public static readonly Type Double = ClrUniverse.CoreType(ClrNames.Double);

    public static readonly Type Decimal = ClrUniverse.CoreType(ClrNames.Decimal);

    public static readonly Type Char = ClrUniverse.CoreType(ClrNames.Char);

    public static readonly Type Boolean = ClrUniverse.CoreType(ClrNames.Boolean);

    public static readonly Type String = ClrUniverse.CoreType(ClrNames.String);

    public static readonly Type Object = ClrUniverse.CoreType(ClrNames.Object);

    public static readonly Type ValueType = ClrUniverse.CoreType(ClrNames.ValueType);

    public static readonly Type Exception = ClrUniverse.CoreType(ClrNames.Exception);

    public static readonly Type Console = ClrUniverse.FindType(ClrNames.Console)!;

    public static readonly Type TextWriter = ClrUniverse.FindType(ClrNames.TextWriter)!;

    public static readonly ConstructorInfo ObjectConstructor = Object.GetConstructor(Type.EmptyTypes)!;

    /// <summary><c>Exception.Message</c>.</summary>
    public static readonly MethodInfo ExceptionMessage = Exception.GetProperty("Message")!.GetGetMethod()!;

    /// <summary><c>Console.Error</c>.</summary>
    public static readonly MethodInfo ConsoleError = Console.GetProperty("Error")!.GetGetMethod()!;

    /// <summary><c>TextWriter.WriteLine(string)</c>.</summary>
    public static readonly MethodInfo TextWriterWriteLine = TextWriter.GetMethod("WriteLine", [String])!;

    /// <summary><c>Console.WriteLine(T)</c> for one of the types <c>print</c> accepts.</summary>
    public static MethodInfo ConsoleWriteLine(Type parameterType) => Console.GetMethod("WriteLine", [parameterType])!;
}
