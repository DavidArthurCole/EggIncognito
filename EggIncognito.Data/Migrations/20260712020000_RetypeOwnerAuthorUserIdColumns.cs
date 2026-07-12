using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetypeOwnerAuthorUserIdColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM docs WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'docs has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM doc_images WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'doc_images has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM env_designs WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'env_designs has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM env_design_versions WHERE author_user_id IS NOT NULL AND author_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'env_design_versions has unbackfilled author_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM stored_endpoints WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'stored_endpoints has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM stored_routes WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'stored_routes has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                    IF EXISTS (SELECT 1 FROM feed_subscriptions WHERE owner_user_id IS NOT NULL AND owner_user_id !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$') THEN
                        RAISE EXCEPTION 'feed_subscriptions has unbackfilled owner_user_id rows - run the backfill tool before this migration';
                    END IF;
                END $$;

                ALTER TABLE docs ADD COLUMN owner_user_id_new uuid;
                UPDATE docs SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE docs DROP COLUMN owner_user_id;
                ALTER TABLE docs RENAME COLUMN owner_user_id_new TO owner_user_id;

                ALTER TABLE doc_images ADD COLUMN owner_user_id_new uuid;
                UPDATE doc_images SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE doc_images DROP COLUMN owner_user_id;
                ALTER TABLE doc_images RENAME COLUMN owner_user_id_new TO owner_user_id;

                ALTER TABLE env_designs ADD COLUMN owner_user_id_new uuid;
                UPDATE env_designs SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE env_designs DROP COLUMN owner_user_id;
                ALTER TABLE env_designs RENAME COLUMN owner_user_id_new TO owner_user_id;

                ALTER TABLE env_design_versions ADD COLUMN author_user_id_new uuid;
                UPDATE env_design_versions SET author_user_id_new = author_user_id::uuid WHERE author_user_id IS NOT NULL;
                ALTER TABLE env_design_versions DROP COLUMN author_user_id;
                ALTER TABLE env_design_versions RENAME COLUMN author_user_id_new TO author_user_id;

                ALTER TABLE stored_endpoints ADD COLUMN owner_user_id_new uuid;
                UPDATE stored_endpoints SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE stored_endpoints DROP COLUMN owner_user_id;
                ALTER TABLE stored_endpoints RENAME COLUMN owner_user_id_new TO owner_user_id;

                ALTER TABLE stored_routes ADD COLUMN owner_user_id_new uuid;
                UPDATE stored_routes SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE stored_routes DROP COLUMN owner_user_id;
                ALTER TABLE stored_routes RENAME COLUMN owner_user_id_new TO owner_user_id;

                ALTER TABLE feed_subscriptions ADD COLUMN owner_user_id_new uuid;
                UPDATE feed_subscriptions SET owner_user_id_new = owner_user_id::uuid WHERE owner_user_id IS NOT NULL;
                ALTER TABLE feed_subscriptions DROP COLUMN owner_user_id;
                ALTER TABLE feed_subscriptions RENAME COLUMN owner_user_id_new TO owner_user_id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE docs ADD COLUMN owner_user_id_old text;
                UPDATE docs SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE docs DROP COLUMN owner_user_id;
                ALTER TABLE docs RENAME COLUMN owner_user_id_old TO owner_user_id;

                ALTER TABLE doc_images ADD COLUMN owner_user_id_old text;
                UPDATE doc_images SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE doc_images DROP COLUMN owner_user_id;
                ALTER TABLE doc_images RENAME COLUMN owner_user_id_old TO owner_user_id;

                ALTER TABLE env_designs ADD COLUMN owner_user_id_old text;
                UPDATE env_designs SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE env_designs DROP COLUMN owner_user_id;
                ALTER TABLE env_designs RENAME COLUMN owner_user_id_old TO owner_user_id;

                ALTER TABLE env_design_versions ADD COLUMN author_user_id_old text;
                UPDATE env_design_versions SET author_user_id_old = author_user_id::text WHERE author_user_id IS NOT NULL;
                ALTER TABLE env_design_versions DROP COLUMN author_user_id;
                ALTER TABLE env_design_versions RENAME COLUMN author_user_id_old TO author_user_id;

                ALTER TABLE stored_endpoints ADD COLUMN owner_user_id_old text;
                UPDATE stored_endpoints SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE stored_endpoints DROP COLUMN owner_user_id;
                ALTER TABLE stored_endpoints RENAME COLUMN owner_user_id_old TO owner_user_id;

                ALTER TABLE stored_routes ADD COLUMN owner_user_id_old text;
                UPDATE stored_routes SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE stored_routes DROP COLUMN owner_user_id;
                ALTER TABLE stored_routes RENAME COLUMN owner_user_id_old TO owner_user_id;

                ALTER TABLE feed_subscriptions ADD COLUMN owner_user_id_old text;
                UPDATE feed_subscriptions SET owner_user_id_old = owner_user_id::text WHERE owner_user_id IS NOT NULL;
                ALTER TABLE feed_subscriptions DROP COLUMN owner_user_id;
                ALTER TABLE feed_subscriptions RENAME COLUMN owner_user_id_old TO owner_user_id;
            ");
        }
    }
}
