# Copilot Instructions

## General Guidelines
- Use bilingual Chinese-English comments in code and explanations by default.

## Code Style
- Follow specific formatting rules.
- Maintain consistent naming conventions.

## Project-Specific Rules
- Keep SimpleAlarm plugin's subscription-based demonstration; do not modify its subscription logic.
- Prefer using `Initialize(IPluginContext)` as the canonical plugin initialization method; constructor injection can be optional or deprecated but not required.