using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace PepperDash.Essentials.Plugin.WyreStorm.Tests;

/// <summary>
/// Config contract tests. NetworkHD config classes are plain POCOs deserialized by
/// Newtonsoft using property names (no [JsonProperty] attributes), so these verify the
/// types exist, are constructible, and expose the expected property shapes.
/// </summary>
public class ConfigDeserializationTests
{
    private static Type? Find(string fullName) => AssemblyFixture.PluginAssembly.GetType(fullName);

    [Theory]
    [InlineData("PepperDash.Essentials.Plugin.NhdDeviceProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.Nhd232Properties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdCustomMultiviewLayoutProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdCustomMultiviewWindowProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdMultiviewPresetProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdMultiviewPresetWindowRouteProperties")]
    public void Config_Class_Exists(string fullName)
    {
        Find(fullName).Should().NotBeNull($"config class '{fullName}' should exist in the assembly");
    }

    [Theory]
    [InlineData("PepperDash.Essentials.Plugin.NhdDeviceProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.Nhd232Properties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdCustomMultiviewLayoutProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdCustomMultiviewWindowProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdMultiviewPresetProperties")]
    [InlineData("PepperDash.Essentials.Plugin.Config.NhdMultiviewPresetWindowRouteProperties")]
    public void Config_Has_Parameterless_Constructor(string fullName)
    {
        var type = Find(fullName);
        type.Should().NotBeNull($"config class '{fullName}' should exist");
        type!.GetConstructor(Type.EmptyTypes).Should()
            .NotBeNull($"config class '{fullName}' must have a parameterless constructor for deserialization");
    }

    [Theory]
    [InlineData("MatrixInputSlot",  "Int32")]
    [InlineData("MatrixOutputSlot", "Int32")]
    [InlineData("Alias",            "String")]
    [InlineData("ApiUsername",      "String")]
    [InlineData("ApiPassword",      "String")]
    public void NhdDeviceProperties_Property_Type_Matches(string propertyName, string expectedTypeName)
    {
        var type = Find("PepperDash.Essentials.Plugin.NhdDeviceProperties")!;
        var prop = type.GetProperty(propertyName);
        prop.Should().NotBeNull($"NhdDeviceProperties should expose {propertyName}");
        prop!.PropertyType.Name.Should().Be(expectedTypeName);
    }

    [Theory]
    [InlineData("CustomMultiviewLayouts", "NhdCustomMultiviewLayoutProperties")]
    [InlineData("MultiviewPresets",       "NhdMultiviewPresetProperties")]
    public void NhdDeviceProperties_List_Property_Element_Type(string propertyName, string expectedElementType)
    {
        var type = Find("PepperDash.Essentials.Plugin.NhdDeviceProperties")!;
        var prop = type.GetProperty(propertyName);
        prop.Should().NotBeNull($"NhdDeviceProperties should expose {propertyName}");

        var pType = prop!.PropertyType;
        pType.IsGenericType.Should().BeTrue($"{propertyName} must be a generic collection");
        pType.GetGenericTypeDefinition().Name.Should().Be("List`1");
        pType.GetGenericArguments()[0].Name.Should().Be(expectedElementType);
    }

    [Fact]
    public void NhdDeviceProperties_Deserializes_Sample_Json()
    {
        // Plain property-name deserialization (Newtonsoft default contract).
        const string json = """
            {
                "matrixInputSlot": 1,
                "matrixOutputSlot": 2,
                "alias": "Decoder-1",
                "apiUsername": "admin",
                "customMultiviewLayouts": [],
                "multiviewPresets": []
            }
            """;

        var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        dict.Should().NotBeNull();

        // Plain POCO contract: every JSON key must map (case-insensitively, Newtonsoft's default
        // contract) to a real NhdDeviceProperties property — so this fails if the config drifts.
        var propNames = Find("PepperDash.Essentials.Plugin.NhdDeviceProperties")!
            .GetProperties().Select(p => p.Name).ToList();

        foreach (var key in dict!.Keys)
        {
            propNames.Should().Contain(
                n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase),
                $"JSON key '{key}' should map to an NhdDeviceProperties property");
        }
    }
}
