using System.Text.Json;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Narrow server-side bridge from current native Origin adjudication to the
/// existing character kernel. Only a full first-party review can supply the
/// native projection output to this reader; a browser preview is never input.
/// Recheck each reported scalar against the exact committed grant occurrence.
/// </summary>
public sealed record VerifiedOriginIntrinsics(
    string CreatureType, string Size, int BaseWalkingSpeedFeet);

public static class VerifiedOriginIntrinsicReader
{
    public static VerifiedOriginIntrinsics? Read(
        JsonElement? nativeIntrinsics, string committedProfileJson)
    {
        if (nativeIntrinsics is not { ValueKind: JsonValueKind.Object } response ||
            !TextIs(response, "status", "resolved") ||
            !BooleanIs(response, "previewOnly", true) ||
            !response.TryGetProperty("unresolvedSlots", out var unresolved) ||
            unresolved.ValueKind != JsonValueKind.Array ||
            unresolved.GetArrayLength() != 0)
            return null;

        try
        {
            using var committed = JsonDocument.Parse(committedProfileJson);
            var profile = committed.RootElement;
            if (profile.ValueKind != JsonValueKind.Object ||
                !profile.TryGetProperty("underlying", out var underlying) ||
                !profile.TryGetProperty("current", out var current) ||
                !JsonElement.DeepEquals(underlying, current) ||
                !current.TryGetProperty("inherentGrants", out var grants) ||
                grants.ValueKind != JsonValueKind.Array)
                return null;

            var type = Value("creatureType", JsonValueKind.String);
            var size = Value("size", JsonValueKind.String);
            var walk = Value("walkingSpeedFeet", JsonValueKind.Number);
            if (type is null || size is null || walk is null ||
                !walk.Value.Value.TryGetInt32(out var speed) ||
                speed is < 0 or > 1_000_000 ||
                string.IsNullOrWhiteSpace(type.Value.Value.GetString()) ||
                string.IsNullOrWhiteSpace(size.Value.Value.GetString()))
                return null;

            return new(type.Value.Value.GetString()!,
                size.Value.Value.GetString()!, speed);

            (JsonElement Value, string GrantId)? Value(
                string slot, JsonValueKind requiredKind)
            {
                if (!response.TryGetProperty(slot, out var projected) ||
                    projected.ValueKind != JsonValueKind.Object ||
                    !projected.TryGetProperty("value", out var value) ||
                    value.ValueKind != requiredKind ||
                    !TryText(projected, "grantId", out var grantId) ||
                    !TryText(projected, "sourceId", out var sourceId) ||
                    !TryText(projected, "occurrencePath", out var path))
                    return null;

                var matching = grants.EnumerateArray()
                    .Where(grant => grant.ValueKind == JsonValueKind.Object &&
                        grant.TryGetProperty("id", out var id) &&
                        id.ValueKind == JsonValueKind.String &&
                        id.GetString() == grantId).ToArray();
                if (matching.Length != 1) return null;
                var entry = matching[0];
                if (!entry.TryGetProperty("effectiveValue", out var oldValue) ||
                    !JsonElement.DeepEquals(oldValue, value) ||
                    !entry.TryGetProperty("identity", out var identity) ||
                    identity.ValueKind != JsonValueKind.Object ||
                    !TextIs(identity, "intrinsicSlotId", slot) ||
                    !identity.TryGetProperty("kind", out var kind) ||
                    !EnumIs(kind, "Intrinsic", 0) ||
                    !identity.TryGetProperty("occurrence", out var occurrence) ||
                    occurrence.ValueKind != JsonValueKind.Object ||
                    !TextIs(occurrence, "occurrencePath", path) ||
                    !occurrence.TryGetProperty("source", out var source) ||
                    source.ValueKind != JsonValueKind.Object ||
                    !TextIs(source, "id", sourceId) ||
                    !source.TryGetProperty("kind", out var sourceKind) ||
                    !EnumIs(sourceKind, "Native", 0))
                    return null;

                return (value, grantId);
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool EnumIs(JsonElement value, string label, int ordinal) =>
        value.ValueKind == JsonValueKind.String && value.GetString() == label ||
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index) &&
            index == ordinal;

    private static bool BooleanIs(JsonElement parent, string property, bool expected) =>
        parent.TryGetProperty(property, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static bool TextIs(JsonElement parent, string property, string expected) =>
        parent.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool TryText(JsonElement parent, string property, out string? text)
    {
        text = null;
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(text = value.GetString()))
            return false;
        return true;
    }
}
