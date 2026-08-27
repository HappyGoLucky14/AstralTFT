# Riot TFT Policy Boundary

This is an engineering constraint, not an optional UI preference.

As of the current Riot TFT developer policy (August 2026), unapproved uses include:

- dynamic real-time information
- apps that dictate player decisions
- recommendations based on the player's current game state
- opponent champion/board tracking during gameplay

Riot explicitly encourages pre-game best practices and post-game analysis, and permits static recommendations that were available prior to the game.

## Architecture consequence

Keep these layers separate:

1. **Capture/state engine** — reconstructs the player's own game for diagnostics/history.
2. **Analysis engine** — can deeply score recorded states for post-game coaching.
3. **Live presentation policy** — filters what can be shown during active gameplay.

Do not couple a detector directly to a prescriptive overlay action.

## Registration

Riot states products serving players should be registered even if they do not use official documented APIs. Before public distribution, the product flow should be shown to Riot via an approved/acknowledged developer product process.
