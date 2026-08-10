# LinkScape Activation Protocols

Other apps can open a LinkScape tab workspace by sending an active-tabs JSON package through the registered `link2scape` protocol or the live MCP navigation tool.

## Active Tabs Package

```json
{
  "version": 1,
  "source": "calling-app-name",
  "mode": "replace",
  "selectedTabId": "api",
  "selectedIndex": 1,
  "saveState": true,
  "collectionName": "Research",
  "tabs": [
    {
      "id": "docs",
      "title": "Docs",
      "url": "https://example.com/docs",
      "selected": false,
      "isFavorite": false,
      "isHomeTab": false,
      "visitedCount": 3,
      "order": 0,
      "scrollX": 0,
      "scrollY": 120,
      "isSleeping": false
    },
    {
      "id": "api",
      "title": "API",
      "url": "https://example.com/api",
      "selected": true,
      "order": 1
    }
  ]
}
```

Required field: `tabs[].url`.

Useful fields:

- `mode`: `replace` makes the package the active tab set. `append` adds the package to existing tabs.
- `saveState`: `true` persists the resulting active tabs as the normal startup state. `false` opens them without updating saved startup tabs.
- `collectionName`: optional. When present, the package URLs are also saved into that named collection.
- Selected tab priority: `selectedTabId`, then the first tab with `selected: true`, then `selectedIndex`, then the first tab.

## Protocol Calls

URL-encoded JSON:

```text
link2scape://open?tabs=%7B%22tabs%22%3A%5B%7B%22url%22%3A%22https%3A%2F%2Fexample.com%22%7D%5D%7D
```

Base64url JSON:

```text
link2scape://tabs/active?payload=eyJ0YWJzIjpbeyJ1cmwiOiJodHRwczovL2V4YW1wbGUuY29tIn1dfQ
```

Existing routes remain available:

- `link2scape://open?url=https%3A%2F%2Fexample.com`
- `link2scape://navigate/saved-tabs`
- `link2scape://navigate/collections`
- `link2scape://collection/{collectionId}`

## MCP Tool Call

Use `browser.tabs.openPackage` with `packageJson`:

```json
{
  "packageJson": "{\"mode\":\"replace\",\"tabs\":[{\"url\":\"https://example.com\"}]}"
}
```
