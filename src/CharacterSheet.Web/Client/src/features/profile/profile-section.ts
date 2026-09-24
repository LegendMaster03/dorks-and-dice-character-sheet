import type { CharacterRoutineUiState } from "../../app-state.js";
import type {
    CharacterProfileInput,
    CharacterProfileResponse
} from "../../character-state-api.js";
import { renderMechanicalValue } from "../../core/mechanics/mechanic-value.js";
import type { CalculatedMechanicalValueView } from "../../ui/character-mechanics.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import type { RoutineCharacterHandlers } from "../../ui/sheet-contracts.js";

type ProfileKey = keyof CharacterProfileInput;

interface ProfileField {
    key: ProfileKey;
    label: string;
    long?: boolean;
    rows?: number;
    maxLength: number;
}

const PROFILE_FIELDS: readonly ProfileField[] = [
    { key: "alignment", label: "Alignment", maxLength: 300 },
    { key: "deity", label: "Custom / Historical Deity", maxLength: 300 },
    { key: "age", label: "Age", maxLength: 300 },
    { key: "height", label: "Height", maxLength: 300 },
    { key: "weight", label: "Weight", maxLength: 300 },
    { key: "appearance", label: "Appearance", long: true, rows: 4, maxLength: 4000 },
    { key: "personalityTraits", label: "Personality Traits", long: true, rows: 4, maxLength: 4000 },
    { key: "ideals", label: "Ideals", long: true, rows: 4, maxLength: 4000 },
    { key: "bonds", label: "Bonds", long: true, rows: 4, maxLength: 4000 },
    { key: "flaws", label: "Flaws", long: true, rows: 4, maxLength: 4000 },
    { key: "alliesAndOrganizations", label: "Allies & Organizations", long: true, rows: 5, maxLength: 20000 },
    { key: "symbol", label: "Symbol", long: true, rows: 3, maxLength: 4000 },
    { key: "backstory", label: "Backstory", long: true, rows: 10, maxLength: 20000 }
];

export function renderProfileSection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers,
    ruleMetadata: readonly CalculatedMechanicalValueView[] | undefined
): HTMLElement {
    const content = createElement("div", "dd-routine-section dd-profile");

    if (routine.status === "idle" || routine.status === "loading") {
        content.append(createInlineState("Loading Character details…", "loading"));
        return content;
    }
    if (routine.status === "error" || routine.state === null) {
        content.append(createInlineState(
            routine.message ?? "Character details are unavailable.",
            "error"));
        return content;
    }

    if (ruleMetadata?.length) {
        const rules = createElement("section", "dd-profile__rules");
        rules.append(createElement("h3", "dd-primary-content__subtitle", "Rules-derived details"));
        const grid = createElement("div", "dd-profile__grid");
        for (const value of ruleMetadata) {
            grid.append(renderMechanicalValue(value, true));
        }
        rules.append(grid);
        content.append(rules);
    }

    const profile = routine.state.profile ?? null;
    const pending = routine.mutation !== null;
    const editable = !readOnly && !routine.state.readOnly;

    if (routine.mutationError !== undefined) {
        content.append(createInlineState(routine.mutationError, "error"));
    }

    content.append(renderCharacterArt(routine, editable, handlers));

    if (editable) {
        content.append(renderProfileForm(
            profile,
            pending,
            routine.mutation?.kind === "profile-update",
            handlers));
        return content;
    }

    content.append(renderProfileSummary(profile));
    return content;
}

function renderCharacterArt(
    routine: CharacterRoutineUiState,
    editable: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const section = createElement("section", "dd-character-art");
    section.append(createElement("h3", "dd-primary-content__subtitle", "Character Art"));

    const assets = routine.state?.artAssets ?? [];
    const pending = routine.mutation?.kind === "art-update" || routine.mutation?.kind === "art-delete";
    const portrait = assets.find(asset => asset.isPortrait);

    if (assets.length === 0) {
        section.append(createElement(
            "p",
            "dd-profile__empty",
            "No Character art has been uploaded."));
    } else {
        const gallery = createElement("div", "dd-character-art__gallery");
        for (const asset of assets) {
            const card = createElement("article", "dd-character-art__asset");
            card.setAttribute("data-art-asset-id", asset.id);

            const link = document.createElement("a");
            link.className = "dd-character-art__image-link";
            link.href = handlers.artContentUrl(asset.id);
            link.target = "_blank";
            link.rel = "noopener";
            link.setAttribute("aria-label", `View ${asset.originalFileName} full size`);

            const image = document.createElement("img");
            image.className = "dd-character-art__image";
            image.src = handlers.artContentUrl(asset.id);
            image.alt = asset.isPortrait
                ? `Character portrait: ${asset.originalFileName}`
                : `Character art: ${asset.originalFileName}`;
            image.loading = "lazy";
            link.append(image);

            card.append(
                link,
                createElement("p", "dd-character-art__name", asset.originalFileName));
            if (asset.isPortrait) {
                card.append(createElement("span", "dd-status-pill", "Portrait"));
            }

            if (editable) {
                const actions = createElement("div", "dd-routine-actions");
                if (!asset.isPortrait) {
                    actions.append(createButton(
                        "Use as Portrait",
                        "dd-button dd-button--secondary",
                        () => handlers.setPortrait(asset.id),
                        pending));
                }
                actions.append(createButton(
                    "Delete",
                    "dd-button dd-button--ghost",
                    () => handlers.deleteArt(asset.id),
                    pending));
                card.append(actions);
            }
            gallery.append(card);
        }
        section.append(gallery);
    }

    if (editable) {
        const controls = createElement("div", "dd-character-art__controls");
        const input = document.createElement("input");
        input.type = "file";
        input.accept = "image/png,image/jpeg,image/webp,image/gif";
        input.setAttribute("aria-label", "Choose Character art image");
        input.disabled = pending;
        const upload = createButton(
            pending ? "Saving…" : "Upload Art",
            "dd-button dd-button--secondary",
            () => {
                const file = input.files?.[0];
                if (file !== undefined) handlers.uploadArt(file);
            },
            pending);
        controls.append(input, upload);
        if (portrait !== undefined) {
            controls.append(createButton(
                "Clear Portrait",
                "dd-button dd-button--ghost",
                () => handlers.clearPortrait(),
                pending));
        }
        section.append(controls);
        section.append(createElement(
            "p",
            "dd-routine-meta",
            "PNG, JPEG, WebP, or GIF. Maximum 8 MiB."));
    }

    return section;
}

function renderProfileForm(
    profile: CharacterProfileResponse | null,
    pending: boolean,
    saving: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const form = createElement("form", "dd-profile__form");
    form.append(createElement(
        "p",
        "dd-routine-meta",
        "These fields are Character-authored details. When Rules Core provides a canonical identity selection, the sheet presents that separately without overwriting authored values."));

    const grid = createElement("div", "dd-profile__grid");
    const controls = new Map<ProfileKey, HTMLInputElement | HTMLTextAreaElement>();

    for (const field of PROFILE_FIELDS) {
        const wrapper = createElement(
            "label",
            field.long ? "dd-profile__field dd-profile__field--wide" : "dd-profile__field");
        wrapper.append(createElement("span", "dd-routine-label", field.label));

        const control = field.long
            ? createElement("textarea", "dd-routine-textarea")
            : createElement("input", "dd-routine-input");
        control.setAttribute("data-profile-field", field.key);
        control.maxLength = field.maxLength;
        control.value = profile?.[field.key] ?? "";
        control.disabled = pending;
        if (control.tagName === "TEXTAREA") {
            (control as HTMLTextAreaElement).rows = field.rows ?? 4;
        }

        controls.set(field.key, control);
        wrapper.append(control);
        grid.append(wrapper);
    }

    const actions = createElement("div", "dd-routine-actions");
    const save = createButton(
        saving ? "Saving…" : "Save Details",
        "dd-button dd-button--primary",
        () => undefined,
        pending);
    save.type = "submit";
    actions.append(save);

    form.append(grid, actions);
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (pending) return;

        const input = Object.fromEntries(
            PROFILE_FIELDS.map(field => [
                field.key,
                optionalText(controls.get(field.key)?.value)
            ])
        ) as unknown as CharacterProfileInput;
        handlers.setProfile(input);
    });
    return form;
}

function renderProfileSummary(profile: CharacterProfileResponse | null): HTMLElement {
    const summary = createElement("div", "dd-profile__summary");
    if (profile === null || PROFILE_FIELDS.every(field => profile[field.key] === null)) {
        summary.append(createElement(
            "p",
            "dd-profile__empty",
            "No Character details have been added."));
        return summary;
    }

    const grid = createElement("div", "dd-profile__grid");
    for (const field of PROFILE_FIELDS) {
        const value = profile[field.key];
        if (value === null) continue;

        const item = createElement(
            "section",
            field.long ? "dd-profile__field dd-profile__field--wide" : "dd-profile__field");
        item.append(
            createElement("h3", "dd-routine-label", field.label),
            createElement("p", "dd-profile__value", value));
        grid.append(item);
    }
    summary.append(grid);
    return summary;
}

function optionalText(value: string | undefined): string | null {
    const normalized = value?.trim() ?? "";
    return normalized.length === 0 ? null : normalized;
}
