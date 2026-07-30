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
}
