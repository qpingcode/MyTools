export enum HttpStatusCategory {
    Informational = 'informational',
    Success = 'success',
    Redirection = 'redirection',
    ClientError = 'client-error',
    ServerError = 'server-error',
    Unknown = 'unknown',
}

enum HttpStatusClass {
    Informational = 1,
    Success = 2,
    Redirection = 3,
    ClientError = 4,
    ServerError = 5,
}

const StatusClassDivisor = 100;
const Categories: Readonly<Partial<Record<HttpStatusClass, HttpStatusCategory>>> = {
    [HttpStatusClass.Informational]: HttpStatusCategory.Informational,
    [HttpStatusClass.Success]: HttpStatusCategory.Success,
    [HttpStatusClass.Redirection]: HttpStatusCategory.Redirection,
    [HttpStatusClass.ClientError]: HttpStatusCategory.ClientError,
    [HttpStatusClass.ServerError]: HttpStatusCategory.ServerError,
};

export function httpStatusCategory(status: number | undefined): HttpStatusCategory {
    if (!Number.isInteger(status)) return HttpStatusCategory.Unknown;
    return Categories[Math.floor(status! / StatusClassDivisor) as HttpStatusClass]
        ?? HttpStatusCategory.Unknown;
}
