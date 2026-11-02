export interface HostEnvironment {
    embedded: boolean;
    toolBasePath: string;
    toolRoute: string;
    contextUrl: string | null;
    standaloneDevelopment: boolean;
}

export function resolveHostEnvironment(root: HTMLElement, locationPathname: string): HostEnvironment {
    const hostedRoute = root.dataset.toolRoute;
    const hostedBasePath = root.dataset.toolBasePath;
    const contextUrl = root.dataset.toolContextUrl ?? null;

    if (hostedRoute !== undefined && hostedBasePath !== undefined) {
        return {
            embedded: true,
            toolBasePath: hostedBasePath,
            toolRoute: hostedRoute,
            contextUrl,
            standaloneDevelopment: false
        };
    }

    const standaloneDevelopment = root.dataset.standaloneDevelopment === "true";
    return {
        embedded: false,
        toolBasePath: "",
        toolRoute: locationPathname,
        contextUrl: null,
        standaloneDevelopment
    };
}
