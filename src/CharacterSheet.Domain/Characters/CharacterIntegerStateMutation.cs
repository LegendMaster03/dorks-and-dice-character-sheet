namespace CharacterSheet.Domain.Characters;

public enum CharacterIntegerStateMutationOperation
{
    Adjust = 1,
    Expend = 2,
    Set = 3
}

public enum CharacterIntegerStateMutationTarget
{
    CurrentHitPoints = 1,
    DeathSaveSuccesses = 2,
    DeathSaveFailures = 3,
    Resource = 4
}

public sealed record CharacterIntegerStateMutation(
    CharacterIntegerStateMutationTarget Target,
    CharacterIntegerStateMutationOperation Operation,
    int Amount,
    string? ResourceKey = null);
