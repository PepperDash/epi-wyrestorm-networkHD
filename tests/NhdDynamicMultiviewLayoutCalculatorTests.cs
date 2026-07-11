using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugin.WyreStorm.Tests;

/// <summary>
/// Structural/shape tests for <c>NhdDynamicMultiviewLayoutCalculator</c>, matching the conventions
/// used elsewhere in this test project.
///
/// Note: the participant-source DTO (<c>MultiviewParticipantSource</c>) now lives in
/// PepperDash.Essentials.Core.DeviceTypeInterfaces (alongside the <c>IHasDynamicMultiviewLayout</c>
/// interface Nhd150Rx implements), not as a nested type in this plugin - so it isn't re-tested
/// here; see the Essentials repo's own tests for that.
///
/// This project loads the plugin assembly via <see cref="System.Reflection.MetadataLoadContext"/>
/// (see <see cref="AssemblyFixture"/>), which supports metadata inspection only - it cannot invoke
/// methods (the plugin depends on Crestron SimplSharp assemblies that aren't available/executable
/// outside a real Crestron runtime). So unlike a typical "pure algorithm" class, the calculator's
/// actual geometry math can't be exercised end-to-end here; these tests instead verify its public
/// shape (types/members exist with expected signatures) and, via source-text assertions, that the
/// key algorithmic invariants described in its doc comments are actually implemented as documented.
/// </summary>
public class NhdDynamicMultiviewLayoutCalculatorTests
{
    private static Type CalculatorType =>
        AssemblyFixture.PluginAssembly.GetType("PepperDash.Essentials.Plugin.NhdDynamicMultiviewLayoutCalculator")!;

    [Fact]
    public void Calculator_Type_Exists()
    {
        CalculatorType.Should().NotBeNull();
    }

    [Fact]
    public void Calculator_Is_Static()
    {
        CalculatorType.IsAbstract.Should().BeTrue("static classes are abstract+sealed at the IL level");
        CalculatorType.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void CalculateLayout_Method_Exists_With_Expected_Signature()
    {
        var method = CalculatorType.GetMethod("CalculateLayout");
        method.Should().NotBeNull();

        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(5);
        parameters[1].ParameterType.Name.Should().Be("String", "presentationSourceKey should be a string");
        parameters[2].ParameterType.Name.Should().Be("Int32", "canvasWidth should be an int");
        parameters[3].ParameterType.Name.Should().Be("Int32", "canvasHeight should be an int");
        parameters[4].ParameterType.Name.Should().Be("Int32", "maxTileCount should be an int");
    }

    [Fact]
    public void Grid_Layout_Uses_Ceiling_Sqrt_For_Column_Count()
    {
        var content = AssemblyFixture.FindSourceForClass("NhdDynamicMultiviewLayoutCalculator");
        content.Should().NotBeNull();
        Regex.IsMatch(content!, @"Math\.Ceiling\(\s*Math\.Sqrt\(")
            .Should().BeTrue("the no-presentation grid layout should compute columns via ceil(sqrt(N))");
    }

    [Fact]
    public void Presentation_Tile_Is_Always_Tile_Number_One()
    {
        var content = AssemblyFixture.FindSourceForClass("NhdDynamicMultiviewLayoutCalculator");
        content.Should().NotBeNull();
        Regex.IsMatch(content!, @"BuildPresentationTile\([^)]*\)[\s\S]{0,120}new NhdMultiviewTileState\(\s*1,")
            .Should().BeTrue("the presentation tile should always be assigned tile number 1");
    }

    [Fact]
    public void Participants_Are_Ordered_By_Priority_Ascending()
    {
        var content = AssemblyFixture.FindSourceForClass("NhdDynamicMultiviewLayoutCalculator");
        content.Should().NotBeNull();
        content!.Should().Contain(".OrderBy(p => p.Priority)",
            "lower priority values (higher priority) should be placed first");
    }

    [Fact]
    public void Overflow_Participants_Are_Clamped_To_Available_Tile_Capacity()
    {
        var content = AssemblyFixture.FindSourceForClass("NhdDynamicMultiviewLayoutCalculator");
        content.Should().NotBeNull();
        content!.Should().Contain(".Take(availableParticipantTiles)",
            "participants beyond capacity should be dropped (lowest priority first, since the list is already ordered)");
    }
}
