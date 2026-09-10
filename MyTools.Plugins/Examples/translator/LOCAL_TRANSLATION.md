# Local translation resources

The Translator plugin installs its optional local engine from the GitHub Release tag
`translator-local-v1`. Large binaries must be Release assets and must not be committed
to Git.

Prepare this directory outside source control:

```text
local-package/
  llama/
    llama-server.exe
    (all DLLs required by that llama.cpp build)
  models/
    Hy-MT2-1.8B-Q4_K_M.gguf
  dictionaries/
    ecdict.db
```

`ecdict.db` must contain the ECDICT `stardict` table and the `word`, `phonetic`,
`definition`, and `translation` columns. Add an index on `word COLLATE NOCASE` before
publishing it.

Generate flattened Release assets and a checksummed manifest:

```powershell
npm run prepare-local-release
gh release create translator-local-v1 --repo qpingcode/MyTools --draft `
  --title "Translator Local Resources v1" `
  --notes "HY-MT2-1.8B Q4_K_M, ECDICT and llama.cpp runtime"

Get-ChildItem -LiteralPath .\release-upload -File | ForEach-Object {
  gh release upload translator-local-v1 $_.FullName --repo qpingcode/MyTools --clobber
  if ($LASTEXITCODE -ne 0) { throw "Failed to upload $($_.Name)" }
}

gh release edit translator-local-v1 --repo qpingcode/MyTools --draft=false
```

The generated manifest points each flattened Release asset back to its installation
path. The plugin downloads into `%APPDATA%/MyTools.Desktop/pluginsData/translator/local`,
supports HTTP range resume, and validates every SHA-256 before installation.

Before publishing, preserve and review the licenses and notices for Hy-MT2, llama.cpp,
ECDICT, and any redistributed runtime libraries.

For development, the manifest URL can be overridden with
`MYTOOLS_TRANSLATOR_LOCAL_MANIFEST_URL`. Existing resources can also be supplied with
`MYTOOLS_TRANSLATOR_LLAMA_SERVER_PATH`, `MYTOOLS_TRANSLATOR_HYMT_MODEL_PATH`, and
`MYTOOLS_TRANSLATOR_ECDICT_PATH`, or an already running server can be selected with
`MYTOOLS_TRANSLATOR_LLAMA_SERVER_URL`.
