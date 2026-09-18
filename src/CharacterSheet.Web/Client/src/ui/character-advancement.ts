import type { DisplayFieldView, MechanicalScalar, SourceAttributionView } from "./character-mechanics.js";

export interface AdvancementProgressionView {
    label: string;
    value: MechanicalScalar;
    formattedValue?: string;
}

export interface AdvancementGrantView {
    key: string;
    label: string;
    detail?: string;
    conceptKey?: string;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface AdvancementOccurrenceView {
    occurrenceId: string;
    conceptKey: string;
    kind: string;
    kindLabel?: string;
    displayName: string;
    progression?: AdvancementProgressionView;
    parentOccurrenceId?: string;
    progressionDetails?: readonly DisplayFieldView[];
    grantedFeaturesOrMechanics?: readonly AdvancementGrantView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CharacterAdvancementView {
    occurrences: readonly AdvancementOccurrenceView[];
}

export interface AdvancementPresentationItem {
    occurrence: AdvancementOccurrenceView;
    parent?: AdvancementOccurrenceView;
    unresolvedParent: boolean;
}

export interface CompactAdvancementSummary {
    value: string;
    detail?: string;
    overflowCount: number;
}

export function formatAdvancementProgression(progression: AdvancementProgressionView): string {
    if (progression.formattedValue !== undefined && progression.formattedValue.trim().length > 0) {
        return progression.formattedValue;
    }
    return `${progression.label} ${progression.value}`;
}

export function formatAdvancementOccurrence(occurrence: AdvancementOccurrenceView): string {
    if (occurrence.progression === undefined) return occurrence.displayName;
    return `${occurrence.displayName} · ${formatAdvancementProgression(occurrence.progression)}`;
}

export function advancementKindLabel(occurrence: AdvancementOccurrenceView): string {
    return occurrence.kindLabel?.trim() || occurrence.kind;
}

export function buildAdvancementPresentation(
    advancement: CharacterAdvancementView
): readonly AdvancementPresentationItem[] {
    const byOccurrenceId = new Map(
        advancement.occurrences.map(occurrence => [occurrence.occurrenceId, occurrence] as const)
    );
    return advancement.occurrences.map(occurrence => {
        const parent = occurrence.parentOccurrenceId === undefined
            ? undefined
            : byOccurrenceId.get(occurrence.parentOccurrenceId);
        return {
            occurrence,
            parent,
            unresolvedParent: occurrence.parentOccurrenceId !== undefined && parent === undefined
        };
    });
}

export function createCompactAdvancementSummary(
    advancement: CharacterAdvancementView,
    maxRootGroups = 2
): CompactAdvancementSummary {
    if (advancement.occurrences.length === 0) {
        return { value: "No advancement", overflowCount: 0 };
    }

    const items = buildAdvancementPresentation(advancement);
    const childrenByParent = new Map<string, AdvancementOccurrenceView[]>();
    for (const item of items) {
        if (item.parent === undefined) continue;
        const children = childrenByParent.get(item.parent.occurrenceId) ?? [];
        children.push(item.occurrence);
        childrenByParent.set(item.parent.occurrenceId, children);
    }

    const roots = items.filter(item => item.parent === undefined).map(item => item.occurrence);
    const groups = roots.map(root => {
        const children = childrenByParent.get(root.occurrenceId) ?? [];
        return [formatAdvancementOccurrence(root), ...children.map(formatAdvancementOccurrence)].join(" / ");
    });

    const limit = Math.max(1, maxRootGroups);
    const visible = groups.slice(0, limit);
    const overflowCount = Math.max(0, groups.length - visible.length);
    const detailParts = visible.slice(1);
    if (overflowCount > 0) {
        detailParts.push(`+${overflowCount} more advancement ${overflowCount === 1 ? "track" : "tracks"}`);
    }
    return {
        value: visible[0] ?? formatAdvancementOccurrence(advancement.occurrences[0]),
        detail: detailParts.length === 0 ? undefined : detailParts.join(" • "),
        overflowCount
    };
}
