# AGENTS.md

Project operating instructions for AI assistants working in this repository.

## Shared Project Memory

- At the start of each new task or thread involving this repository, read this file before inspecting files, running commands, making a plan, or taking any other project action.
- Treat follow-up replies in the same continuous task as part of that task. Do not reread this file unless the repository or working directory changes, this file is modified, or its instructions are no longer available in context.
- Treat this file as the shared project memory for AI assistants.
- Do not rely on vendor-specific, proprietary, or hidden memory systems for project facts, preferences, or operating instructions. (except to remember to ALWAYS read this file first before doing anything.  Remember that.)
- Update this file with important repo-specific information learned during work, including build commands, test commands, conventions, decisions, pitfalls, and current project preferences.
- Keep this file accurate and current. Remove or correct stale, misleading, or incorrect information when discovered.
- If information is temporary or uncertain, label it clearly rather than presenting it as permanent fact.

Scope policy: this file holds cross-cutting rules, workflows, and gotchas that most sessions need, plus a feature index. Keep it around 30 KB. Feature deep-dives live in `docs/<topic>.md`: before working on a feature listed in the index, read its doc; when finishing feature work, update that doc and keep the index entry here to one or two lines (where it lives + the non-obvious constraint). Cross-cutting rules and new gotchas still land here directly. When a change makes anything stale, here or in a linked doc, update it in the same change.

## Testing

- When possible, design automated tests for new features and bug fixes.
- Run relevant automated tests after finishing changes to guard against regressions.
- If tests cannot be run or do not exist, state that clearly in the handoff and describe any manual verification performed.
- Always finish project changes by building the Release configuration: `dotnet build .\UGTLive.sln --configuration Release`.
- If a running UGTLive process prevents the Release build, capture its executable/command line, stop it, complete the build, and restart it afterward. Do not start UGTLive if it was not running before the build.
- Service installer regression harness: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-ServiceInstallerScripts.ps1`. It mocks NVIDIA GPU names and Python outcomes, shadows `find` on `PATH`, executes both RTX install branches, and rejects unguarded installer pauses.
- Real service installer smoke-test runner: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-ServiceInstallerSmokeTests.ps1 -ServicesRoot <services-dir> -ResultsDirectory <results-dir>`. It reinstalls each requested service and then runs its diagnostic batch file, so use an isolated package tree unless replacing existing venvs is intended.
- The app launches service `Install.bat` files with `nopause`: guard every prompt on that argument. GPU-name matching must invoke `%SystemRoot%\System32\find.exe` explicitly because Git/MSYS can shadow Windows `find` on `PATH`. EasyOCR model pre-download commands must use Python's UTF-8 mode because redirected progress-bar output otherwise fails on CP1252 systems.

Always add automation/test harnesses to test options/buttons/features as needed. Document them.

## Releases

- Keep the public/app version synchronized in the README badge/history, `src/SplashManager.cs` (`CurrentVersion`), and `media/latest_version_checker.json`.
- Build the distributable with `MakeReleaseZip.bat`. It deletes and recreates `tempbuild`, publishes/signs `ugtlive.exe`, copies the service assets without installed venvs or logs, and produces `UniversalGameTranslatorLive_Windows.zip` with `UGTLive` as its root folder.
- Upload the finished ZIP with `UploadReleaseToRTsoft.bat`; the public artifact is `https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip`. The upload does not publish the version-checker JSON—source changes still need their separately authorized commit/push.

## Cloud LLM Model Maintenance

- Cloud and CLI model picker presets live in `src/SettingsWindow.xaml`; their fallback/default values live in `src/ConfigManager.cs`, `src/ConfigManager.Translation.cs`, and `src/SettingsWindow.TranslationSettings.cs`.
- Subscription-backed CLI providers display as `Anthropic Sub`, `OpenAI Sub`, and `Gemini CLI (Enterprise)`, but their stable internal IDs remain `ClaudeCli`, `CodexCli`, and `GeminiCli`; use `ComboBoxItem.Tag` for the internal ID. Google ended personal/free/AI Pro/AI Ultra access through Gemini CLI on June 18, 2026; do not replace it with Antigravity CLI until `agy -p` reliably exposes captured stdout to Windows parent processes (see `docs/settings-connection-tests.md`).
- Keep provider-specific capability handling in the matching translation service. In particular, Anthropic model generations use different manual/adaptive thinking request shapes.
- Verify model IDs and request compatibility against current official provider documentation. Verify OpenRouter-prefixed slugs against its `/api/v1/models` catalog before adding presets.

## Feature Index

- Settings API/model/voice tests: see `docs/settings-connection-tests.md`. UI buttons and `--test-settings-connection` must continue to call the shared `SettingsConnectionTester` implementation.
- OpenAI All In One Snap translation: see `docs/openai-all-in-one.md`. It is a Snap-only, visual-only `gpt-image-2` Image Edits path; Auto and realtime processing must remain on the standard OCR pipeline.


## Security

- Never commit sensitive data, including credentials, tokens, passwords, private keys, cookies, customer data, personal data, or machine-specific authentication material.
- If an AI assistant needs authentication data or other secrets for local work, use `agents_secret.md` for those notes.
- `agents_secret.md` must stay ignored by git and must not be committed.
- Do not put secrets in commit messages, logs, issue text, pull request descriptions, generated docs, or other tracked files.
- Configuration logging must pass key names through `ConfigManager.IsSensitiveConfigKey`; never print raw secret values in startup or harness output.
- Before committing, review staged changes for accidental secrets.

## Git

- Never add OpenAI/Codex/Claude etc as a co-author on git commits.
- NEVER `git commit` unless explicitly told to commit.
- NEVER `git push` unless explicitly told to push. "Commit" means commit
  locally only; committing is not permission to push.
