# UGTLive Cursor Rules

This directory contains comprehensive Cursor Rules for the Universal Game Translator Live project. These rules help maintain code consistency, document patterns, and guide development.

## Rule Files

### Core Rules (Always Applied)

1. **code-style-conventions.mdc** (`alwaysApply: true`)
   - Naming conventions (PascalCase, camelCase, UPPER_SNAKE_CASE)
   - Code layout and formatting
   - Singleton pattern implementation
   - Error handling patterns
   - Nullable reference types
   - Async/await naming conventions
   - String handling best practices

2. **async-threading-patterns.mdc** (`alwaysApply: true`)
   - UI thread updates (Dispatcher pattern)
   - Async service methods
   - Background operations
   - HttpClient usage
   - Task cancellation
   - Thread safety
   - Timer usage patterns

### Domain-Specific Rules

3. **architecture-patterns.mdc**
   - Project structure and organization
   - Design patterns (Singleton, Factory, Strategy)
   - Separation of concerns
   - Dependency management
   - Communication patterns
   - Resource management
   - Extension points for new services

4. **translation-workflow.mdc**
   - Translation pipeline overview
   - OCR processing (6 methods: Windows OCR, EasyOCR, MangaOCR, PaddleOCR, DocTR, Google Vision)
   - Universal Block Detection algorithms
   - Translation service interfaces
   - Context management
   - Audio features (TTS, real-time transcription)
   - Performance considerations

5. **ugtlive-project-guide.mdc**
   - Project structure overview
   - Core application files
   - UI components and dialogs
   - OCR and translation services
   - Utilities and managers
   - Key architectural patterns
   - Development guidelines
   - Version management
   - Common development tasks

6. **service-integration-patterns.mdc**
   - Translation service interface
   - HTTP client patterns
   - Error handling in services
   - Service factory pattern
   - OCR service integration (Windows OCR, Google Vision, Python services)
   - Text-to-Speech integration
   - Real-time audio streaming
   - Retry logic and timeouts
   - Service discovery and lifecycle management

7. **configuration-management.mdc**
   - ConfigManager singleton pattern
   - Getter/Setter patterns
   - Configuration file structure
   - Service-specific configuration
   - Window position persistence
   - API key security
   - Configuration validation

8. **wpf-ui-patterns.mdc**
   - Window lifecycle management
   - XAML structure and styling
   - Data binding patterns
   - Event handlers
   - Window positioning
   - Custom controls
   - Transparent windows
   - Text display and scrolling

9. **ui-ux-patterns.mdc**
   - Window architecture and lifecycle
   - Keyboard shortcuts (HotkeyManager)
   - UI design patterns
   - ChatBox customization
   - Settings organization
   - Mouse interaction patterns
   - Window state management
   - Service management UI

10. **debugging-testing.mdc**
    - Logging patterns (LogManager, Console)
    - Error file writing
    - Debug configuration files
    - Exception handling
    - Testing translation services
    - OCR testing
    - Performance debugging
    - Network debugging

## Usage

### For Developers

These rules are automatically applied when editing code in Cursor. The rules with `alwaysApply: true` are always active, while others are contextually applied based on file patterns.

### Key Patterns to Follow

1. **Naming**: Use PascalCase for public members, camelCase for private, underscore prefix for fields
2. **UI Updates**: Always use `Dispatcher.Invoke()` when updating UI from background threads
3. **Configuration**: Access settings through `ConfigManager.Instance`
4. **Error Handling**: Log errors via LogManager, show user-friendly messages via ErrorPopupManager
5. **Async**: Use `async`/`await`, never `.Result` or `.Wait()` in UI code
6. **Services**: Implement `ITranslationService` interface for new translation services
7. **Hotkeys**: Use `HotkeyManager.Instance` for keyboard shortcuts (replaces KeyboardShortcuts)
8. **Block Detection**: Use `UniversalBlockDetector.Instance` for text grouping (replaces BlockDetectionManager)

### Adding New Rules

When adding new rules:
1. Create a `.mdc` file in `.cursor/rules/`
2. Add frontmatter with description, globs, and alwaysApply flag
3. Use markdown format with code examples
4. Reference actual files using `[filename](mdc:path/to/file)` syntax
5. Update this README

### Rule File Format

```markdown
---
description: Brief description of what this rule covers
globs: ["**/*.cs"]  # File patterns this applies to
alwaysApply: false  # Whether to always apply or contextually
---

# Rule Title

Content and examples...
```

## Best Practices

- Keep rules focused on specific domains
- Include code examples from the actual codebase
- Reference actual files and classes
- Update rules when patterns change
- Keep examples up-to-date with current code

## Current Architecture Highlights

### Key Changes from Previous Versions
- **Block Detection**: `BlockDetectionManager` → `UniversalBlockDetector`
- **Hotkeys**: `KeyboardShortcuts` → `HotkeyManager`
- **OCR Services**: Now supports 6 methods (Windows OCR, EasyOCR, MangaOCR, PaddleOCR, DocTR, Google Vision)
- **Translation Services**: Added Google Translate and llama.cpp
- **New Managers**: AudioPlaybackManager, AudioPreloadService, ErrorPopupManager, GamepadManager, WebViewEnvironmentManager

### Supported OCR Methods
1. EasyOCR - Decent at most languages
2. MangaOCR - Vertical Japanese manga
3. PaddleOCR - Multi-language (100+ languages)
4. DocTR - Great at non-Asian languages
5. Windows OCR - Built-in Windows OCR
6. Google Vision - Cloud-based (costs money)

### Supported Translation Services
1. Gemini - Google Gemini API
2. ChatGPT - OpenAI API
3. Ollama - Local LLM
4. Google Translate - Google Translate API
5. llama.cpp - Local llama.cpp server

## Maintenance

- Review rules periodically for accuracy
- Update when architectural patterns change
- Add new rules for new patterns or domains
- Remove obsolete rules
- Keep examples consistent with codebase style
