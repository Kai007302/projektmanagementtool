import { describe, expect, it } from 'vitest'
import appCss from './App.css?raw'
import indexCss from './index.css?raw'

/**
 * The stylesheets use no CSS nesting. A lost closing brace would silently nest every later rule inside the one before
 * it (that once switched off the menus and dialogs), so rules may only sit at the top level or inside an @-rule.
 */
function nestedRules(css: string): string[] {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, ' '))
  const stack: { atRule: boolean; line: number }[] = []
  const problems: string[] = []
  let selector = ''
  let line = 1
  for (const char of withoutComments) {
    if (char === '\n') line++
    if (char === '{') {
      const atRule = selector.trim().startsWith('@')
      const parent = stack.at(-1)
      if (parent && !parent.atRule) problems.push(`line ${line}: "${selector.trim()}" is nested in the rule from line ${parent.line}`)
      stack.push({ atRule, line })
      selector = ''
    } else if (char === '}') {
      stack.pop()
      selector = ''
    } else if (char === ';') {
      selector = ''
    } else {
      selector += char
    }
  }
  if (stack.length > 0) problems.push(`${stack.length} block(s) not closed, the first from line ${stack[0].line}`)
  return problems
}

describe('stylesheets', () => {
  it.each([
    ['App.css', appCss],
    ['index.css', indexCss],
  ])('%s has no rule nested inside another rule', (_, css) => {
    expect(nestedRules(css)).toEqual([])
  })
})
