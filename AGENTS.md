# Repository Guidelines

## Project Overview

- This is a C# WinForms Game Boy / Game Boy Color emulator targeting `net5.0-windows`.
- Keep changes focused on emulator behavior, UI wiring, or build configuration as requested.
- Avoid broad refactors unless they directly support the task.

## Code Style

- Follow the existing C# style: file-scoped organization by class, braces on new lines, and explicit access modifiers where already used.
- Prefer clear descriptive names over abbreviations, especially for emulator state and hardware concepts.
- Preserve existing public APIs and serialized designer structure unless the task requires changing them.
- Do not add copyright or license headers.

## WinForms Notes

- Treat `*.Designer.cs` and `*.resx` files as generated UI artifacts.
- Prefer editing the main form logic files manually; only change designer files when UI layout or control declarations must change.
- Keep UI event handlers lightweight and place emulator logic in the relevant emulator, CPU, memory, video, or sound classes.

## Build And Validation

- Use `dotnet build ZarthGB.sln` for a compile check when practical.
- For behavior changes, prefer the narrowest relevant manual or automated validation available.
- Do not introduce new test frameworks or formatting tools unless explicitly requested.

## Repository Hygiene

- Do not commit changes unless explicitly asked.
- Do not modify ROM files or generated build outputs under `bin/` or `obj/`.
- Keep patches minimal and avoid unrelated cleanup.
