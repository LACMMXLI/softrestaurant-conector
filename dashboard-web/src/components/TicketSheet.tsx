import { useEffect, useState } from 'react'
import { Ban, CreditCard, PackageOpen, X } from 'lucide-react'
import { api, ApiError } from '../api'
import { formatAmount, formatTime } from '../format'
import type { TicketDetail } from '../types'

type TicketSheetProps = {
  branchCode: string
  folio: number
  onClose: () => void
  onUnauthorized: () => void
}

export function TicketSheet({ branchCode, folio, onClose, onUnauthorized }: TicketSheetProps) {
  const [detail, setDetail] = useState<TicketDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api.ticket(branchCode, folio, controller.signal)
      .then(setDetail)
      .catch((reason: unknown) => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        if (reason instanceof ApiError && reason.status === 401) onUnauthorized()
        else setError(reason instanceof Error ? reason.message : 'No fue posible abrir el ticket.')
      })
    return () => controller.abort()
  }, [branchCode, folio, onUnauthorized])

  useEffect(() => {
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [onClose])

  return (
    <div className="sheet-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <dialog className="ticket-sheet" open aria-labelledby="ticket-sheet-title">
        <div className="sheet-handle" aria-hidden="true" />
        <button className="icon-button sheet-close" type="button" onClick={onClose} aria-label="Cerrar detalle">
          <X size={20} />
        </button>

        {error ? <p className="form-error" role="alert">{error}</p> : null}
        {!detail && !error ? <div className="skeleton sheet-skeleton" aria-label="Cargando detalle" /> : null}
        {detail ? (
          <>
            <header className="sheet-header">
              <p className="utility-label">Rastro del ticket</p>
              <h2 id="ticket-sheet-title">Ticket {detail.ticket.checkNumber || detail.ticket.folio}</h2>
              <div className="sheet-ticket-meta">
                <span>{formatTime(detail.ticket.closedAt ?? detail.ticket.openedAt)}</span>
                {detail.ticket.table ? <span>Mesa {detail.ticket.table}</span> : null}
                {detail.ticket.cancelled ? <span className="danger-text"><Ban size={14} /> Cancelado</span> : <span>Pagado</span>}
              </div>
            </header>

            <section className="sheet-total">
              <span>Total registrado</span>
              <strong>{formatAmount(detail.ticket.total)}</strong>
              {detail.ticket.tip !== null ? <small>Propina: {formatAmount(detail.ticket.tip)}</small> : null}
            </section>

            <section className="sheet-section">
              <div className="sheet-section-title"><PackageOpen size={18} /><h3>Productos</h3></div>
              {detail.lines.length === 0 ? <p className="quiet-empty">Sin líneas sincronizadas.</p> : (
                <div className="detail-rows">
                  {detail.lines.map((line, index) => (
                    <div className="detail-row" key={`${line.productId ?? 'sin-id'}-${index}`}>
                      <div>
                        <strong>{line.productId ? `ID ${line.productId}` : 'Producto sin identificador'}</strong>
                        <span>{line.quantity ?? '—'} × {formatAmount(line.price)}</span>
                        {line.comment ? <small>{line.comment}</small> : null}
                      </div>
                      <b>{line.quantity !== null && line.price !== null ? formatAmount(line.quantity * line.price) : '—'}</b>
                    </div>
                  ))}
                </div>
              )}
              <p className="data-note">Los nombres se habilitarán cuando el catálogo real de productos esté sincronizado.</p>
            </section>

            <section className="sheet-section">
              <div className="sheet-section-title"><CreditCard size={18} /><h3>Pagos</h3></div>
              {detail.payments.length === 0 ? <p className="quiet-empty">Sin pagos sincronizados.</p> : (
                <div className="detail-rows">
                  {detail.payments.map((payment, index) => (
                    <div className="detail-row" key={`${payment.paymentMethodId ?? 'sin-id'}-${index}`}>
                      <div>
                        <strong>{payment.paymentMethodId ? `Forma ${payment.paymentMethodId}` : 'Forma no registrada'}</strong>
                        {payment.cardBrand ? <span>{payment.cardBrand}</span> : null}
                        {payment.exchangeRate && payment.exchangeRate !== 1 ? <small>Tipo de cambio {payment.exchangeRate}</small> : null}
                      </div>
                      <b>{formatAmount(payment.amount)}</b>
                    </div>
                  ))}
                </div>
              )}
              <p className="data-note">Se muestra el identificador de origen hasta recibir el catálogo de formas de pago.</p>
            </section>

            <section className="sheet-section compact">
              <dl className="detail-list">
                <div><dt>Estación</dt><dd>{detail.station || 'No registrada'}</dd></div>
                <div><dt>Área</dt><dd>{detail.restaurantArea || 'No registrada'}</dd></div>
                <div><dt>Mesero ID</dt><dd>{detail.waiterId || 'No registrado'}</dd></div>
                <div><dt>Cajero</dt><dd>{detail.ticket.paymentUser || 'No registrado'}</dd></div>
              </dl>
              {detail.ticket.cancelled ? (
                <div className="cancellation-note">
                  <strong>{detail.cancellationReason || 'Sin motivo registrado'}</strong>
                  <span>{detail.cancelledBy ? `Canceló: ${detail.cancelledBy}` : 'Usuario no registrado'}</span>
                </div>
              ) : null}
            </section>
          </>
        ) : null}
      </dialog>
    </div>
  )
}
