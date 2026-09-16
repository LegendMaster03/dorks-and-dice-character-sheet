import type { CharacterBuildResponse, CharacterBuilderChoice } from "./builder-api.js";
import type { FetchLike } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";
import {
    resolveRuleConcept,
    type ResolvedRuleDetail
} from "./rules-core-api.js";

export type RuleReferenceState =
    | { status: "none" }
    | { status: "loading"; conceptKey: string }
    | { status: "resolved"; conceptKey: string; rule: ResolvedRuleDetail }
    | { status: "unavailable"; conceptKey: string }
    | { status: "error"; conceptKey: string; message: string };

export function getStoredChoiceConceptKey(
    build: CharacterBuildResponse,
    choice: CharacterBuilderChoice
): string | null {
    if (choice === "raceSpecies") {
        return build.foundationalSelections.find(value => value.category === "raceSpecies")?.ruleConceptKey ?? null;
    }

    return build.progressionEntries.find(value =>
        value.kind === "class"
        && value.ordinal === 0
        && value.parentAdvancementEntryId === null)?.ruleConceptKey ?? null;
}

export function loadingRuleReference(
    build: CharacterBuildResponse,
    choice: CharacterBuilderChoice
): RuleReferenceState {
    const conceptKey = getStoredChoiceConceptKey(build, choice);
    return conceptKey === null
        ? { status: "none" }
        : { status: "loading", conceptKey };
}

export async function resolveStoredChoice(
    environment: HostEnvironment,
    build: CharacterBuildResponse,
    choice: CharacterBuilderChoice,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<RuleReferenceState> {
    const conceptKey = getStoredChoiceConceptKey(build, choice);
    if (conceptKey === null) {
        return { status: "none" };
    }

    try {
        const rule = await resolveRuleConcept(environment, conceptKey, fetcher);
        const expectedEntityType = choice === "raceSpecies" ? "race" : "class";
        if (rule === null || rule.entityType !== expectedEntityType || rule.conceptKey !== conceptKey) {
            return { status: "unavailable", conceptKey };
        }

        return { status: "resolved", conceptKey, rule };
    } catch (error) {
        return {
            status: "error",
            conceptKey,
            message: error instanceof Error ? error.message : "Rules Core rule resolution failed."
        };
    }
}
