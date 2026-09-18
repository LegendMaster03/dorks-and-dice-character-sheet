export type MechanicalScalar = string | number;

export type ExtensiblePresentationKey<Known extends string> = Known | (string & {});

export interface DisplayFieldView {
    key: string;
    label: string;
    value: string;
}

export interface SourceAttributionView {
    key: string;
    label: string;
    detail?: string;
    officialUrl?: string;
    linkLabel?: string;
}

export interface MechanicalContributionView {
    key: string;
    label: string;
    effectiveValue: MechanicalScalar;
    formattedValue?: string;
    unit?: string;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface RelatedMechanicalValueView {
    key: string;
    label: string;
    effectiveValue: MechanicalScalar;
    formattedValue?: string;
    unit?: string;
}

export interface CalculatedMechanicalValueView {
    key: string;
    label: string;
    effectiveValue: MechanicalScalar;
    formattedValue?: string;
    unit?: string;
    breakdown?: readonly MechanicalContributionView[];
    relatedValues?: readonly RelatedMechanicalValueView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface SavingThrowView extends CalculatedMechanicalValueView {
    governingAbility?: string;
    training?: string;
}

export type DefensePresentationRole = ExtensiblePresentationKey<"primary" | "related" | "other">;

export interface DefenseView extends CalculatedMechanicalValueView {
    role?: DefensePresentationRole;
}

export interface DefenseGroupView {
    primaryKey?: string;
    values: readonly DefenseView[];
}

export type HealthTrackRole = ExtensiblePresentationKey<
    "hit-points" | "temporary-hit-points" | "nonlethal-damage" | "resource"
>;

export interface HealthTrackView {
    key: string;
    label: string;
    role: HealthTrackRole;
    current?: MechanicalScalar;
    maximum?: MechanicalScalar;
    formattedValue?: string;
    detail?: string;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface ArmorCheckPenaltyView {
    applies: boolean;
    formattedEffect?: string;
}

export type CompetencyKind = ExtensiblePresentationKey<"skill" | "tool" | "other">;

export interface CompetencyView extends CalculatedMechanicalValueView {
    kind?: CompetencyKind;
    ranks?: MechanicalScalar;
    governingAbility?: string;
    training?: string;
    classSkill?: boolean;
    trainedOnly?: boolean;
    armorCheckPenalty?: ArmorCheckPenaltyView;
    specialty?: string;
}

export interface CompetencyRelationshipView {
    parentKey: string;
    componentKeys: readonly string[];
}

export type CompetencyPresentationItem =
    | { kind: "standalone"; competency: CompetencyView }
    | {
        kind: "composite";
        parent: CompetencyView;
        components: readonly [CompetencyView, ...CompetencyView[]];
    };

export interface CompetencyCollectionView {
    entries: readonly CompetencyView[];
    relationships?: readonly CompetencyRelationshipView[];
}

export interface ActionAttackView {
    key: string;
    name: string;
    actionType?: string;
    attackOrCheck?: CalculatedMechanicalValueView;
    damage?: string;
    damageType?: string;
    criticalRange?: string;
    criticalMultiplier?: string;
    range?: string;
    reach?: string;
    ammunition?: string;
    target?: string;
    notes?: readonly string[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface ItemOccurrenceMechanicsView {
    occurrenceId: string;
    values?: readonly CalculatedMechanicalValueView[];
    facts?: readonly DisplayFieldView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CarryingLoadView {
    carried?: DisplayFieldView;
    load?: DisplayFieldView;
    thresholds?: readonly DisplayFieldView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CharacterCheckView {
    key: string;
    name: string;
    ability?: DisplayFieldView;
    competencyOrTool?: DisplayFieldView;
    effectiveModifierOrResult?: CalculatedMechanicalValueView;
    target?: DisplayFieldView;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CharacterProcedureView {
    key: string;
    name: string;
    components: readonly CharacterCheckView[];
    result?: DisplayFieldView;
    state?: DisplayFieldView;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface ComponentMaterialView {
    key: string;
    label: string;
    quantity?: string;
    fields?: readonly DisplayFieldView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CraftingProcedureView {
    key: string;
    name: string;
    targetOutput?: DisplayFieldView;
    requiredInputs?: readonly DisplayFieldView[];
    competenciesOrTools?: readonly DisplayFieldView[];
    procedure?: CharacterProcedureView;
    progress?: DisplayFieldView;
    result?: DisplayFieldView;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface InventoryMechanicsView {
    itemOccurrences?: readonly ItemOccurrenceMechanicsView[];
    carrying?: CarryingLoadView;
    components?: readonly ComponentMaterialView[];
    procedures?: readonly CharacterProcedureView[];
    crafting?: readonly CraftingProcedureView[];
}

export interface SpellcastingProfileView {
    key: string;
    label: string;
    castingSource?: string;
    castingAbility?: string;
    saveDc?: CalculatedMechanicalValueView;
    spellAttack?: CalculatedMechanicalValueView;
    resourceSystem?: DisplayFieldView;
    domains?: readonly string[];
    specialtySchool?: string;
    prohibitedSchools?: readonly string[];
    arcaneSpellFailure?: DisplayFieldView;
    bonusSpells?: readonly DisplayFieldView[];
    metadata?: readonly DisplayFieldView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface CharacterMechanicsView {
    abilityValues?: readonly CalculatedMechanicalValueView[];
    savingThrows?: readonly SavingThrowView[];
    defenses?: DefenseGroupView;
    combatFundamentals?: readonly CalculatedMechanicalValueView[];
    healthTracks?: readonly HealthTrackView[];
    competencies?: CompetencyCollectionView;
    actions?: readonly ActionAttackView[];
    movement?: readonly CalculatedMechanicalValueView[];
    inventory?: InventoryMechanicsView;
    checks?: readonly CharacterCheckView[];
    procedures?: readonly CharacterProcedureView[];
    spellcastingProfiles?: readonly SpellcastingProfileView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export function formatMechanicalValue(value: {
    effectiveValue: MechanicalScalar;
    formattedValue?: string;
    unit?: string;
}): string {
    if (value.formattedValue !== undefined && value.formattedValue.trim().length > 0) {
        return value.formattedValue;
    }
    const raw = String(value.effectiveValue);
    return value.unit === undefined || value.unit.trim().length === 0
        ? raw
        : `${raw} ${value.unit}`;
}

export function formatHealthTrack(track: HealthTrackView): string {
    if (track.formattedValue !== undefined && track.formattedValue.trim().length > 0) {
        return track.formattedValue;
    }
    if (track.current !== undefined && track.maximum !== undefined) {
        return `${track.current} / ${track.maximum}`;
    }
    if (track.current !== undefined) return String(track.current);
    if (track.maximum !== undefined) return `Max ${track.maximum}`;
    return "Available";
}

export function hasMechanicalDetails(value: CalculatedMechanicalValueView): boolean {
    return (value.breakdown?.length ?? 0) > 0
        || (value.relatedValues?.length ?? 0) > 0
        || (value.sourceAttributions?.length ?? 0) > 0;
}

export function buildCompetencyPresentation(
    collection: CompetencyCollectionView
): readonly CompetencyPresentationItem[] {
    const byKey = new Map(collection.entries.map(entry => [entry.key, entry] as const));
    const consumed = new Set<string>();
    const groups = new Map<string, CompetencyPresentationItem>();

    for (const relationship of collection.relationships ?? []) {
        if (consumed.has(relationship.parentKey)) continue;
        const parent = byKey.get(relationship.parentKey);
        if (parent === undefined) continue;

        const componentKeys = [...new Set(relationship.componentKeys)]
            .filter(key => key !== relationship.parentKey && !consumed.has(key));
        const components = componentKeys
            .map(key => byKey.get(key))
            .filter((entry): entry is CompetencyView => entry !== undefined);
        if (components.length === 0) continue;

        consumed.add(parent.key);
        for (const component of components) consumed.add(component.key);
        groups.set(parent.key, {
            kind: "composite",
            parent,
            components: components as [CompetencyView, ...CompetencyView[]]
        });
    }

    const result: CompetencyPresentationItem[] = [];
    for (const entry of collection.entries) {
        const group = groups.get(entry.key);
        if (group !== undefined) {
            result.push(group);
            continue;
        }
        if (!consumed.has(entry.key)) {
            result.push({ kind: "standalone", competency: entry });
        }
    }
    return result;
}

export function findAbilityValue(
    values: readonly CalculatedMechanicalValueView[] | undefined,
    abilityKey: string
): CalculatedMechanicalValueView | undefined {
    return values?.find(value => value.key === abilityKey);
}

export function findItemOccurrenceMechanics(
    mechanics: InventoryMechanicsView | undefined,
    occurrenceId: string
): ItemOccurrenceMechanicsView | undefined {
    return mechanics?.itemOccurrences?.find(value => value.occurrenceId === occurrenceId);
}
