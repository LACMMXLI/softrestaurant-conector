CREATE TABLE IF NOT EXISTS branches (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code text NOT NULL UNIQUE,
    name text NOT NULL,
    timezone text NOT NULL DEFAULT 'America/Tijuana',
    token_hash text NULL,
    legacy_auth_enabled boolean NOT NULL DEFAULT false,
    active boolean NOT NULL DEFAULT true,
    last_sync_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- Migración compatible: token_hash pertenece únicamente al mecanismo legacy.
ALTER TABLE branches ADD COLUMN IF NOT EXISTS token_hash text NULL;
ALTER TABLE branches ALTER COLUMN token_hash DROP NOT NULL;
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'branches'
          AND column_name = 'legacy_auth_enabled'
    ) THEN
        ALTER TABLE branches ADD COLUMN legacy_auth_enabled boolean NOT NULL DEFAULT false;
        UPDATE branches SET legacy_auth_enabled = true WHERE token_hash IS NOT NULL;
    END IF;
END $$;
COMMENT ON COLUMN branches.token_hash IS 'LEGACY: hash del token compartido por sucursal; eliminar tras migrar conectores.';

CREATE TABLE IF NOT EXISTS connectors (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id uuid NOT NULL REFERENCES branches(id),
    machine_name text NOT NULL,
    token_hash text NOT NULL,
    active boolean NOT NULL DEFAULT true,
    agent_version text NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    last_seen_at timestamptz NULL,
    last_ip text NULL,
    last_user_agent text NULL,
    activated_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL,
    token_rotated_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_connectors_branch ON connectors(branch_id, active);

-- Latido independiente del agente (ver central-api/Program.cs POST /api/agents/heartbeat y
-- extractor/HeartbeatWorker.cs): last_heartbeat_at indica si el agente está vivo, separado de
-- last_seen_at (que ya se actualiza en cualquier llamada autenticada, incluida la ingesta).
ALTER TABLE connectors ADD COLUMN IF NOT EXISTS last_status text NULL;
ALTER TABLE connectors ADD COLUMN IF NOT EXISTS last_error text NULL;
ALTER TABLE connectors ADD COLUMN IF NOT EXISTS pending_batches integer NULL;
ALTER TABLE connectors ADD COLUMN IF NOT EXISTS last_heartbeat_at timestamptz NULL;
ALTER TABLE connectors ADD COLUMN IF NOT EXISTS last_sync_request_handled_at timestamptz NULL;

-- Mecanismo simple de solicitud remota de sincronización: el panel marca la sucursal, el
-- agente la recoge en su siguiente latido (HeartbeatWorker) y la corre vía SyncCoordinator.
ALTER TABLE branches ADD COLUMN IF NOT EXISTS sync_requested_at timestamptz NULL;
ALTER TABLE branches ADD COLUMN IF NOT EXISTS sync_requested_by uuid NULL;

CREATE TABLE IF NOT EXISTS connector_activation_keys (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id uuid NOT NULL REFERENCES branches(id),
    key_hash text NOT NULL UNIQUE,
    expires_at timestamptz NOT NULL,
    used_at timestamptz NULL,
    used_by_connector_id uuid NULL REFERENCES connectors(id),
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_connector_activation_keys_branch
    ON connector_activation_keys(branch_id, expires_at DESC);

CREATE TABLE IF NOT EXISTS sync_batches (
    id text NOT NULL,
    branch_id uuid NOT NULL REFERENCES branches(id),
    range_start timestamp NOT NULL,
    range_end timestamp NOT NULL,
    agent_version text NOT NULL,
    reconciliation_ok boolean NOT NULL,
    counts jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, id)
);

CREATE TABLE IF NOT EXISTS sales (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_folio bigint NOT NULL,
    source_shift_id integer NULL,
    source_temp_folio bigint NULL,
    business_date timestamp NULL,
    closed_at timestamp NULL,
    paid boolean NOT NULL,
    cancelled boolean NOT NULL,
    total numeric(18,4) NULL,
    tip numeric(18,4) NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
ALTER TABLE sales ADD COLUMN IF NOT EXISTS source_shift_id integer NULL;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS source_temp_folio bigint NULL;
UPDATE sales
SET source_temp_folio = NULLIF(payload->>'foliotTempCheques', '')::bigint
WHERE source_temp_folio IS NULL
  AND NULLIF(payload->>'foliotTempCheques', '') IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_sales_branch_date ON sales(branch_id, business_date);
CREATE INDEX IF NOT EXISTS ix_sales_branch_folio ON sales(branch_id, source_folio);
CREATE INDEX IF NOT EXISTS ix_sales_branch_shift_temp
    ON sales(branch_id, source_shift_id, source_temp_folio)
    WHERE source_temp_folio IS NOT NULL;

CREATE TABLE IF NOT EXISTS sale_lines (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_folio bigint NOT NULL,
    source_shift_id integer NULL,
    product_id text NULL,
    quantity numeric(18,4) NULL,
    price numeric(18,4) NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_sale_lines_branch_folio ON sale_lines(branch_id, source_folio);

CREATE TABLE IF NOT EXISTS sale_payments (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_folio bigint NOT NULL,
    payment_method text NULL,
    amount numeric(18,4) NULL,
    tip numeric(18,4) NULL,
    exchange_rate numeric(18,6) NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
ALTER TABLE sale_payments ADD COLUMN IF NOT EXISTS exchange_rate numeric(18,6) NULL;
CREATE INDEX IF NOT EXISTS ix_sale_payments_branch_folio ON sale_payments(branch_id, source_folio);

CREATE TABLE IF NOT EXISTS shifts (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_shift_id integer NOT NULL,
    opened_at timestamp NULL,
    closed_at timestamp NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);

CREATE TABLE IF NOT EXISTS cash_declarations (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_shift_id integer NOT NULL,
    payment_method text NULL,
    amount numeric(18,4) NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);

CREATE TABLE IF NOT EXISTS cash_movements (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_folio bigint NOT NULL,
    source_shift_id integer NULL,
    movement_date timestamp NULL,
    movement_type integer NOT NULL,
    amount numeric(18,4) NULL,
    cancelled boolean NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
ALTER TABLE cash_movements ADD COLUMN IF NOT EXISTS source_shift_id integer NULL;
CREATE INDEX IF NOT EXISTS ix_cash_movements_branch_date ON cash_movements(branch_id, movement_date);

-- Snapshot actual de dbo.tempcheques. Estas filas son transitorias y nunca se mezclan
-- físicamente con sales; desaparecen al dejar de formar parte del snapshot o cuando la
-- venta definitiva llega con (idturno, foliotempcheques).
CREATE TABLE IF NOT EXISTS transient_sales (
    branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    idempotency_key text NOT NULL,
    source_temp_folio bigint NOT NULL,
    source_shift_id integer NULL,
    check_number text NULL,
    opened_at timestamp NULL,
    closed_at timestamp NULL,
    paid boolean NOT NULL,
    cancelled boolean NOT NULL,
    total numeric(18,4) NULL,
    tip numeric(18,4) NULL,
    payload jsonb NOT NULL,
    snapshot_id text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_transient_sales_branch_shift_folio
    ON transient_sales(branch_id, source_shift_id, source_temp_folio)
    WHERE source_shift_id IS NOT NULL AND source_shift_id > 0;
CREATE INDEX IF NOT EXISTS ix_transient_sales_branch_shift
    ON transient_sales(branch_id, source_shift_id);

CREATE TABLE IF NOT EXISTS transient_sale_lines (
    branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    idempotency_key text NOT NULL,
    header_key text NOT NULL,
    source_temp_folio bigint NOT NULL,
    source_shift_id integer NULL,
    product_id text NULL,
    quantity numeric(18,4) NULL,
    price numeric(18,4) NULL,
    payload jsonb NOT NULL,
    snapshot_id text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_transient_lines_header
    ON transient_sale_lines(branch_id, header_key);

CREATE TABLE IF NOT EXISTS transient_sale_payments (
    branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    idempotency_key text NOT NULL,
    header_key text NOT NULL,
    source_temp_folio bigint NOT NULL,
    source_shift_id integer NULL,
    payment_method text NULL,
    amount numeric(18,4) NULL,
    tip numeric(18,4) NULL,
    exchange_rate numeric(18,6) NULL,
    payload jsonb NOT NULL,
    snapshot_id text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_transient_payments_header
    ON transient_sale_payments(branch_id, header_key);

CREATE TABLE IF NOT EXISTS transient_snapshot_state (
    branch_id uuid PRIMARY KEY REFERENCES branches(id) ON DELETE CASCADE,
    last_created_at timestamptz NOT NULL,
    last_batch_id text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS cancellation_summaries (
    branch_id uuid NOT NULL REFERENCES branches(id),
    snapshot_key text NOT NULL,
    cancellation_date date NOT NULL,
    source_folio bigint NULL,
    product_id text NULL,
    quantity numeric(18,4) NULL,
    price numeric(18,4) NULL,
    occurrences integer NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, snapshot_key)
);
CREATE INDEX IF NOT EXISTS ix_cancellations_branch_date ON cancellation_summaries(branch_id, cancellation_date);

CREATE TABLE IF NOT EXISTS app_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email text NOT NULL,
    display_name text NOT NULL,
    password_hash text NOT NULL,
    role text NOT NULL CHECK (role IN ('SUPERADMIN', 'OWNER', 'MANAGER', 'VIEWER')),
    active boolean NOT NULL DEFAULT true,
    last_login_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- Suscripción por cuenta. La prueba y las activaciones no cambian `active`: una cuenta
-- vencida conserva sesión, negocios e historial, pero el API web deja de entregar contenido.
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS subscription_plan text NOT NULL DEFAULT 'BASIC';
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS trial_ends_at timestamptz NOT NULL DEFAULT (now() + interval '15 days');
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS paid_until timestamptz NULL;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS subscription_suspended boolean NOT NULL DEFAULT false;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS subscription_updated_at timestamptz NOT NULL DEFAULT now();
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'app_users_subscription_plan_check') THEN
        ALTER TABLE app_users ADD CONSTRAINT app_users_subscription_plan_check
            CHECK (subscription_plan IN ('BASIC', 'PLUS'));
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'branches_sync_requested_by_fkey'
    ) THEN
        ALTER TABLE branches
            ADD CONSTRAINT branches_sync_requested_by_fkey
            FOREIGN KEY (sync_requested_by) REFERENCES app_users(id);
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_email_lower ON app_users(lower(email));

-- Migración compatible: bases existentes tienen el CHECK sin SUPERADMIN (panel admin del SaaS).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'app_users_role_check'
    ) THEN
        ALTER TABLE app_users DROP CONSTRAINT app_users_role_check;
    END IF;
    ALTER TABLE app_users ADD CONSTRAINT app_users_role_check
        CHECK (role IN ('SUPERADMIN', 'OWNER', 'MANAGER', 'VIEWER'));
END $$;

-- Relación muchos-a-muchos entre cuentas OWNER/MANAGER/VIEWER y las sucursales a las que
-- tienen acceso. Ya la usa DashboardReportService para acotar /api/web/* a las sucursales
-- asignadas; la fase de "gestión de usuarios" del panel admin reutilizará esta misma tabla
-- para asignar/quitar sucursales a una cuenta en vez de crear un esquema nuevo.
CREATE TABLE IF NOT EXISTS app_user_branches (
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, branch_id)
);
-- La PK (user_id, branch_id) ya cubre "sucursales de un usuario"; este índice cubre la
-- consulta inversa ("qué usuarios tienen acceso a esta sucursal") que necesitará el
-- detalle de sucursal del panel admin y, después, la pantalla de usuarios.
CREATE INDEX IF NOT EXISTS ix_app_user_branches_branch ON app_user_branches(branch_id);

CREATE TABLE IF NOT EXISTS app_sessions (
    token_hash text PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    ip text NULL,
    user_agent text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_app_sessions_user_expiry ON app_sessions(user_id, expires_at DESC);

CREATE TABLE IF NOT EXISTS audit_log (
    id bigserial PRIMARY KEY,
    user_id uuid NULL REFERENCES app_users(id) ON DELETE SET NULL,
    event_type text NOT NULL,
    branch_id uuid NULL REFERENCES branches(id) ON DELETE SET NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    ip text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_audit_log_user_date ON audit_log(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_log_branch_date ON audit_log(branch_id, created_at DESC);

-- ── Modelo SaaS: Business por encima de Branch, identidad de dispositivo propia ────────────
-- Reemplaza la activación por código (connector_activation_keys) y el token compartido legacy
-- por sucursal por un flujo de vinculación desde una sesión de usuario autenticada. Ver
-- central-api/BusinessRegistry.cs, central-api/ConnectorInstallationRegistry.cs.

CREATE TABLE IF NOT EXISTS businesses (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    slug text NOT NULL UNIQUE,
    active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE branches ADD COLUMN IF NOT EXISTS business_id uuid NULL REFERENCES businesses(id);
CREATE INDEX IF NOT EXISTS ix_branches_business ON branches(business_id);

-- Membresía a nivel negocio: reemplaza app_user_branches (que era por sucursal individual).
-- Un negocio puede tener varias sucursales; un miembro con acceso al negocio ve todas.
CREATE TABLE IF NOT EXISTS business_members (
    business_id uuid NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    role text NOT NULL CHECK (role IN ('OWNER','MANAGER','VIEWER')),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (business_id, user_id)
);
CREATE INDEX IF NOT EXISTS ix_business_members_user ON business_members(user_id);

  -- app_users.role ahora solo distingue operador de plataforma (SUPERADMIN, panel admin) de
  -- cuenta normal (USER); el permiso real de negocio vive en business_members.role.
  DO $$
  BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'app_users_role_check') THEN
        ALTER TABLE app_users DROP CONSTRAINT app_users_role_check;
    END IF;
    UPDATE app_users SET role = 'USER' WHERE role IN ('OWNER','MANAGER','VIEWER');
    ALTER TABLE app_users ADD CONSTRAINT app_users_role_check CHECK (role IN ('SUPERADMIN','USER'));
END $$;

-- Backfill: si hay sucursales sin negocio (base existente antes de este cambio), un único
-- negocio bootstrap se queda con todas y hereda los accesos que ya existían en
-- app_user_branches, para no perder datos ni dejar a nadie sin acceso.
DO $$
DECLARE bootstrap_business_id uuid;
BEGIN
    IF EXISTS (SELECT 1 FROM branches WHERE business_id IS NULL)
       AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'app_user_branches') THEN
        INSERT INTO businesses (name, slug) VALUES ('Negocio principal', 'negocio-principal')
        ON CONFLICT (slug) DO NOTHING;
        SELECT id INTO bootstrap_business_id FROM businesses WHERE slug = 'negocio-principal';

        UPDATE branches SET business_id = bootstrap_business_id WHERE business_id IS NULL;

        INSERT INTO business_members (business_id, user_id, role)
        SELECT DISTINCT bootstrap_business_id, ub.user_id, u.role
        FROM app_user_branches ub
        JOIN app_users u ON u.id = ub.user_id
        WHERE u.role IN ('OWNER','MANAGER','VIEWER')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

UPDATE app_users SET role = 'USER' WHERE role IN ('OWNER','MANAGER','VIEWER');
ALTER TABLE app_users ALTER COLUMN role SET DEFAULT 'USER';

-- Solo se exige NOT NULL una vez que el backfill de arriba garantiza que toda sucursal tiene
-- negocio (en una base nueva, sin filas en branches, esto no bloquea nada).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM branches WHERE business_id IS NULL) THEN
        ALTER TABLE branches ALTER COLUMN business_id SET NOT NULL;
    END IF;
END $$;

-- connectors → connector_installations: mismo concepto (un dispositivo/agente vinculado a una
-- sucursal), renombrado para reflejar que ya no se crea por activación sino por vinculación
-- desde una sesión de usuario. Ver central-api/ConnectorInstallationRegistry.cs.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'connectors')
       AND NOT EXISTS (
           SELECT 1 FROM information_schema.tables
           WHERE table_schema = 'public' AND table_name = 'connector_installations'
       ) THEN
        ALTER TABLE connectors RENAME TO connector_installations;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS connector_installations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id uuid NOT NULL REFERENCES branches(id),
    machine_name text NOT NULL,
    token_hash text NOT NULL,
    active boolean NOT NULL DEFAULT true,
    agent_version text NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    last_seen_at timestamptz NULL,
    last_ip text NULL,
    last_user_agent text NULL,
    linked_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz NULL,
    token_rotated_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_connector_installations_branch ON connector_installations(branch_id, active);
DROP INDEX IF EXISTS ix_connectors_branch;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'connector_installations' AND column_name = 'activated_at'
    ) THEN
        ALTER TABLE connector_installations RENAME COLUMN activated_at TO linked_at;
    END IF;
END $$;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS linked_by_user_id uuid NULL REFERENCES app_users(id);
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS last_success_at timestamptz NULL;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS last_status text NULL;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS last_error text NULL;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS pending_batches integer NULL;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS last_heartbeat_at timestamptz NULL;
ALTER TABLE connector_installations ADD COLUMN IF NOT EXISTS last_sync_request_handled_at timestamptz NULL;

-- Solo un conector ACTIVO por sucursal a la vez: garantía real a nivel de base de datos de
-- "no crear silenciosamente un segundo extractor activo" (el 409 de la API es solo UX).
CREATE UNIQUE INDEX IF NOT EXISTS ux_connector_installations_branch_active
    ON connector_installations(branch_id) WHERE active = true;

-- ── Elimina activación por código y token legacy por completo (sin compatibilidad) ────────
DROP TABLE IF EXISTS connector_activation_keys;
ALTER TABLE branches DROP COLUMN IF EXISTS token_hash;
ALTER TABLE branches DROP COLUMN IF EXISTS legacy_auth_enabled;
DROP TABLE IF EXISTS app_user_branches;
