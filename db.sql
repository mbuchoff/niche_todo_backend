CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    CREATE TABLE users (
        "Id" uuid NOT NULL,
        "GoogleSub" character varying(128) NOT NULL,
        "Email" character varying(320) NOT NULL,
        "Name" character varying(256) NOT NULL,
        "AvatarUrl" character varying(512),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "LastLoginAt" timestamp with time zone,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    CREATE TABLE refresh_tokens (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(256) NOT NULL,
        "DeviceId" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "RevokedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_refresh_tokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_refresh_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    CREATE UNIQUE INDEX "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    CREATE INDEX "IX_refresh_tokens_UserId" ON refresh_tokens ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    CREATE UNIQUE INDEX "IX_users_GoogleSub" ON users ("GoogleSub");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260104225038_InitialAuthSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260104225038_InitialAuthSchema', '9.0.0');
    END IF;
END $EF$;
COMMIT;

