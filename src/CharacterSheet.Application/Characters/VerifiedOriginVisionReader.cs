using System.Text.Json;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// One verified passive visual sense: an authored rule maps actual Dim Light
/// to perceived Bright Light. This is not Darkvision, a general visibility
/// calculation, an unrestricted trait loader or an authorization token.
/// </summary>
public sealed record VerifiedOriginLowLightVision(
    string GrantId, string SourceId, string OccurrencePath,
    string ActualLightLevel, string PerceivedLightLevel);

public static class VerifiedOriginVisionReader
{
    public static VerifiedOriginLowLightVision? Read(
        JsonElement? evaluatedVision, string committedProfileJson)
    {
        if (evaluatedVision is not { ValueKind: JsonValueKind.Object } review ||
            !StringIs(review, "status", "resolved") ||
            !BoolIs(review, "previewOnly", true) ||
            !review.TryGetProperty("unresolvedReasons", out var issues) ||
            issues.ValueKind != JsonValueKind.Array || issues.GetArrayLength() != 0 ||
            !review.TryGetProperty("lowLightVision", out var low) ||
            low.ValueKind != JsonValueKind.Object ||
            !StringIs(low, "ruleId", "rule.low-light-vision") ||
            !StringIs(low, "actualLightLevel", "dim") ||
            !StringIs(low, "perceivedLightLevel", "bright") ||
            !TryText(low, "grantId", out var grantId) ||
            !TryText(low, "sourceId", out var sourceId) ||
            !TryText(low, "occurrencePath", out var path))
            return null;

        try
        {
            using var saved = JsonDocument.Parse(committedProfileJson);
            var profile = saved.RootElement;
            if (profile.ValueKind != JsonValueKind.Object ||
                !profile.TryGetProperty("underlying", out var underlying) ||
                !profile.TryGetProperty("current", out var current) ||
                underlying.ValueKind != JsonValueKind.Object ||
                current.ValueKind != JsonValueKind.Object ||
                !JsonElement.DeepEquals(underlying, current) ||
                !current.TryGetProperty("inherentGrants", out var inherent) ||
                inherent.ValueKind != JsonValueKind.Array)
                return null;

            var matching = inherent.EnumerateArray().Where(grant =>
                grant.ValueKind == JsonValueKind.Object &&
                StringIs(grant, "id", grantId!)).ToArray();
            if (matching.Length != 1)
                return null;

            var entry = matching[0];
            if (!entry.TryGetProperty("identity", out var identity) ||
                identity.ValueKind != JsonValueKind.Object ||
                !identity.TryGetProperty("kind", out var kind) ||
                !EnumIs(kind, "Trait", 1) ||
                !StringIs(identity, "mechanicalDefinitionId",
                    sourceId + "#trait:low-light-vision") ||
                !identity.TryGetProperty("occurrence", out var occurrence) ||
                occurrence.ValueKind != JsonValueKind.Object ||
                !StringIs(occurrence, "occurrencePath", path!) ||
                !occurrence.TryGetProperty("source", out var source) ||
                source.ValueKind != JsonValueKind.Object ||
                !StringIs(source, "id", sourceId!) ||
                !source.TryGetProperty("kind", out var sourceKind) ||
                !EnumIs(sourceKind, "Native", 0) ||
                !entry.TryGetProperty("effectiveValue", out var trait) ||
                trait.ValueKind != JsonValueKind.Object ||
                !StringIs(trait, "id", "low-light-vision") ||
                !StringIs(trait, "rule", "rule.low-light-vision") ||
                trait.EnumerateObject().Any(property =>
                    property.Name is not ("id" or "name" or "description" or "rule")))
                return null;

            return new(grantId!, sourceId!, path!, "dim", "bright");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool EnumIs(JsonElement value, string name, int ordinal) =>
        value.ValueKind == JsonValueKind.String && value.GetString() == name ||
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) &&
            number == ordinal;

    private static bool BoolIs(JsonElement parent, string key, bool expected) =>
        parent.TryGetProperty(key, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static bool StringIs(JsonElement parent, string key, string expected) =>
        parent.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool TryText(JsonElement parent, string key, out string? text)
    {
        text = null;
        if (!parent.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(text = value.GetString()))
            return false;
        return true;
    }
}
