import {
    clearCharacterBuildChoice,
    setCharacterAdvancementLevel,
    setCharacterBuildChoice,
    type CharacterBuilderChoice
} from "../../builder-api.js";
import {
    filterSubclassesForClass,
    filterSubspeciesForSpecies,
    getStartingClassEntry,
    getStoredChoiceConceptKey
} from "../../builder-rules.js";
import {
    loadCharacterState,
    setCharacterRulesInput,
    type CharacterStateResponse
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import {
    resolveRuleConcept,
    searchResolvedRules,
    type ResolvedRuleCatalogItem
} from "../../rules-core-api.js";
import type { BuildStateWorkflow } from "../../core/application/build-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import {
    applyCharacterAdvancement,
    previewCharacterAdvancement,
    type CharacterAdvancementPlan,
    type CharacterClassAdvancementRequest
} from "./advancement-api.js";

export type AdvancementCandidateTarget = "class" | "subclass";

export type AdvancementCandidateChooserState =
    | { kind: "closed" }
    | {
        kind: "open";
        target: AdvancementCandidateTarget;
        query: string;
        status: "idle" | "loading" | "ready" | "error";
        results: ResolvedRuleCatalogItem[];
        message?: string;
    };

export interface AdvancementWorkflowState {
    status: "idle" | "previewing" | "ready" | "applying" | "error";
    request: CharacterClassAdvancementRequest | null;
    plan: CharacterAdvancementPlan | null;
    chooser: AdvancementCandidateChooserState;
    message?: string;
}

export interface AdvancementWorkflow {
    openChooser(target: CharacterBuilderChoice): void;
    closeChooser(): void;
    search(target: CharacterBuilderChoice, query: string): Promise<void>;
    save(
        characterId: string,
        target: CharacterBuilderChoice,
        conceptKey: string
    ): Promise<void>;
    clear(characterId: string, target: CharacterBuilderChoice): Promise<void>;
    setLevel(characterId: string, occurrenceId: string, level: number): Promise<void>;

    current(): Readonly<AdvancementWorkflowState>;
    previewExisting(characterId: string, occurrenceId: string): Promise<void>;
    openCandidateChooser(target: AdvancementCandidateTarget): void;
    closeCandidateChooser(): void;
    searchCandidates(target: AdvancementCandidateTarget, query: string): Promise<void>;
    selectCandidate(
        characterId: string,
        target: AdvancementCandidateTarget,
        conceptKey: string
    ): Promise<void>;
    reviewHitPointGain(characterId: string, hitDieValue: number): Promise<void>;
    resolveChoice(characterId: string, choiceKey: string, value: string): Promise<void>;
    apply(characterId: string): Promise<void>;
    cancel(): void;
}

let activeAdvancementWorkflow: AdvancementWorkflow | null = null;

export function getActiveAdvancementWorkflow(): AdvancementWorkflow | null {
    return activeAdvancementWorkflow;
}

export function createAdvancementWorkflow(
    application: CharacterSheetApplication,
    buildState: BuildStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): AdvancementWorkflow {
    let workflowState: AdvancementWorkflowState = createInitialWorkflowState();

    function setWorkflowState(next: AdvancementWorkflowState): void {
        workflowState = next;
        application.dispatch({ type: "rerender" });
    }

    async function search(
        target: CharacterBuilderChoice,
        query: string
    ): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({
            type: "chooser-load-started",
            target,
            query: normalizedQuery
        });
        try {
            const entityType = target === "species"
                ? "species"
                : target === "subspecies"
                    ? "subspecies"
                    : target === "background"
                        ? "background"
                        : target === "deity"
                            ? "deity"
                            : target === "startingClass"
                                ? "class"
                                : "subclass";
            const catalog = await searchResolvedRules(
                environment,
                entityType,
                normalizedQuery);
            let results = catalog.rules.filter(
                rule => rule.entityType === entityType);
            if (target === "subspecies") {
                const build = buildState.current().build;
                const speciesConceptKey = build === null
                    ? null
                    : getStoredChoiceConceptKey(build, "species");
                if (speciesConceptKey === null) {
                    throw new Error("Choose a Species before selecting a Subspecies.");
                }
                results = filterSubspeciesForSpecies(results, speciesConceptKey);
            } else if (target === "subclass") {
                const build = buildState.current().build;
                const startingClass = build === null
                    ? null
                    : getStartingClassEntry(build);
                if (startingClass === null) {
                    throw new Error(
                        "Choose a Class before selecting a Subclass.");
                }
                results = filterSubclassesForClass(
                    results,
                    startingClass.ruleConceptKey);
            }
            application.dispatch({
                type: "chooser-loaded",
                target,
                query: normalizedQuery,
                results
            });
        } catch (error) {
            application.dispatch({
                type: "chooser-load-failed",
                target,
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    async function mutate(
        characterId: string,
        target: CharacterBuilderChoice,
        operation: () => ReturnType<typeof setCharacterBuildChoice>
    ): Promise<void> {
        application.dispatch({ type: "selection-save-started", target });
        try {
            const build = await operation();
            application.dispatch({ type: "selection-saved", build });
            await buildState.resolveReferences(build);
            await presentation.load(characterId);
        } catch (error) {
            application.dispatch({
                type: "selection-save-failed",
                message: requestErrorMessage(error)
            });
        }
    }

    async function preview(
        characterId: string,
        request: CharacterClassAdvancementRequest
    ): Promise<void> {
        setWorkflowState({
            ...workflowState,
            status: "previewing",
            request,
            chooser: { kind: "closed" },
            message: undefined
        });
        try {
            const plan = await previewCharacterAdvancement(
                environment,
                characterId,
                request);
            setWorkflowState({
                status: "ready",
                request,
                plan,
                chooser: { kind: "closed" }
            });
        } catch (error) {
            setWorkflowState({
                ...workflowState,
                status: "error",
                request,
                chooser: { kind: "closed" },
                message: requestErrorMessage(error)
            });
        }
    }

    function classConceptKeyForCurrentRequest(): string | null {
        if (workflowState.plan?.classConceptKey) {
            return workflowState.plan.classConceptKey;
        }
        if (workflowState.request?.classConceptKey) {
            return workflowState.request.classConceptKey;
        }
        const occurrenceId = workflowState.request?.classAdvancementEntryId;
        const build = buildState.current().build;
        if (occurrenceId === undefined || occurrenceId === null || build === null) {
            return null;
        }
        return build.progressionEntries.find(value => value.id === occurrenceId)?.ruleConceptKey ?? null;
    }

    async function searchCandidates(
        target: AdvancementCandidateTarget,
        query: string
    ): Promise<void> {
        const normalizedQuery = query.trim();
        setWorkflowState({
            ...workflowState,
            chooser: {
                kind: "open",
                target,
                query: normalizedQuery,
                status: "loading",
                results: []
            },
            message: undefined
        });
        try {
            const entityType = target === "class" ? "class" : "subclass";
            const catalog = await searchResolvedRules(environment, entityType, normalizedQuery);
            let results = catalog.rules.filter(rule => rule.entityType === entityType);
            if (target === "class") {
                const ownedClasses = new Set(
                    (buildState.current().build?.progressionEntries ?? [])
                        .filter(value => value.kind === "class")
                        .map(value => value.ruleConceptKey));
                results = results.filter(rule => !ownedClasses.has(rule.conceptKey));
            } else {
                const classConceptKey = classConceptKeyForCurrentRequest();
                if (classConceptKey === null) {
                    throw new Error("Preview a Class advancement before choosing a Subclass.");
                }
                results = filterSubclassesForClass(results, classConceptKey);
            }
            setWorkflowState({
                ...workflowState,
                chooser: {
                    kind: "open",
                    target,
                    query: normalizedQuery,
                    status: "ready",
                    results
                }
            });
        } catch (error) {
            setWorkflowState({
                ...workflowState,
                chooser: {
                    kind: "open",
                    target,
                    query: normalizedQuery,
                    status: "error",
                    results: [],
                    message: requestErrorMessage(error)
                }
            });
        }
    }

    async function resolveRoutineReferences(state: CharacterStateResponse): Promise<void> {
        const targets = [
            ...state.inventoryItemOccurrences
                .filter(value => value.ruleConceptKey !== null)
                .map(value => ({ id: value.id, conceptKey: value.ruleConceptKey! })),
            ...(state.rulesInputs ?? [])
                .filter(value => value.kind === "knownSpell")
                .map(value => ({ id: value.id, conceptKey: value.key })),
            ...(state.conditions ?? [])
                .filter(value => value.ruleConceptKey !== null)
                .map(value => ({ id: value.id, conceptKey: value.ruleConceptKey! }))
        ];

        await Promise.all(targets.map(async target => {
            try {
                const rule = await resolveRuleConcept(environment, target.conceptKey);
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: target.id,
                    conceptKey: target.conceptKey,
                    reference: rule === null
                        ? { status: "unavailable", conceptKey: target.conceptKey }
                        : { status: "resolved", conceptKey: target.conceptKey, rule }
                });
            } catch (error) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: target.id,
                    conceptKey: target.conceptKey,
                    reference: {
                        status: "error",
                        conceptKey: target.conceptKey,
                        message: requestErrorMessage(error)
                    }
                });
            }
        }));
    }

    async function refreshRoutine(characterId: string): Promise<void> {
        application.dispatch({ type: "routine-load-started" });
        try {
            const state = await loadCharacterState(environment, characterId);
            application.dispatch({ type: "routine-loaded", state });
            await resolveRoutineReferences(state);
        } catch (error) {
            application.dispatch({
                type: "routine-load-failed",
                message: requestErrorMessage(error)
            });
        }
    }

    const workflow: AdvancementWorkflow = {
        openChooser(target: CharacterBuilderChoice): void {
            application.dispatch({ type: "chooser-opened", target });
            void search(target, "");
        },

        closeChooser(): void {
            application.dispatch({ type: "chooser-closed" });
        },

        search,

        async save(characterId, target, conceptKey): Promise<void> {
            const classId = target === "subclass"
                ? buildState.currentStartingClassId()
                : undefined;
            await mutate(
                characterId,
                target,
                () => setCharacterBuildChoice(
                    environment,
                    characterId,
                    target,
                    conceptKey,
                    classId));
        },

        async clear(characterId, target): Promise<void> {
            const classId = target === "subclass"
                ? buildState.currentStartingClassId()
                : undefined;
            await mutate(
                characterId,
                target,
                () => clearCharacterBuildChoice(
                    environment,
                    characterId,
                    target,
                    classId));
        },

        async setLevel(characterId, occurrenceId, level): Promise<void> {
            application.dispatch({
                type: "advancement-level-save-started",
                occurrenceId
            });
            try {
                const build = await setCharacterAdvancementLevel(
                    environment,
                    characterId,
                    occurrenceId,
                    level);
                application.dispatch({ type: "advancement-level-saved", build });
                await buildState.resolveReferences(build);
                await presentation.load(characterId);
            } catch (error) {
                application.dispatch({
                    type: "advancement-level-save-failed",
                    occurrenceId,
                    message: requestErrorMessage(error)
                });
            }
        },

        current(): Readonly<AdvancementWorkflowState> {
            return workflowState;
        },

        async previewExisting(characterId, occurrenceId): Promise<void> {
            await preview(characterId, {
                classAdvancementEntryId: occurrenceId
            });
        },

        openCandidateChooser(target): void {
            setWorkflowState({
                ...workflowState,
                chooser: {
                    kind: "open",
                    target,
                    query: "",
                    status: "idle",
                    results: []
                },
                message: undefined
            });
            void searchCandidates(target, "");
        },

        closeCandidateChooser(): void {
            setWorkflowState({
                ...workflowState,
                chooser: { kind: "closed" }
            });
        },

        searchCandidates,

        async selectCandidate(characterId, target, conceptKey): Promise<void> {
            if (target === "class") {
                await preview(characterId, { classConceptKey: conceptKey });
                return;
            }
            if (workflowState.request === null) {
                setWorkflowState({
                    ...workflowState,
                    status: "error",
                    chooser: { kind: "closed" },
                    message: "Preview a Class advancement before choosing a Subclass."
                });
                return;
            }
            await preview(characterId, {
                ...workflowState.request,
                subclassConceptKey: conceptKey
            });
        },

        async reviewHitPointGain(characterId, hitDieValue): Promise<void> {
            if (workflowState.request === null) return;
            await preview(characterId, {
                ...workflowState.request,
                hitDieValue
            });
        },

        async resolveChoice(characterId, choiceKey, value): Promise<void> {
            if (workflowState.request === null) return;
            application.dispatch({
                type: "routine-mutation-started",
                kind: "rules-input-update",
                entryId: `choice:${choiceKey}`
            });
            try {
                const state = await setCharacterRulesInput(
                    environment,
                    characterId,
                    {
                        kind: "choice",
                        key: choiceKey,
                        textValue: value
                    });
                application.dispatch({ type: "routine-mutation-succeeded", state });
                await resolveRoutineReferences(state);
                await preview(characterId, workflowState.request);
            } catch (error) {
                const message = requestErrorMessage(error);
                application.dispatch({ type: "routine-mutation-failed", message });
                setWorkflowState({
                    ...workflowState,
                    status: "error",
                    message
                });
            }
        },

        async apply(characterId): Promise<void> {
            if (workflowState.request === null
                || workflowState.plan === null
                || !workflowState.plan.canApply) {
                return;
            }
            const request = workflowState.request;
            setWorkflowState({
                ...workflowState,
                status: "applying",
                chooser: { kind: "closed" },
                message: undefined
            });
            try {
                const result = await applyCharacterAdvancement(
                    environment,
                    characterId,
                    request);
                workflowState = {
                    status: "ready",
                    request: null,
                    plan: result.plan,
                    chooser: { kind: "closed" }
                };
                application.dispatch({ type: "builder-loaded", build: result.build });
                await Promise.all([
                    buildState.resolveReferences(result.build),
                    refreshRoutine(characterId),
                    presentation.load(characterId)
                ]);
                application.dispatch({ type: "rerender" });
            } catch (error) {
                setWorkflowState({
                    ...workflowState,
                    status: "error",
                    request,
                    message: requestErrorMessage(error)
                });
            }
        },

        cancel(): void {
            setWorkflowState(createInitialWorkflowState());
        }
    };

    activeAdvancementWorkflow = workflow;
    return workflow;
}

function createInitialWorkflowState(): AdvancementWorkflowState {
    return {
        status: "idle",
        request: null,
        plan: null,
        chooser: { kind: "closed" }
    };
}
