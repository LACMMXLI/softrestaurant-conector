using SoftRestaurant.CentralApi;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

public sealed class UserValidationTests
{
    [Theory]
    [InlineData("owner@example.com")]
    [InlineData("a.b+tag@sub.example.co")]
    public void Valid_emails_are_accepted(string email) => Assert.True(UserValidation.IsValidEmail(email));

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    [InlineData(" spaced@example.com")]
    [InlineData(null)]
    public void Invalid_emails_are_rejected(string? email) => Assert.False(UserValidation.IsValidEmail(email));

    [Fact]
    public void Email_longer_than_320_characters_is_rejected()
    {
        var localPart = new string('a', 315);
        Assert.False(UserValidation.IsValidEmail($"{localPart}@example.com"));
    }

    [Theory]
    [InlineData("SUPERADMIN")]
    [InlineData("OWNER")]
    [InlineData("MANAGER")]
    [InlineData("VIEWER")]
    public void Valid_roles_are_accepted(string role) => Assert.True(UserValidation.IsValidRole(role));

    [Theory]
    [InlineData("")]
    [InlineData("ADMIN")]
    [InlineData("owner")]
    [InlineData(null)]
    public void Invalid_roles_are_rejected(string? role) => Assert.False(UserValidation.IsValidRole(role));

    [Fact]
    public void Password_shorter_than_twelve_characters_is_rejected() =>
        Assert.False(UserValidation.IsValidPassword("short-pass"));

    [Fact]
    public void Password_with_twelve_or_more_characters_is_accepted() =>
        Assert.True(UserValidation.IsValidPassword("twelve-chars-ok"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Missing_display_name_is_rejected(string? displayName) =>
        Assert.False(UserValidation.IsValidDisplayName(displayName));

    [Fact]
    public void Display_name_longer_than_200_characters_is_rejected() =>
        Assert.False(UserValidation.IsValidDisplayName(new string('a', 201)));
}
