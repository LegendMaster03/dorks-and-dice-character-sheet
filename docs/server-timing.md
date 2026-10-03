# Character Sheet Server Timing

Character Sheet uses the standard HTTP `Server-Timing` response header for low-cardinality request diagnostics.

## Metrics

- `character-sheet` — whole Character Sheet request time until response headers are committed.
- `character-sheet-auth` — Tool Host authentication/introspection time when a hosted request presents Tool Host authentication headers.
- `character-sheet-rules-core` — elapsed time for one delegated Rules Core dependency call, including delegation lookup, transport, response-header wait, and response deserialization.

`character-sheet-rules-core` uses its `desc` field to identify a low-cardinality operation and outcome. Examples include:

```text
character-sheet-rules-core;desc="character-mechanics:ok";dur=18.2
character-sheet-rules-core;desc="character-mechanics:http-403";dur=4.1
character-sheet-rules-core;desc="character-mechanics:delegation-unavailable";dur=0.1
character-sheet-rules-core;desc="rule-resolution:unavailable-404";dur=3.0
```

The operation portion is one of the stable dependency categories owned by Character Sheet, such as `rule-resolution`, `character-mechanics`, `advancement-eligibility`, `support`, `recovery`, or `crafting`. HTTP failure outcomes retain only the numeric status code; request paths, Character IDs, Campaign IDs, concept keys, credentials, and hostnames are not exposed.

Character Sheet may propagate downstream `rules-core` and `rules-core-*` metrics returned by Rules Core. It does not propagate nested `platform-*` metrics from the internal dependency call. Site appends the outer platform timing independently when it proxies Character Sheet to the browser.

Repeated Rules Core calls may therefore produce repeated `character-sheet-rules-core` and downstream `rules-core-*` entries. Those component timings can overlap and must not be summed blindly.
