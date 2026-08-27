# Set 18 — Enchanted Wilds Recognition Notes

Patch baseline: 18.1 launch (August 2026)

## Critical shop behavior: Wisps

Set 18 Wisps make a naïve "five champion cards" shop model incorrect.

Riot's launch documentation states:

- Wisps appear in every other shop.
- They occupy the far-right shop position.
- They are purchasable only during planning.
- At the end of planning, the Wisp disappears and reveals the unit hidden behind it.
- Wisp categories have distinct colors/icons.
- After Stage 5, every other Wisp is guaranteed to be a Combat Wisp.

Therefore the recognizer must model a shop slot as a typed entry, not merely `ChampionId`.

```text
ShopEntry
  Kind: Champion | Wisp | Unknown
  VisibleChampionId?
  WispCategory?
  WispId?
  UnderlyingChampionId?  // unknown until revealed unless another supported source exists
```

Do not infer the hidden champion behind a visible Wisp from pixels that are not actually available.

## Shop-transforming traits/mechanics

Set 18 also contains mechanics that can alter the normal shop distribution/appearance. Examples from Riot's set overview include:

- Jungle: at a breakpoint, a future shop can be invaded by Jungle monsters.
- Infernal: post-combat shop slots can produce higher-tier champions.

The visual classifier should therefore identify the displayed entity first and let the state/meta layer explain why it appeared; it should not reject a valid card simply because it violates ordinary shop odds.

## Carousel

Carousel returned in 18.1 and may contain more champions and/or higher-cost champions than normal. Acquisition tracking already supports `Carousel` as a source; the carousel detector should be a separate future module rather than being folded into board recognition.

## Opening encounters

Opening encounters can alter starting items/resources. A future state field should record the opening encounter when reliably recognizable because it changes the prior for early-game resource events.

## Unreal transition

18.1 is the first live TFT patch on Unreal. Riot notes that loading times, UI parity, cosmetics, and other elements are still being brought to parity. Layout profiles must be versioned aggressively during the first patches of Set 18.

## Cosmetic availability

Riot's 18.1 notes contain a launch allowlist of arenas, booms, and tacticians that have been ported to Unreal. The cosmetic detector should version its candidate set by client patch so it does not waste work comparing against cosmetics that cannot be present in the current build.
