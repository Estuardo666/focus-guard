using FocusGuard.Desktop;

namespace FocusGuard.Desktop.Tests;

public sealed class StrictConfirmationViewModelTests
{
    [Fact]
    public void NewReview_IsNotActivatableAndShowsFiniteEndTime()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var phraseGenerator = new StrictConfirmationPhraseGenerator(_ => 0);
        var viewModel = new StrictConfirmationViewModel(clock, phraseGenerator);

        Assert.False(viewModel.CanActivate);
        Assert.Contains("2030", viewModel.EndTimeText);
        Assert.Contains(viewModel.EndTimeText, viewModel.ActivationButtonText);
        Assert.Equal("Calm Balance", viewModel.RequiredConfirmationText);
        Assert.Contains("does not activate", viewModel.StatusMessage);
    }

    [Fact]
    public void AllGatesAndExactSentence_EnableReviewActionButDoNotActivate()
    {
        var viewModel = new StrictConfirmationViewModel(
            new TestTimeProvider(DateTimeOffset.UtcNow),
            new StrictConfirmationPhraseGenerator(_ => 0));

        viewModel.AcknowledgeRecoverySaved = true;
        viewModel.AcknowledgeUnsavedWork = true;
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;

        Assert.True(viewModel.CanActivate);
        viewModel.StartStrictFocusCommand.Execute(null);
        Assert.Contains("No strict session was activated", viewModel.StatusMessage);
    }

    [Fact]
    public void NearMatchConfirmationDoesNotEnableAction()
    {
        var viewModel = new StrictConfirmationViewModel(
            new TestTimeProvider(DateTimeOffset.UtcNow),
            new StrictConfirmationPhraseGenerator(_ => 0))
        {
            AcknowledgeRecoverySaved = true,
            AcknowledgeUnsavedWork = true,
            TypedConfirmation = "Calm Focus"
        };

        Assert.False(viewModel.IsConfirmationValid);
        Assert.False(viewModel.CanActivate);
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => initialUtc;
    }
}
