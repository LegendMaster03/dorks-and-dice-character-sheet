import type {
    CharacterAdvancementEntryResponse,
    CharacterBuildResponse,
    CharacterBuilderChoice
} from "./builder-api.js";
import type { FetchLike } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";
import { loadNativeRuleset, searchNativeOptions, type NativeOptionSummary } from "./rules-core-native-api.js";
import type { ResolvedRuleCatalogItem } from "./rules-core-api.js";

export type RuleReferenceState =
    | { status: "none" }
    | { status: "loading"; conceptKey: string }
    | { status: "resolved"; conceptKey: string; rule: NativeOptionSummary }
    | { status: "unavailable"; conceptKey: string }
    | { status: "error"; conceptKey: string; message: string };

export function getStartingClassEntry(
    build: CharacterBuildResponse
): CharacterAdvancementEntryResponse | null {
    return build.progressionEntries.find(value =>
        value.kind === "class"
        && value.ordinal === 0
        && value.parentAdvancementEntryId === null) ?? null;
}

export function getStoredSubclassEntry(
    build: CharacterBuildResponse
): CharacterAdvancementEntryResponse | null {
    const startingClass = getStartingClassEntry(build);
    if (startingClass === null) {
        return null;
    }

    return build.progressionEntries.find(value =>
        value.kind === "subclass"
        && value.parentAdvancementEntryId === startingClass.id) ?? null;
}

export function getStoredChoiceConceptKey(
    build: CharacterBuildResponse,
    choice: CharacterBuilderChoice
): string | null {
    if (choice === "species" || choice === "subspecies") {
        return build.foundationalSelections.find(value => value.category === choice)?.ruleConceptKey ?? null;
    }
    if (choice === "background" || choice === "deity") {
        return build.foundationalSelections.find(value => value.category === choice)?.ruleConceptKey ?? null;
    }
    if (choice === "startingClass") {
        return getStartingClassEntry(build)?.ruleConceptKey ?? null;
    }
    return getStoredSubclassEntry(build)?.ruleConceptKey ?? null;
}

export function filterSubspeciesForSpecies(
    rules: readonly ResolvedRuleCatalogItem[],
    speciesConceptKey: string
): ResolvedRuleCatalogItem[] {
    const normalizedSpeciesKey = speciesConceptKey.trim();
    if (normalizedSpeciesKey.length === 0) {
        return [];
    }

    return rules.filter(rule =>
        rule.entityType === "subspecies"
        && (rule.relationships ?? []).some(relationship =>
            relationship.kind === "parent-species"
            && relationship.relatedEntityType === "species"
            && relationship.relatedConceptKey === normalizedSpeciesKey));
}

export function filterSubclassesForClass(
    rules: readonly ResolvedRuleCatalogItem[],
    classConceptKey: string
): ResolvedRuleCatalogItem[] {
    const normalizedClassKey = classConceptKey.trim();
    if (normalizedClassKey.length === 0) {
        return [];
    }

    return rules.filter(rule =>
        rule.entityType === "subclass"
        && (rule.relationships ?? []).some(relationship =>
            relationship.kind === "parent-class"
            && relationship.relatedEntityType === "class"
            && relationship.relatedConceptKey === normalizedClassKey));
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
    fetcher: FetchLike = globalThis.fetch.bind(globalThis)
): Promise<RuleReferenceState> {
    const conceptKey = getStoredChoiceConceptKey(build, choice);
    if (conceptKey === null) return { status: "none" };

    // The current native catalog exposes standalone class and subclass
    // summaries. Ancestry/background/deity records are not available here.
    const category = choice === "startingClass" ? "class"
        : choice === "subclass" ? "subclass" : null;
    if (category === null || !conceptKey.startsWith(category + ".")) {
        return { status: "unavailable", conceptKey };
    }
    try {
        const metadata = await loadNativeRuleset(environment, fetcher);
        const found = await searchNativeOptions(
            environment, metadata, category, conceptKey, 100, 0, fetcher);
        const rule = found.options.find(option =>
            option.id === conceptKey && option.kind === category);
        return rule === undefined
            ? { status: "unavailable", conceptKey }
            : { status: "resolved", conceptKey, rule };
    } catch (error) {
        return {
            status: "error", conceptKey,
            message: error instanceof Error ? error.message
                : "Native Rules Core option lookup failed."
        };
    }
}
