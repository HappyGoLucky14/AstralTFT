# Data Sources — Research Baseline

## Riot Developer API

Use for the user's authoritative account/ranked/match data where available:

- TFT league/ranked endpoints
- TFT match history
- account/PUUID identity flow

Do not attempt to build a global meta crawler from a personal Riot key.

## Riot Data Dragon

Use as an official static asset/data source for TFT champions/items/traits/arenas/tacticians where available.

Known limitation: Riot states TFT Data Dragon updates are a manual process and may not update immediately after a patch.

## CommunityDragon

Candidate fast-moving static data/asset source. Normalize into our own versioned schema and cross-check against official Riot data rather than binding app logic to raw CommunityDragon naming.

## Aggregate statistical sources

Research-gated. Requirements before an adapter is approved:

- clear permission/terms for our use
- stable endpoint/API or documented data route
- patch/rank/sample metadata
- update frequency
- failure behavior
- no dependence on private/undocumented endpoints that are likely to break

Potential sources may include MetaTFT, tactics.tools or others, but no production dependency should be created until terms/access are validated.

## Source-quality score

Each statistical source record should carry:

- patch exactness
- rank filter
- region filter
- sample size
- last-updated age
- source reliability
- condition specificity

The meta engine combines sources using these dimensions instead of simple arithmetic averaging.
