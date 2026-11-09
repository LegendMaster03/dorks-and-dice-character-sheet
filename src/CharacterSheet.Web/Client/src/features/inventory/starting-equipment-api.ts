/**
 * Re-exports the starting-equipment transport boundary for the inventory feature without a
 * separate implementation of grants.
 */
// Keep the feature-level entrypoint for existing inventory consumers.
// The authenticated API transport is owned by the shared client layer.
export * from "../../starting-equipment-api.js";
