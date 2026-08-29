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
CREATE INDEX IF NOT EXISTS ix_sales_branch_date ON sales(branch_id, business_date);
CREATE INDEX IF NOT EXISTS ix_sales_branch_folio ON sales(branch_id, source_folio);

CREATE TABLE IF NOT EXISTS sale_lines (
    branch_id uuid NOT NULL REFERENCES branches(id),
    idempotency_key text NOT NULL,
    source_folio bigint NOT NULL,
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
    movement_date timestamp NULL,
    movement_type integer NOT NULL,
    amount numeric(18,4) NULL,
    cancelled boolean NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (branch_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_cash_movements_branch_date ON cash_movements(branch_id, movement_date);

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
    role text NOT NULL CHECK (role IN ('OWNER', 'MANAGER', 'VIEWER')),
    active boolean NOT NULL DEFAULT true,
    last_login_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_email_lower ON app_users(lower(email));

CREATE TABLE IF NOT EXISTS app_user_branches (
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    branch_id uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, branch_id)
);

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
