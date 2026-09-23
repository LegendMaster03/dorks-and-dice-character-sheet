import type { CharacterRecoveryRequestInput } from "../../character-state-api.js";
import { resolveCharacterRecovery } from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface RecoveryWorkflow {
    begin(characterId: string, procedureKey: string): Promise<void>;
    continue(characterId: string, input: CharacterRecoveryRequestInput): Promise<void>;
    cancel(): void;
}

export function createRecoveryWorkflow(
    application: CharacterSheetApplication,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): RecoveryWorkflow {
    async function resolve(
        characterId: string,
        procedureKey: string,
        request: CharacterRecoveryRequestInput
    ): Promise<void> {
        const routine = application.getState().routine;
        if (routine.status !== "ready"
            || routine.state === null
            || routine.state.readOnly
            || routine.mutation !== null
            || routine.recovery.kind === "resolving") {
            return;
        }

        application.dispatch({
            type: "recovery-started",
            procedureKey,
            request
        });
        try {
            const response = await resolveCharacterRecovery(
                environment,
                characterId,
                procedureKey,
                request);
            if (response.resolution.status === "resolved") {
                application.dispatch({
                    type: "recovery-succeeded",
                    state: response.state
                });
                await presentation.load(characterId);
                return;
            }

            application.dispatch({
                type: "recovery-continuation",
                procedureKey,
                request,
                resolution: response.resolution
            });
        } catch (error) {
            application.dispatch({
                type: "recovery-failed",
                procedureKey,
                request,
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        async begin(characterId: string, procedureKey: string): Promise<void> {
            await resolve(characterId, procedureKey, {});
        },

        async continue(characterId: string, input: CharacterRecoveryRequestInput): Promise<void> {
            const recovery = application.getState().routine.recovery;
            if (recovery.kind !== "continuation" && recovery.kind !== "error") return;
            await resolve(
                characterId,
                recovery.procedureKey,
                mergeRecoveryRequest(recovery.request, input));
        },

        cancel(): void {
            application.dispatch({ type: "recovery-cancelled" });
        }
    };
}

function mergeRecoveryRequest(
    current: CharacterRecoveryRequestInput,
    next: CharacterRecoveryRequestInput
): CharacterRecoveryRequestInput {
    return {
        integerInputs: mergeRecord(current.integerInputs, next.integerInputs),
        booleanInputs: mergeRecord(current.booleanInputs, next.booleanInputs),
        stringInputs: mergeRecord(current.stringInputs, next.stringInputs),
        choices: mergeRecord(current.choices, next.choices),
        rolls: mergeRecord(current.rolls, next.rolls)
    };
}

function mergeRecord<T>(
    current: Record<string, T> | undefined,
    next: Record<string, T> | undefined
): Record<string, T> | undefined {
    if (current === undefined && next === undefined) return undefined;
    return { ...(current ?? {}), ...(next ?? {}) };
}
