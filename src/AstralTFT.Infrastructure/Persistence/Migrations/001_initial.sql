PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS games (
    game_id TEXT PRIMARY KEY,
    started_utc TEXT NOT NULL,
    ended_utc TEXT NULL,
    patch TEXT NULL,
    set_id TEXT NULL,
    queue_type TEXT NULL,
    placement INTEGER NULL,
    rank_before TEXT NULL,
    lp_before INTEGER NULL,
    rank_after TEXT NULL,
    lp_after INTEGER NULL
);

CREATE TABLE IF NOT EXISTS game_events (
    game_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    occurred_utc TEXT NOT NULL,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    PRIMARY KEY (game_id, sequence),
    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_game_events_time
ON game_events(game_id, occurred_utc);

CREATE TABLE IF NOT EXISTS state_checkpoints (
    game_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    occurred_utc TEXT NOT NULL,
    state_json TEXT NOT NULL,
    PRIMARY KEY (game_id, sequence),
    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS recognition_failures (
    failure_id TEXT PRIMARY KEY,
    game_id TEXT NULL,
    occurred_utc TEXT NOT NULL,
    detector_id TEXT NOT NULL,
    region_id TEXT NULL,
    confidence REAL NULL,
    reason TEXT NULL,
    image_path TEXT NULL,
    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS data_snapshots (
    snapshot_id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    patch TEXT NOT NULL,
    rank_filter TEXT NULL,
    region_filter TEXT NULL,
    captured_utc TEXT NOT NULL,
    sample_size INTEGER NULL,
    confidence REAL NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_data_snapshots_patch_source
ON data_snapshots(patch, source_id, captured_utc DESC);

CREATE TABLE IF NOT EXISTS user_profile (
    profile_key TEXT PRIMARY KEY,
    updated_utc TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_version(version, applied_utc)
VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
