# @qping/content-formatter

Shared content-language detection and Prettier-based formatting used by MyTools plugins.

```ts
import {detectLanguage, formatSource, tryFormatSource} from '@qping/content-formatter';

const language = detectLanguage(source);
const formatted = language ? await formatSource(source, language) : source;
const safeResult = await tryFormatSource(source);
```

`tryFormatSource` preserves the original source when the language cannot be detected or formatting fails.
