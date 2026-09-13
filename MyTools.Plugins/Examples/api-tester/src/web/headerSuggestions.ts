import { ContentType } from '../shared/model.js';

const MediaTypeAny = '*/*';
const MediaTypeHtml = 'text/html';
const MediaTypeXml = 'application/xml';
const MediaTypeText = 'text/plain';
const EncodingGzip = 'gzip';
const EncodingDeflate = 'deflate';
const EncodingBrotli = 'br';
const EncodingIdentity = 'identity';
const CacheNoCache = 'no-cache';
const CacheNoStore = 'no-store';
const CacheMaxAgeZero = 'max-age=0';
const ConnectionKeepAlive = 'keep-alive';
const ConnectionClose = 'close';
const AuthorizationBearerPrefix = 'Bearer ';
const AuthorizationBasicPrefix = 'Basic ';
const XmlHttpRequest = 'XMLHttpRequest';
const LanguageEnglish = 'en-US';
const LanguageChinese = 'zh-CN';

export const CommonRequestHeaderNames = [
  'Accept',
  'Accept-Encoding',
  'Accept-Language',
  'Authorization',
  'Cache-Control',
  'Connection',
  'Content-Type',
  'Cookie',
  'If-Match',
  'If-Modified-Since',
  'If-None-Match',
  'Origin',
  'Range',
  'Referer',
  'User-Agent',
  'X-API-Key',
  'X-CSRF-Token',
  'X-Request-ID',
  'X-Requested-With',
] as const;

const HeaderValueSuggestions: Record<string, readonly string[]> = {
  accept: [ContentType.Json, MediaTypeXml, MediaTypeText, MediaTypeHtml, MediaTypeAny],
  'accept-encoding': [EncodingGzip, EncodingDeflate, EncodingBrotli, EncodingIdentity],
  'accept-language': [LanguageEnglish, LanguageChinese],
  authorization: [AuthorizationBearerPrefix, AuthorizationBasicPrefix],
  'cache-control': [CacheNoCache, CacheNoStore, CacheMaxAgeZero],
  connection: [ConnectionKeepAlive, ConnectionClose],
  'content-type': [
    ContentType.Json,
    ContentType.Form,
    ContentType.Multipart,
    MediaTypeText,
    MediaTypeXml,
    MediaTypeHtml,
    ContentType.Binary,
  ],
  'x-requested-with': [XmlHttpRequest],
};

export function headerValueSuggestions(name: string): readonly string[] {
  return HeaderValueSuggestions[name.trim().toLowerCase()] || [];
}
