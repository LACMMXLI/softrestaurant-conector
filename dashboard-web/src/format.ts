const amountFormatter = new Intl.NumberFormat('es-MX', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

const integerFormatter = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 0 })

export function formatAmount(value: number | null): string {
  return value === null ? '—' : amountFormatter.format(value)
}

export function formatInteger(value: number | null): string {
  return value === null ? '—' : integerFormatter.format(value)
}

export function formatTime(value: string | null): string {
  if (!value) return 'Sin hora'
  return new Intl.DateTimeFormat('es-MX', {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

export function formatDateLabel(date: string): string {
  const parsed = new Date(`${date}T12:00:00`)
  return new Intl.DateTimeFormat('es-MX', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(parsed)
}

export function dateInTimezone(timezone: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: timezone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date())
  const values = Object.fromEntries(parts.map((part) => [part.type, part.value]))
  return `${values.year}-${values.month}-${values.day}`
}

export function timeAgo(value: string | null): string {
  if (!value) return 'Sin sincronización'
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60_000))
  if (minutes < 1) return 'Actualizado hace menos de un minuto'
  if (minutes < 60) return `Actualizado hace ${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `Actualizado hace ${hours} h`
  return `Actualizado hace ${Math.floor(hours / 24)} d`
}
