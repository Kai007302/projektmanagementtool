import { useEffect, useState } from 'react'
import { fetchProjects, type ProjectSummary } from '../projects/api'
import { fetchTasks, type Task } from '../tasks/api'
import { searchArticles, type ArticleSummary } from './api'
import { blockTypes, calloutTones, move, newBlock, type Block, type BlockType, type CalloutTone } from './blocks'
import { CloseIcon } from '../ui/icons'

type Props = { blocks: Block[]; onChange: (blocks: Block[]) => void; articleId?: string }

/** Edits the block list: add, change, reorder and remove blocks. Every control is keyboard reachable. */
export function BlockEditor({ blocks, onChange, articleId }: Props) {
  const [type, setType] = useState<BlockType>('paragraph')

  const update = (index: number, block: Block) => onChange(blocks.map((b, i) => (i === index ? block : b)))

  return (
    <div className="block-editor">
      <ol className="block-list">
        {blocks.map((block, index) => (
          <li key={block.id} className="block-item">
            <div className="row block-toolbar">
              <span className="muted">
                {index + 1}. {blockTypes[block.type]}
              </span>
              <span className="block-actions">
                <button
                  type="button"
                  aria-label={`Block ${index + 1} nach oben`}
                  disabled={index === 0}
                  onClick={() => onChange(move(blocks, index, -1))}
                >
                  ↑
                </button>
                <button
                  type="button"
                  aria-label={`Block ${index + 1} nach unten`}
                  disabled={index === blocks.length - 1}
                  onClick={() => onChange(move(blocks, index, 1))}
                >
                  ↓
                </button>
                <button type="button" aria-label={`Block ${index + 1} entfernen`} onClick={() => onChange(blocks.filter((_, i) => i !== index))}>
                  <CloseIcon />
                </button>
              </span>
            </div>
            <BlockFields block={block} position={index + 1} onChange={(changed) => update(index, changed)} articleId={articleId} />
          </li>
        ))}
      </ol>
      <div className="inline-form">
        <label>
          Neuer Block
          <select value={type} onChange={(event) => setType(event.target.value as BlockType)}>
            {Object.entries(blockTypes).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <button type="button" onClick={() => onChange([...blocks, newBlock(type)])}>
          Block hinzufügen
        </button>
      </div>
    </div>
  )
}

type FieldsProps = { block: Block; position: number; onChange: (block: Block) => void; articleId?: string }

function BlockFields({ block, position, onChange, articleId }: FieldsProps) {
  const name = `${blockTypes[block.type]} ${position}`
  switch (block.type) {
    case 'heading':
      return (
        <div className="block-fields">
          <label>
            Ebene
            <select value={block.level} onChange={(event) => onChange({ ...block, level: Number(event.target.value) as 1 | 2 | 3 })}>
              <option value={1}>1</option>
              <option value={2}>2</option>
              <option value={3}>3</option>
            </select>
          </label>
          <label className="grow">
            {name}
            <input value={block.text} onChange={(event) => onChange({ ...block, text: event.target.value })} maxLength={20000} />
          </label>
        </div>
      )
    case 'paragraph':
    case 'quote':
      return (
        <label className="block-fields stacked">
          {name}
          <textarea value={block.text} onChange={(event) => onChange({ ...block, text: event.target.value })} rows={3} maxLength={20000} />
        </label>
      )
    case 'callout':
      return (
        <div className="block-fields">
          <label>
            Art
            <select value={block.tone} onChange={(event) => onChange({ ...block, tone: event.target.value as CalloutTone })}>
              {Object.entries(calloutTones).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label className="grow">
            {name}
            <textarea value={block.text} onChange={(event) => onChange({ ...block, text: event.target.value })} rows={2} maxLength={20000} />
          </label>
        </div>
      )
    case 'code':
      return (
        <div className="block-fields">
          <label>
            Sprache
            <input value={block.language ?? ''} onChange={(event) => onChange({ ...block, language: event.target.value || undefined })} maxLength={40} size={10} />
          </label>
          <label className="grow">
            {name}
            <textarea className="code-input" value={block.code} onChange={(event) => onChange({ ...block, code: event.target.value })} rows={4} maxLength={20000} />
          </label>
        </div>
      )
    case 'bullet_list':
    case 'numbered_list':
      return (
        <label className="block-fields stacked">
          {name} (ein Eintrag pro Zeile)
          <textarea value={block.items.join('\n')} onChange={(event) => onChange({ ...block, items: event.target.value.split('\n') })} rows={Math.max(3, block.items.length)} />
        </label>
      )
    case 'checklist':
      return (
        <fieldset className="block-fields stacked">
          <legend>{name}</legend>
          {block.items.map((item, index) => (
            <div key={index} className="row">
              <input
                type="checkbox"
                aria-label={`Punkt ${index + 1} erledigt`}
                checked={item.checked}
                onChange={(event) => onChange({ ...block, items: block.items.map((it, i) => (i === index ? { ...it, checked: event.target.checked } : it)) })}
              />
              <input
                className="grow"
                aria-label={`Punkt ${index + 1}`}
                value={item.text}
                onChange={(event) => onChange({ ...block, items: block.items.map((it, i) => (i === index ? { ...it, text: event.target.value } : it)) })}
              />
              <button type="button" aria-label={`Punkt ${index + 1} entfernen`} onClick={() => onChange({ ...block, items: block.items.filter((_, i) => i !== index) })}>
                <CloseIcon />
              </button>
            </div>
          ))}
          <button type="button" onClick={() => onChange({ ...block, items: [...block.items, { text: '', checked: false }] })}>
            Punkt hinzufügen
          </button>
        </fieldset>
      )
    case 'image':
    case 'file':
    case 'link': {
      const textField = block.type === 'image' ? 'alt' : block.type === 'file' ? 'name' : 'label'
      const textLabel = block.type === 'image' ? 'Alternativtext' : block.type === 'file' ? 'Dateiname' : 'Linktext'
      const text = (block as Record<string, unknown>)[textField] as string | undefined
      return (
        <div className="block-fields">
          <label className="grow">
            {name}: Adresse
            <input
              value={block.url}
              onChange={(event) => onChange({ ...block, url: event.target.value })}
              placeholder="https://… oder /pfad"
              required
              maxLength={2000}
            />
          </label>
          <label className="grow">
            {textLabel}
            <input value={text ?? ''} onChange={(event) => onChange({ ...block, [textField]: event.target.value || undefined } as Block)} maxLength={500} />
          </label>
        </div>
      )
    }
    case 'project_reference':
      return <ProjectPicker label={name} value={block.projectId} onChange={(projectId) => onChange({ ...block, projectId })} />
    case 'task_reference':
      return <TaskPicker label={name} value={block.taskId} onChange={(taskId) => onChange({ ...block, taskId })} />
    case 'knowledge_reference':
      return <ArticlePicker label={name} value={block.articleId} exclude={articleId} onChange={(id) => onChange({ ...block, articleId: id })} />
  }
}

type PickerProps = { label: string; value: string; onChange: (id: string) => void }

export function ProjectPicker({ label, value, onChange, required = true }: PickerProps & { required?: boolean }) {
  const [projects, setProjects] = useState<ProjectSummary[]>([])
  useEffect(() => {
    fetchProjects().then((page) => setProjects(page.items), () => setProjects([]))
  }, [])
  return (
    <label className="block-fields stacked">
      {label}
      <select value={value} onChange={(event) => onChange(event.target.value)} required={required}>
        <option value="">Projekt wählen …</option>
        {value && !projects.some((project) => project.id === value) && <option value={value}>Bisher gewähltes Projekt</option>}
        {projects.map((project) => (
          <option key={project.id} value={project.id}>
            {project.name}
          </option>
        ))}
      </select>
    </label>
  )
}

/** Picks a project first, then one of its tasks. */
export function TaskPicker({ label, value, onChange }: PickerProps) {
  const [projectId, setProjectId] = useState('')
  const [tasks, setTasks] = useState<Task[]>([])
  useEffect(() => {
    if (!projectId) return
    fetchTasks(projectId).then((page) => setTasks(page.items), () => setTasks([]))
  }, [projectId])
  return (
    <div className="block-fields">
      <ProjectPicker label={`${label}: Projekt`} value={projectId} onChange={setProjectId} required={false} />
      <label className="grow">
        {label}: Aufgabe
        <select value={value} onChange={(event) => onChange(event.target.value)} required disabled={!projectId && !value}>
          <option value="">Aufgabe wählen …</option>
          {value && !tasks.some((task) => task.id === value) && <option value={value}>Bisher gewählte Aufgabe</option>}
          {tasks.map((task) => (
            <option key={task.id} value={task.id}>
              {task.title}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}

export function ArticlePicker({ label, value, onChange, exclude }: PickerProps & { exclude?: string }) {
  const [articles, setArticles] = useState<ArticleSummary[]>([])
  useEffect(() => {
    searchArticles({}).then((page) => setArticles(page.items), () => setArticles([]))
  }, [])
  return (
    <label className="block-fields stacked">
      {label}
      <select value={value} onChange={(event) => onChange(event.target.value)} required>
        <option value="">Artikel wählen …</option>
        {value && !articles.some((article) => article.id === value) && <option value={value}>Bisher gewählter Artikel</option>}
        {articles
          .filter((article) => article.id !== exclude)
          .map((article) => (
            <option key={article.id} value={article.id}>
              {article.title}
            </option>
          ))}
      </select>
    </label>
  )
}
