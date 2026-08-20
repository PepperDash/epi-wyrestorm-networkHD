using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugin.WyreStorm.Tests;

/// <summary>
/// Structural/shape tests for Nhd150Rx's ApplyDynamicLayout overloads, matching this project's
/// MetadataLoadContext-based (metadata/reflection-only, no invocation) testing conventions - see
/// the note in NhdDynamicMultiviewLayoutCalculatorTests.cs for why.
/// </summary>
public class Nhd150RxTests
{
    private static System.Type Nhd150RxType =>
        AssemblyFixture.PluginAssembly.GetType("PepperDash.Essentials.Plugin.Nhd150Rx")!;

    [Fact]
    public void Nhd150Rx_Type_Exists()
    {
        Nhd150RxType.Should().NotBeNull();
    }

    [Fact]
    public void ApplyDynamicLayout_ParticipantSource_Overload_Exists()
    {
        // GetMethod with an exact IReadOnlyList<T> parameter type via MetadataLoadContext is brittle,
        // so just assert a 2-parameter ApplyDynamicLayout overload exists whose second parameter is
        // a string (the presentationSourceKey).
        var overloads = Nhd150RxType.GetMethods();
        var hasTwoParamOverload = System.Array.Exists(overloads, m =>
            m.Name == "ApplyDynamicLayout"
            && m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType.Name == "String");

        hasTwoParamOverload.Should().BeTrue("a 2-parameter ApplyDynamicLayout(participantSources, presentationSourceKey) overload should exist");
    }

    [Fact]
    public void ApplyDynamicLayout_Devjson_Friendly_Overload_Exists()
    {
        var overloads = Nhd150RxType.GetMethods();
        var devjsonOverload = System.Array.Find(overloads, m =>
            m.Name == "ApplyDynamicLayout"
            && m.GetParameters().Length == 3);

        devjsonOverload.Should().NotBeNull(
            "a devjson-friendly 3-parameter ApplyDynamicLayout(string[] sourceKeys, int[] priorities, string presentationSourceKey) overload should exist");

        var parameters = devjsonOverload!.GetParameters();
        parameters[0].ParameterType.IsArray.Should().BeTrue("sourceKeys should be a true array (devjson can construct these from a JSON array)");
        parameters[0].ParameterType.GetElementType()!.Name.Should().Be("String");
        parameters[1].ParameterType.IsArray.Should().BeTrue("priorities should be a true array (devjson can construct these from a JSON array)");
        parameters[1].ParameterType.GetElementType()!.Name.Should().Be("Int32");
        parameters[2].ParameterType.Name.Should().Be("String");
    }
}
