import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type { HostEnvironment } from "../../host-environment.js";
import { searchResolvedRules } from "../../rules-core-api.js";
import type { RulesInputWorkflow } from "../../core/application/rules-input-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface KnownSpellWorkflow {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): Promise<void>;
    add(characterId: string, conceptKey: string): Promise<void>;
    remove(characterId: string, conceptKey: string): Promise<void>;
}

export function createKnownSpellWorkflow(
    application: CharacterSheetApplication,
    rulesInputs: RulesInputWorkflow,
    environment: HostEnvironment
): KnownSpellWorkflow {
    async function search(query: string): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({
            type: "spell-chooser-load-started",
            query: normalizedQuery
        });
        try {
            const catalog = await searchResolvedRules(
                environment,
                "spell",
                normalizedQuery);
            application.dispatch({
                type: "spell-chooser-loaded",
                query: normalizedQuery,
                results: catalog.rules.filter(rule => rule.entityType === "spell")
            });
        } catch (error) {
            application.dispatch({
                type: "spell-chooser-load-failed",
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        openChooser(): void {
            application.dispatch({ type: "spell-chooser-opened" });
            void search("");
        },

        closeChooser(): void {
            application.dispatch({ type: "spell-chooser-closed" });
        },

        search,

        async add(characterId: string, conceptKey: string): Promise<void> {
            const changed = await rulesInputs.set(characterId, {
                kind: "knownSpell",
                key: conceptKey
            });
            if (changed) {
                application.dispatch({ type: "spell-chooser-closed" });
            }
        },

        async remove(characterId: string, conceptKey: string): Promise<void> {
            await rulesInputs.remove(
                characterId,
                "knownSpell",
                conceptKey);
        }
    };
}
