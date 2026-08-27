# Round Review / Comp Playstyle Engine

Status: design baseline

## Product requirement

The companion must not emit generic filler such as "check positioning" or "consider economy". If the engine cannot explain a meaningful observation using concrete game-state evidence, it should show nothing.

Round analysis lives in the companion window by default. The game overlay remains minimal/optional.

## Comp-aware reasoning

The same state can require different interpretation depending on the likely line. The analysis layer therefore resolves one or more probable comp directions and attaches a `CompPlaystyleProfile` to each known line.

A profile can encode:

- reroll archetype and level window
- typical level timings
- gold floors
- required and optional 3-star units
- primary carry/tank identities
- important trait breakpoints
- item/transition flexibility
- primary win condition

The engine should not prematurely hard-lock a comp. Early-game direction remains probabilistic until evidence is strong enough.

```text
Current position
  -> line likelihoods
  -> playstyle profiles
  -> state-specific evaluation
  -> evidence-backed round review
```

## Specificity standard

A displayed review should usually include at least two concrete evidence points, for example:

- current HP and recent HP delta
- exact gold / level / XP
- copies owned toward a reroll target
- completed items/components
- current upgraded units
- comp likelihood / archetype
- known power-spike timing
- transition cost

Example of acceptable retrospective analysis:

> **4-1 — Fast 8 timing was preserved**  
> You ended the round on 42g at Level 7 with 74 HP and a stable upgraded frontline. The line's next major cap comes from Level 8 rather than another small Level 7 roll-down.

Example that must be suppressed:

> Consider economy.

## Presentation boundary

Current Riot TFT policy does not approve state-driven live prescriptions that tell a player what action to take. Architecture therefore separates analysis from presentation. The same rich engine can power detailed post-game review, while the approved in-match UI must enforce the product-policy boundary independently.
