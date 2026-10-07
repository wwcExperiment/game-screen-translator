# game-screen-translator

Capture a screen region (game window, galgame, manga, documents, …) and send image to LLM, translate the on-screen text and display **in place** — via any OpenAI-compatible model (LM Studio, Ollama, …) or a remote API.

It watches the chosen window/region, detects when the text on screen changes, and sends only the changed frame to the model. The translation is rendered as an overlay at the original position (or in a side panel).

## Features

- **Screen capture & translate** — grab a region or window and translate the on-screen text in place.
- **Anti-jitter** — re-translates only when the text actually changes, ignoring noise and animations so it doesn't hammer the model.
- **Window lock** — lock onto a specific window/region and keep translating whatever appears there.
- **Overlay display** — render the translation over the original text position (or in a side panel).
- **Short-term context** — a rolling window of recent turns keeps names, terms and wording consistent.
- **Story summary** — optional per-app archive that summarizes the plot as you play.
<img width="1775" height="970" alt="image" src="https://github.com/user-attachments/assets/d0f6126f-76a6-450f-88a5-6fa11cba5241" />

## How it works

The core loop is **change → capture → translate → display**:

1. **Change detection** — watch the chosen window/region and detect when the on-screen text actually changes (each frame is downscaled to a grayscale signature and compared).
2. **Capture** — grab the changed frame.
3. **Translate** — send the frame to the LLM via an OpenAI-compatible `/v1/chat/completions` request. **Your model should support vision**
4. **Display** — render the returned translation as an overlay at the original text position (or in a side panel).

Supporting details:

- **Debounce** — a short wait before triggering a translation, so mid-transition text isn't sent.
- **Backoff** — when the screen keeps changing (animations), the wait before forcing a translation grows up to a ceiling instead of hammering the model.
- **Model auto-discovery** — available models are listed from the server's `/v1/models` at startup.
- **Rolling context + compression** — the last few turns ride along with each request; older turns are compressed into a short summary (and, optionally, a per-app story archive).

## Requirements

- Windows + [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (WinForms).
- A running OpenAI-compatible server (e.g. [LM Studio](https://lmstudio.ai/) or [Ollama](https://ollama.com/)) or a remote API.

## Build

```bash
dotnet build ScreenTranslator/ScreenTranslator.csproj -c Release
```

The executable lands in `ScreenTranslator/bin/Release/net10.0-windows/`.

Run the tests:

```bash
dotnet test ScreenTranslator.Tests/ScreenTranslator.Tests.csproj
```

## Configure

Copy the config template next to the executable (or wherever you launch it from):

```bash
cp config.json ScreenTranslator/bin/Release/net10.0-windows/config.json
```

Then edit `config.json`. If `EndpointUrl` is left empty, the app shows a dialog on startup and exits — fill it in first:

| Key | Meaning |
|-----|---------|
| `EndpointUrl` | OpenAI-compatible chat completions URL, e.g. `http://127.0.0.1:1234/v1/chat/completions` (LM Studio), `http://127.0.0.1:11434/v1/chat/completions` (Ollama), `https://api.openai.com/v1/chat/completions`, or `https://api.deepseek.com/chat/completions` (DeepSeek) |
| `ApiKey` | Bearer API key for remote endpoints; leave `""` for local servers (LM Studio / Ollama) |
| `Prompt` / `SummaryPrompt` / `StoryPrompt` / `SummaryLengthPrompt` / `SummaryPrefix` / `UserTurnMarker` | prompts & markers (all editable); `{lang}` in `Prompt` is replaced by `TargetLanguage` (default `中文`) |

Everything else — model & summary model (auto-discovered from `/v1/models`), overlay, pause behaviour, capture tuning, and the summary/story toggles — is configurable in the UI.

The app reads `config.json` next to the executable by default, or from a path passed as the first command-line argument.

## Notes

- `logs/` (runtime logs) and `memory/` (story archives) are written next to the executable and are git-ignored.
- `ARCHITECTURE.md` (Chinese) is the design overview, aimed at AI-assisted development.
