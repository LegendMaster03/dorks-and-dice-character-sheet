using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class AbilityScoreInputModelTests
{
    [Theory]
    [InlineData(" strength ", CharacterAbilityKey.Strength)]
    [InlineData("DEXTERITY", CharacterAbilityKey.Dexterity)]
    [InlineData(" Constitution ", CharacterAbilityKey.Constitution)]
    [InlineData("INTELLIGENCE", CharacterAbilityKey.Intelligence)]
    [InlineData(" wisdom ", CharacterAbilityKey.Wisdom)]
    [InlineData("CHARISMA", CharacterAbilityKey.Charisma)]
    public void AbilityKeysNormalizeToStablePlayableBaseline(string input, string expected)
    {
        Assert.Equal(expected, CharacterAbilityKey.Normalize(input));
    }

    [Fact]
    public void UnsupportedAbilityKeyIsRejectedWithoutTurningItIntoARulesConcept()
    {
        var exception = Assert.Throws<ArgumentException>(() => CharacterAbilityKey.Normalize("luck"));

        Assert.Contains("current playable baseline", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingSameBaseAbilityInputReplacesCurrentDecisionWithoutDuplicatingIt()
    {
        var createdAt = new DateTimeOffset(2026, 9, 18, 3, 40, 0, TimeSpan.Zero);
        var changedAt = createdAt.AddMinutes(5);
        var root = new CharacterSheetRoot(Guid.NewGuid(), createdAt);

        var first = root.SetBaseAbilityScoreInput(" STRENGTH ", 15, createdAt.AddMinutes(1));
        var replacement = root.SetBaseAbilityScoreInput("strength", 18, changedAt);

        Assert.Same(first, replacement);
        Assert.Single(root.BaseAbilityScoreInputs);
        Assert.Equal(CharacterAbilityKey.Strength, replacement.AbilityKey);
        Assert.Equal(18, replacement.Score);
        Assert.Equal(createdAt.AddMinutes(1), replacement.CreatedAt);
        Assert.Equal(changedAt, replacement.UpdatedAt);
        Assert.Equal(changedAt, root.UpdatedAt);
    }

    [Fact]
    public void DifferentBaseAbilityInputsCoexistAndClearRemovesOnlyRequestedDecision()
    {
        var createdAt = new DateTimeOffset(2026, 9, 18, 3, 40, 0, TimeSpan.Zero);
        var root = new CharacterSheetRoot(Guid.NewGuid(), createdAt);
        root.SetBaseAbilityScoreInput("strength", 15, createdAt.AddMinutes(1));
        root.SetBaseAbilityScoreInput("dexterity", 14, createdAt.AddMinutes(2));

        Assert.Equal(2, root.BaseAbilityScoreInputs.Count);
        Assert.True(root.ClearBaseAbilityScoreInput(" STRENGTH ", createdAt.AddMinutes(3)));

        var remaining = Assert.Single(root.BaseAbilityScoreInputs);
        Assert.Equal(CharacterAbilityKey.Dexterity, remaining.AbilityKey);
        Assert.Equal(14, remaining.Score);
        Assert.False(root.ClearBaseAbilityScoreInput("strength", createdAt.AddMinutes(4)));
    }

    [Fact]
    public void BaseInputDoesNotImposeEditionSpecificNumericRange()
    {
        var root = new CharacterSheetRoot(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var low = root.SetBaseAbilityScoreInput("constitution", int.MinValue, DateTimeOffset.UtcNow.AddMinutes(1));
        var high = root.SetBaseAbilityScoreInput("constitution", int.MaxValue, DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.Same(low, high);
        Assert.Equal(int.MaxValue, high.Score);
    }
}
