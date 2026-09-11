import { useEffect, useState } from 'react'
import { ArrowDownUp, Plus, Save, Tag, Trash2, X } from 'lucide-react'
import { api, ApiError } from '../api'
import type { ExpenseCategory, ExpenseCategoryInput, ExpenseCategoryRule } from '../types'

type Props = { branchCode: string; canManage: boolean; onUnauthorized: () => void }
type Draft = { id: string | null; name: string; displayOrder: number; rules: ExpenseCategoryRule[] }

const emptyDraft = (): Draft => ({ id: null, name: '', displayOrder: 100, rules: [{ keyword: '', priority: 100 }] })
const toDraft = (category: ExpenseCategory): Draft => ({ id: category.id, name: category.name, displayOrder: category.displayOrder, rules: category.rules })

export function ExpenseCategoriesScreen({ branchCode, canManage, onUnauthorized }: Props) {
  const [categories, setCategories] = useState<ExpenseCategory[]>([])
  const [draft, setDraft] = useState<Draft>(emptyDraft)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    try { setCategories(await api.expenseCategories(branchCode)) }
    catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) onUnauthorized()
      else setError(reason instanceof Error ? reason.message : 'No fue posible cargar las categorías.')
    } finally { setLoading(false) }
  }

  useEffect(() => { void load() }, [branchCode])

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true); setError(null)
    const input: ExpenseCategoryInput = { name: draft.name, displayOrder: Number(draft.displayOrder), rules: draft.rules }
    try {
      if (draft.id) await api.updateExpenseCategory(branchCode, draft.id, input)
      else await api.createExpenseCategory(branchCode, input)
      setDraft(emptyDraft())
      await load()
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) onUnauthorized()
      else setError(reason instanceof Error ? reason.message : 'No fue posible guardar la categoría.')
    } finally { setSaving(false) }
  }

  async function remove(category: ExpenseCategory) {
    if (!window.confirm(`Eliminar “${category.name}”? Los gastos corregidos manualmente quedarán sin clasificar.`)) return
    try {
      await api.deleteExpenseCategory(branchCode, category.id)
      if (draft.id === category.id) setDraft(emptyDraft())
      await load()
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'No fue posible eliminar la categoría.') }
  }

  function changeRule(index: number, patch: Partial<ExpenseCategoryRule>) {
    setDraft(current => ({ ...current, rules: current.rules.map((rule, position) => position === index ? { ...rule, ...patch } : rule) }))
  }

  if (!canManage) return <section className="content-card"><h1>Categorías de gastos</h1><p className="quiet-empty">Solo el OWNER del negocio puede administrar reglas de gastos.</p></section>

  return <div className="screen-stack">
    <section className="screen-title"><p className="utility-label">Configuración</p><h1>Categorías de gastos</h1><p>Las palabras clave y su prioridad se aplican a todas las sucursales de este negocio.</p></section>

    <section className="content-card expense-settings-form">
      <div className="section-heading horizontal"><div><h2>{draft.id ? 'Editar categoría' : 'Nueva categoría'}</h2></div>{draft.id ? <button className="icon-button" type="button" onClick={() => setDraft(emptyDraft())} aria-label="Cancelar edición"><X size={18} /></button> : null}</div>
      <form onSubmit={save}>
        <div className="expense-form-grid"><label>Nombre<input value={draft.name} onChange={event => setDraft(current => ({ ...current, name: event.target.value }))} placeholder="Ej. Proveedores" required maxLength={80} /></label><label>Prioridad de categoría<input type="number" min="1" max="10000" value={draft.displayOrder} onChange={event => setDraft(current => ({ ...current, displayOrder: Number(event.target.value) }))} /></label></div>
        <div className="section-heading horizontal expense-rules-heading"><div><p className="utility-label">Reglas</p><h3>Palabras clave</h3></div><button className="secondary-button" type="button" onClick={() => setDraft(current => ({ ...current, rules: [...current.rules, { keyword: '', priority: 100 }] }))}><Plus size={16} /> Agregar</button></div>
        <div className="expense-rule-editor">
          {draft.rules.map((rule, index) => <div className="expense-rule-row" key={`${index}:${rule.keyword}`}><label>Palabra clave<input value={rule.keyword} onChange={event => changeRule(index, { keyword: event.target.value })} placeholder="Ej. DIDI" required maxLength={100} /></label><label>Prioridad<input type="number" min="1" max="10000" value={rule.priority} onChange={event => changeRule(index, { priority: Number(event.target.value) })} /></label><button className="icon-button" type="button" disabled={draft.rules.length === 1} onClick={() => setDraft(current => ({ ...current, rules: current.rules.filter((_, position) => position !== index) }))} aria-label="Eliminar regla"><Trash2 size={17} /></button></div>)}
        </div>
        {error ? <p className="form-error" role="alert">{error}</p> : null}
        <button className="primary-button" disabled={saving}><Save size={17} />{saving ? 'Guardando…' : 'Guardar categoría'}</button>
      </form>
    </section>

    <section className="content-card"><div className="section-heading"><div><p className="utility-label">Reglas activas</p><h2>Categorías del negocio</h2></div></div>
      {loading ? <div className="skeleton list-skeleton" /> : null}
      {!loading && categories.length === 0 ? <p className="quiet-empty">Aún no hay reglas. Crea la primera para clasificar automáticamente las salidas futuras e históricas.</p> : null}
      <div className="expense-category-list">{categories.map(category => <article className="expense-config-row" key={category.id}><span className="expense-config-icon"><Tag size={18} /></span><div><strong>{category.name}</strong><p>{category.rules.map(rule => `${rule.keyword} · prioridad ${rule.priority}`).join('  |  ')}</p></div><span className="expense-config-order"><ArrowDownUp size={14} /> {category.displayOrder}</span><button className="secondary-button" type="button" onClick={() => setDraft(toDraft(category))}>Editar</button><button className="icon-button danger-button" type="button" onClick={() => void remove(category)} aria-label={`Eliminar ${category.name}`}><Trash2 size={17} /></button></article>)}</div>
    </section>
  </div>
}
