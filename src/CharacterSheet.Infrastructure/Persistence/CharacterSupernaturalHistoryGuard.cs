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
            if (command.IsTransition || command.IsExpressionChange ||
                command.ExpectedRevision != 0 ||
                command.ExpectedLedgerRevision != 0 || command.NextLedgerRevision != 0 ||
                nextEvents.Length != 0 || HasItems(next, "instances") ||
                HasItems(next, "interactions"))
                throw new ArgumentException("First acquisition can not silently create an activated manifestation.");

            // A newly acquired sheet or influence may own a new resource
            // pool before any form manifests. The first record has no prior
            // ledger to compare; validate ownership, source receipt, and
            // quantity instead of incorrectly requiring zero resource pools.
            ValidateInitialResources(next, command.AlternateSheetsJson,
                command.PersistentInfluencesJson);
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
            PreserveAuditedInstanceActions(old, next, command);
            PreserveAuditedActivationResources(old, next, command.TransitionRequestJson!);
            PreserveInteractionDecisions(old, next, command.TransitionRequestJson!);
        }

        else if (command.IsExpressionChange)
        {
            if (!JsonEqual(existing.AlternateSheetsJson, command.AlternateSheetsJson) ||
                !JsonElement.DeepEquals(old, next))
                throw new ArgumentException(
                    "An expression change cannot modify owned sheets or the manifestation ledger.");
            PreserveExpressionChange(existing.PersistentInfluencesJson,
                command.PersistentInfluencesJson, command.ExpressionChangeJson!);
        }
        else if (command.IsOverlapDecision)
        {
            // A winner selection is not a new influence, form, grant or
            // progression. The only mutation permitted on this path is the
            // separately verified, historical overlap-decision collection.
            if (!JsonEqual(existing.AlternateSheetsJson, command.AlternateSheetsJson) ||
                !JsonEqual(existing.PersistentInfluencesJson, command.PersistentInfluencesJson) ||
                !JsonElement.DeepEquals(old, next))
                throw new ArgumentException(
                    "An overlap decision cannot mutate owners or the manifestation ledger.");
        }
        else
        {
            if (!JsonElement.DeepEquals(old, next))
                throw new ArgumentException("An acquisition can not mutate manifestation state without a ledger revision.");
            PreserveAcquiredOwners(existing.AlternateSheetsJson, command.AlternateSheetsJson,
                "alternate sheets");
            PreserveAcquiredOwners(existing.PersistentInfluencesJson, command.PersistentInfluencesJson,
                "persistent influences", allowEarnedEffects: command.IsOwnershipPlan);
        }
    }

    /// <summary>
    /// Core's planner either inherits every existing interaction unchanged or
    /// replaces the entire interaction set with the explicitly authorized
    /// request list. Compare the resulting ledger to the independently retained
    /// transition audit so an unrelated interaction cannot be introduced or
    /// silently discarded within a legitimate form change.
    /// </summary>
    private static void PreserveInteractionDecisions(
        JsonElement previous, JsonElement proposed, string transitionRequestJson)
    {
        using var audited = JsonDocument.Parse(transitionRequestJson);
        var request = audited.RootElement;
        var replaces = request.TryGetProperty("replacementInteractions", out var replacements) &&
            replacements.ValueKind != JsonValueKind.Null;
        if (replaces && replacements.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Interaction replacement must be an audited array.");

        var expected = replaces
            ? replacements
            : previous.TryGetProperty("interactions", out var inherited)
                ? inherited : default;
        var actual = proposed.TryGetProperty("interactions", out var committed)
            ? committed : default;

        // Native's optional null and omitted interaction collections mean the
        // same empty policy. Explicit [] remains distinct from a null policy.
        static bool IsMissing(JsonElement element) =>
            element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
        if (!(IsMissing(expected) && IsMissing(actual)) &&
            (IsMissing(expected) || IsMissing(actual) ||
             !JsonElement.DeepEquals(expected, actual)))
            throw new ArgumentException(
                "Manifestation interactions must match the authorized transition's replacement decisions.");
    }

    private static void ValidateInitialResources(JsonElement ledger,
        string sheetsJson, string influencesJson)
    {
        using var sheets = JsonDocument.Parse(sheetsJson);
        using var influences = JsonDocument.Parse(influencesJson);
        var sheetIds = ById(sheets.RootElement, "initial sheets");
        var influenceIds = ById(influences.RootElement, "initial influences");
        var resources = ById(ledger.GetProperty("resources"), "initial resources");
        if (resources.Count > 4096)
            throw new ArgumentException("Too many independently owned initial resource pools.");

        foreach (var resource in resources.Values)
        {
            string Text(string key)
            {
                if (!resource.TryGetProperty(key, out var value) ||
                    value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(value.GetString()))
                    throw new ArgumentException("Incomplete initial resource source or ownership.");
                return value.GetString()!;
            }
            int Number(string key)
            {
                if (!resource.TryGetProperty(key, out var value) ||
                    value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var result))
                    throw new ArgumentException("Invalid initial resource quantity.");
                return result;
            }

            var ownerId = Text("ownerId");
            _ = Text("definitionId");
            _ = Text("evidenceId");
            var current = Number("current");
            var maximum = Number("maximum");
            if (current < 0 || maximum is < 0 or > 1_000_000 ||
                current > maximum ||
                !resource.TryGetProperty("sourceOccurrence", out var occurrence) ||
                occurrence.ValueKind != JsonValueKind.Object ||
                !occurrence.TryGetProperty("occurrencePath", out var sourcePath) ||
                sourcePath.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sourcePath.GetString()) ||
                !occurrence.TryGetProperty("source", out var source) ||
                source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("id", out var sourceId) ||
                sourceId.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sourceId.GetString()) ||
                !source.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(version.GetString()) ||
                !source.TryGetProperty("kind", out var sourceKind) ||
                sourceKind.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                throw new ArgumentException("An initial resource requires a valid acquired source and quantity.");

            if (!resource.TryGetProperty("ownerKind", out var ownerKind))
                throw new ArgumentException("Initial resources require an explicit owner.");
            var kind = ownerKind.ValueKind switch
            {
                JsonValueKind.Number when ownerKind.TryGetInt32(out var n) => n,
                JsonValueKind.String => ownerKind.GetString() switch
                {
                    "Primary" => 0,
                    "Sheet" => 1,
                    "Influence" => 2,
                    _ => -1
                },
                _ => -1
            };
            if (kind switch
            {
                0 => ownerId != "@primary",
                1 => !sheetIds.ContainsKey(ownerId),
                2 => !influenceIds.ContainsKey(ownerId),
                _ => true
            })
                throw new ArgumentException("Initial resource owner must be the primary, an acquired sheet, or an acquired influence.");
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

    /// <summary>
    /// Reconcile every ordered transition step with the exact persisted
    /// instance delta. An audited activation must be the new instance that
    /// was actually committed. End, Suppress and Restore can change only the
    /// lifecycle/contribution of their named existing instance. A command
    /// can not create, modify or omit additional instances behind the audit.
    /// This never replaces the Rules Core legality and host-source checks.
    /// </summary>
    private static void PreserveAuditedInstanceActions(
        JsonElement previous, JsonElement proposed, VerifiedSupernaturalStateWrite command)
    {
        var oldInstances = ById(previous.GetProperty("instances"), "previous instances");
        var newInstances = ById(proposed.GetProperty("instances"), "proposed instances");
        using var audited = JsonDocument.Parse(command.TransitionRequestJson!);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in audited.RootElement.GetProperty("steps").EnumerateArray())
        {
            var target = step.GetProperty("targetInstanceId").GetString()!;
            if (!touched.Add(target))
                throw new ArgumentException(
                    "The ordered transition can not change one manifestation instance twice.");
            var action = StepAction(step.GetProperty("action"));
            if (action == 0)
            {
                if (oldInstances.ContainsKey(target) ||
                    !newInstances.TryGetValue(target, out var created) ||
                    !step.TryGetProperty("newInstance", out var declared) ||
                    declared.ValueKind != JsonValueKind.Object ||
                    !JsonElement.DeepEquals(declared, created))
                    throw new ArgumentException(
                        "An audited Activate must exactly match its newly committed instance.");
                continue;
            }

            if (step.TryGetProperty("newInstance", out var unexpected) &&
                unexpected.ValueKind != JsonValueKind.Null ||
                !oldInstances.TryGetValue(target, out var before) ||
                !newInstances.TryGetValue(target, out var after))
                throw new ArgumentException(
                    "A non-activation step must target an existing manifestation.");

            var oldLifecycle = EnumValue(before, "lifecycle", "Active", "Ended");
            var newLifecycle = EnumValue(after, "lifecycle", "Active", "Ended");
            var oldContribution = EnumValue(before, "contribution", "Contributing", "Suppressed");
            var newContribution = EnumValue(after, "contribution", "Contributing", "Suppressed");
            var valid = action switch
            {
                1 => oldLifecycle == 0 && newLifecycle == 1 &&
                    oldContribution == newContribution,
                2 => oldLifecycle == 0 && newLifecycle == 0 &&
                    oldContribution == 0 && newContribution == 1,
                3 => oldLifecycle == 0 && newLifecycle == 0 &&
                    oldContribution == 1 && newContribution == 0,
                _ => false
            };
            if (!valid || !after.TryGetProperty("lastEventId", out var last) ||
                last.ValueKind != JsonValueKind.String ||
                last.GetString() != command.EventId)
                throw new ArgumentException(
                    "The committed manifestation lifecycle does not match its audited action.");
        }

        foreach (var (id, prior) in oldInstances)
            if (!touched.Contains(id) &&
                (!newInstances.TryGetValue(id, out var retained) ||
                 !JsonElement.DeepEquals(prior, retained)))
                throw new ArgumentException(
                    "An untouched manifestation may not change during a transition.");
        foreach (var id in newInstances.Keys)
            if (!oldInstances.ContainsKey(id) && !touched.Contains(id))
                throw new ArgumentException(
                    "Every newly created manifestation needs an audited Activate step.");
    }

    private static int StepAction(JsonElement action) =>
        action.ValueKind == JsonValueKind.Number && action.TryGetInt32(out var number)
            ? number : action.GetString() switch
            {
                "Activate" => 0, "End" => 1, "Suppress" => 2,
                "Restore" => 3, _ => -1
            };

    private static int EnumValue(
        JsonElement parent, string property, string zero, string one)
    {
        if (!parent.TryGetProperty(property, out var value)) return -1;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) &&
                number is 0 or 1 => number,
            JsonValueKind.String when value.GetString() == zero => 0,
            JsonValueKind.String when value.GetString() == one => 1,
            _ => -1
        };
    }

    /// <summary>
    /// A new manifestation pool is minted only by the matching authorized
    /// Activate step. Its source, identity, quantity and owner must match the
    /// exact NewResources payload retained in the immutable event audit.
    /// This is an equality check on a Core-planned command, not a separate
    /// grant authority or a resource spending operation.
    /// </summary>
    private static void PreserveAuditedActivationResources(
        JsonElement previous, JsonElement proposed, string transitionRequestJson)
    {
        var previousResources = ById(previous.GetProperty("resources"), "previous resources");
        var proposedResources = ById(proposed.GetProperty("resources"), "proposed resources");
        var previousInstances = ById(previous.GetProperty("instances"), "previous instances");
        var proposedInstances = ById(proposed.GetProperty("instances"), "proposed instances");

        using var audit = JsonDocument.Parse(transitionRequestJson);
        var steps = audit.RootElement.GetProperty("steps");
        var expectedNewResources = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var step in steps.EnumerateArray())
        {
            var action = step.GetProperty("action");
            var activates = action.ValueKind == JsonValueKind.Number &&
                action.TryGetInt32(out var ordinal) && ordinal == 0 ||
                action.ValueKind == JsonValueKind.String &&
                action.GetString() == "Activate";
            if (!step.TryGetProperty("newResources", out var resources) ||
                resources.ValueKind == JsonValueKind.Null)
                continue;
            if (!activates || resources.ValueKind != JsonValueKind.Array)
                throw new ArgumentException(
                    "Only an audited Activate step can introduce manifestation resources.");
            var target = step.GetProperty("targetInstanceId").GetString();
            if (string.IsNullOrWhiteSpace(target) ||
                previousInstances.ContainsKey(target) ||
                !proposedInstances.ContainsKey(target))
                throw new ArgumentException(
                    "An activation resource must belong to a newly created manifestation.");

            foreach (var resource in resources.EnumerateArray())
            {
                if (resource.ValueKind != JsonValueKind.Object ||
                    !resource.TryGetProperty("id", out var identity) ||
                    identity.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(identity.GetString()) ||
                    !resource.TryGetProperty("ownerKind", out var ownerKind) ||
                    !IsManifestationOwner(ownerKind) ||
                    !resource.TryGetProperty("ownerId", out var owner) ||
                    owner.ValueKind != JsonValueKind.String ||
                    owner.GetString() != target ||
                    !expectedNewResources.TryAdd(identity.GetString()!, resource))
                    throw new ArgumentException(
                        "An audited activation resource has an invalid or duplicate owner.");
            }
        }

        var minted = proposedResources
            .Where(pair => !previousResources.ContainsKey(pair.Key))
            .ToArray();
        if (minted.Length != expectedNewResources.Count ||
            minted.Any(pair =>
                !expectedNewResources.TryGetValue(pair.Key, out var audited) ||
                !JsonElement.DeepEquals(pair.Value, audited)))
            throw new ArgumentException(
                "New manifestation resources must exactly match the ordered activation audit.");
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

    /// <summary>
    /// Only an existing influence's expression and its source-verified
    /// expression receipt may change. Earned progression, acquired identity,
    /// effects, applicability rules, other influences, and resources survive.
    /// The persisted audit must describe the exact change on both sides.
    /// </summary>
    private static void PreserveExpressionChange(string previousJson, string proposedJson,
        string auditJson)
    {
        using var previousDocument = JsonDocument.Parse(previousJson);
        using var proposedDocument = JsonDocument.Parse(proposedJson);
        using var auditDocument = JsonDocument.Parse(auditJson);
        var previous = ById(previousDocument.RootElement, "previous influences");
        var proposed = ById(proposedDocument.RootElement, "proposed influences");
        var audit = auditDocument.RootElement;
        var targetId = audit.GetProperty("influenceId").GetString()!;
        if (previous.Count != proposed.Count || !previous.ContainsKey(targetId))
            throw new ArgumentException("An expression change must retain every influence identity.");

        foreach (var (id, original) in previous)
        {
            if (!proposed.TryGetValue(id, out var updated))
                throw new ArgumentException("An expression change cannot acquire or remove influences.");
            if (id != targetId)
            {
                if (!JsonElement.DeepEquals(original, updated))
                    throw new ArgumentException("An expression change cannot rewrite another influence.");
                continue;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal)
                { "expression", "expressionEvidenceId" };
            var originalFields = original.EnumerateObject().Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal);
            var updatedFields = updated.EnumerateObject().Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!originalFields.SetEquals(updatedFields) ||
                !allowed.IsSubsetOf(originalFields))
                throw new ArgumentException("An expression change cannot alter influence structure.");
            foreach (var field in original.EnumerateObject())
                if (!allowed.Contains(field.Name) &&
                    !JsonElement.DeepEquals(field.Value, updated.GetProperty(field.Name)))
                    throw new ArgumentException("An expression change cannot rewrite earned influence state.");

            var oldExpression = original.GetProperty("expression");
            var newExpression = updated.GetProperty("expression");
            ValidateExpressionDimensions(oldExpression);
            ValidateExpressionDimensions(newExpression);
            var oldEvidence = original.GetProperty("expressionEvidenceId");
            var newEvidence = updated.GetProperty("expressionEvidenceId");
            if (oldEvidence.ValueKind != JsonValueKind.String ||
                newEvidence.ValueKind != JsonValueKind.String ||
                !JsonElement.DeepEquals(oldEvidence,
                    audit.GetProperty("previousExpressionEvidenceId")) ||
                !JsonElement.DeepEquals(newEvidence,
                    audit.GetProperty("nextExpressionEvidenceId")) ||
                !JsonElement.DeepEquals(oldExpression,
                    audit.GetProperty("previousExpression")) ||
                !JsonElement.DeepEquals(newExpression,
                    audit.GetProperty("nextExpression")))
                throw new ArgumentException("Expression audit does not match committed state.");
        }
    }

    internal static void ValidateExpressionDimensions(JsonElement expression)
    {
        if (expression.ValueKind != JsonValueKind.Object ||
            expression.EnumerateObject().Count() > 128)
            throw new ArgumentException("Invalid expression dimensions.");
        var dimensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in expression.EnumerateObject())
            if (!dimensions.Add(field.Name) ||
                string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 160 ||
                field.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(field.Value.GetString()) ||
                field.Value.GetString()!.Length > 1024)
                throw new ArgumentException("Malformed or excessive expression dimension.");
    }

    private static void PreserveAcquiredOwners(string previousJson, string proposedJson,
        string kind, bool allowEarnedEffects = false)
    {
        using var previousDoc = JsonDocument.Parse(previousJson);
        using var nextDoc = JsonDocument.Parse(proposedJson);
        var previous = ById(previousDoc.RootElement, "previous " + kind);
        var next = ById(nextDoc.RootElement, "proposed " + kind);
        foreach (var (id, oldOwner) in previous)
        {
            if (!next.TryGetValue(id, out var candidate))
                throw new ArgumentException("An acquisition can not erase an existing " + kind + " owner.");

            // Only separately earned progression may be appended to an
            // existing owner in an acquisition event. Retain parent/source,
            // overrides, decisions, expression state, source-qualified
            // effects and all future contract fields unchanged. Those fields
            // require their own explicitly authorized transition contract.
            foreach (var field in oldOwner.EnumerateObject())
            {
                if (field.NameEquals("progression") ||
                    allowEarnedEffects && field.NameEquals("effects") ||
                    allowEarnedEffects && field.NameEquals("grantApplicability")) continue;
                if (!candidate.TryGetProperty(field.Name, out var value) ||
                    !JsonElement.DeepEquals(field.Value, value))
                    throw new ArgumentException(
                        "An acquisition can not rewrite existing " + kind + " state.");
            }
            foreach (var field in candidate.EnumerateObject())
            {
                if (field.NameEquals("progression") ||
                    allowEarnedEffects && field.NameEquals("effects") ||
                    allowEarnedEffects && field.NameEquals("grantApplicability")) continue;
                if (!oldOwner.TryGetProperty(field.Name, out var value) ||
                    !JsonElement.DeepEquals(field.Value, value))
                    throw new ArgumentException(
                        "An acquisition can not introduce unverified changes to existing " + kind + " state.");
            }
            PreserveRecords(oldOwner, candidate, "progression", "earned progression");
            if (allowEarnedEffects)
            {
                // Native progression can append newly earned effects and grant
                // use rules. Never allow a verified event to rewrite existing
                // effect bodies or alter previously recorded applicability.
                PreserveRecords(oldOwner, candidate, "effects", "earned effects");
                PreserveArrayPrefix(oldOwner, candidate,
                    "grantApplicability", "grant applicability");
            }
        }
    }

    private static void PreserveArrayPrefix(JsonElement before, JsonElement after,
        string field, string label)
    {
        if (!before.TryGetProperty(field, out var previous) ||
            previous.ValueKind == JsonValueKind.Null) return;
        if (previous.ValueKind != JsonValueKind.Array ||
            !after.TryGetProperty(field, out var current) ||
            current.ValueKind != JsonValueKind.Array ||
            current.GetArrayLength() < previous.GetArrayLength())
            throw new ArgumentException("A verified progression can not remove existing " + label + ".");
        for (var index = 0; index < previous.GetArrayLength(); index++)
            if (!JsonElement.DeepEquals(previous[index], current[index]))
                throw new ArgumentException("A verified progression can not rewrite existing " + label + ".");
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
