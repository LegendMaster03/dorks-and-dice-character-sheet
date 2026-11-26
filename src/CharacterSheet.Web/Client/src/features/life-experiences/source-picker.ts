import type { CharacterBuilderUiState } from "../../app-state.js";
import { getStoredChoiceConceptKey } from "../../builder-rules.js";
import type { HostEnvironment } from "../../host-environment.js";
import { loadNativeRuleset } from "../../rules-core-native-api.js";
import { createButton, createElement, createInlineState, createSectionCard } from "../../ui/components.js";
import { hasPendingBuildMutation } from "../../ui/sheet-model.js";
import { loadExperienceTitleCatalog } from "./api.js";
import type { ExperienceCatalog, ExperienceKind } from "./model.js";

export function createExperienceSourcePicker(environment: HostEnvironment, notify: () => void, kind: ExperienceKind) {
    const title = kind === "upbringing" ? "Upbringing" : "Background";
    const article = kind === "upbringing" ? "an" : "a";
    let catalog: ExperienceCatalog | null = null;
    let loading = false;
    let failed = false;
    let pending: { characterId: string; id: string } | undefined;

    async function load(): Promise<void> {
        if (loading) return;
        loading = true;
        failed = false;
        notify();
        try {
            const metadata = await loadNativeRuleset(environment);
            catalog = await loadExperienceTitleCatalog(environment, metadata, undefined, [kind]);
            failed = !catalog.hierarchyComplete;
        } catch {
            failed = true;
        } finally {
            loading = false;
            notify();
        }
    }

    const label = (id: string) => catalog?.labels[id];
    function render(characterId: string, builder: Readonly<CharacterBuilderUiState>,
        handlers: { save(id: string): void; clear(): void }): HTMLElement {
        const card = createSectionCard(title, "dd-build");
        const saved = builder.build ? getStoredChoiceConceptKey(builder.build, kind) : null;
        const disabled = builder.build?.readOnly !== false || hasPendingBuildMutation(builder);
        if (pending?.characterId !== characterId || pending.id === saved) pending = undefined;
        card.append(createElement("p", undefined, saved ? label(saved) ?? saved : "Not selected"));

        if (saved) {
            card.append(createButton(`Clear ${title}`, "dd-button dd-button--ghost", () => {
                pending = undefined;
                handlers.clear();
            }, disabled));
        }

        if (failed) {
            card.append(createInlineState(`${title} options are unavailable.`, "warning"),
                createButton(`Retry ${title} options`, "dd-button dd-button--secondary", () => void load()));
            return card;
        }
        if (catalog === null || loading) {
            if (!loading) queueMicrotask(() => { if (!loading && !failed && !catalog) void load(); });
            card.append(createInlineState(`Loading ${title} options…`, "loading"));
            return card;
        }

        const options = catalog.nodes.filter(node => node.selectable && node.kind === kind);
        if (!options.length) {
            card.append(createInlineState(`The active ruleset has no selectable ${title} entries.`, "neutral"));
            return card;
        }

        const field = createElement("label", undefined, `Choose ${article} ${title} `);
        const select = createElement("select");
        select.disabled = disabled;
        const empty = createElement("option", undefined, `Select ${article} ${title}`);
        empty.value = "";
        select.append(empty);
        for (const entry of options) {
            const option = createElement("option", undefined, entry.label);
            option.value = entry.id;
            select.append(option);
        }
        select.value = pending?.id ?? (saved && options.some(entry => entry.id === saved) ? saved : "");
        field.append(select);

        const action = createButton(`Save ${title}`, "dd-button dd-button--secondary", () => {
            if (select.value && select.value !== saved) handlers.save(select.value);
        }, disabled || select.value === "" || select.value === saved);
        const description = createElement("p");
        function updateSelection(): void {
            action.disabled = disabled || select.value === "" || select.value === saved;
            const entry = options.find(option => option.id === select.value);
            description.textContent = entry?.detail.description ??
                (entry?.parentId ? "Inherits from " + (label(entry.parentId) ?? entry.parentId) + "." : "");
            description.hidden = !description.textContent;
        }
        select.addEventListener("change", () => {
            pending = select.value ? { characterId, id: select.value } : undefined;
            updateSelection();
        });
        updateSelection();
        card.append(field, action, description);
        if (builder.saving === kind)
            card.append(createInlineState(`Saving ${title}…`, "loading"));
        return card;
    }

    return { render, label };
}
