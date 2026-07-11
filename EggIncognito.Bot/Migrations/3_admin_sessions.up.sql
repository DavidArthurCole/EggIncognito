CREATE TABLE IF NOT EXISTS admin_sessions (
    token TEXT PRIMARY KEY,
    discord_id TEXT NOT NULL,
    expires_at BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
