import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type { HostEnvironment } from "../../host-environment.js";
import { loadCharacterPresentation } from "../../character-presentation-api.js";
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
            application.dispatch({ type: "presentation-load-started", requestId });
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
        }
    };
}
