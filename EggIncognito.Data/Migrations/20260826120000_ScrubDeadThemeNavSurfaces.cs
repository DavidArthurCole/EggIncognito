using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations;

public partial class ScrubDeadThemeNavSurfaces : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("""
            update user_themes
            set model = jsonb_set(
                    model,
                    '{css}',
                    to_jsonb(regexp_replace(
                        regexp_replace(model ->> 'css', '(?<![-[:alnum:]_])nav-item[[:space:]]*\{[^{}]*\}', '', 'g'),
                        '(?<![-[:alnum:]_])nav[[:space:]]*\{[^{}]*\}', '', 'g')))
            where jsonb_typeof(model -> 'css') = 'string'
              and model ->> 'css' ~ '(?<![-[:alnum:]_])nav(-item)?[[:space:]]*\{';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("select 1;");
    }
}
