using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Checks an already authorized DM mixed-Origin projection against its exact
/// immutable completed event, approved decision audit and current Rules Core
/// catalog identity before exposing supported native mechanical scalars.
/// This is not an Origin constructor and never reauthorizes an old DM decision.
/// </summary>
public static class VerifiedCommittedMixedOriginReader
{
    public static VerifiedOriginProjectionContext? Read(
        CharacterAuthorizedOriginState saved,
        IReadOnlyList<CharacterOriginEventReceipt>? receipts,
        JsonNode? currentRuleset)
    {
        if (saved.EvaluatedProjectionJson is not { } storedProjection ||
            saved.SourceDraftRevision is not > 0 ||
            receipts is null ||
            currentRuleset is null)
            return null;
        try
        {
            if (currentRuleset["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                currentRuleset["fingerprint"]?.GetValue<string>() != saved.Fingerprint)
                return null;

            var committed = receipts.Where(receipt =>
                receipt.CommittedRevision == saved.Revision).ToArray();
            if (committed.Length != 1) return null;
            var eventReceipt = committed[0];
            if (eventReceipt.AuthorizationKind != "campaign-dm" ||
                eventReceipt.MethodRuleId != "rule.creature-origination" ||
                eventReceipt.MethodId != "biological" ||
                eventReceipt.ApprovalActorUserId is not Guid actor ||
                actor == Guid.Empty ||
                eventReceipt.ApprovalCampaignId is not Guid campaign ||
                campaign == Guid.Empty ||
                string.IsNullOrWhiteSpace(eventReceipt.AuthorizationEvidenceId) ||
                eventReceipt.ApprovedDecisionsJson is null)
                return null;

            // Every ruling in the frozen audit must have been issued for
            // exactly the approving actor, campaign and persisted evidence.
            using var approved = JsonDocument.Parse(eventReceipt.ApprovedDecisionsJson);
            var decisions = approved.RootElement;
            if (decisions.ValueKind != JsonValueKind.Array ||
                decisions.GetArrayLength() is < 1 or > 128)
                return null;
            var decisionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var decision in decisions.EnumerateArray())
            {
                if (!TryString(decision, "id", out var id) ||
                    !decisionIds.Add(id!) ||
                    !Object(decision, "authority", out var authority) ||
                    !TryString(authority, "actorId", out var actorId) ||
                    actorId != actor.ToString("D") ||
                    !TryString(authority, "authorizationEvidenceId", out var evidence) ||
                    evidence != eventReceipt.AuthorizationEvidenceId ||
                    !Object(decision, "scope", out var scope) ||
                    !TryString(scope, "kind", out var kind) || kind != "campaign" ||
                    !TryString(scope, "id", out var scopeId) ||
                    scopeId != campaign.ToString("D"))
                    return null;
            }

            using var stored = JsonDocument.Parse(saved.ProfileJson);
            var profile = stored.RootElement;
            if (!Object(profile, "adjudication", out var adjudication) ||
                !Numeric(adjudication, "adjudication", 3) ||
                !TryString(profile, "proposalId", out var proposalId) ||
                !TryString(profile, "id", out var profileId) ||
                profileId != "origin:" + proposalId ||
                !profile.TryGetProperty("history", out var history) ||
                history.ValueKind != JsonValueKind.Array ||
                history.GetArrayLength() != 1 ||
                !TryString(history[0], "id", out var eventId) ||
                eventId != eventReceipt.EventId ||
                !Numeric(history[0], "stage", 2))
                return null;

            using var projection = JsonDocument.Parse(storedProjection);
            var root = projection.RootElement;
            if (!TryString(root, "rulesetId", out var ruleset) ||
                ruleset != saved.RulesetId ||
                !TryString(root, "fingerprint", out var fingerprint) ||
                fingerprint != saved.Fingerprint ||
                !Object(root, "intrinsics", out var intrinsics) ||
                !Object(root, "vision", out var vision))
                return null;

            var scalars = VerifiedOriginIntrinsicReader.Read(
                intrinsics, saved.ProfileJson);
            if (scalars is null) return null;
            var lowLight = VerifiedOriginVisionReader.Read(vision, saved.ProfileJson);
            return new(saved.RulesetId, saved.Fingerprint, scalars, lowLight);
        }
        catch (Exception error) when (error is JsonException or
            InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Object(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryString(JsonElement parent, string name, out string? value)
    {
        value = null;
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var text) &&
            text.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = text.GetString());
    }

    private static bool Numeric(JsonElement parent, string name, int expected) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) &&
        number == expected;
}
