import { ref } from 'vue';
import { bus, text } from './i18n.js';
import { ErrorKind, type Failure } from '../shared/model.js';

export const notification = ref<Failure | string>('');

export function errorText(error: Failure): string {
  const captions: Record<ErrorKind, () => string> = {
    [ErrorKind.Configuration]: text.ConfigurationError,
    [ErrorKind.Variable]: text.VariableError,
    [ErrorKind.File]: text.FileError,
    [ErrorKind.Dns]: text.DnsError,
    [ErrorKind.Connection]: text.ConnectionError,
    [ErrorKind.Tls]: text.TlsError,
    [ErrorKind.Timeout]: text.TimeoutError,
    [ErrorKind.Cancelled]: text.CancelledError,
    [ErrorKind.Decode]: text.DecodeError,
    [ErrorKind.BodyLimit]: text.BodyLimitError,
    [ErrorKind.RedirectLimit]: text.RedirectLimitError,
    [ErrorKind.Extraction]: text.ExtractionError,
    [ErrorKind.Storage]: text.StorageError,
    [ErrorKind.Cache]: text.CacheError,
  };
  return (
    (captions[error.kind] || text.TransportError)() +
    (error.field ? '\n' + error.field : '') +
    (error.detail ? '\n' + error.detail : '')
  );
}

export async function rpc<T>(route: string, payload?: unknown): Promise<T> {
  let response: { ok: boolean; value: T; error: Failure };
  try {
    response = await bus.call(route, payload);
  } catch (error) {
    notification.value = text.TransportError();
    throw error;
  }
  if (!response.ok) {
    notification.value = response.error;
    throw new Error(response.error.kind);
  }
  return response.value;
}
