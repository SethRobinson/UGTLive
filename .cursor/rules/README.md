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

### Domain-Specific Rules

2. **ui-ux-patterns.mdc**
   - Window architecture and lifecycle
   - Keyboard shortcuts
   - UI design patterns
   - ChatBox customization
   - Settings organization
   - Mouse interaction patterns
   - Window state management

3. **translation-workflow.mdc**
   - Translation pipeline overview
   - OCR processing (Windows OCR, EasyOCR)
   - Block detection algorithms
   - Translation service interfaces
   - Context management
   - Audio features (TTS, real-time transcription)
   - Performance considerations

4. **ugtlive-project-guide.mdc**
   - Project structure overview
   - Core application files
   - Key architectural patterns
   - Development guidelines
   - Version management
   - Common development tasks

5. **async-threading-patterns.mdc**
   - UI thread updates (Dispatcher pattern)
   - Async service methods
   - Background operations
   - HttpClient usage
   - Task cancellation
   - Thread safety
   - Timer usage patterns

6. **configuration-management.mdc**
   - ConfigManager singleton pattern
   - Getter/Setter patterns
   - Configuration file structure
   - Service-specific configuration
   - Window position persistence
   - API key security
   - Configuration validation

7. **service-integration-patterns.mdc**
   - Translation service interface
   - HTTP client patterns
   - Error handling in services
   - Service factory pattern
   - OCR service integration
   - Text-to-Speech integration
   - Real-time audio streaming
   - Retry logic and timeouts

8. **wpf-ui-patterns.mdc**
   - Window lifecycle management
   - XAML structure and styling
   - Data binding patterns
   - Event handlers
   - Window positioning
   - Custom controls
   - Transparent windows
   - Text display and scrolling

9. **debugging-testing.mdc**
   - Logging patterns (LogManager, Console)
   - Error file writing
   - Debug configuration files
   - Exception handling
   - Testing translation services
   - OCR testing
   - Performance debugging
   - Network debugging

10. **architecture-patterns.mdc**
    - Project structure
    - Design patterns (Singleton, Factory, Strategy)
    - Separation of concerns
    - Dependency management
    - Communication patterns
    - Resource management
    - State management
    - Extension points

## Usage

### For Developers

These rules are automatically applied when editing code in Cursor. The rules with `alwaysApply: true` are always active, while others are contextually applied based on file patterns.

### Key Patterns to Follow

1. **Naming**: Use PascalCase for public members, camelCase for private, underscore prefix for fields
2. **UI Updates**: Always use `Dispatcher.Invoke()` when updating UI from background threads
3. **Configuration**: Access settings through `ConfigManager.Instance`
4. **Error Handling**: Log errors, return null instead of throwing when possible
5. **Async**: Use `async`/`await`, never `.Result` or `.Wait()` in UI code
6. **Services**: Implement `ITranslationService` interface for new translation services

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

## Maintenance

- Review rules periodically for accuracy
- Update when architectural patterns change
- Add new rules for new patterns or domains
- Remove obsolete rules
- Keep examples consistent with codebase style

