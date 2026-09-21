export function requestErrorMessage(error: unknown): string {
    return error instanceof Error
        ? error.message
        : "Character Sheet request failed.";
}
