# MAYBE: Advanced Linker Add-Ons

## Idea

Offer paid advanced Linker options later, while keeping core local tools useful without AI or an API key.

## Free / Offline

- Rule-based smart collections from local history, favorites, tabs, and collections.
- Move Smart Collection category definitions out of code into local editable rules, preferably SQLite cache tables or an AppData JSON fallback.
- Add user-selected nationalized Smart Collection preset packs, such as United States, United Kingdom, Canada, Australia / New Zealand, India, Europe, Latin America, and Custom.
- Seed each regional pack with local domains, government/service patterns, news sources, and common regional services; do not infer the user's region from browsing traffic.
- Let users edit categories, URL/domain patterns, blocked auth patterns, weights, item limits, and the generated collection prefix.
- Local MCP tools for browser data, navigation, and simple maintenance.
- Deterministic scripts or app actions that do not call an AI provider.

## Paid / API-Key Enabled

- Advanced Linker workflows that use the user's configured API key or a future add-on purchase.
- Create richer scripts or automations on the fly from user intent.
- Generate CLI-style task plans, reusable recipes, and multi-step browser actions.
- Offer premium organization features, such as semantic clustering, summaries, and custom collection-rule suggestions.
- Optionally let AI suggest a nationalized or personal Smart Collection rule pack, but require user approval before saving it as local deterministic rules.

## Product Notes

- Keep the boundary clear: basic usefulness should not depend on AI.
- Treat API-key mode and paid add-ons as optional accelerators.
- Make generated scripts previewable and editable before execution.
- Keep Smart Collections explainable: local history plus favorites, frequency, recency, and user-approved patterns.
