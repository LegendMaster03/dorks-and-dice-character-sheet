export interface HostEnvironment {
    embedded: boolean;
    toolBasePath: string;
    toolRoute: string;
    contextUrl: string | null;
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
            contextUrl
        };
    }

    return {
        embedded: false,
        toolBasePath: "",
        toolRoute: locationPathname,
        contextUrl: null
    };
}
