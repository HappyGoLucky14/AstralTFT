# Meta / Patch / Trend Engine

## Source model

AstralTFT should not make one public website a single point of failure. Every provider is represented by a replaceable `IDataSourceAdapter` with an explicit dataset capability, expected refresh cadence, scope, sample size and quality score.

Primary categories:

- Riot APIs: player rank/LP and match history
- Riot Data Dragon: official static TFT assets/data, with known manual-update lag
- CommunityDragon: fast static/client-data fallback
- aggregate statistical providers: composition/item/augment/unit population data where access and terms permit

## New-patch transition

The user preference is to begin adapting within a couple hours of a patch while avoiding day-one noise. `PatchBlendPolicy` therefore uses previous-patch values as priors, with authority moving quickly toward current-patch data as:

- hours since patch launch increase
- new-patch sample size matures
- source quality improves

Major set launches automatically reduce the usefulness of previous-set priors.

These constants must be backtested and tuned rather than treated as permanent truth.

## Trend detection

The user values identifying rising lines before public tier lists fully catch up. Trend detection is therefore a core feature, but it must be confidence-gated.

Signals combine:

- recency weighting
- sample-size maturity
- source quality
- magnitude/direction of change

A tiny high-roll sample should not become a "breaking meta" signal. A line with sustained improving placement/top-4/win/usage metrics across a large fresh sample should surface as an emerging trend.
