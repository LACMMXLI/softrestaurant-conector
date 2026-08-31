using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class BranchValidationTests
{
    [Theory]
    [InlineData("sucursal-piloto")]
    [InlineData("cdmx01")]
    [InlineData("ab")]
    public void Valid_codes_are_accepted(string code) =>
        Assert.True(BranchValidation.IsValidCode(code));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Sucursal")]
    [InlineData("sucursal_piloto")]
    [InlineData("-sucursal")]
    [InlineData(" sucursal")]
    [InlineData(null)]
    public void Invalid_codes_are_rejected(string? code) =>
        Assert.False(BranchValidation.IsValidCode(code));

    [Fact]
    public void Code_longer_than_63_characters_is_rejected() =>
        Assert.False(BranchValidation.IsValidCode(new string('a', 64)));

    [Theory]
    [InlineData("America/Tijuana")]
    [InlineData("America/Mexico_City")]
    [InlineData("UTC")]
    public void Valid_iana_timezones_are_accepted(string timezone) =>
        Assert.True(BranchValidation.IsValidTimezone(timezone));

    [Theory]
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Invalid_timezones_are_rejected(string? timezone) =>
        Assert.False(BranchValidation.IsValidTimezone(timezone));

    [Fact]
    public void Timezone_longer_than_100_characters_is_rejected() =>
        Assert.False(BranchValidation.IsValidTimezone("America/" + new string('a', 100)));
}
