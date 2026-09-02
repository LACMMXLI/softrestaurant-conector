import { useState } from 'react'
import type { FormEvent } from 'react'
import {
  ArrowRight,
  BarChart3,
  Building2,
  Check,
  Clock3,
  CreditCard,
  Eye,
  EyeOff,
  Laptop,
  Menu,
  MonitorSmartphone,
  ReceiptText,
  ShieldCheck,
  Sparkles,
  Smartphone,
  Store,
  TrendingUp,
  Wifi,
  X,
} from 'lucide-react'
import { Brand } from './Brand'

type LoginScreenProps = {
  error: string | null
  busy: boolean
  onLogin: (email: string, password: string) => Promise<void>
  onRegister: (email: string, password: string, displayName: string) => Promise<void>
}

const capabilities = [
  { icon: TrendingUp, title: 'Ventas en tiempo real', copy: 'Consulta el avance del turno con la última sincronización disponible.' },
  { icon: Building2, title: 'Sucursales', copy: 'Cambia de ubicación sin mezclar información ni permisos.' },
  { icon: Clock3, title: 'Turnos y cajas', copy: 'Revisa la actividad operativa con su contexto de fecha y sucursal.' },
  { icon: ReceiptText, title: 'Tickets', copy: 'Abre el detalle de cada venta desde un mismo lugar.' },
  { icon: CreditCard, title: 'Formas de pago', copy: 'Entiende cómo se compone la venta del periodo consultado.' },
  { icon: BarChart3, title: 'Cobertura visible', copy: 'Identifica datos completos, parciales o pendientes de conciliar.' },
]

const plans = [
  {
    name: 'Estándar',
    price: '$199',
    description: 'Para tener la operación diaria de una sucursal siempre a la mano.',
    features: ['1 sucursal', 'Historial de 4 días', 'Ventas y cuentas abiertas', 'Sincronización en vivo'],
  },
  {
    name: 'Plus',
    price: '$499',
    description: 'Para equipos que necesitan más alcance, historial y herramientas de análisis.',
    features: ['Hasta 5 sucursales', 'Historial de 90 días', 'Analítica y alertas', 'Exportación y soporte prioritario'],
    featured: true,
  },
]

function ProductLogo({ compact = false }: { compact?: boolean }) {
  return <Brand compact={compact} className="landing-logo" />
}

function DashboardPreview() {
  return (
    <div className="device-stage" aria-label="Vista ilustrativa del dashboard en computadora y celular">
      <div className="laptop-device">
        <div className="laptop-screen">
          <div className="preview-sidebar"><ProductLogo compact /><span>Inicio</span><span>Ventas</span><span>Sucursales</span><span>Operación</span></div>
          <div className="preview-content">
            <div className="preview-topline"><span>Resumen general</span><span className="preview-live"><Wifi size={10} /> Sincronizado</span></div>
            <p className="preview-caption">Vista de ejemplo</p>
            <div className="preview-value">Información de tu sucursal</div>
            <svg className="preview-chart" viewBox="0 0 340 74" role="img" aria-label="Gráfica ilustrativa de ventas">
              <defs><linearGradient id="chart-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#2f91ff" stopOpacity=".24" /><stop offset="1" stopColor="#2f91ff" stopOpacity="0" /></linearGradient></defs>
              <path d="M0 66 L35 57 L67 60 L99 45 L128 49 L159 30 L191 39 L224 23 L255 31 L286 18 L315 25 L340 5 L340 74 L0 74Z" fill="url(#chart-fill)" />
              <polyline points="0,66 35,57 67,60 99,45 128,49 159,30 191,39 224,23 255,31 286,18 315,25 340,5" fill="none" stroke="#2f91ff" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            <div className="preview-grid"><div><span>Ventas por sucursal</span><i /><i /><i /></div><div><span>Formas de pago</span><b className="preview-donut" /></div></div>
          </div>
        </div>
        <div className="laptop-base" />
      </div>
      <div className="phone-device">
        <div className="phone-speaker" />
        <div className="phone-head"><ProductLogo compact /><span>•••</span></div>
        <p>Tu restaurante</p>
        <strong>Datos al momento</strong>
        <span className="phone-status">Cobertura visible</span>
        <div className="phone-cards"><span>Sucursales<b>Según tu acceso</b></span><span>Cortes<b>Por turno</b></span></div>
        <div className="phone-total"><small>Resumen del turno</small><b>Listo para consultar</b></div>
        <div className="phone-nav"><span>⌂</span><span>▤</span><span>⌁</span><span>•••</span></div>
      </div>
    </div>
  )
}

export function LoginScreen({ error, busy, onLogin, onRegister }: LoginScreenProps) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (mode === 'register') await onRegister(email, password, displayName)
    else await onLogin(email, password)
  }

  function closeMenu() {
    setMenuOpen(false)
  }

  function openAccess(nextMode: 'login' | 'register') {
    setMode(nextMode)
    setMenuOpen(false)
    window.setTimeout(() => document.querySelector('#acceso input')?.scrollIntoView({ behavior: 'smooth', block: 'center' }), 0)
  }

  return (
    <main className="landing-page">
      <section className="landing-hero" id="inicio">
        <nav className="landing-nav" aria-label="Navegación pública">
          <a href="#inicio" aria-label="Ir al inicio"><ProductLogo /></a>
          <button className="landing-menu-button" type="button" onClick={() => setMenuOpen((open) => !open)} aria-expanded={menuOpen} aria-controls="landing-links" aria-label={menuOpen ? 'Cerrar menú' : 'Abrir menú'}>
            {menuOpen ? <X size={22} /> : <Menu size={22} />}
          </button>
          <div className={`landing-links${menuOpen ? ' is-open' : ''}`} id="landing-links">
            <a href="#inicio" onClick={closeMenu}>Inicio</a>
            <a href="#beneficios" onClick={closeMenu}>Beneficios</a>
            <a href="#dispositivos" onClick={closeMenu}>Características</a>
            <a href="#planes" onClick={closeMenu}>Planes</a>
            <a href="#acceso" onClick={() => openAccess('login')}>Acceso</a>
          </div>
          <a className="nav-login" href="#acceso" onClick={() => setMode('login')}>Iniciar sesión</a>
        </nav>

        <div className="hero-inner">
          <div className="hero-copy">
            <p className="hero-kicker">Información operativa, estés donde estés</p>
            <h1>Tu restaurante,<br /><span>siempre contigo</span></h1>
            <p className="hero-lead">Conecta la información de RestaurantAgent con un panel web para consultar ventas, sucursales y operación desde cualquier dispositivo.</p>
            <div className="hero-actions">
              <a className="landing-button landing-button--primary" href="#acceso" onClick={() => setMode('register')}>Probar gratis 15 días <ArrowRight size={18} /></a>
              <a className="landing-button landing-button--ghost" href="#acceso" onClick={() => setMode('login')}>Iniciar sesión</a>
            </div>
            <div className="hero-trust"><span><Wifi size={17} /> Sin reemplazar tu sistema</span><span><ShieldCheck size={17} /> Acceso protegido</span><span><MonitorSmartphone size={17} /> Diseño adaptable</span></div>
          </div>
          <DashboardPreview />
        </div>
      </section>

      <section className="landing-section benefits-section" id="beneficios">
        <div className="landing-container benefits-layout">
          <div className="section-intro">
            <p className="section-kicker">Cómo funciona</p>
            <h2>No necesitas estar en el restaurante para saber cómo va el día.</h2>
            <p>El conector lleva la información autorizada a un panel web. Cada consulta conserva el contexto de sucursal, fecha, sincronización y cobertura.</p>
            <div className="connection-note"><ProductLogo compact /><span>No reemplaza RestaurantAgent.<strong>Lo conecta.</strong></span></div>
          </div>
          <div className="capability-grid">
            {capabilities.map(({ icon: Icon, title, copy }) => (
              <article className="capability" key={title}><span className="capability-icon"><Icon size={23} /></span><h3>{title}</h3><p>{copy}</p></article>
            ))}
          </div>
        </div>
      </section>

      <section className="landing-section devices-section" id="dispositivos">
        <div className="landing-container devices-layout">
          <div className="section-intro">
            <p className="section-kicker">Tu información, en tus dispositivos</p>
            <h2>Control claro,<br />donde lo necesitas.</h2>
            <p>Consulta desde celular, tablet o computadora. La experiencia se adapta al tamaño de pantalla sin perder contexto operativo.</p>
            <div className="device-options"><span><Smartphone size={24} />Celular</span><span><MonitorSmartphone size={24} />Tablet</span><span><Laptop size={26} />Computadora</span></div>
          </div>
          <div className="devices-visual" aria-hidden="true">
            <div className="devices-halo" />
            <div className="mini-laptop"><div><span>Resumen</span><svg viewBox="0 0 160 42"><polyline points="0,38 26,31 52,33 78,18 104,24 130,14 160,4" /></svg><i /><i /></div></div>
            <div className="mini-tablet"><span>Panel web</span><svg viewBox="0 0 120 38"><polyline points="0,34 24,27 48,29 72,15 96,20 120,4" /></svg></div>
            <div className="mini-phone"><span>Hoy</span><b>Operación visible</b><i /></div>
          </div>
        </div>
      </section>

      <section className="landing-section plans-section" id="planes">
        <div className="landing-container">
          <div className="plans-heading">
            <div>
              <p className="section-kicker">Planes claros, sin sorpresas</p>
              <h2>Empieza con 15 días gratis.<br />Elige después.</h2>
            </div>
            <p>Conecta tu restaurante, conoce el panel con tu propia operación y decide qué alcance necesita tu equipo. No solicitamos pago para crear tu cuenta.</p>
          </div>
          <div className="public-pricing-grid">
            <article className="trial-card">
              <span className="trial-orbit" aria-hidden="true"><Sparkles size={25} /></span>
              <p className="utility-label">Primero, compruébalo</p>
              <strong>15</strong>
              <span>días de prueba gratis</span>
              <p>Acceso completo para conocer el servicio antes de elegir un plan.</p>
              <button type="button" className="plan-cta plan-cta--light" onClick={() => openAccess('register')}>Crear mi cuenta <ArrowRight size={17} /></button>
            </article>
            {plans.map((plan) => (
              <article className={`public-plan-card${plan.featured ? ' is-featured' : ''}`} key={plan.name}>
                {plan.featured ? <span className="plan-ribbon">Más completo</span> : null}
                <p className="utility-label">Plan {plan.name}</p>
                <div className="public-plan-price"><strong>{plan.price}</strong><span>MXN / mes</span></div>
                <p className="public-plan-description">{plan.description}</p>
                <ul>{plan.features.map((feature) => <li key={feature}><Check size={16} /><span>{feature}</span></li>)}</ul>
                <button type="button" className="plan-cta" onClick={() => openAccess('register')}>Comenzar prueba gratis <ArrowRight size={17} /></button>
              </article>
            ))}
          </div>
          <p className="plans-note">Precios informativos. La prueba inicia al crear tu cuenta; la contratación del plan se gestiona por separado.</p>
        </div>
      </section>

      <section className="branches-band">
        <div className="landing-container branches-layout">
          <div className="section-intro section-intro--light">
            <p className="section-kicker">Una vista para cada negocio</p>
            <h2>Más de una sucursal.<br />Una sola vista.</h2>
            <p>Los usuarios ven únicamente las sucursales que tienen asignadas. La administración global permanece separada y protegida.</p>
          </div>
          <div className="branch-panel" aria-label="Ejemplo de acceso por sucursal"><div><Store size={20} /><strong>Sucursales asignadas</strong></div><span>Centro <b>Disponible</b></span><span>Norte <b>Disponible</b></span><span>Otras <em>Según permisos</em></span></div>
        </div>
      </section>

      <section className="access-section" id="acceso">
        <div className="landing-container access-layout">
          <div className="access-copy"><p className="section-kicker">{mode === 'register' ? 'Tu prueba comienza aquí' : 'Acceso para clientes'}</p><h2>{mode === 'register' ? 'Crea tu cuenta.' : 'Consulta tu operación.'}</h2><p>{mode === 'register' ? 'Regístrate para iniciar tus 15 días de prueba y crear tu primer negocio.' : 'Inicia sesión con la cuenta y las sucursales que tu administrador te asignó.'}</p><div className="access-proof"><ShieldCheck size={20} /><span>Sesión protegida y permisos por sucursal</span></div></div>
          <section className="login-card" aria-label={mode === 'register' ? 'Crear cuenta' : 'Iniciar sesión'}>
            <div>
              <p className="utility-label">Panel de control</p>
              <h2>{mode === 'register' ? 'Crear cuenta' : 'Iniciar sesión'}</h2>
              <p className="login-card-copy">
                {mode === 'register' ? 'Incluye 15 días de prueba gratis. No necesitas ingresar datos de pago.' : 'Accede a tu información operativa.'}
              </p>
            </div>
            <form onSubmit={handleSubmit}>
              {mode === 'register' ? (
                <>
                  <label className="field-label" htmlFor="displayName">Nombre</label>
                  <input id="displayName" name="displayName" type="text" autoComplete="name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} required />
                </>
              ) : null}
              <label className="field-label" htmlFor="email">Correo electrónico</label>
              <input id="email" name="email" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} required />
              <label className="field-label" htmlFor="password">Contraseña</label>
              <div className="password-field"><input id="password" name="password" type={showPassword ? 'text' : 'password'} autoComplete={mode === 'register' ? 'new-password' : 'current-password'} value={password} onChange={(event) => setPassword(event.target.value)} required /><button type="button" className="icon-button password-toggle" onClick={() => setShowPassword((visible) => !visible)} aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}>{showPassword ? <EyeOff size={19} /> : <Eye size={19} />}</button></div>
              {error ? <p className="form-error" role="alert">{error}</p> : null}
              <button className="primary-button" type="submit" disabled={busy}>
                <span>{busy ? 'Verificando…' : mode === 'register' ? 'Crear cuenta' : 'Entrar'}</span>
                <ArrowRight size={19} aria-hidden="true" />
              </button>
            </form>
            <button
              type="button"
              className="landing-button landing-button--ghost"
              onClick={() => setMode((current) => (current === 'login' ? 'register' : 'login'))}
            >
              {mode === 'register' ? '¿Ya tienes cuenta? Inicia sesión' : '¿No tienes cuenta? Créala aquí'}
            </button>
          </section>
        </div>
      </section>

      <footer className="landing-footer"><div className="landing-container"><ProductLogo compact /><span>Información operativa con contexto y cobertura visible.</span><a href="#inicio">Volver al inicio</a></div></footer>
    </main>
  )
}
