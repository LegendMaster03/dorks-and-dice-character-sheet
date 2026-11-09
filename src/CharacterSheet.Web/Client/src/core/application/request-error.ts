/**
 * Normalizes rejected requests into readable status messages without interpreting the failure as
 * a game rule.
 */
export function requestErrorMessage(error: unknown): string {
    return error instanceof Error
        ? error.message
        : "Character Sheet request failed.";
}
