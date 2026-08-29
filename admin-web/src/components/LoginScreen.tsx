import { useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowRight, Eye, EyeOff, ShieldCheck } from 'lucide-react'

type LoginScreenProps = {
  error: string | null
  busy: boolean
  onLogin: (email: string, password: string) => Promise<void>
}

export function LoginScreen({ error, busy, onLogin }: LoginScreenProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await onLogin(email, password)
  }

  return (
    <main className="login-page">
      <section className="login-card" aria-label="Iniciar sesión">
        <div className="brand-mark" aria-hidden="true">
          <ShieldCheck size={26} strokeWidth={1.8} />
        </div>
        <p className="eyebrow">SoftRestaurant Sync · Administración</p>
        <h1>Panel de administración</h1>
        <p className="login-copy">Acceso restringido a cuentas con rol SUPERADMIN.</p>

        <form onSubmit={handleSubmit}>
          <label className="field-label" htmlFor="email">Correo</label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />

          <label className="field-label" htmlFor="password">Contraseña</label>
          <div className="password-field">
            <input
              id="password"
              name="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
            <button
              type="button"
              className="icon-button password-toggle"
              onClick={() => setShowPassword((visible) => !visible)}
              aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
            >
              {showPassword ? <EyeOff size={19} /> : <Eye size={19} />}
            </button>
          </div>

          {error ? <p className="form-error" role="alert">{error}</p> : null}

          <button className="primary-button" type="submit" disabled={busy}>
            <span>{busy ? 'Verificando…' : 'Entrar'}</span>
            <ArrowRight size={19} aria-hidden="true" />
          </button>
        </form>
      </section>
    </main>
  )
}
