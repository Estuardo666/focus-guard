using System.Security.Cryptography;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

/// <summary>
/// Produces a short deliberate-action phrase for the strict review. This phrase
/// is not a secret or an authentication factor; the recovery credential remains
/// the only high-entropy value in the flow.
/// </summary>
public sealed class StrictConfirmationPhraseGenerator
{
    private readonly Func<int, int> _nextIndex;

    public StrictConfirmationPhraseGenerator()
        : this(RandomNumberGenerator.GetInt32)
    {
    }

    internal StrictConfirmationPhraseGenerator(Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(nextIndex);
        _nextIndex = nextIndex;
    }

    internal static IReadOnlyList<string> FirstWords => ParseWords(UiStrings.StrictPhraseFirstWords);

    internal static IReadOnlyList<string> SecondWords => ParseWords(UiStrings.StrictPhraseSecondWords);

    public string Generate()
    {
        var firstWords = FirstWords;
        var secondWords = SecondWords;
        return $"{firstWords[_nextIndex(firstWords.Count)]} {secondWords[_nextIndex(secondWords.Count)]}";
    }

    private static string[] ParseWords(string serializedWords)
    {
        var words = serializedWords.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words.Any(word => word.Contains(' ')))
        {
            throw new InvalidDataException("Strict confirmation dictionaries must contain single words.");
        }

        return words;
    }
}
