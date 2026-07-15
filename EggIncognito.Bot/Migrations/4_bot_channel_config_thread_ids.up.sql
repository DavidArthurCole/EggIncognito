ALTER TABLE bot_channel_config ADD COLUMN IF NOT EXISTS github_feed_thread_id TEXT;
ALTER TABLE bot_channel_config ADD COLUMN IF NOT EXISTS deploy_notifications_thread_id TEXT;
ALTER TABLE bot_channel_config ADD COLUMN IF NOT EXISTS success_embed_json TEXT;
ALTER TABLE bot_channel_config ADD COLUMN IF NOT EXISTS failure_embed_json TEXT;
ALTER TABLE bot_channel_config ADD COLUMN IF NOT EXISTS uptodate_embed_json TEXT;
