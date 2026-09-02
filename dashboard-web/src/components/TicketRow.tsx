import { ChevronRight } from 'lucide-react'
import { formatAmount, formatTime } from '../format'
import type { SalesTicket } from '../types'

type TicketRowProps = {
  ticket: SalesTicket
  onOpen: (folio: number) => void
}

export function TicketRow({ ticket, onOpen }: TicketRowProps) {
  return (
    <button
      className="ticket-row"
      type="button"
      onClick={() => { if (!ticket.transient) onOpen(ticket.folio) }}
      aria-disabled={ticket.transient}
      title={ticket.transient ? 'Ticket cobrado del turno abierto; el detalle seguirá disponible al consolidarse en el corte.' : undefined}
    >
      <span className="ticket-status-rail" data-state={ticket.cancelled ? 'cancelled' : 'paid'} />
      <span className="ticket-main">
        <span className="ticket-title">
          Ticket {ticket.checkNumber || `folio ${ticket.folio}`}
          {ticket.cancelled ? <em>Cancelado</em> : null}
          {ticket.transient ? <em>Turno abierto</em> : null}
        </span>
        <span className="ticket-meta">
          {formatTime(ticket.closedAt ?? ticket.openedAt)}
          {ticket.table ? ` · Mesa ${ticket.table}` : ''}
          {ticket.paymentUser ? ` · ${ticket.paymentUser}` : ''}
        </span>
      </span>
      <strong>{formatAmount(ticket.total)}</strong>
      {ticket.transient ? null : <ChevronRight size={18} aria-hidden="true" />}
    </button>
  )
}
