import {tryFormatSource, type LanguageId} from '@qping/content-formatter';

interface FormatRequest {
    id: number;
    source: string;
    language?: LanguageId;
}

interface FormatResponse {
    id: number;
    formatted: string;
}

self.addEventListener('message', (event: MessageEvent<FormatRequest>) => {
    const request = event.data;
    void tryFormatSource(request.source, request.language).then(formatted => {
        const response: FormatResponse = {id: request.id, formatted};
        self.postMessage(response);
    }).catch(() => {
        const response: FormatResponse = {id: request.id, formatted: request.source};
        self.postMessage(response);
    });
});
