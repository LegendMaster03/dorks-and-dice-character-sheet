import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type { HostEnvironment } from "../../host-environment.js";
import { loadCharacterPresentation } from "../../character-presentation-api.js";
import { loadNativeStartingEquipmentPreview } from "../../starting-equipment-api.js";
import { requestErrorMessage } from "./request-error.js";

export interface PresentationWorkflow {
    load(characterId: string): Promise<void>;
}

export function createPresentationWorkflow(
    application: CharacterSheetApplication,
    environment: HostEnvironment
): PresentationWorkflow {
    let requestSequence = 0;

    return {
        async load(characterId: string): Promise<void> {
            const requestId = ++requestSequence;
            const current = application.getState();
            const alreadyReceived = current.routine.state?.rulesInputs?.some(input =>
                input.kind === "booleanFact" &&
                input.key === "starting-equipment.received" &&
                input.booleanValue === true) === true;
            // Preview is a Guided Setup operation, not part of every routine
            // mechanics refresh. After equipment is acquired it remains ordinary
            // inventory, and the native source may be edited or removed.
            const withEquipment = current.guidedBuilder.open && !alreadyReceived;
            application.dispatch({
                type: "presentation-load-started", requestId, withEquipment
            });
            const mechanicsRequest = (async () => {
            try {
                const presentation = await loadCharacterPresentation(
                    environment,
                    characterId);
                application.dispatch({
                    type: "presentation-loaded",
                    requestId,
                    presentation
                });
            } catch (error) {
                application.dispatch({
                    type: "presentation-load-failed",
                    requestId,
                    message: requestErrorMessage(error)
                });
            }
            })();
            const equipmentRequest = withEquipment ? (async () => {
                try {
                    const preview = await loadNativeStartingEquipmentPreview(
                        environment, characterId);
                    application.dispatch({
                        type: "starting-equipment-preview-loaded", requestId, preview
                    });
                } catch (error) {
                    application.dispatch({
                        type: "starting-equipment-preview-failed",
                        requestId, message: requestErrorMessage(error)
                    });
                }
            })() : Promise.resolve();
            await Promise.all([mechanicsRequest, equipmentRequest]);
        }
    };
}
