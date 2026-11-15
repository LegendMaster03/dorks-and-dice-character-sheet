using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Defense-in-depth for committed supernatural history. A Rules Core transition
/// planner and independently verified host authority remain mandatory: these
/// checks only prevent a verified-write command from silently rewriting prior
/// owner evidence or bypassing ledger revisions.
/// </summary>
internal static class CharacterSupernaturalHistoryGuard
{
    public static void Validate(CharacterSupernaturalState? existing,
        VerifiedSupernaturalStateWrite command)
    {
        using var nextDocument = JsonDocument.Parse(command.LedgerJson);
        var next = nextDocument.RootElement;
        var nextEvents = Strings(next, "processedEventIds");
        if (nextEvents.Length > 16384 || nextEvents.Distinct(StringComparer.Ordinal).Count() != nextEvents.Length)
            throw new ArgumentException("The manifestation journal contains duplicate or excessive events.");

        if (existing is null)
        {
            if (command.IsTransition || command.ExpectedRevision != 0 ||
                command.ExpectedLedgerRevision != 0 || command.NextLedgerRevision != 0 ||
                nextEvents.Length != 0 || HasItems(next, "instances") ||
                HasItems(next, "resources") || HasItems(next, "interactions"))
                throw new ArgumentException("First acquisition can not silently create an activated manifestation.");
            return;
        }

        // Ruleset migrations and definition rebinds require their own authored,
        // independently authorized operations; neither form switching nor a
        // routine acquisition implicitly updates existing sources.
        if (!string.Equals(existing.RulesetId, command.RulesetId, StringComparison.Ordinal) ||
            !string.Equals(existing.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            throw new ArgumentException("A supernatural event can not silently change its pinned native catalog.");

        using var oldDocument = JsonDocument.Parse(existing.LedgerJson);
        var old = oldDocument.RootElement;
        if (command.IsTransition)
        {
            if (!JsonEqual(existing.AlternateSheetsJson, command.AlternateSheetsJson) ||
                !JsonEqual(existing.PersistentInfluencesJson, command.PersistentInfluencesJson))
                throw new ArgumentException("Manifestation transitions can not rewrite owned sheets, influences, or grants.");

            var previousEvents = Strings(old, "processedEventIds");
            if (nextEvents.Length != previousEvents.Length + 1 ||
                nextEvents[^1] != command.EventId ||
                !previousEvents.SequenceEqual(nextEvents.Take(previousEvents.Length),
                    StringComparer.Ordinal))
                throw new ArgumentException("A transition must append exactly its event without rewriting prior history.");

            PreserveResources(old, next);
            PreserveInstances(old, next, command);
        }
        else
        {
            if (!JsonElement.DeepEquals(old, next))
                throw new ArgumentException("An acquisition can not mutate manifestation state without a ledger revision.");
            PreserveAcquiredOwners(existing.AlternateSheetsJson, command.AlternateSheetsJson,
                "alternate sheets", ["parentId", "definitionId", "definitionSource", "acquisitionEvidenceId"]);
            PreserveAcquiredOwners(existing.PersistentInfluencesJson, command.PersistentInfluencesJson,
                "persistent influences", ["definitionId", "definitionSource", "acquisitionEvidenceId"]);
        }
    }

    private static void PreserveResources(JsonElement previous, JsonElement proposed)
    {
        var oldResources = ById(previous.GetProperty("resources"), "previous resources");
        var newResources = ById(proposed.GetProperty("resources"), "proposed resources");
        var newInstances = ById(proposed.GetProperty("instances"), "proposed instances");
        var oldInstances = ById(previous.GetProperty("instances"), "previous instances");

        foreach (var (id, previousResource) in oldResources)
            if (!newResources.TryGetValue(id, out var nextResource) ||
                !JsonElement.DeepEquals(previousResource, nextResource))
                throw new ArgumentException("A form transition can not spend, refill, transfer or erase existing resource pools.");

        foreach (var (id, resource) in newResources)
        {
            if (oldResources.ContainsKey(id)) continue;
            if (!resource.TryGetProperty("ownerKind", out var kind) ||
                !IsManifestationOwner(kind) ||
                !resource.TryGetProperty("ownerId", out var owner) ||
                owner.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(owner.GetString()) ||
                !newInstances.ContainsKey(owner.GetString()!) ||
                oldInstances.ContainsKey(owner.GetString()!))
                throw new ArgumentException("A new transition resource must belong to a new manifestation instance.");
        }
    }

    private static bool IsManifestationOwner(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == 3 ||
        value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), "Manifestation", StringComparison.Ordinal);

    private static void PreserveInstances(JsonElement previous,
        JsonElement proposed, VerifiedSupernaturalStateWrite command)
    {
        var oldInstances = ById(previous.GetProperty("instances"), "previous instances");
        var nextInstances = ById(proposed.GetProperty("instances"), "proposed instances");
        foreach (var (id, before) in oldInstances)
        {
            if (!nextInstances.TryGetValue(id, out var after))
                throw new ArgumentException("A manifestation transition can not erase an earlier activation.");
            var oldBody = JsonNode.Parse(before.GetRawText())!.AsObject();
            var nextBody = JsonNode.Parse(after.GetRawText())!.AsObject();
            foreach (var name in new[] { "lifecycle", "contribution", "lastEventId" })
            {
                oldBody.Remove(name);
                nextBody.Remove(name);
            }
            if (!JsonNode.DeepEquals(oldBody, nextBody))
                throw new ArgumentException("A transition can not rewrite activation identity, receipts or captures.");
            if (!JsonElement.DeepEquals(before, after) &&
                (!after.TryGetProperty("lastEventId", out var last) ||
                 last.ValueKind != JsonValueKind.String ||
                 last.GetString() != command.EventId))
                throw new ArgumentException("A changed manifestation must reference the new transition event.");
        }
        foreach (var (id, instance) in nextInstances)
        {
            if (oldInstances.ContainsKey(id)) continue;
            if (!instance.TryGetProperty("characterId", out var characterId) ||
                characterId.ValueKind != JsonValueKind.String ||
                characterId.GetString() != command.CharacterId.ToString("D") ||
                !instance.TryGetProperty("activationEventId", out var activated) ||
                activated.ValueKind != JsonValueKind.String ||
                activated.GetString() != command.EventId ||
                !instance.TryGetProperty("lastEventId", out var last) ||
                last.ValueKind != JsonValueKind.String ||
                last.GetString() != command.EventId)
                throw new ArgumentException("New activations must be owned by the character and current event.");
        }
    }

    private static void PreserveAcquiredOwners(string previousJson, string proposedJson,
        string kind, IReadOnlyList<string> immutableFields)
    {
        using var previousDoc = JsonDocument.Parse(previousJson);
        using var nextDoc = JsonDocument.Parse(proposedJson);
        var previous = ById(previousDoc.RootElement, "previous " + kind);
        var next = ById(nextDoc.RootElement, "proposed " + kind);
        foreach (var (id, oldOwner) in previous)
        {
            if (!next.TryGetValue(id, out var candidate))
                throw new ArgumentException("An acquisition can not erase an existing " + kind + " owner.");
            foreach (var field in immutableFields)
                if (!SameOptionalField(oldOwner, candidate, field))
                    throw new ArgumentException("An acquisition can not rebind existing " + kind + " evidence.");
            PreserveRecords(oldOwner, candidate, "progression", "earned progression");
            // Influence effects may be augmented as expression progresses,
            // but an already-authored effect ID may not change semantics.
            if (kind == "persistent influences")
                PreserveRecords(oldOwner, candidate, "effects", "influence effects");
        }
    }

    private static void PreserveRecords(JsonElement oldOwner, JsonElement nextOwner,
        string field, string label)
    {
        if (!oldOwner.TryGetProperty(field, out var earlier) ||
            earlier.ValueKind == JsonValueKind.Null) return;
        var existing = ById(earlier, "previous " + label);
        if (!nextOwner.TryGetProperty(field, out var later) ||
            later.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("An acquisition can not remove " + label + ".");
        var proposed = ById(later, "proposed " + label);
        foreach (var (id, record) in existing)
            if (!proposed.TryGetValue(id, out var next) ||
                !JsonElement.DeepEquals(record, next))
                throw new ArgumentException("An acquisition can not rewrite an earlier " + label + ".");
    }

    private static bool SameOptionalField(JsonElement left, JsonElement right, string field)
    {
        var a = left.TryGetProperty(field, out var valueA);
        var b = right.TryGetProperty(field, out var valueB);
        return a == b && (!a || JsonElement.DeepEquals(valueA, valueB));
    }

    private static Dictionary<string, JsonElement> ById(JsonElement values, string label)
    {
        if (values.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Expected array for " + label);
        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !found.TryAdd(id.GetString()!, value))
                throw new ArgumentException("Duplicate or missing identity in " + label);
        }
        return found;
    }

    private static bool HasItems(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var array) &&
        array.ValueKind != JsonValueKind.Null &&
        (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 0);

    private static string[] Strings(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Missing manifestation event history.");
        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > 160)
                throw new ArgumentException("Invalid manifestation event identity.");
            result.Add(item.GetString()!);
        }
        return result.ToArray();
    }

    private static bool JsonEqual(string a, string b)
    {
        using var left = JsonDocument.Parse(a);
        using var right = JsonDocument.Parse(b);
        return JsonElement.DeepEquals(left.RootElement, right.RootElement);
    }
}
