/** Article content blocks, as the API stores them (docs/DATA_MODEL.md). Text only, never HTML. */
export type Block =
  | { id: string; type: 'heading'; level: 1 | 2 | 3; text: string }
  | { id: string; type: 'paragraph' | 'quote'; text: string }
  | { id: string; type: 'callout'; tone: CalloutTone; text: string }
  | { id: string; type: 'code'; language?: string; code: string }
  | { id: string; type: 'bullet_list' | 'numbered_list'; items: string[] }
  | { id: string; type: 'checklist'; items: { text: string; checked: boolean }[] }
  | { id: string; type: 'image'; url: string; alt?: string }
  | { id: string; type: 'file'; url: string; name?: string }
  | { id: string; type: 'link'; url: string; label?: string }
  | { id: string; type: 'task_reference'; taskId: string }
  | { id: string; type: 'project_reference'; projectId: string }
  | { id: string; type: 'knowledge_reference'; articleId: string }

export type BlockType = Block['type']

export type CalloutTone = 'info' | 'warning' | 'success'

export const blockTypes: Record<BlockType, string> = {
  heading: 'Überschrift',
  paragraph: 'Absatz',
  bullet_list: 'Aufzählung',
  numbered_list: 'Nummerierte Liste',
  checklist: 'Checkliste',
  quote: 'Zitat',
  callout: 'Hinweis',
  code: 'Code',
  image: 'Bild',
  file: 'Datei',
  link: 'Link',
  task_reference: 'Aufgabe',
  project_reference: 'Projekt',
  knowledge_reference: 'Wissensartikel',
}

export const calloutTones: Record<CalloutTone, string> = { info: 'Info', warning: 'Warnung', success: 'Erfolg' }

const newId = () => crypto.randomUUID().replaceAll('-', '')

export function newBlock(type: BlockType): Block {
  const id = newId()
  switch (type) {
    case 'heading':
      return { id, type, level: 2, text: '' }
    case 'paragraph':
    case 'quote':
      return { id, type, text: '' }
    case 'callout':
      return { id, type, tone: 'info', text: '' }
    case 'code':
      return { id, type, code: '' }
    case 'bullet_list':
    case 'numbered_list':
      return { id, type, items: [''] }
    case 'checklist':
      return { id, type, items: [{ text: '', checked: false }] }
    case 'image':
    case 'file':
    case 'link':
      return { id, type, url: '' }
    case 'task_reference':
      return { id, type, taskId: '' }
    case 'project_reference':
      return { id, type, projectId: '' }
    case 'knowledge_reference':
      return { id, type, articleId: '' }
  }
}

/** Moves the block at <paramref name="index"/> by <paramref name="offset"/> positions. */
export function move<T>(items: T[], index: number, offset: number): T[] {
  const target = index + offset
  if (target < 0 || target >= items.length) return items
  const result = [...items]
  const [item] = result.splice(index, 1)
  result.splice(target, 0, item)
  return result
}

/** Only http(s) and same-origin paths are rendered as links, like the API accepts them. */
export const safeUrl = (url: string) => /^https?:\/\//i.test(url) || (url.startsWith('/') && !url.startsWith('//'))
