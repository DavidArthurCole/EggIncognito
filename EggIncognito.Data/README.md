# EggIncognito.Data

The optional Postgres layer (EF Core + Npgsql). Active only when a Postgres connection string is set; without one the app is file-only, and a transient DB error degrades to the file default rather than a failed request. Migrations auto-apply at startup. Part of [EggIncognito](../README.md).

Users are not stored here; they live in the external EggIdentity service, and row ownership is the identity id with no local foreign key. Auth-cookie data-protection keys persist in the DB so logins survive restarts.
