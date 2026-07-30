using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void Create_RejectsBlankName()
    {
        var result = Profile.Create("   ");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.name.required");
    }

    [Fact]
    public void Create_TrimsValidName()
    {
        var result = Profile.Create("  Deep Work  ");

        Assert.True(result.IsValid);
        Assert.Equal("Deep Work", result.Value!.Name);
    }

    [Fact]
    public void ValidateName_TrimsValidNameAndRejectsInvalidLengths()
    {
        var valid = Profile.ValidateName("  Deep Work  ");
        var empty = Profile.ValidateName("   ");
        var overlong = Profile.ValidateName(new string('A', 81));

        Assert.Equal("Deep Work", valid.Value);
        Assert.Contains(empty.Errors, error => error.Code == "profile.name.required");
        Assert.Contains(overlong.Errors, error => error.Code == "profile.name.too_long");
    }
}
